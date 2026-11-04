using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Ecosystem;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using UnityEngine;

namespace GuildMaster.Session
{
    /// <summary>
    /// Trial fights for tuning (debug, P4-87): fresh members with no traits, of the given classes, level and gear, on a hunt
    /// against a fresh pack of the given kind and level out in the open, again and again. Each round ends when the pack is
    /// down, the members are down or have pulled back, or time runs out; the members of a round are let go afterwards, so
    /// one round's deaths do not carry into the next. Read the tally with <see cref="Report"/>.
    /// Started by <see cref="DebugCommands.Trial"/>.
    /// </summary>
    public class TrialRunner : MonoBehaviour
    {
        public struct Round
        {
            public bool Won, TimedOut;
            public int Deaths, Retreated, PotionsUsed;
            public float EndHpRatio, LowestHpRatio, Seconds;
            public int MonstersLeft;
        }

        public string Label { get; private set; }
        public readonly List<Round> Rounds = new();
        public bool Running { get; private set; }
        public int Planned { get; private set; }

        /// <summary>A round gives up after this many (scaled) seconds: a game hour is ten, so the day stays light from 7 o'clock.</summary>
        const float MaxSeconds = 110f;

        public static TrialRunner Start(string species, int pack, int level, AdventurerClass[] team, int memberLevel, int gear, int rounds, int speed)
        {
            var go = new GameObject("TrialRunner");
            var runner = go.AddComponent<TrialRunner>();
            runner.Label = $"{species}x{pack} Lv{level} vs [{string.Join(",", team)}] Lv{memberLevel} gear{gear}";
            runner.Planned = rounds;
            runner.StartCoroutine(runner.Run(species, pack, level, team, memberLevel, gear, rounds, speed));
            return runner;
        }

        IEnumerator Run(string speciesId, int pack, int level, AdventurerClass[] team, int memberLevel, int gear, int rounds, int speed)
        {
            Running = true;
            var eco = Services.Get<EcosystemManager>();
            var world = Services.Get<World.WorldService>();
            var roster = Services.Get<GuildRoster>();
            var board = Services.Get<QuestBoard>();
            var clock = Services.Get<GameClock>();
            var pool = Services.Get<PoolService>();
            var species = eco.Species.First(s => s.id == speciesId);
            TownQuestPoster.Enabled = false;
            Incidents.IncidentService.Enabled = false;
            Disasters.DisasterService.ChanceOverride = 0f;
            // The guild's own stay in town out of the way.
            foreach (var a in roster.Members.ToList()) if (a.IsAlive) a.ChangeState(a.RestState);

            var site = PickSite(world, eco);
            Services.Get<Fog.FogOfWar>().Reveal(site, 25f);
            if (Services.TryGet<CameraControl.CameraController>(out var cam)) cam.JumpTo(site);

            for (int r = 0; r < rounds; r++)
            {
                // Daytime, so no camp for the night breaks the fight off (forward only: hours passed fire their events).
                if (clock.HourOfDay < 6f || clock.HourOfDay > 8f)
                {
                    float day = Mathf.Floor(clock.TotalHours / 24f) * 24f;
                    typeof(GameClock).GetField("_totalHours", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        ?.SetValue(clock, clock.HourOfDay < 6f ? day + 7f : day + 31f);
                }
                ClearAround(site, 30f);

                var monsters = new List<Monster>();
                for (int i = 0; i < pack; i++)
                {
                    world.Grid.TryRandomWalkableNear(World.GridMap.WorldToCell(site), 2, GameRandom.Shared, out var c);
                    var m = pool.Spawn<Monster>(Monster.PoolKey, World.GridMap.CellToWorld(c));
                    m.Setup(species, null);
                    m.SetLevel(level);
                    monsters.Add(m);
                }

                var toTown = (world.TownCenterWorld - site).normalized;
                var stand = site + toTown * 9f;
                var members = new List<Adventurer>();
                foreach (var cls in team)
                {
                    var d = AdventurerGenerator.Generate(GameRandom.Shared, memberLevel);
                    d.cls = cls;
                    d.skillIds?.Clear();
                    d.traitIds?.Clear();
                    d.level = memberLevel;
                    d.gearTier = gear;
                    d.potions = 1;
                    d.fatigue = d.stress = 0f;
                    world.Grid.TryFindNearestWalkable(World.GridMap.WorldToCell(stand + GameRandom.Shared.InsideUnitCircle() * 1.5f), 4, out var cell);
                    members.Add(roster.Recruit(d, World.GridMap.CellToWorld(cell)));
                }
                // The board posts hunts only for kinds the guild has seen (P4-124: a trial in a new game posted nothing
                // and its members idled in town).
                if (Services.TryGet<GuildIntel>(out var intel)) intel.MarkKnown(species);
                if (!board.TryPostRequest(new QuestDraft
                {
                    Type = QuestType.Hunt, Species = species, Count = pack, Point = site, HasPoint = true, Radius = QuestBoard.HuntRadius,
                    RewardGold = 100, RewardExp = 10, MaxParty = members.Count,
                }, QuestClient.Town, out var quest, out var postError))
                    Debug.LogWarning($"[Trial] the hunt was not posted: {postError}");
                // Everyone takes it before anyone sets out (P4-124: the first taker alone counted as party work gathering
                // in town and walked off towards it before the others had taken it).
                var takers = new List<Adventurer>();
                for (int i = 0; i < members.Count; i++)
                {
                    var a = members[i];
                    if (quest == null || !board.TryAccept(quest, a, invited: i > 0)) continue;
                    a.CurrentQuest = quest;
                    takers.Add(a);
                }
                if (quest != null && Services.TryGet<PartyService>(out var parties)) parties.IsGathering(quest); // all here: it ends
                foreach (var a in takers) a.ChangeState(a.TravelState);

                clock.SetSpeed(speed);
                float startTime = Time.time;
                int potions = members.Sum(a => a.Data.potions);
                var retreated = new HashSet<Adventurer>();
                float lowest = 1f;
                float quietSince = -1f;
                while (true)
                {
                    yield return null;
                    foreach (var a in members)
                    {
                        if (!a.IsAlive) continue;
                        lowest = Mathf.Min(lowest, a.HpRatio);
                        string state = a.StateLabel;
                        if (state.Contains("후퇴") || state.Contains("야영") || state.Contains("귀환")) retreated.Add(a);
                    }
                    bool won = monsters.All(m => !m.IsAlive);
                    var living = members.Where(a => a.IsAlive).ToList();
                    bool gone = living.Count == 0 || living.All(a => retreated.Contains(a) && !a.IsInFight(3f));
                    if (gone) { if (quietSince < 0f) quietSince = clock.TotalHours; }
                    else quietSince = -1f;
                    bool timedOut = Time.time - startTime > MaxSeconds;
                    if (won || timedOut || gone && clock.TotalHours - quietSince > 0.05f)
                    {
                        Rounds.Add(new Round
                        {
                            Won = won, TimedOut = timedOut && !won,
                            Deaths = members.Count(a => !a.IsAlive),
                            Retreated = retreated.Count,
                            PotionsUsed = potions - members.Where(a => a.IsAlive).Sum(a => a.Data.potions),
                            EndHpRatio = living.Count > 0 ? living.Average(a => a.HpRatio) : 0f,
                            LowestHpRatio = lowest,
                            Seconds = Time.time - startTime,
                            MonstersLeft = monsters.Count(m => m.IsAlive),
                        });
                        break;
                    }
                }

                clock.SetSpeed(0);
                foreach (var m in monsters) if (m.IsAlive) m.Perish();
                ClearAround(site, 40f); // anything that wandered into the fight
                if (quest != null)
                {
                    foreach (var t in quest.Assignees.ToList()) board.Abandon(quest, t);
                    board.Cancel(quest);
                }
                foreach (var a in members) if (a != null && a.IsAlive) roster.Dismiss(a);
                yield return null;
            }
            Running = false;
        }

        /// <summary>Open walkable ground 40-60 cells out, clear of every den by 30 cells.</summary>
        static Vector2 PickSite(World.WorldService world, EcosystemManager eco)
        {
            var town = world.Map.TownCenter;
            for (int r = 40; r <= 90; r += 5)
            for (int k = 0; k < 24; k++)
            {
                float ang = k * Mathf.PI * 2f / 24f;
                var cell = town + new Vector2Int(Mathf.RoundToInt(Mathf.Cos(ang) * r), Mathf.RoundToInt(Mathf.Sin(ang) * r));
                if (!world.Grid.InBounds(cell) || world.Map[cell.x, cell.y] != World.TileType.Grass) continue;
                var p = World.GridMap.CellToWorld(cell);
                if (eco.Nests.Any(n => !n.Retired && Vector2.Distance(n.WorldPosition, p) < 30f)) continue;
                bool open = true;
                for (int dy = -3; dy <= 3 && open; dy++)
                for (int dx = -3; dx <= 3 && open; dx++)
                    if (!world.Grid.IsWalkable(cell + new Vector2Int(dx, dy))) open = false;
                if (open) return p;
            }
            return world.TownCenterWorld + new Vector2(0f, -45f);
        }

        /// <summary>Wild monsters that wandered close are sent off (they would join the fight).</summary>
        static void ClearAround(Vector2 site, float radius)
        {
            var near = new List<Units.Unit>();
            Units.UnitRegistry.Collect(site, radius, u => u is Monster { IsAlive: true }, near);
            foreach (var u in near) ((Monster)u).Perish();
        }

        public string Report()
        {
            if (Rounds.Count == 0) return $"{Label}: {(Running ? "running" : "no rounds")}";
            int n = Rounds.Count;
            var sb = new StringBuilder();
            sb.Append($"{Label}: {n}/{Planned} rounds{(Running ? " (running)" : "")} · won {Rounds.Count(x => x.Won)}")
              .Append($" · timed out {Rounds.Count(x => x.TimedOut)} · deaths {Rounds.Sum(x => x.Deaths)}")
              .Append($" · rounds with a retreat {Rounds.Count(x => x.Retreated > 0)}")
              .Append($" · end hp {Rounds.Average(x => x.EndHpRatio) * 100f:0}% · lowest hp {Rounds.Average(x => x.LowestHpRatio) * 100f:0}%")
              .Append($" · potions {Rounds.Average(x => x.PotionsUsed):0.0} · {Rounds.Average(x => x.Seconds):0}s · left {Rounds.Average(x => x.MonstersLeft):0.0}");
            return sb.ToString();
        }
    }
}
