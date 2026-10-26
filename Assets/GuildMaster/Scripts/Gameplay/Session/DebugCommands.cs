using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Economy;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using GuildMaster.Town;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Session
{
    /// <summary>Developer helpers for testing (callable from editor tooling or a debug console).</summary>
    public static class DebugCommands
    {
        public static string Status()
        {
            var sb = new StringBuilder();
            var clock = Services.Get<GameClock>();
            var t = Services.Get<GuildTreasury>();
            sb.AppendLine($"[{clock.FormatTime()}] gold={t.Gold} stock={string.Join(",", t.Stock.Where(kv => kv.Value > 0).Select(kv => kv.Key + ":" + kv.Value))}");
            var eco = Services.Get<EcosystemManager>();
            sb.AppendLine("pop: " + string.Join(" ", eco.Species.Select(s => $"{s.id}={eco.GetPopulation(s)}")));
            foreach (var a in Services.Get<GuildRoster>().Members)
                sb.AppendLine($"  {a.Data.id} {a.Data.name} {a.Data.cls} Lv{a.Data.level} hp={a.Hp:0}/{a.Stats.MaxHp:0} gold={a.Data.gold} fat={a.Data.fatigue:0} loot={a.Data.LootCount} pot={a.Data.potions} state={a.StateLabel} quest={a.CurrentQuest?.Id}");
            foreach (var q in Services.Get<QuestBoard>().Quests)
                sb.AppendLine($"  {q.Id} {q.Title} {q.Status} {q.ProgressText} assignees={string.Join(",", q.Assignees.Select(x => x.TakerName))}");
            foreach (var line in Services.Get<GameLog>().Recent(12)) sb.AppendLine("  > " + line);
            return sb.ToString();
        }

        /// <summary>
        /// Posts a hunt around (x, y) as the town's quest (P4-82: the guild no longer posts hunts; <paramref name="gold"/>
        /// is what the town pays, the guild's fee included). Without a point it uses the nearest known nest of the
        /// species, or (debug only, ignoring the information rule) the nearest living one.
        /// </summary>
        public static string PostHunt(string speciesId, int count, int gold, int exp, int party = 1, float x = -1f, float y = -1f)
        {
            var eco = Services.Get<EcosystemManager>();
            var species = eco.Species.FirstOrDefault(s => s.id == speciesId);
            if (species == null) return "unknown species: " + speciesId;
            var center = Services.Get<World.WorldService>().TownCenterWorld;
            Vector2 point = new(x, y);
            if (x < 0f || y < 0f)
            {
                var nest = Services.Get<GuildIntel>().FindNearestKnownNest(center, species);
                var any = Units.UnitRegistry.FindNearest(center, 999f, u => u is Monster m && m.Def == species && m.IsAlive);
                if (nest != null) point = nest.WorldPosition;
                else if (any != null) point = any.Position;
                else return "no " + speciesId + " to hunt";
            }
            var ok = Services.Get<QuestBoard>().TryPostRequest(new QuestDraft
            {
                Type = QuestType.Hunt, Species = species, Count = count, RewardGold = gold, RewardExp = exp, MaxParty = party,
                Point = point, HasPoint = true, Radius = QuestBoard.HuntRadius,
            }, QuestClient.Town, out var q, out var err);
            return ok ? q.Id : err;
        }

        /// <summary>The region lords (P4-83): lair, the lord's state and its guards, who fell, the cleansed land (real values, debug).</summary>
        public static string Lords()
        {
            var svc = Services.Get<RegionLordService>();
            var eco = Services.Get<EcosystemManager>();
            var town = Services.Get<World.WorldService>().TownCenterWorld;
            var intel = Services.Get<GuildIntel>();
            var sb = new StringBuilder($"lords {svc.Lords.Count}, known fallen {svc.KnownFallen}, victory {svc.Victory}, sealed nests {eco.Nests.Count(n => n.Sealed)}\n");
            for (int i = 0; i < svc.Lords.Count; i++)
            {
                var l = svc.Lords[i];
                var lord = l.Lair.LivingLord;
                int guards = l.Lair.Members.Count(m => m != null && m.IsAlive && !m.IsRegionLord);
                string state = lord != null ? $"alive Lv{lord.Level} hp {lord.Hp:0}/{lord.Stats.MaxHp:0} atk {lord.Stats.Attack:0.#} def {lord.Stats.Defense:0.#} tier {lord.Tier} at {lord.Position.x:0},{lord.Position.y:0}" : "dead";
                sb.AppendLine($"  [{i}] {RegionLords.RegionName(l.Region)} {l.Def.displayName} lair {l.Lair.NestId} @{Vector2.Distance(l.Lair.WorldPosition, town):0} lv{l.Lair.BirthLevel} " +
                              $"guards {guards}/{l.Lair.Capacity} ({l.Lair.Species.id}) {state} seen {intel.IsKnown(l.Def)} slain {l.Slain} known {l.Known} by [{string.Join(",", l.Participants)}]");
            }
            return sb.ToString();
        }

        /// <summary>Kills a region lord as if <paramref name="memberIndex"/> had struck it down (debug: cleansing, titles, victory).</summary>
        public static string KillLord(int index, int memberIndex = 0)
        {
            var svc = Services.Get<RegionLordService>();
            if (index < 0 || index >= svc.Lords.Count) return "no lord " + index;
            var lord = svc.Lords[index].Lair.LivingLord;
            if (lord == null) return "already dead";
            var members = Services.Get<GuildRoster>().Members;
            var by = memberIndex >= 0 && memberIndex < members.Count ? members[memberIndex] : null;
            lord.ReceiveDamage(lord.Hp + 1e6f, by, false);
            return Lords();
        }

        /// <summary>
        /// A test fight against a region lord (debug, tuning): <paramref name="members"/> members (recruited up to it) set to
        /// <paramref name="level"/> and gear tier <paramref name="gear"/>, put on the lord's quest a few cells from its lair, at
        /// midday so no camp breaks it off. Read the fight with <see cref="Lords"/> (or <c>LordFightStatus</c>).
        /// </summary>
        public static string LordFight(int index, int members, int level, int gear)
        {
            var svc = Services.Get<RegionLordService>();
            if (index < 0 || index >= svc.Lords.Count) return "no lord " + index;
            var lair = svc.Lords[index].Lair;
            var lord = lair.LivingLord;
            if (lord == null) return "already dead";
            var clock = Services.Get<GameClock>();
            typeof(GameClock).GetField("_totalHours", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(clock, Mathf.Floor(clock.TotalHours / 24f) * 24f + 9f);
            var roster = Services.Get<GuildRoster>();
            var world = Services.Get<World.WorldService>();
            while (roster.Members.Count < members) roster.Recruit(AdventurerGenerator.Generate(GameRandom.Shared), world.TownCenterWorld);
            Services.Get<GuildIntel>().MarkKnown(lord.Def);
            var board = Services.Get<QuestBoard>();
            board.TryPostRequest(new QuestDraft
            {
                Type = QuestType.Hunt, Species = lord.Def, Count = 1, Point = lair.WorldPosition, HasPoint = true, Radius = QuestBoard.HuntRadius,
                RewardGold = 1000, RewardExp = 600, MaxParty = members,
            }, QuestClient.Town, out var q, out var err, lair);
            if (q == null) return "not posted: " + err;
            var stand = lair.WorldPosition + (world.TownCenterWorld - lair.WorldPosition).normalized * 12f;
            // Every class at least once before the second of any.
            var team = roster.Members.Where(a => a.IsAlive).OrderBy(a => roster.Members.Where(b => b.Data.cls == a.Data.cls).ToList().IndexOf(a)).Take(members).ToList();
            for (int i = 0; i < team.Count; i++)
            {
                var a = team[i];
                a.Data.level = level;
                a.Data.gearTier = gear;
                a.Data.potions = 3;
                a.RecalculateStats(fullHeal: true);
                world.Grid.TryFindNearestWalkable(World.GridMap.WorldToCell(stand + new Vector2(i % 3, i / 3)), 4, out var cell);
                a.Mover.Stop();
                a.transform.position = World.GridMap.CellToWorld(cell);
            }
            AcceptAsParty(board, q, team);
            Services.Get<Fog.FogOfWar>().Reveal(lair.WorldPosition, 20f);
            Services.Get<CameraControl.CameraController>().JumpTo(lair.WorldPosition);
            return $"{q.Id}: {string.Join(", ", team.Select(a => $"{a.Data.name}({a.Data.cls} {a.Stats.MaxHp:0}hp {a.Stats.Attack:0.#}atk)"))} vs {lord.DisplayName} {lord.Hp:0}hp";
        }

        /// <summary>
        /// Puts a trial team on party work as one party (debug): the others go on the quest first and the leader takes it
        /// last, so the party service does not go looking in town for comrades — the team stands out by the lair, and a lone
        /// first taker of party work gives it up as 무모 (P4-90; every member gave up the dragon trial so, P4-94) — then
        /// the party forms as if all had said yes.
        /// </summary>
        static void AcceptAsParty(QuestBoard board, Quest q, List<Adventurer> team)
        {
            if (team.Count == 0) return;
            for (int i = 1; i < team.Count; i++) q.AddAssignee(team[i]);
            if (!board.TryAccept(q, team[0], invited: true)) return;
            foreach (var a in team)
            {
                a.CurrentQuest = q;
                a.ChangeState(a.TravelState);
            }
            EventBus.Publish(new PartyFormedEvent { Quest = q, Leader = team[0], Members = team.Skip(1).ToList() });
        }

        /// <summary>Moves the camera to a lair (debug: the lords are deep in the fog).</summary>
        public static string JumpToLair(int index)
        {
            var svc = Services.Get<RegionLordService>();
            if (index < 0 || index >= svc.Lords.Count) return "no lord " + index;
            Services.Get<CameraControl.CameraController>().JumpTo(svc.Lords[index].Lair.WorldPosition);
            return svc.Lords[index].Lair.NestId;
        }

        /// <summary>The town's postings (P4-82): what it posted, and why each known den is or is not on the board.</summary>
        public static string TownQuests(bool checkNow = false)
        {
            var poster = Services.Get<TownQuestPoster>();
            var intel = Services.Get<GuildIntel>();
            var town = Services.Get<World.WorldService>().TownCenterWorld;
            var sb = new StringBuilder();
            if (checkNow) sb.AppendLine($"checked now: +{poster.Check()}");
            sb.AppendLine($"town posted {poster.Posted} (enabled {TownQuestPoster.Enabled}), treasury {Services.Get<GuildTreasury>().Gold}G");
            foreach (var q in Services.Get<QuestBoard>().Quests)
                sb.AppendLine($"  {q.Id} [{q.ClientLabel}] {q.Title} {q.Status} pay {q.RewardGold}G+fee {q.FeeGold}G /{q.RewardExp}EXP party {q.Assignees.Count}/{q.MaxParty} {Quest.DangerLabel(q.Danger)}");
            foreach (var n in intel.KnownNests)
                sb.AppendLine($"  den {intel.NameOf(n)} @{Vector2.Distance(n.WorldPosition, town):0} seen {intel.Get(n).LastCount}: {poster.SkipReason(n) ?? "would post"}");
            return sb.ToString();
        }

        // ---------------- Sudden incidents (P4-85) ----------------

        /// <summary>Every incident, real values: kind, where it is and where the rumour put it, the quest, time left, how it ended.</summary>
        public static string Incidents()
        {
            var svc = Services.Get<GuildMaster.Incidents.IncidentService>();
            var clock = Services.Get<GameClock>();
            var sb = new StringBuilder($"incidents active {svc.Active.Count}, past {svc.History.Count} (enabled {GuildMaster.Incidents.IncidentService.Enabled}) at {clock.TotalHours:0.00}h\n");
            foreach (var i in svc.Active.Concat(svc.History))
            {
                var m = i.Target;
                string target = m != null ? $" target {m.DisplayName} Lv{m.Level} hp {m.Hp:0}/{m.Stats.MaxHp:0} at {m.Position.x:0},{m.Position.y:0} {m.StateLabel} off-circle {Vector2.Distance(m.Position, i.RumourCentre) - i.RumourRadius:0.0}" : "";
                if (i.Kind == GuildMaster.Incidents.IncidentKind.Rescue)
                {
                    var t = i.Traveller;
                    target = $" traveller {i.TargetName} {(t != null ? $"hp {t.Hp:0}/{t.Stats.MaxHp:0} {t.StateLabel} at {t.Position.x:0},{t.Position.y:0}" : "gone")} → {QuestBoard.DescribeArea(i.Destination)}, " +
                             $"ambushers {i.Members.Count} {i.SpeciesName} Lv{i.TargetLevel}";
                }
                if (i.Kind == GuildMaster.Incidents.IncidentKind.Horde)
                    target = $" horde {i.SpeciesName} Lv{i.HordeLevel} {i.Members.Count}/{i.HordeSize} at {i.Spot.x:0},{i.Spot.y:0} ({QuestBoard.DescribeArea(i.Spot)}) → {i.Site?.DisplayName} {(i.Site != null ? QuestBoard.DescribeArea(i.Site.WorldPosition) : "")} " +
                             $"marching {i.Band != null && Services.Get<MigrationService>().Bands.Contains(i.Band)} off-circle {Vector2.Distance(i.Spot, i.RumourCentre) - i.RumourRadius:0.0}";
                var q = i.Quest;
                string quest = q != null ? $" quest {q.Id} {q.Status} party {q.Assignees.Count}/{q.MaxParty} pay {q.RewardGold}+{q.FeeGold}G point {q.Point.x:0},{q.Point.y:0} r{q.Radius:0}" : "";
                sb.AppendLine($"  #{i.Id} {i.Kind} {i.Outcome} spot {i.Spot.x:0},{i.Spot.y:0} ({QuestBoard.DescribeArea(i.Spot)}) rumour@{i.RumourAtHour:0.0} {(i.Rumoured ? $"({i.RumourCentre.x:0},{i.RumourCentre.y:0}) r{i.RumourRadius:0}" : "not yet")} " +
                              $"sighted {i.Sighted} ends@{i.EndsAtHour:0.0}{target}{quest}");
            }
            return sb.ToString();
        }

        /// <summary>Starts a rare monster now (debug): at a spot (else a random one), of a kind (else one that lives there); <paramref name="rumourNow"/> brings its rumour at once.</summary>
        public static string RareNow(string speciesId = null, float x = float.NaN, float y = float.NaN, bool rumourNow = true)
        {
            var svc = Services.Get<GuildMaster.Incidents.IncidentService>();
            var kind = string.IsNullOrEmpty(speciesId) ? null : Services.Get<GameDatabase>().Monster(speciesId);
            var i = svc.StartRareMonster(float.IsNaN(x) ? null : new Vector2(x, y), kind);
            if (i == null) return "no spot found";
            if (rumourNow) i.RumourAtHour = Services.Get<GameClock>().TotalHours;
            return Incidents();
        }

        /// <summary>Sends a horde now (debug): to the empty site nearest the town (or nest <paramref name="nestIndex"/>), of <paramref name="size"/> (0 = by the rules).</summary>
        public static string HordeNow(int nestIndex = -1, int size = 0, bool rumourNow = true)
        {
            var svc = Services.Get<GuildMaster.Incidents.IncidentService>();
            var nests = Services.Get<EcosystemManager>().Nests;
            var site = nestIndex >= 0 && nestIndex < nests.Count ? nests[nestIndex] : null;
            var i = svc.StartHorde(site, null, size);
            if (i == null) return "no site for a horde";
            if (rumourNow) i.RumourAtHour = Services.Get<GameClock>().TotalHours;
            return Incidents();
        }

        /// <summary>A traveller ambushed now (debug): on a road to a village by the rules, or at x, y (heading for our town).</summary>
        public static string RescueNow(float x = float.NaN, float y = float.NaN, int ambushers = 0, bool rumourNow = true)
        {
            var svc = Services.Get<GuildMaster.Incidents.IncidentService>();
            var i = svc.StartRescue(float.IsNaN(x) ? null : new Vector2(x, y), ambushers);
            if (i == null) return "no road or spot for an ambush";
            if (rumourNow) i.RumourAtHour = Services.Get<GameClock>().TotalHours;
            return Incidents();
        }

        /// <summary>Word of a ruin now (debug): ruin <paramref name="index"/> (else a random one not looted nor talked of).</summary>
        public static string RuinNow(int index = -1, bool rumourNow = true)
        {
            var svc = Services.Get<GuildMaster.Incidents.IncidentService>();
            var i = svc.StartRuin(index);
            if (i == null) return "no ruin left";
            if (rumourNow) i.RumourAtHour = Services.Get<GameClock>().TotalHours;
            return Ruins();
        }

        /// <summary>Every ruin, real values: where its way down is, its dungeon, looted, the incident on it.</summary>
        public static string Ruins()
        {
            var world = Services.Get<World.WorldService>();
            var svc = Services.Get<GuildMaster.Incidents.IncidentService>();
            var fog = Services.Get<GuildMaster.Fog.FogOfWar>();
            var sb = new StringBuilder($"ruins {world.Map.Ruins.Count}, map {world.Map.Width}x{world.Map.Height} (surface {world.Map.SurfaceHeight})\n");
            foreach (var r in world.Map.Ruins)
            {
                var i = svc.ActiveAt(r);
                string talk = i == null ? "" : $" incident #{i.Id} rumoured {i.Rumoured} sighted {i.Sighted} guards {i.Members.Count} {i.SpeciesName} Lv{i.TargetLevel} quest {i.Quest?.Id} {i.Quest?.Status} party {i.Quest?.Assignees.Count}";
                int inside = UnitRegistry.All.Count(u => u.IsAlive && world.Map.RuinAt(World.GridMap.WorldToCell(u.Position)) == r && u.Faction == Faction.Guild);
                sb.AppendLine($"  #{r.Index} {GuildMaster.Incidents.IncidentRules.RuinName(r)} {r.Region} way down {r.Entrance} ({QuestBoard.DescribeArea(World.GridMap.CellToWorld(r.Entrance))}) " +
                              $"seen {fog.IsExplored(r.Entrance)} inside {r.Inside} chest {r.Chest} looted {svc.IsLooted(r.Index)} members inside {inside}{talk}");
            }
            return sb.ToString();
        }

        /// <summary>Points the camera into a ruin's dungeon (debug).</summary>
        public static string ViewRuin(int index)
        {
            var map = Services.Get<World.WorldService>().Map;
            if (index < 0 || index >= map.Ruins.Count) return "no ruin " + index;
            var cam = Services.Get<CameraControl.CameraController>();
            cam.Follow(null);
            cam.JumpTo(World.GridMap.CellToWorld(map.Ruins[index].Inside));
            return map.Ruins[index].Inside.ToString();
        }

        /// <summary>Puts the rare monster's time up now (debug: it slips away once nobody sees it).</summary>
        public static string RareTimeUp()
        {
            var now = Services.Get<GameClock>().TotalHours;
            foreach (var i in Services.Get<GuildMaster.Incidents.IncidentService>().Active) i.EndsAtHour = now;
            return Incidents();
        }

        // ---------------- Other villages (P4-84) ----------------

        /// <summary>Every village, real values: where, durability, fallen, what the guild knows, candidates, raids on it.</summary>
        public static string Villages()
        {
            var svc = Services.Get<GuildMaster.Villages.VillageService>();
            var raids = Services.Get<GuildMaster.Villages.VillageRaids>();
            var town = Services.Get<World.WorldService>().TownCenterWorld;
            var sb = new StringBuilder($"villages {svc.Villages.Count} (standing {svc.Standing.Count()}, started with {svc.InitialCount}), raids so far {raids.RaidCount}\n");
            for (int i = 0; i < svc.Villages.Count; i++)
            {
                var v = svc.Villages[i];
                sb.AppendLine($"  [{i}] #{v.Id} {v.DisplayName} {v.Region} {(v.Inner ? "inner" : "outer")} @{v.Cell} {Vector2.Distance(v.WorldPosition, town):0} cells " +
                              $"hp {v.Hp:0}/{v.MaxHp:0}{(v.IsDestroyed ? $" FELL@{v.FellAtHour:0.0}" : "")} known={v.Known} seenRuined={v.SeenRuined} seenHp={v.SeenHpRatio:0.00} " +
                              $"candidates={(v.CandidatesDrawn ? string.Join(",", v.Candidates.Select(c => $"{c.data.name} Lv{c.data.level}")) : "(not drawn)")} " +
                              $"out={svc.CandidatesOut(v).Count()} refugeesSent={v.RefugeesSent} underAttack={raids.IsUnderAttack(v)}");
            }
            foreach (var r in raids.Report())
                sb.AppendLine($"  raid on {r.village.DisplayName}: {r.raiders} raiders sighted={r.sighted} passing={r.passing} {r.hours:0.0}h");
            return sb.ToString();
        }

        /// <summary>Puts the guild master (and the camera) at a village's doorstep: its candidates come out.</summary>
        public static string JumpToVillage(int index)
        {
            var svc = Services.Get<GuildMaster.Villages.VillageService>();
            if (index < 0 || index >= svc.Villages.Count) return "no village " + index;
            var v = svc.Villages[index];
            var master = Services.Get<Player.PlayerAvatar>();
            var world = Services.Get<World.WorldService>();
            if (!world.Grid.TryFindNearestWalkable(World.GridMap.WorldToCell(v.Doorstep + Vector2.down * 1.5f), 4, out var cell)) return "no ground";
            master.Mover.Stop();
            master.transform.position = World.GridMap.CellToWorld(cell);
            Services.Get<CameraControl.CameraController>().JumpTo(v.WorldPosition);
            return $"{v.DisplayName} at {cell}";
        }

        /// <summary>A raid on a village: from the nearest den within reach, or <paramref name="count"/> monsters of a kind spawned beside it.</summary>
        public static string RaidVillage(int index, string speciesId = null, int count = 4, float distance = 10f)
        {
            var svc = Services.Get<GuildMaster.Villages.VillageService>();
            var raids = Services.Get<GuildMaster.Villages.VillageRaids>();
            if (index < 0 || index >= svc.Villages.Count) return "no village " + index;
            var v = svc.Villages[index];
            if (string.IsNullOrEmpty(speciesId))
            {
                var nest = Services.Get<EcosystemManager>().Nests
                    .Where(n => !n.IsLair && n.Species.canRaid && !n.IsEmpty && Vector2.Distance(n.WorldPosition, v.WorldPosition) <= GuildMaster.Villages.VillageRules.RaidRadius)
                    .OrderBy(n => Vector2.Distance(n.WorldPosition, v.WorldPosition)).FirstOrDefault();
                if (nest == null) return "no raiding den within reach; pass a species";
                return $"raid from {nest.DisplayName}: {raids.Launch(nest, v)}";
            }
            var species = Services.Get<EcosystemManager>().Species.FirstOrDefault(s => s.id == speciesId);
            if (species == null) return "unknown species: " + speciesId;
            var world = Services.Get<World.WorldService>();
            var group = new List<Monster>();
            for (int i = 0; i < count; i++)
            {
                var p = v.WorldPosition + GameRandom.Shared.InsideUnitCircle().normalized * distance;
                if (!world.Grid.TryFindNearestWalkable(World.GridMap.WorldToCell(p), 3, out var c)) continue;
                var mon = Services.Get<PoolService>().Spawn<Monster>(Monster.PoolKey, World.GridMap.CellToWorld(c));
                mon.Setup(species, null);
                group.Add(mon);
            }
            return $"raid of {raids.LaunchGroup(group, v)} {species.displayName}";
        }

        /// <summary>Knocks a village down by <paramref name="amount"/> (a negative amount does nothing).</summary>
        public static string HurtVillage(int index, float amount)
        {
            var svc = Services.Get<GuildMaster.Villages.VillageService>();
            if (index < 0 || index >= svc.Villages.Count) return "no village " + index;
            var v = svc.Villages[index];
            v.Damage(amount, null, Services.Get<GameClock>().TotalHours);
            return $"{v.DisplayName} hp {v.Hp:0}/{v.MaxHp:0}{(v.IsDestroyed ? " fell" : "")}";
        }

        /// <summary>Founds a new village now (as the midnight roll would).</summary>
        public static string FoundVillage()
            => Services.Get<GuildMaster.Villages.VillageService>().TryFound(out var v) ? $"#{v.Id} {v.DisplayName} {v.Region} @{v.Cell}" : "no room";

        public static void SetSpeed(int index) => Services.Get<GameClock>().SetSpeed(index);

        /// <summary>
        /// Guild master and members take no damage (testing; F6 = both flags). With
        /// <paramref name="untargetable"/> enemies also cannot target them; pass false for lure tests.
        /// </summary>
        public static string Invulnerable(bool on, bool untargetable = true)
        {
            Units.Unit.GuildInvulnerable = on;
            Units.Unit.GuildUntargetable = on && untargetable;
            string msg = !on ? "[디버그] 길드 무적 끔 (F6)"
                : untargetable ? "[디버그] 길드 무적 켬: 피해 없음·표적 불가 (F6)" : "[디버그] 길드 무적 켬: 피해만 없음";
            Debug.Log(msg);
            if (Services.TryGet<GameLog>(out var log)) log.Add(msg, LogCategory.Info);
            return msg;
        }

        /// <summary>
        /// Spawns 'count' nest-less monsters a few cells outside town, teleports living guild
        /// members next to them and makes them fight, then points the camera there.
        /// Starts/reset the <see cref="SkirmishMonitor"/>; read results with <see cref="SkirmishReport"/>.
        /// </summary>
        public static string SpawnSkirmish(string speciesId, int count, bool pullGuild = true)
        {
            var eco = Services.Get<EcosystemManager>();
            var species = eco.Species.FirstOrDefault(s => s.id == speciesId);
            if (species == null) return "unknown species: " + speciesId;
            var world = Services.Get<World.WorldService>();
            var pool = Services.Get<PoolService>();
            var rng = GameRandom.Shared;
            var town = world.Map.TownRect;
            var center = world.TownCenterWorld;

            // First walkable, reachable spot 8 cells beyond each town edge.
            var dirs = new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down };
            var path = new List<Vector2Int>();
            Vector2 site = default;
            Vector2 toTown = default;
            bool found = false;
            foreach (var d in dirs)
            {
                float reach = (Mathf.Abs(d.x) > 0 ? town.width : town.height) * 0.5f + 8f;
                var cell = World.GridMap.WorldToCell(center + d * reach);
                if (!world.Grid.TryFindNearestWalkable(cell, 6, out cell) || world.Grid.IsInTown(cell)) continue;
                site = World.GridMap.CellToWorld(cell);
                if (!world.FindPath(center, site, path)) continue;
                toTown = -d;
                found = true;
                break;
            }
            if (!found) return "no skirmish site found";

            var monsters = new List<Monster>();
            for (int i = 0; i < count; i++)
            {
                world.Grid.TryRandomWalkableNear(World.GridMap.WorldToCell(site), 2, rng, out var c);
                var m = pool.Spawn<Monster>(Monster.PoolKey, World.GridMap.CellToWorld(c) + rng.InsideUnitCircle() * 0.3f);
                m.Setup(species, null);
                monsters.Add(m);
            }

            int pulled = 0;
            if (pullGuild)
            {
                foreach (var a in Services.Get<GuildRoster>().Members)
                {
                    if (!a.IsAlive) continue;
                    var spot = site + toTown * 7f + rng.InsideUnitCircle() * 1.5f;
                    if (!world.Grid.TryFindNearestWalkable(World.GridMap.WorldToCell(spot), 4, out var c)) continue;
                    a.Mover.Stop();
                    var p = World.GridMap.CellToWorld(c);
                    a.transform.position = new Vector3(p.x, p.y, a.transform.position.z);
                    a.ChangeState(a.ReturnState);
                    a.ForceTarget(monsters.OrderBy(m => (m.Position - p).sqrMagnitude).First());
                    pulled++;
                }
            }

            if (Services.TryGet<CameraControl.CameraController>(out var cam))
            {
                cam.Follow(null);
                cam.JumpTo(site + toTown * 3.5f);
            }
            FightCamera.Ensure(); // keeps the view on the brawl as it moves (user request 2026-09-22)
            SkirmishMonitor.Ensure().ResetStats();
            return $"skirmish {species.id}x{count} at {site} (guild pulled: {pulled})";
        }

        /// <summary>
        /// A skirmish at a forest (P4-54): a 5×5 forest block far from town. <paramref name="mode"/>:
        /// "inside" — monsters 2 cells deep, the guild 5 cells outside; "edge" — monsters on the edge
        /// cells, the guild just outside; "deep" — both inside. Only the first member is sent at the
        /// nearest monster: whether the others join is what is measured (<see cref="ForestReport"/>).
        /// </summary>
        public static string SpawnForestSkirmish(string speciesId, int count, string mode = "inside", string questSpeciesId = null)
        {
            var eco = Services.Get<EcosystemManager>();
            var species = eco.Species.FirstOrDefault(s => s.id == speciesId);
            if (species == null) return "unknown species: " + speciesId;
            var world = Services.Get<World.WorldService>();
            var grid = world.Grid;
            var town = World.GridMap.WorldToCell(world.TownCenterWorld);
            var path = new List<Vector2Int>();
            Vector2Int deep = default, outside = default;
            bool found = false;
            for (int r = 30; r < 120 && !found; r += 2)
            for (int k = 0; k < 32 && !found; k++)
            {
                float a = k * Mathf.PI / 16f;
                var c = town + new Vector2Int(Mathf.RoundToInt(Mathf.Cos(a) * r), Mathf.RoundToInt(Mathf.Sin(a) * r));
                bool block = true;
                for (int dy = -2; dy <= 2 && block; dy++)
                for (int dx = -2; dx <= 2 && block; dx++)
                    if (!grid.IsForest(c + new Vector2Int(dx, dy)) || !grid.IsWalkable(c + new Vector2Int(dx, dy))) block = false;
                if (!block) continue;
                foreach (var d in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                {
                    var o = c + d * 7;
                    if (grid.IsForest(o) || !grid.IsWalkable(o) || grid.IsForest(o - d) ) continue;
                    if (!world.FindPath(World.GridMap.CellToWorld(o), World.GridMap.CellToWorld(c), path)) continue;
                    deep = c; outside = o; found = true;
                    break;
                }
            }
            if (!found) return "no forest block found";
            var toOut = ((Vector2)(outside - deep)).normalized;
            Vector2 center = World.GridMap.CellToWorld(deep);
            Vector2 monsterAt = mode == "edge" ? center + toOut * 2f : center;
            Vector2 guildAt = mode == "deep" ? center + toOut * 1.5f : mode == "edge" ? center + toOut * 4f : World.GridMap.CellToWorld(outside);

            var pool = Services.Get<PoolService>();
            var rng = GameRandom.Shared;
            var monsters = new List<Monster>();
            for (int i = 0; i < count; i++)
            {
                var m = pool.Spawn<Monster>(Monster.PoolKey, monsterAt + rng.InsideUnitCircle() * 0.8f);
                m.Setup(species, null);
                monsters.Add(m);
            }
            // The guild is out on a hunt quest marked here (as in play: a party working an area by a
            // forest), so it patrols the spot instead of walking home.
            s_forestGuild.Clear();
            var board = Services.Get<QuestBoard>();
            var members = Services.Get<GuildRoster>().Members.Where(a => a.IsAlive).ToList();
            // Out of town first, so taking the quest sends no party invitations to them.
            foreach (var a in members) a.ChangeState(a.ReturnState);
            // The quest may be for another species (a party hunting slimes by a forest where goblins hide).
            var questSpecies = string.IsNullOrEmpty(questSpeciesId) ? species : eco.Species.FirstOrDefault(s => s.id == questSpeciesId) ?? species;
            if (Services.TryGet<GuildIntel>(out var intel)) { intel.MarkKnown(species); intel.MarkKnown(questSpecies); }
            // The board only takes spots the guild has seen.
            if (Services.TryGet<Fog.FogOfWar>(out var fog)) fog.Reveal(guildAt, 6f);
            board.TryPostRequest(new QuestDraft
            {
                Type = QuestType.Hunt, Species = questSpecies, Count = count, RewardGold = 10, RewardExp = 5, MaxParty = Mathf.Max(1, members.Count),
                Point = guildAt, HasPoint = true, Radius = QuestBoard.HuntRadius,
            }, QuestClient.Town, out var quest, out var questError);
            if (quest == null) return "quest not posted: " + questError;
            foreach (var a in members)
            {
                a.Mover.Stop();
                var p = guildAt + rng.InsideUnitCircle() * 1.2f;
                if (grid.TryFindNearestWalkable(World.GridMap.WorldToCell(p), 3, out var cell)) p = World.GridMap.CellToWorld(cell);
                a.transform.position = new Vector3(p.x, p.y, a.transform.position.z);
                if (quest != null && board.TryAccept(quest, a, invited: quest.Assignees.Count > 0))
                {
                    a.CurrentQuest = quest;
                    a.ChangeState(a.HuntState);
                }
                s_forestGuild.Add(a);
            }
            if (s_forestGuild.Count > 0)
                s_forestGuild[0].ForceTarget(monsters.OrderBy(m => (m.Position - (Vector2)s_forestGuild[0].transform.position).sqrMagnitude).First());
            s_forestMonsters.Clear();
            s_forestMonsters.AddRange(monsters);
            s_forestSpecies = species;
            s_forestStart = Time.time;
            s_forestMaxEngaged = 0;
            if (Services.TryGet<CameraControl.CameraController>(out var cam)) { cam.Follow(null); cam.JumpTo(center); }
            FightCamera.Ensure();
            SkirmishMonitor.Ensure().ResetStats();
            return $"forest skirmish '{mode}' {species.id}x{count} at {deep} (guild {s_forestGuild.Count}, outside cell {outside})";
        }

        static readonly List<Adventurer> s_forestGuild = new();
        static readonly List<Monster> s_forestMonsters = new();
        static MonsterDefinition s_forestSpecies; // pooled monsters come back as other species
        static float s_forestStart;
        static int s_forestMaxEngaged;

        /// <summary>How the forest skirmish is going: members fighting (now / most at once), monsters left, their targets.</summary>
        public static string ForestReport()
        {
            var world = Services.Get<World.WorldService>();
            int engaged = s_forestGuild.Count(a => a.IsAlive && a.CurrentTarget != null && a.CurrentTarget.IsAlive);
            s_forestMaxEngaged = Mathf.Max(s_forestMaxEngaged, engaged);
            var sb = new StringBuilder();
            sb.AppendLine($"[Forest] {Time.time - s_forestStart:0}s game  guild fighting {engaged}/{s_forestGuild.Count(a => a.IsAlive)} (max {s_forestMaxEngaged})  monsters left {s_forestMonsters.Count(m => m.IsAlive && m.Def == s_forestSpecies)}/{s_forestMonsters.Count}");
            foreach (var a in s_forestGuild)
                sb.AppendLine($"  {a.Data.name} {(a.IsAlive ? "" : "(dead) ")}{a.StateLabel} hp={a.Hp:0} forest={World.ForestSight.InForest(world, a.Position)} tgt={(a.CurrentTarget != null ? a.CurrentTarget.DisplayName : "-")}");
            foreach (var m in s_forestMonsters.Where(m => m.IsAlive && m.Def == s_forestSpecies))
                sb.AppendLine($"  {m.DisplayName} {m.StateLabel} hp={m.Hp:0} forest={World.ForestSight.InForest(world, m.Position)} tgt={(m.CurrentTarget != null ? m.CurrentTarget.DisplayName : "-")}");
            return sb.ToString();
        }

        /// <summary>Kills every living nest-less monster (leftovers from <see cref="SpawnSkirmish"/>) — not the dragon (P4-93).</summary>
        public static int ClearSkirmish()
        {
            var strays = Units.UnitRegistry.All.OfType<Monster>().Where(m => m.IsAlive && m.Home == null && !m.IsDragon).ToList();
            foreach (var m in strays) m.Perish();
            return strays.Count;
        }

        // ---------------- Recruitment (P3-3) ----------------

        static Recruitment.RecruitmentSession s_recruit;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_recruit = null;

        public static string SpawnVisitor()
        {
            var v = Services.Get<Recruitment.VisitorService>().SpawnVisitor();
            return $"{v.Data.name} {AdventurerData.ClassLabel(v.Data.cls)} Lv{v.Data.level} {v.Data.mbti}/{v.Data.values} traits={string.Join(",", v.Data.traitIds)}";
        }

        /// <summary>Talks to the first visitor (starting a session if needed). Poll RecruitStatus() for the reply.</summary>
        public static string RecruitSay(string message, bool proposal = false)
        {
            if (s_recruit == null || s_recruit.Visitor == null || s_recruit.Outcome != Recruitment.RecruitOutcome.Ongoing)
            {
                var v = Services.Get<Recruitment.VisitorService>().Visitors.FirstOrDefault(x => x != null);
                if (v == null) return "no visitor";
                s_recruit = new Recruitment.RecruitmentSession(v, Services.Get<Player.PlayerAvatar>().transform);
            }
            _ = s_recruit.SendAsync(message, proposal);
            return "sent";
        }

        public static string RecruitStatus()
        {
            if (s_recruit == null) return "no session";
            var sb = new StringBuilder($"{s_recruit.Visitor?.Data.name} affinity={s_recruit.Affinity} turns={s_recruit.PlayerTurns} busy={s_recruit.Busy} outcome={s_recruit.Outcome} llm={s_recruit.LastReplyFromLlm}\n");
            foreach (var t in s_recruit.Turns) sb.AppendLine((t.FromPlayer ? "  나: " : "  NPC: ") + t.Text);
            return sb.ToString();
        }

        // ---------------- Members' keep (P4-68) ----------------

        /// <summary>Each member's gold and debt, whether they are paying for a bed, and what the guild has received.</summary>
        public static string Keep()
        {
            var keep = Services.Get<LivingCosts>();
            var sb = new StringBuilder($"received={keep.TotalReceived}G owed={keep.Outstanding}G (meal {keep.MealGold}G/day, lodging {keep.LodgingGoldPerHour}G/h)\n");
            foreach (var a in Services.Get<GuildRoster>().Members)
                sb.AppendLine($"  {a.Data.name}: gold={a.Data.gold} debt={a.Data.debt} {(keep.IsLodging(a) ? "lodging" : a.StateLabel)}");
            return sb.ToString();
        }

        // ---------------- Guild master & escort (P3-4a) ----------------

        /// <summary>Hires an escort (the guild's own quest; gold only since P4-82).</summary>
        public static string PostEscort(int goldPerHour, int hours, int party)
        {
            var ok = Services.Get<QuestBoard>().TryPost(new QuestDraft
            {
                Type = QuestType.Escort, Hours = hours, RewardGold = goldPerHour * hours,
                MaxParty = party, Point = Services.Get<Player.PlayerAvatar>().Position,
            }, out var q, out var err);
            return ok ? q.Id : err;
        }

        /// <summary>Makes up to 'count' idle members take the quest, skipping the decision step.</summary>
        /// <summary>
        /// Party work on the board (P4-97): who is on it, whether its first taker is still gathering and for how long, how
        /// many the guild could gather, the margin of those on it — then every member: level, state, quest, party.
        /// </summary>
        public static string Recruits()
        {
            var board = Services.Get<QuestBoard>();
            var invites = Services.Get<PartyService>();
            Services.TryGet<GuildParties>(out var parties);
            var sb = new StringBuilder();
            foreach (var q in board.Quests.Where(q => q.IsActive && QuestRisk.IsPartyWork(q)))
            {
                var lead = q.Assignees.OfType<Adventurer>().FirstOrDefault();
                sb.Append($"{q.Id} {q.Title} {q.Status} on {q.Assignees.Count}/{q.MaxParty} ({string.Join(",", q.Assignees.Select(a => a.TakerName))})");
                sb.Append($" gathering={invites.IsRecruiting(q)} left={invites.RecruitHoursLeft(q):0.0}h resting={invites.RestingAfterFailure(q)}");
                sb.Append($" gatherable={QuestRisk.GatherableCount(q)} rec=Lv{QuestRisk.RecommendedLevel(q)}");
                if (lead != null)
                {
                    float power = AdventurerStats.Power(lead.Data);
                    sb.Append($" margin={QuestRisk.Margin(power, q.Assignees.Count, q):0.00} ready={QuestRisk.ReadyToSetOut(q, power, q.Assignees.Count)}");
                }
                sb.AppendLine();
            }
            if (sb.Length == 0) sb.AppendLine("no party work");
            foreach (var a in Services.Get<GuildRoster>().Members.Where(a => a.IsAlive))
                sb.AppendLine($"  {a.Data.name} Lv{a.Data.level} {a.StateLabel} quest={a.CurrentQuest?.Id ?? "-"} party={parties?.PartyOf(a)?.Name ?? "-"}");
            return sb.ToString();
        }

        public static string ForceAccept(string questId, int count)
        {
            var board = Services.Get<QuestBoard>();
            var q = board.FindById(questId);
            if (q == null) return "no quest " + questId;
            var names = new List<string>();
            foreach (var a in Services.Get<GuildRoster>().Members)
            {
                if (names.Count >= count) break;
                if (!a.IsAlive || a.CurrentQuest != null || !board.TryAccept(q, a, invited: true)) continue;
                a.CurrentQuest = q;
                a.ChangeState(a.TravelState);
                names.Add(a.Data.name);
            }
            return string.Join(",", names);
        }

        /// <summary>Orders the guild master to walk to a point 'cells' beyond the town edge in a direction (0=E,1=W,2=N,3=S).</summary>
        public static string MasterWalkOut(int dir, float cells)
        {
            var world = Services.Get<World.WorldService>();
            var dirs = new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down };
            var d = dirs[Mathf.Clamp(dir, 0, 3)];
            var town = world.Map.TownRect;
            float reach = (Mathf.Abs(d.x) > 0 ? town.width : town.height) * 0.5f + cells;
            var cell = World.GridMap.WorldToCell(world.TownCenterWorld + d * reach);
            if (!world.Grid.TryFindNearestWalkable(cell, 6, out cell)) return "no walkable cell";
            var target = World.GridMap.CellToWorld(cell);
            return Services.Get<Player.PlayerAvatar>().MoveTo(target) ? $"walking to {target}" : "no path";
        }

        public static string MasterHome() => Services.Get<Player.PlayerAvatar>().MoveTo(Services.Get<World.WorldService>().TownCenterWorld) ? "walking home" : "no path";

        public static string MasterStatus()
        {
            var m = Services.Get<Player.PlayerAvatar>();
            var world = Services.Get<World.WorldService>();
            var t = Services.Get<GuildTreasury>();
            var sb = new StringBuilder($"master ({m.Position.x:0.0},{m.Position.y:0.0}) hp={m.Hp:0}/{m.Stats.MaxHp:0} inTown={world.Grid.IsInTown(m.Position)} moving={m.Mover.IsMoving} gold={t.Gold}\n");
            foreach (var q in Services.Get<QuestBoard>().Quests.Concat(Services.Get<QuestBoard>().History).Where(q => q.Type == QuestType.Escort))
                sb.AppendLine($"  {q.Id} {q.Status} {q.ProgressText} escrow={q.RewardGold}/{q.RewardExp} pay={q.PayGold}/{q.PayExp} assignees={string.Join(",", q.Assignees.Select(x => x.TakerName))}");
            foreach (var a in Services.Get<GuildRoster>().Members)
                sb.AppendLine($"  {a.Data.name} {a.StateLabel} d={Vector2.Distance(a.Position, m.Position):0.0} quest={a.CurrentQuest?.Id} gold={a.Data.gold}");
            return sb.ToString();
        }

        /// <summary>Spawns monsters next to the guild master (they aggro on it).</summary>
        public static string SpawnNearMaster(string speciesId, int count, float distance = 4f, bool forceTarget = true)
        {
            var species = Services.Get<EcosystemManager>().Species.FirstOrDefault(s => s.id == speciesId);
            if (species == null) return "unknown species: " + speciesId;
            var world = Services.Get<World.WorldService>();
            var master = Services.Get<Player.PlayerAvatar>();
            var rng = GameRandom.Shared;
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                var p = master.Position + rng.InsideUnitCircle().normalized * distance;
                if (!world.Grid.TryFindNearestWalkable(World.GridMap.WorldToCell(p), 3, out var c)) continue;
                var mon = Services.Get<PoolService>().Spawn<Monster>(Monster.PoolKey, World.GridMap.CellToWorld(c));
                mon.Setup(species, null);
                if (forceTarget) mon.ForceTarget(master);
                spawned++;
            }
            return $"spawned {spawned}";
        }

        // ---------------- Natural disasters (P4-72) ----------------

        /// <summary>Every disaster: kind, where, cells, core work left, known, quest, ending.</summary>
        public static string Disasters()
        {
            var service = Services.Get<GuildMaster.Disasters.DisasterService>();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"재해 {service.Active.Count}건 진행, {service.History.Count}건 끝남, 탄 숲 {service.BurntCells}칸");
            foreach (var d in service.Active)
                sb.AppendLine($"  #{d.Id} {d.Name} {Quests.QuestBoard.DescribeArea(d.WorldOrigin)} cells={d.Cells.Count} " +
                              $"{(d.HasCore ? $"core={d.CoreWorkLeft:0.00}h " : "")}known={d.Known} quest={d.Quest?.Id ?? "-"}({d.Quest?.Status}) " +
                              $"ending={d.Ending} town={d.ReachedTown} age={Services.Get<GameClock>().TotalHours - d.StartedHour:0.0}h");
            return sb.ToString();
        }

        /// <summary>Starts a disaster (kind 0 산불, 1 독안개, 2 눈사태, 3 모래폭풍, 4 지진) at a cell, or near the camera when x &lt; 0.</summary>
        public static string StartDisaster(int kind, int x = -1, int y = -1)
        {
            var at = x >= 0 ? new Vector2Int(x, y) : World.GridMap.WorldToCell(Camera.main.transform.position);
            var d = Services.Get<GuildMaster.Disasters.DisasterService>().Begin((GuildMaster.Disasters.DisasterKind)kind, at);
            return d == null ? $"{at}: 이 재해에 맞는 땅이 아닙니다" : $"#{d.Id} {d.Name} at {d.Origin} cells={d.Cells.Count}";
        }

        /// <summary>Chance that a hazard skill on the right ground starts a disaster (-1: back to the rule).</summary>
        public static string DisasterChance(float chance)
        {
            GuildMaster.Disasters.DisasterService.ChanceOverride = chance;
            return $"disaster chance = {(chance >= 0f ? chance : GuildMaster.Disasters.DisasterRules.TriggerChance):0.00}";
        }

        // ---------------- Camps (P4-66) ----------------

        /// <summary>Every camp: where, why, how long, who sits at it (state, HP, fatigue), the watch, raids.</summary>
        public static string Camps()
        {
            var service = Services.Get<Adventurers.CampService>();
            var clock = Services.Get<GameClock>();
            var sb = new StringBuilder($"[{clock.FormatTime()}] camps={service.Camps.Count} drawn={service.MonstersDrawn} night={clock.IsNight}\n");
            foreach (var c in service.Camps)
            {
                sb.AppendLine($"  #{c.Id} {c.Reason} {Quests.QuestBoard.DescribeArea(c.Position)} {clock.TotalHours - c.PitchedAtHour:0.0}h " +
                              $"sitting={c.Sitting(clock.TotalHours)} watch={c.Watcher?.Data.name ?? "-"} alarmed={c.Alarmed} raids={c.Raids} talked={c.Talked}");
                foreach (var m in c.Members)
                    sb.AppendLine($"    {m.Data.name} {m.StateLabel} hp={m.HpRatio:P0} fat={m.Data.fatigue:0} → {c.ResumeTo[m]?.Label}");
            }
            return sb.ToString();
        }

        /// <summary>Makes camp right away where roster member <paramref name="index"/> stands, with the members within 4 cells.</summary>
        public static string CampNow(int index = 0, bool night = false)
        {
            var roster = Services.Get<GuildRoster>().Members;
            if (index < 0 || index >= roster.Count) return "no such member";
            var first = roster[index];
            var group = new List<Adventurer> { first };
            group.AddRange(roster.Where(m => m != first && m.IsAlive && Vector2.Distance(m.Position, first.Position) <= 4f));
            bool ok = Services.Get<Adventurers.CampService>().Pitch(group, night ? Adventurers.CampReason.Night : Adventurers.CampReason.Recover);
            return ok ? Camps() : "no spot to camp";
        }

        /// <summary>Nest-less monsters <paramref name="distance"/> cells from the first camp, left to notice the fire themselves.</summary>
        public static string SpawnNearCamp(string speciesId, int count, float distance = 9f)
        {
            var camp = Services.Get<Adventurers.CampService>().Camps.FirstOrDefault();
            if (camp == null) return "no camp";
            var species = Services.Get<EcosystemManager>().Species.FirstOrDefault(s => s.id == speciesId);
            if (species == null) return "unknown species: " + speciesId;
            var world = Services.Get<World.WorldService>();
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                var p = camp.Position + GameRandom.Shared.InsideUnitCircle().normalized * distance;
                if (!world.Grid.TryFindNearestWalkable(World.GridMap.WorldToCell(p), 3, out var c)) continue;
                Services.Get<PoolService>().Spawn<Monster>(Monster.PoolKey, World.GridMap.CellToWorld(c)).Setup(species, null);
                spawned++;
            }
            return $"spawned {spawned}";
        }

        // ---------------- Banter (P3-5) ----------------

        /// <summary>Gathers the first two idle members at the plaza and starts a banter right away.</summary>
        public static string ForceBanter()
        {
            var world = Services.Get<World.WorldService>();
            var idle = Services.Get<GuildRoster>().Members.Where(a => a.IsAlive && a.CurrentQuest == null).Take(2).ToList();
            if (idle.Count < 2) return "need 2 idle members";
            var spot = world.TownCenterWorld + new Vector2(2f, 0f);
            for (int i = 0; i < 2; i++)
            {
                var a = idle[i];
                a.Mover.Stop();
                a.transform.position = new Vector3(spot.x + i * 1.2f, spot.y, a.transform.position.z);
                a.ChangeState(a.IdleState);
            }
            return Services.Get<BanterService>().TryStart() ? $"started: {idle[0].Data.name}, {idle[1].Data.name}" : "not started (LLM busy?)";
        }

        /// <summary>Adds to how roster member #from feels about #to (indices into the roster).</summary>
        public static string Relate(int from, int to, int delta)
        {
            var m = Services.Get<GuildRoster>().Members;
            Relationships.Add(m[from], m[to], delta, "디버그");
            return $"{m[from].Data.name} → {m[to].Data.name}: {Relationships.Describe(Relationships.Get(m[from], m[to]))}";
        }

        public static string Relations()
        {
            var r = Services.Get<GuildRoster>().Members;
            var sb = new StringBuilder();
            foreach (var a in r)
            {
                sb.Append(a.Data.name).Append(": ");
                foreach (var b in r) if (a != b) sb.Append($"{b.Data.name} {Relationships.Describe(Relationships.Get(a, b))}  ");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>Deals damage to roster member #index (no attacker). For death checks.</summary>
        public static string Hurt(int index, float amount)
        {
            var a = Services.Get<GuildRoster>().Members[index];
            a.ReceiveDamage(amount, null, false);
            return $"{a.Data.name} hp={a.Hp:0} alive={a.IsAlive} state={a.StateLabel}";
        }

        // ---------------- Raids (P3-6) ----------------

        /// <summary>
        /// Makes raid-capable nest #index (see RaidNests) raid now. fill = grow it to capacity first.
        /// </summary>
        public static string ForceRaid(int index, bool fill = true)
        {
            var nests = Services.Get<EcosystemManager>().Nests.Where(n => n.Species.canRaid && !n.IsEmpty).ToList();
            if (index < 0 || index >= nests.Count) return "no such raid nest (count " + nests.Count + ")";
            var nest = nests[index];
            var eco = Services.Get<EcosystemManager>();
            if (fill) while (nest.Population < nest.Capacity) eco.Spawn(nest);
            int n = Services.Get<Town.WaveManager>().Launch(nest);
            return $"{nest.DisplayName} pop={nest.Population} → {n} raiders";
        }

        /// <summary>Sends a band of that raid nest (same numbering as <see cref="RaidNests"/>) off to a nearer site (P4-56).</summary>
        public static string ForceMigrate(int index, bool fill = true)
        {
            var eco = Services.Get<EcosystemManager>();
            var nests = eco.Nests.Where(n => n.Species.canRaid && !n.IsEmpty).ToList();
            if (index < 0 || index >= nests.Count) return "no such raid nest (count " + nests.Count + ")";
            var nest = nests[index];
            if (fill) while (nest.Population < nest.Capacity) eco.Spawn(nest);
            var svc = Services.Get<MigrationService>();
            var target = svc.PickTarget(nest, nest.Species);
            bool ok = svc.TryLaunch(nest);
            return ok ? $"{nest.DisplayName} → {target?.DisplayName} ({target?.Species.displayName} pop={target?.Population})" : $"{nest.DisplayName}: nowhere to go (or too many bands)";
        }

        /// <summary>Newcomers from beyond the map edge into an empty outer site (random kind unless given).</summary>
        public static string ForceArrival(string speciesId = null)
        {
            var eco = Services.Get<EcosystemManager>();
            var species = string.IsNullOrEmpty(speciesId) ? null : eco.Species.FirstOrDefault(s => s.id == speciesId);
            bool ok = Services.Get<MigrationService>().TryArrive(null, species);
            return ok ? MigrationStatus() : "no empty outer site (or two newcomer bands already on the way)";
        }

        /// <summary>Bands on the move: where from and to, how many, resting or not, average HP and distance left.</summary>
        public static string MigrationStatus()
        {
            var svc = Services.Get<MigrationService>();
            var sb = new StringBuilder($"bands={svc.Bands.Count}\n");
            foreach (var b in svc.Bands)
            {
                var alive = b.Members.Where(m => m != null && m.IsAlive).ToList();
                float left = alive.Count == 0 || b.Target == null ? 0f : alive.Min(m => Vector2.Distance(m.Position, b.Target.WorldPosition));
                sb.Append($"  #{b.Id} {(b.Arrival ? "[newcomers] " : "")}{b.Species.displayName} Lv{(alive.Count > 0 ? alive.Max(m => m.Level) : 0)} x{alive.Count} {b.From.DisplayName} → {b.Target?.DisplayName}({b.Target?.Species.displayName} {b.Target?.Population})"
                          + $" resting={b.Resting} hp={(alive.Count > 0 ? alive.Average(m => m.HpRatio) : 0f):0.00} left={left:0}"
                          + $" states={string.Join("/", alive.Select(m => m.StateLabel).Distinct())}\n");
            }
            return sb.ToString();
        }

        /// <summary>Raid-capable nests with population, danger level and whether they are out raiding.</summary>
        public static string RaidNests()
        {
            var w = Services.Get<Town.WaveManager>();
            var sb = new StringBuilder();
            int i = 0;
            foreach (var n in Services.Get<EcosystemManager>().Nests.Where(n => n.Species.canRaid && !n.IsEmpty))
                sb.AppendLine($"  #{i++} {n.DisplayName} pop={n.Population}/{n.Capacity} level={w.LevelOf(n)} raiding={w.IsRaiding(n)} dist={Vector2.Distance(n.WorldPosition, Services.Get<World.WorldService>().TownCenterWorld):0}");
            return sb.ToString();
        }

        public static string RaidStatus()
        {
            var w = Services.Get<Town.WaveManager>();
            var sb = new StringBuilder($"raids={w.RaidCount} active={w.RaidActive} sighted={w.RaidSighted} seenRaiders={w.Raiders.Count(m => m != null && m.IsAlive)}\n");
            foreach (var g in w.Raiders.Where(m => m != null && m.IsAlive).GroupBy(m => m.DisplayName))
                sb.Append($"  {g.Key} x{g.Count()} ({string.Join(",", g.Select(m => m.StateLabel).Distinct())})\n");
            var centre = Services.Get<World.WorldService>().TownCenterWorld;
            foreach (var f in w.Fronts)
                sb.Append($"  front raid#{f.RaidId} at {f.Point - centre} strength={f.Strength}\n");
            foreach (var b in Services.Get<Town.TownService>().Buildings)
                sb.Append($"  {b.DisplayName} {b.Hp:0}/{b.MaxHp:0}\n");
            foreach (var a in Services.Get<GuildRoster>().Members)
                sb.Append($"  {a.Data.name} {a.StateLabel} hp={a.Hp:0}/{a.Stats.MaxHp:0}"
                          + (a.Fsm.Current == a.DefendState && a.DefendState.Front is { } fp ? $" front={fp - centre}" : "")
                          + (a.CurrentTarget != null ? $" target={a.CurrentTarget.name}" : "") + "\n");
            return sb.ToString();
        }

        public static string BanterStatus()
        {
            var b = Services.Get<BanterService>();
            return $"attempts={b.Attempts} talks={b.Talks}";
        }

        // ---------------- Territories (P4-18) ----------------

        /// <summary>Every nest with population, territory radius and fighting strength.</summary>
        public static string Territories()
        {
            var eco = Services.Get<EcosystemManager>();
            var sb = new StringBuilder();
            int i = 0;
            foreach (var n in eco.Nests)
            {
                float str = n.Members.Where(m => m.IsAlive).Sum(TerritoryService.Strength);
                sb.Append($"  #{i++} {n.DisplayName} pop={n.Population}/{n.Capacity} r={n.TerritoryRadius:0.0} str={str:0} at {n.WorldPosition}\n");
            }
            return sb.ToString();
        }

        /// <summary>Roaming packs (P4-58): species, size, where they are, walking or resting.</summary>
        public static string Roamers()
        {
            var svc = Services.Get<RoamingPackService>();
            var clock = Services.Get<GameClock>();
            var sb = new StringBuilder();
            foreach (var p in svc.Packs)
                sb.Append($"  #{p.Id} {p.Species.id} x{p.Members.Count} at {p.Center} " +
                          (p.Moving ? $"walking to {p.Goal}" : $"resting {Mathf.Max(0f, p.RestUntilHour - clock.TotalHours):0.0}h") +
                          (p.Bound != null ? $" bound for {p.Bound.DisplayName} {Mathf.Max(0f, p.BoundUntilHour - clock.TotalHours):0}h" : "") +
                          $" town in {Mathf.Max(0f, p.DesperateAtHour - clock.TotalHours):0}h" +
                          $" states={string.Join("/", p.Members.Select(m => m.StateLabel))}\n");
            return sb.Length == 0 ? "no roaming packs" : sb.ToString();
        }

        /// <summary>The wilds' pressure now (P4-44): days, birth level by depth, newcomers' chance and packs on their way.</summary>
        public static string Wilds()
        {
            var eco = Services.Get<EcosystemManager>();
            float days = EcosystemManager.Days;
            var sb = new StringBuilder($"  day {days:0.0}: outermost +{WildsPressure.Bonus(days):0.0} levels, newcomers come for a held site {WildsPressure.InvasionChancePerHour(days) * 100f:0}%/h\n  birth level by depth:");
            foreach (float d in new[] { 0f, 0.5f, 1f, 1.5f, 2f })
                sb.Append($" {d:0.0}=Lv{WildsPressure.BirthLevel(d, days)}(was {MonsterLevel.BirthLevel(d)})");
            sb.AppendLine();
            var roaming = Services.Get<RoamingPackService>();
            sb.AppendLine($"  roaming packs {roaming.Packs.Count} (bound for a held site {roaming.Packs.Count(p => p.Bound != null)})");
            foreach (var g in eco.Nests.Where(n => !n.IsEmpty).GroupBy(n => Mathf.Clamp(Mathf.FloorToInt(eco.Depth(n.WorldPosition) * 2f), 0, 3)).OrderBy(g => g.Key))
            {
                var alive = g.SelectMany(n => n.Members).Where(m => m != null && m.IsAlive).ToList();
                sb.AppendLine($"  depth {g.Key / 2f:0.0}~{(g.Key + 1) / 2f:0.0}: {g.Count()} dens, {alive.Count} monsters, mean Lv{(alive.Count == 0 ? 0 : alive.Average(m => m.Level)):0.0}, born Lv{g.Min(n => n.BirthLevel)}~{g.Max(n => n.BirthLevel)}");
            }
            return sb.ToString();
        }

        /// <summary>Newcomers come for a held outer site now (P4-44): nest #index, or the best match when -1.</summary>
        public static string Invade(int index = -1)
        {
            var eco = Services.Get<EcosystemManager>();
            var target = index >= 0 && index < eco.Nests.Count ? eco.Nests[index] : null;
            var pack = Services.Get<MigrationService>().TryInvade(target);
            return pack == null ? "no site worth it" : $"pack #{pack.Id}: {pack.Members.Count} {pack.Species.id} Lv{pack.Members[0].Level} → {pack.Bound.DisplayName} ({pack.Bound.Population} {pack.Bound.Species.id})";
        }

        /// <summary>Roaming pack #id comes at the town now (P4-44: homeless too long).</summary>
        public static string Desperate(int id)
        {
            var pack = Services.Get<RoamingPackService>().Packs.FirstOrDefault(p => p.Id == id);
            if (pack == null) return "no such pack";
            pack.Bound = null;
            pack.DesperateAtHour = 0f;
            return $"pack #{id}: {pack.Members.Count} {pack.Species.id} will come at the town";
        }

        /// <summary>Sends nest #index's whole pack on the road as if driven out with nowhere to go (P4-58).</summary>
        public static string ExileNest(int index)
        {
            var eco = Services.Get<EcosystemManager>();
            if (index < 0 || index >= eco.Nests.Count) return "bad index";
            var nest = eco.Nests[index];
            var pack = Services.Get<RoamingPackService>().SendOnTheRoad(nest, null);
            if (pack == null) return "empty nest";
            if (Services.TryGet<CameraControl.CameraController>(out var cam)) { cam.Follow(null); cam.JumpTo(pack.Center); }
            return $"pack #{pack.Id}: {pack.Members.Count} {pack.Species.id} left {nest.DisplayName}, goal {(pack.Moving ? pack.Goal.ToString() : "-")}";
        }

        /// <summary>Every living pack boss (P4-57): name, level, tier, HP and nest.</summary>
        public static string Bosses()
        {
            var sb = new StringBuilder();
            foreach (var u in Units.UnitRegistry.All)
                if (u is Monster { IsBoss: true } m && m.IsAlive)
                    sb.Append($"  {m.DisplayName} Lv.{m.Level} {Units.CombatTiers.Label(m.Tier)} HP {m.Hp:0}/{m.Stats.MaxHp:0} {m.StateLabel} nest={(m.Home != null ? m.Home.DisplayName : "-")} at {m.Position}\n");
            return sb.Length == 0 ? "no bosses" : sb.ToString();
        }

        /// <summary>Adds a boss to nest #index (P4-57) and points the camera there.</summary>
        public static string SpawnBoss(int index)
        {
            var eco = Services.Get<EcosystemManager>();
            if (index < 0 || index >= eco.Nests.Count) return "bad index";
            var nest = eco.Nests[index];
            var boss = eco.Spawn(nest, null, true);
            if (Services.TryGet<CameraControl.CameraController>(out var cam)) { cam.Follow(null); cam.JumpTo(boss.Position); }
            return $"{boss.DisplayName} Lv.{boss.Level} HP {boss.Stats.MaxHp:0} in {nest.DisplayName}";
        }

        /// <summary>Spawns nest-less monsters of a species at the edge of nest #index's territory and points the camera there.</summary>
        public static string InvadeNest(int index, string speciesId, int count)
        {
            var eco = Services.Get<EcosystemManager>();
            if (index < 0 || index >= eco.Nests.Count) return "bad index";
            var nest = eco.Nests[index];
            var species = eco.Species.FirstOrDefault(s => s.id == speciesId);
            if (species == null) return "unknown species";
            var world = Services.Get<World.WorldService>();
            var pool = Services.Get<PoolService>();
            var edge = nest.WorldPosition + Vector2.right * Mathf.Max(1f, nest.TerritoryRadius * 0.8f);
            for (int k = 0; k < count; k++)
            {
                world.Grid.TryRandomWalkableNear(World.GridMap.WorldToCell(edge), 1, GameRandom.Shared, out var c);
                pool.Spawn<Monster>(Monster.PoolKey, World.GridMap.CellToWorld(c)).Setup(species, null);
            }
            if (Services.TryGet<CameraControl.CameraController>(out var cam)) { cam.Follow(null); cam.JumpTo(nest.WorldPosition); }
            SkirmishMonitor.Ensure().ResetStats();
            return $"{count} {species.id} into {nest.DisplayName} (r={nest.TerritoryRadius:0.0}, residents={nest.Population})";
        }

        // ---------------- Merchant (P4-3) ----------------

        /// <summary>Sends the travelling merchant now.</summary>
        public static string MerchantVisit()
            => Services.Get<Town.MerchantService>().StartVisit() ? "merchant on the way" : "merchant already in town";

        /// <summary>Visit state plus every stocked item's supply, demand and merchant price.</summary>
        public static string MerchantStatus()
        {
            var s = Services.Get<Town.MerchantService>();
            var clock = Services.Get<GameClock>();
            var t = Services.Get<GuildTreasury>();
            var db = Services.Get<Data.GameDatabase>();
            var sb = new StringBuilder();
            sb.Append($"[{clock.FormatTime()}] merchant={(s.Merchant != null ? s.Merchant.StateLabel : "none")} open={s.IsOpen} ");
            sb.Append($"closeAt={s.CloseAtHour:0.0} next={s.NextVisitHour:0.0} now={clock.TotalHours:0.0} gold={t.Gold}\n");
            foreach (var kv in t.Stock.Where(kv => kv.Value > 0))
            {
                var item = db.Item(kv.Key);
                sb.Append($"  {kv.Key} x{kv.Value} scarcity={s.Market.Scarcity(item):0.00} sold={s.Market.Sold(kv.Key):0.0} demand={s.Market.Demand(item):0.00} price={s.Market.Price(item)} base={item?.baseValue}\n");
            }
            return sb.ToString();
        }

        public static string MerchantSell(string itemId, int count)
        {
            var s = Services.Get<Town.MerchantService>();
            if (!s.IsOpen) return "stall closed";
            return $"sold for {s.Market.Sell(itemId, count)}G";
        }

        public static string MerchantClose()
        {
            Services.Get<Town.MerchantService>().CloseStall();
            return "closing";
        }

        public static string SkirmishReport()
            => SkirmishMonitor.Instance != null ? SkirmishMonitor.Instance.Report() : "no monitor (call SpawnSkirmish first)";

        /// <summary>Camera follows the busiest fight (<see cref="FightCamera"/>) on/off.</summary>
        public static string WatchFights(bool on)
        {
            if (on) FightCamera.Ensure(); else FightCamera.Stop();
            return $"watchFights={(on ? "on" : "off")}";
        }

        /// <summary>
        /// Testing only (user request 2026-09-23): gives the camera itself sight, so whatever it is
        /// pointed at is in view — monsters included — while the rest of the map keeps its fog. Use
        /// this to watch the simulation where no guild member is standing; F3 lifts the fog everywhere
        /// instead. Nothing registers it outside a debug session, so play is unaffected.
        /// </summary>
        public static string SeeAtCamera(bool on, float radius = 0f)
        {
            if (!on) { CameraVision.Stop(); return "seeAtCamera=off"; }
            var v = CameraVision.Ensure();
            v.Radius = radius;
            return $"seeAtCamera=on (radius {v.SightRadius:0.0}{(radius > 0f ? "" : ", 화면에 맞춤")})";
        }

        /// <summary>Melee footwork on/off (P4-47) for before/after comparisons with SkirmishReport.</summary>
        public static string Steering(bool on)
        {
            Units.LocalSteering.Enabled = on;
            return $"steering={(on ? "on" : "off")}";
        }

        /// <summary>Positions/states of living units within radius of the camera centre.</summary>
        public static string UnitsNearCamera(float radius = 12f)
        {
            var cam = Services.Get<CameraControl.CameraController>();
            Vector2 c = cam.transform.position;
            var sb = new StringBuilder();
            foreach (var u in Units.UnitRegistry.All)
            {
                if (!u.IsAlive || (u.Position - c).sqrMagnitude > radius * radius) continue;
                string state = u is Adventurer a ? a.StateLabel + (a.Engage.IsKiting ? "/kite" : "") : u is Monster m ? m.StateLabel : "-";
                sb.AppendLine($"  {u.DisplayName} ({u.Position.x:0.0},{u.Position.y:0.0}) hp={u.Hp:0}/{u.Stats.MaxHp:0} {state} tgt={u.CurrentTarget?.DisplayName}");
            }
            return sb.ToString();
        }

        /// <summary>Sends a small grammar-constrained request and logs latency/result.</summary>
        public static async void TestLlm()
        {
            var gateway = Services.Get<LLM.LlmGateway>();
            var grammar = new LLM.GbnfObject()
                .Enum("action", new[] { "accept_quest", "rest", "idle" })
                .String("say", 60)
                .Build();
            var result = await gateway.RequestAsync(new LLM.LlmRequest
            {
                System = "너는 판타지 게임의 모험가 NPC다. 반드시 JSON으로만 답한다.",
                User = "체력 30%, 피로 80. 고블린 토벌 의뢰(보상 50골드)가 있다. 어떻게 할래? say는 한국어 한 문장.",
                Grammar = grammar,
                MaxTokens = 80,
                Tag = "debug",
            });
            UnityEngine.Debug.Log($"[LLM TEST] ok={result.Ok} {result.Seconds:0.00}s err={result.Error} text={result.Text}");
        }

        // ---------------- Items on the ground (P4-21) ----------------

        /// <summary>Drops an item stack at (x, y); without a point, next to the guild master.</summary>
        public static string DropItem(string itemId, int count = 1, float x = -1f, float y = -1f)
        {
            var db = Services.Get<GameDatabase>();
            if (db.Item(itemId) == null) return "unknown item: " + itemId;
            var at = x < 0f || y < 0f ? Services.Get<Player.PlayerAvatar>().Position + new Vector2(1.5f, 0f) : new Vector2(x, y);
            Services.Get<Items.GroundItemService>().Drop(at, new[] { new ItemStack(itemId, count) });
            return $"dropped {itemId} x{count} at {at}";
        }

        /// <summary>Every item on the ground: id, count, position, age, who is walking to it.</summary>
        public static string GroundItems()
        {
            var ground = Services.Get<Items.GroundItemService>();
            var clock = Services.Get<GameClock>();
            var sb = new StringBuilder($"{ground.Items.Count} items\n");
            foreach (var g in ground.Items)
                sb.AppendLine($"  {g.Item.id} x{g.Count} at {g.Position} age {clock.TotalHours - g.DroppedAtHour:0.0}h reserved={(g.ReservedBy == null ? "-" : g.ReservedBy.DisplayName)}");
            return sb.ToString();
        }

        /// <summary>Monsters within radius of the camera that wear or swallowed something.</summary>
        public static string MonsterGear(float radius = 15f)
        {
            var cam = Services.Get<CameraControl.CameraController>().Camera.transform.position;
            var sb = new StringBuilder();
            foreach (var u in Units.UnitRegistry.All)
            {
                if (u is not Monster m || !m.IsAlive || Vector2.Distance(m.Position, cam) > radius) continue;
                if (m.Equipment.WornCount == 0 && m.Equipment.AbsorbedHp <= 0f) continue;
                sb.AppendLine($"  {m.DisplayName}({m.Home?.DisplayName ?? "-"}) worn=[{string.Join(",", m.Equipment.Worn.Select(i => i.id))}] absorbed={m.Equipment.AbsorbedHp:0} atk={m.Stats.Attack:0.#} def={m.Stats.Defense:0.#} hp={m.Hp:0}/{m.Stats.MaxHp:0} state={m.StateLabel}");
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }

        // ---------------- Observed gear intel (P4-22) ----------------

        /// <summary>What the guild knows about monster gear: per nest, plus the LLM threat notes.</summary>
        public static string GearIntel()
        {
            var intel = Services.Get<Ecosystem.GuildIntel>();
            var sb = new StringBuilder();
            foreach (var n in intel.KnownNests)
            {
                var gear = intel.GearAt(n);
                sb.AppendLine($"  {intel.NameOf(n)}: danger={Quests.QuestBoard.NestDanger(n)} " +
                              (gear.Any ? $"geared={gear.GearedCount} crown={gear.HasCrown} absorbed={gear.AbsorbedHp:0} raises={Ecosystem.GuildIntel.RaisesDanger(gear)} | {intel.DescribeGear(gear)}"
                                        : "no gear observed"));
            }
            var notes = intel.ThreatNotes();
            sb.AppendLine("  threat_notes: " + (notes.Count == 0 ? "(none)" : string.Join(" / ", notes)));
            return sb.ToString();
        }

        // ---------------- LLM health (P4-35) ----------------

        /// <summary>Gateway state: queue, counters and where recovery stands.</summary>
        public static string LlmStatus()
        {
            var g = Services.Get<LLM.LlmGateway>();
            return $"{g.StatusText}\n  enabled={g.Enabled} ready={g.IsReady} busy={g.IsBusy} queue={g.QueueCount}\n" +
                   $"  completed={g.Completed} failed={g.Failed} consecutiveFailures={g.ConsecutiveFailures}/{LLM.LlmGateway.FailuresBeforeRecovery}\n" +
                   $"  recovering={g.IsRecovering} attempts={g.RecoveryAttempts}/{LLM.LlmGateway.MaxRecoveryAttempts} gaveUp={g.GaveUpOnRecovery}\n" +
                   $"  backend={g.Backend.State} ({g.Backend.Description})\n" +
                   "  " + g.Stats.Summary(10).Replace("\n", "\n  ");
        }

        /// <summary>Restarts the LLM backend by hand (the same thing the gateway does on its own).</summary>
        public static string LlmRecover()
        {
            var g = Services.Get<LLM.LlmGateway>();
            g.TryRecoverNow();
            return "recovery requested; poll LlmStatus()";
        }

        // ---------------- Fog memory (P4-23) ----------------

        // ---------------- Player-placed buildings (P4-13) ----------------

        /// <summary>Builds a watchtower at a map point (defaults to next to the guild master).</summary>
        public static string BuildTower(float x = -1f, float y = -1f)
        {
            var construction = Services.Get<Town.ConstructionService>();
            var point = x < 0f || y < 0f ? Services.Get<Player.PlayerAvatar>().Position + new Vector2(3f, 0f) : new Vector2(x, y);
            return construction.TryBuild(Town.BuildingType.Watchtower, point, out var error)
                ? $"built at ({point.x:0.0}, {point.y:0.0})"
                : "failed: " + error;
        }

        /// <summary>Every player-placed building, with durability and what it sees.</summary>
        public static string Towers()
        {
            var town = Services.Get<Town.TownService>();
            var sb = new StringBuilder();
            foreach (var b in town.Buildings)
            {
                if (System.Array.IndexOf(Town.ConstructionService.Placeable, b.Type) < 0) continue;
                var vision = b.GetComponent<Fog.VisionSource>();
                sb.AppendLine($"  {b.DisplayName} at ({b.Center.x:0.0}, {b.Center.y:0.0}) hp={b.Hp:0}/{b.MaxHp:0} sight={(vision != null ? vision.SightRadius : 0f):0}");
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }

        /// <summary>Tears down the nearest player-placed building to a point (default: the master).</summary>
        public static string Demolish(float x = -1f, float y = -1f)
        {
            var construction = Services.Get<Town.ConstructionService>();
            var town = Services.Get<Town.TownService>();
            var point = x < 0f || y < 0f ? Services.Get<Player.PlayerAvatar>().Position : new Vector2(x, y);
            Town.Building best = null;
            float bestSq = float.MaxValue;
            foreach (var b in town.Buildings)
            {
                if (!construction.CanDemolish(b)) continue;
                float sq = ((Vector2)b.Center - point).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = b; }
            }
            if (best == null) return "nothing to demolish";
            string name = best.DisplayName;
            return construction.TryDemolish(best, out var error) ? "demolished " + name : "failed: " + error;
        }

        // ---------------- Town walls (P4-86) ----------------

        /// <summary>The walls now, the town's sight, the next step's cost and why it is refused; the real dens on its ground.</summary>
        public static string Walls()
        {
            var ex = Services.Get<Town.TownExpansion>();
            var map = Services.Get<World.WorldService>().Map;
            var sb = new StringBuilder($"stage {ex.Stage}/{Town.TownExpansionRules.MaxStage}, half {map.TownHalfSize} ({Town.TownExpansionRules.Width(map.TownHalfSize)} wide), " +
                                       $"rect {map.TownRect}, gates {string.Join(" ", map.Gates)}, town sight {ex.TownSight:0.0}\n");
            if (ex.AtMax) return sb.Append("at the last stage").ToString();
            var cost = ex.NextCost.Value;
            sb.Append($"next: half {ex.NextHalfSize}, {cost.Gold}G {string.Join(" ", cost.Items.Select(c => $"{c.itemId}x{c.count}"))}\n");
            sb.Append($"refused: {ex.CannotExpandReason() ?? "no"}\n");
            var c0 = map.TownCenter;
            foreach (var n in Services.Get<EcosystemManager>().Nests)
                if (!n.Retired && Town.TownExpansionRules.Crowds(n.Cell, c0, ex.NextHalfSize))
                    sb.AppendLine($"  den in the way: {n.DisplayName} at {n.Cell} pop {n.Population} discovered {n.Discovered}");
            return sb.ToString();
        }

        /// <summary>Pushes the walls out a step; <paramref name="free"/> hands over the gold and materials first (tests, measurements).</summary>
        public static string ExpandTown(bool free = true)
        {
            var ex = Services.Get<Town.TownExpansion>();
            if (free && ex.NextCost is { } cost)
            {
                var treasury = Services.Get<Economy.GuildTreasury>();
                treasury.AddGold(cost.Gold);
                foreach (var c in cost.Items) treasury.AddStock(c.itemId, c.count);
            }
            return ex.TryExpand(out var error) ? "expanded: " + Walls() : "failed: " + error;
        }

        // ---------------- Guild facilities (P4-87) ----------------

        /// <summary>What the facilities give now (members' scale, marks, stress relief in town) and each facility standing or fallen.</summary>
        public static string Facilities()
        {
            var f = Services.Get<Town.FacilityService>();
            var town = Services.Get<Town.TownService>();
            var roster = Services.Get<GuildRoster>();
            var k = f.MemberScale;
            var sb = new StringBuilder();
            sb.AppendLine($"  members x atk {k.Attack:0.###} def {k.Defense:0.###} hp {k.MaxHp:0.###} (power x{k.Mean:0.###}, base {Town.FacilityRules.BaseStatFactor})");
            sb.AppendLine($"  marks: poison {f.Poison:0.###} armor -{f.ArmorBreak:0.###} slow -{f.Slow:0.###} ({Town.FacilityRules.AfflictionSeconds}s) · stress relief in town x{f.StressRelief:0.##} · roster {roster.CountText}");
            foreach (var b in town.Buildings)
            {
                if (!Town.FacilityRules.IsFacility(b.Type)) continue;
                sb.AppendLine($"  {b.Type} Lv{b.Level} hp {b.Hp:0}/{b.MaxHp:0}{(b.IsDestroyed ? " FALLEN" : "")} weight {f.WeightOf(b)} at {b.Footprint.position}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Puts up a facility (debug): <paramref name="type"/> by enum name ("Armory"), raised to <paramref name="level"/>,
        /// paid for by the debug when <paramref name="free"/>. Without a point, the first spot inside the walls that takes it,
        /// from the plaza outwards.
        /// </summary>
        public static string BuildFacility(string type, int level = 1, bool free = true, float x = float.NaN, float y = float.NaN)
        {
            if (!System.Enum.TryParse<Town.BuildingType>(type, true, out var t) || !Town.FacilityRules.IsFacility(t)) return "not a facility: " + type;
            var construction = Services.Get<Town.ConstructionService>();
            var upgrades = Services.Get<Town.UpgradeService>();
            var town = Services.Get<Town.TownService>();
            var treasury = Services.Get<Economy.GuildTreasury>();
            var world = Services.Get<World.WorldService>();
            void Pay(Data.BuildingLevel cost)
            {
                if (!free || cost == null) return;
                treasury.AddGold(cost.goldCost);
                foreach (var c in cost.itemCosts) treasury.AddStock(c.itemId, c.count);
            }
            Vector2 point;
            if (!float.IsNaN(x)) point = new Vector2(x, y);
            else
            {
                var centre = world.Map.TownCenter;
                int reach = world.Map.TownRect.width / 2;
                Vector2? found = null;
                for (int r = 2; r <= reach && found == null; r++)
                for (int dy = -r; dy <= r && found == null; dy++)
                for (int dx = -r; dx <= r && found == null; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    var p = World.GridMap.CellToWorld(centre + new Vector2Int(dx, dy));
                    if (construction.CannotPlaceAt(t, p) == null) found = p;
                }
                if (found == null) return "no room inside the walls";
                point = found.Value;
            }
            Pay(construction.PriceToBuild(t));
            if (!construction.TryBuild(t, point, out var error)) return "not built: " + error;
            var built = town.Buildings.Last(b => b.Type == t);
            while (built.Level < level)
            {
                Pay(upgrades.NextLevel(built));
                if (!upgrades.TryUpgrade(built, out error)) return $"built at {built.Footprint.position}, stuck at Lv{built.Level}: {error}";
            }
            return $"{t} Lv{built.Level} at {built.Footprint.position}\n" + Facilities();
        }

        static TrialRunner s_trial;

        /// <summary>
        /// Trial fights for tuning (P4-87, <see cref="TrialRunner"/>): <paramref name="team"/> as class letters — W warrior, R ranger,
        /// P priest, M mage ("WRPM" is one of each, "W" one warrior) — fresh, no traits, at <paramref name="memberLevel"/> and
        /// <paramref name="gear"/>, against <paramref name="pack"/> fresh <paramref name="species"/> of <paramref name="level"/>,
        /// <paramref name="rounds"/> times at clock speed index <paramref name="speed"/>. Read with <see cref="TrialReport"/>.
        /// </summary>
        public static string Trial(string species, int pack, int level, string team, int memberLevel, int gear, int rounds = 5, int speed = 3)
        {
            if (s_trial != null && s_trial.Running) return "a trial is running: " + s_trial.Report();
            var classes = team.ToUpperInvariant().Select(c => c switch
            {
                'W' => AdventurerClass.Warrior,
                'R' => AdventurerClass.Ranger,
                'P' => AdventurerClass.Priest,
                _ => AdventurerClass.Mage,
            }).ToArray();
            if (s_trial != null) Object.Destroy(s_trial.gameObject);
            s_trial = TrialRunner.Start(species, pack, level, classes, memberLevel, gear, rounds, speed);
            return "started: " + s_trial.Label;
        }

        public static string TrialReport() => s_trial == null ? "no trial" : s_trial.Report();

        /// <summary>Last-seen ghosts standing under the fog right now.</summary>
        public static string FogGhosts()
        {
            var svc = Services.Get<Fog.FogGhostService>();
            var sb = new StringBuilder();
            sb.AppendLine($"  ghosts={svc.Ghosts.Count}/{Fog.FogGhostService.MaxGhosts}");
            foreach (var g in svc.Ghosts)
            {
                if (g == null || !g.IsActive) continue;
                string hp = g.HpRatio >= 0f ? $" hp={g.HpRatio * 100f:0}%" : "";
                sb.AppendLine($"  {g.Label} at ({g.Position.x:0.0}, {g.Position.y:0.0}){hp} seen={svc.AgoText(g.SeenAtHour)}");
            }
            return sb.ToString();
        }

        // ---------- P5-1 balance runs ----------

        /// <summary>
        /// Starts a metered run: samples the economy/roster/ecosystem every game hour and, unless
        /// <paramref name="autoPost"/> is false, keeps hunt quests on the board in the player's place.
        /// Writes a CSV under the project root when it finishes (or on BalanceStop).
        /// </summary>
        /// <summary>The ecosystem with no guild (members off the map, town unbreakable): a snapshot per day. Read with <see cref="WildlifeReport"/>.</summary>
        public static string WildlifeRun(int days = 20, int speed = 3)
        {
            var probe = WildlifeProbe.Ensure();
            if (probe.Running) return "already running";
            probe.Begin(days);
            Services.Get<GameClock>().SetSpeed(speed);
            return $"wildlife run started: {days} game days";
        }

        public static string WildlifeReport() => WildlifeProbe.Instance == null ? "not started" : WildlifeProbe.Instance.Report();

        public static string BalanceRun(int days = 10, bool autoPost = true, int speed = 3, string label = null)
        {
            var probe = BalanceProbe.Ensure();
            if (probe.Running) return "already running: " + probe.Report();
            string name = string.IsNullOrEmpty(label) ? "baseline" : label;
            string path = System.IO.Path.Combine(Application.dataPath, "..", "BalanceLogs", $"{name}.csv");
            path = System.IO.Path.GetFullPath(path);
            probe.Begin(days, path);
            if (autoPost) BalanceAutoPlayer.Ensure().Begin();
            AnomalyWatch.Ensure();
            Services.Get<GameClock>().SetSpeed(speed);
            return $"balance run '{name}' started: {days} game days, autoPost={autoPost}, speed=x{GameClock.SpeedScales[Mathf.Clamp(speed, 0, GameClock.SpeedScales.Length - 1)]:0}\n  csv: {path}";
        }

        /// <summary>Ends the run early and writes the CSV.</summary>
        public static string BalanceStop()
        {
            if (BalanceAutoPlayer.Instance != null) BalanceAutoPlayer.Instance.Stop();
            if (BalanceProbe.Instance == null) return "no run";
            BalanceProbe.Instance.Stop();
            return BalanceProbe.Instance.Report();
        }

        /// <summary>Where the run stands right now (it keeps sampling).</summary>
        /// <summary>What <see cref="AnomalyWatch"/> found (stuck members, unworked quests); starts watching if it was not.</summary>
        public static string Anomalies() => AnomalyWatch.Ensure().Summary();

        public static string BalanceReport()
        {
            if (BalanceProbe.Instance == null) return "no run";
            var sb = new StringBuilder(BalanceProbe.Instance.Report());
            if (BalanceAutoPlayer.Instance != null) sb.AppendLine(BalanceAutoPlayer.Instance.Report());
            return sb.ToString();
        }

        // ---------------- Routes (P4-63) ----------------

        /// <summary>
        /// Compares the walk from the guild door to a point at each nerve (P4-63): how long it is and
        /// how much of it crosses cover. Use it to see a cautious party's detour against a hurried
        /// one's shortcut on the map at hand.
        /// </summary>
        public static string RouteReport(float x, float y)
        {
            var world = Services.Get<GuildMaster.World.WorldService>();
            var from = Services.TryGet<TownService>(out var town) && town.Get(BuildingType.GuildHall) is { } hall ? hall.DoorPosition : world.TownCenterWorld;
            var to = new Vector2(x, y);
            var sb = new StringBuilder($"route ({from.x:0},{from.y:0}) -> ({to.x:0},{to.y:0})\n");
            var cells = new List<Vector2Int>();
            foreach (var (name, caution) in new[]
                     {
                         ("급함/무시 ", RouteChoice.Hurried),
                         ("용감함    ", RouteChoice.FromNerve(-0.5f)),
                         ("보통      ", RouteChoice.Normal),
                         ("겁많음    ", RouteChoice.FromNerve(1f)),
                         ("숲 서식종 ", RouteChoice.FromCover),
                     })
            {
                if (!world.FindPath(from, to, cells, true, null, caution)) { sb.AppendLine($"  {name} 경로 없음"); continue; }
                float length = 0f, risk = 0f;
                int forest = 0;
                for (int i = 1; i < cells.Count; i++)
                {
                    var c = cells[i];
                    length += (cells[i] - cells[i - 1]).sqrMagnitude > 1 ? 1.41f : 1f;
                    risk += world.Risk.At(c.x, c.y);
                    if (world.Grid.IsForest(c)) forest++;
                }
                sb.AppendLine($"  {name} caution={caution:0.00} 길이={length:0.0}칸 숲={forest}칸 위험합={risk:0.0}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// How the free hunts are finding their way (P4-64): each member out exploring, the spot it
        /// is headed for, the leg it is walking now, and how often the ground it discovered made it
        /// redraw the route.
        /// </summary>
        public static string ExploreReport()
        {
            var sb = new StringBuilder();
            var fog = Services.TryGet<GuildMaster.Fog.FogOfWar>(out var f) ? f : null;
            var world = Services.Get<GuildMaster.World.WorldService>();
            if (fog != null)
                sb.AppendLine($"탐색된 칸 {fog.ExploredCount} / {world.Map.Width * world.Map.Height} ({100f * fog.ExploredCount / (world.Map.Width * world.Map.Height):0.0}%)");
            var bands = Services.TryGet<HuntBandService>(out var hb) ? hb : null;
            int n = 0;
            foreach (var a in Services.Get<GuildRoster>().Members)
            {
                if (a.Fsm.Current != a.FreeHuntState) continue;
                n++;
                var st = a.FreeHuntState;
                var band = bands?.BandOf(a);
                string spot = band is { HasSpot: true } ? $"({band.Spot.x:0},{band.Spot.y:0})" : "-";
                sb.AppendLine($"  {a.Data.name} {(st.Scouting ? "정찰" : "둥지")} 목표={spot} 구간=({st.Leg.x:0},{st.Leg.y:0}) 구간수={st.LegsWalked} 놀람={st.Surprises} 경로수정={a.Mover.RouteCorrections} 주변탐색={(fog == null ? 0f : fog.ExploredFraction(a.Position, 12f)) * 100f:0}%");
            }
            if (n == 0) sb.AppendLine("  자율 사냥 중인 길드원이 없습니다");
            return sb.ToString();
        }

        // ---------------- Performance (P5-2) ----------------

        /// <summary>
        /// Load test (P5-2): <paramref name="total"/> monsters, half goblins and half skulls (hostile
        /// species with archer variants), spawned 8 cells apart about <paramref name="distance"/> cells
        /// out of town so they fight each other; the camera watches. <paramref name="projectiles"/>
        /// harmless arrows are kept in flight between live units. Read with <see cref="PerfReport"/>.
        /// </summary>
        public static string PerfStress(int total = 200, int projectiles = 100, float distance = 40f)
        {
            var eco = Services.Get<EcosystemManager>();
            var a = eco.Species.FirstOrDefault(s => s.id == "goblin");
            var b = eco.Species.FirstOrDefault(s => s.id == "skull");
            if (a == null || b == null) return "species missing";
            var world = Services.Get<World.WorldService>();
            var pool = Services.Get<PoolService>();
            var rng = GameRandom.Shared;
            var center = world.TownCenterWorld;
            Vector2 site = default;
            bool found = false;
            foreach (var d in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
            {
                var cell = World.GridMap.WorldToCell(center + d * distance);
                if (!world.Grid.TryFindNearestWalkable(cell, 8, out cell) || world.Grid.IsInTown(cell)) continue;
                site = World.GridMap.CellToWorld(cell);
                found = true;
                break;
            }
            if (!found) return "no site";
            int spawned = 0;
            for (int i = 0; i < total; i++)
            {
                var origin = World.GridMap.WorldToCell(site + new Vector2(i % 2 == 0 ? -4f : 4f, 0f));
                if (!world.Grid.TryRandomWalkableNear(origin, 4, rng, out var c)) continue;
                var m = pool.Spawn<Monster>(Monster.PoolKey, World.GridMap.CellToWorld(c) + rng.InsideUnitCircle() * 0.3f);
                m.Setup(i % 2 == 0 ? a : b, null);
                spawned++;
            }
            if (Services.TryGet<CameraControl.CameraController>(out var cam))
            {
                cam.Follow(null);
                cam.JumpTo(site);
            }
            var probe = PerfProbe.Ensure();
            probe.ProjectileFloor = projectiles;
            probe.ResetStats();
            return $"stress: {spawned} monsters at {site}, projectile floor {projectiles}";
        }

        /// <summary>Starts (or restarts) frame sampling without spawning anything.</summary>
        public static string PerfStart(int projectiles = 0)
        {
            var probe = PerfProbe.Ensure();
            probe.ProjectileFloor = projectiles;
            probe.ResetStats();
            return "sampling";
        }

        public static string PerfReport() => PerfProbe.Instance == null ? "not started" : PerfProbe.Instance.Report();

        public static string PerfStop()
        {
            if (PerfProbe.Instance != null) Object.Destroy(PerfProbe.Instance.gameObject);
            return "stopped";
        }

        // ---------------- Save/load (P5-4) ----------------

        public static string Save(int slot)
            => Services.Get<GuildMaster.Save.SaveService>().Save(slot, out var error) ? $"saved to {GuildMaster.Save.SaveService.PathOf(slot)}" : error;

        /// <summary>Loads a slot (the scene restarts; in an empty test scene use the test's own restart).</summary>
        public static string Load(int slot) => GuildMaster.Save.SaveService.Load(slot, out var error) ? "loading" : error;

        public static string Slots()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < GuildMaster.Save.SaveService.SlotCount; i++)
            {
                var info = GuildMaster.Save.SaveService.Peek(i);
                sb.AppendLine(!info.Exists ? $"{i}: (비어 있음)"
                    : info.Problem != null ? $"{i}: {info.Problem}"
                    : $"{i}: {info.Summary.day}일차 {info.Summary.hour}시 · {info.Summary.gold}G · 길드원 {info.Summary.members} · {info.SavedAt:yyyy-MM-dd HH:mm}");
            }
            return sb.ToString();
        }

        /// <summary>Section sizes of the current game's save (nothing written).</summary>
        public static string SaveSections()
        {
            var file = Services.Get<GuildMaster.Save.SaveService>().Capture();
            var sb = new StringBuilder();
            foreach (var s in file.sections) sb.AppendLine($"{s.key}: {s.json.Length}자");
            return sb.ToString();
        }

        // ---------------- Map 512 (P4-81) ----------------

        /// <summary>Ground chunks painted where the fog lifted, still waiting, and in the map; nests by depth.</summary>
        public static string GroundStatus()
        {
            var sb = new StringBuilder();
            if (Services.TryGet<World.MapRenderer>(out var r))
                sb.AppendLine($"땅 덩어리: 그림 {r.GroundChunksPainted} · 대기 {r.GroundChunksWaiting} · 전체 {r.GroundChunksTotal}");
            if (Services.TryGet<Fog.FogOfWar>(out var fog))
                sb.AppendLine($"탐색된 칸 {fog.ExploredCount} / {fog.Width * fog.Height}");
            var eco = Services.Get<EcosystemManager>();
            var nests = eco.Nests.ToList();
            int inner = nests.Count(n => eco.Depth(n.WorldPosition) <= 1f);
            sb.AppendLine($"둥지 {nests.Count}곳: 안쪽(깊이 ≤1) {inner} · 바깥 {nests.Count - inner}, 가장 먼 둥지 {eco.OutermostDistance:0}칸, " +
                          $"태어남 레벨 {nests.Min(n => n.BirthLevel)}~{nests.Max(n => n.BirthLevel)}");
            return sb.ToString();
        }

        /// <summary>Path searches that gave up at the node cap: by caller, and the latest ones (P4-81).</summary>
        public static string PathFailures()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"searches {World.Pathfinder.Searches} · failed {World.Pathfinder.FailedSearches} · expanded {World.Pathfinder.TotalExpanded} (failed {World.Pathfinder.FailedExpanded})");
            foreach (var kv in World.Pathfinder.CappedByCaller.OrderByDescending(kv => kv.Value))
                sb.AppendLine($"  {kv.Value}× {kv.Key}");
            foreach (var c in World.Pathfinder.RecentCapped)
                sb.AppendLine($"  {c.Start} → {c.Goal} ({(c.Goal - c.Start).magnitude:0}칸{(c.Exploring ? ", 탐험" : "")}{(c.PassesGates ? "" : ", 몬스터")}) {c.Caller}");
            return sb.ToString();
        }

        public static string ResetPathStats()
        {
            World.Pathfinder.Searches = World.Pathfinder.FailedSearches = World.Pathfinder.TotalExpanded = World.Pathfinder.FailedExpanded = 0;
            World.Pathfinder.CappedByCaller.Clear();
            World.Pathfinder.RecentCapped.Clear();
            return "reset";
        }

        // ---------------- The dragon (P4-93) ----------------

        /// <summary>The dragon as it really is: lair, health, state, its round, the next day it wakes.</summary>
        public static string Dragon() => Services.TryGet<Dragons.DragonService>(out var d) ? d.Describe() : "no dragon service";

        /// <summary>Wakes it now for a flight of <paramref name="hours"/> on today's round.</summary>
        public static string DragonWake(float hours = 8f)
        {
            Services.Get<Dragons.DragonService>().Wake(Services.Get<GameClock>().Day, hours);
            return Dragon();
        }

        /// <summary>Sends it flying straight to (x, y) — our town when left out — then home.</summary>
        public static string DragonTo(float x = -1f, float y = -1f, float hours = 4f)
        {
            var point = x < 0f ? Services.Get<World.WorldService>().TownCenterWorld : new Vector2(x, y);
            Services.Get<Dragons.DragonService>().FlyTo(point, hours);
            return Dragon();
        }

        /// <summary>Camera to the dragon (or its lair), the fog lifted round it.</summary>
        public static string JumpToDragon()
        {
            var d = Services.Get<Dragons.DragonService>();
            var at = d.Dragon != null ? d.Dragon.Position : d.Lair;
            Services.Get<Fog.FogOfWar>().Reveal(at, 14f);
            Services.Get<CameraControl.CameraController>().JumpTo(at);
            return Dragon();
        }

        public static string KillDragon(int memberIndex = 0)
        {
            var d = Services.Get<Dragons.DragonService>();
            if (d.Dragon == null) return "no living dragon";
            var members = Services.Get<GuildRoster>().Members;
            var by = memberIndex >= 0 && memberIndex < members.Count ? members[memberIndex] : null;
            d.Dragon.ReceiveDamage(d.Dragon.Hp + 1e6f, by, false);
            return Dragon();
        }

        /// <summary>
        /// A test fight against the dragon asleep at home (debug, tuning): <paramref name="members"/> members (recruited up to it)
        /// set to <paramref name="level"/> and gear tier <paramref name="gear"/>, put on a hunt for it a few cells from its lair, at
        /// midday. Read the fight with <see cref="Dragon"/>.
        /// </summary>
        public static string DragonFight(int members, int level, int gear)
        {
            var svc = Services.Get<Dragons.DragonService>();
            var dragon = svc.Dragon;
            if (dragon == null) return "no living dragon";
            var clock = Services.Get<GameClock>();
            typeof(GameClock).GetField("_totalHours", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(clock, Mathf.Floor(clock.TotalHours / 24f) * 24f + 9f);
            var roster = Services.Get<GuildRoster>();
            var world = Services.Get<World.WorldService>();
            while (roster.Members.Count < members) roster.Recruit(AdventurerGenerator.Generate(GameRandom.Shared), world.TownCenterWorld);
            Services.Get<GuildIntel>().MarkKnown(dragon.Def);
            var board = Services.Get<QuestBoard>();
            board.TryPostRequest(new QuestDraft
            {
                Type = QuestType.Hunt, Species = dragon.Def, Count = 1, Point = svc.Lair, HasPoint = true, Radius = QuestBoard.HuntRadius,
                RewardGold = 1500, RewardExp = 900, MaxParty = members,
            }, QuestClient.Town, out var q, out var err);
            if (q == null) return "not posted: " + err;
            var stand = svc.Lair + (world.TownCenterWorld - svc.Lair).normalized * 10f;
            var team = roster.Members.Where(a => a.IsAlive).OrderBy(a => roster.Members.Where(b => b.Data.cls == a.Data.cls).ToList().IndexOf(a)).Take(members).ToList();
            for (int i = 0; i < team.Count; i++)
            {
                var a = team[i];
                a.Data.level = level;
                a.Data.gearTier = gear;
                a.Data.potions = 3;
                a.Data.flares = GuildMaster.Flares.FlareRules.Carry;
                a.RecalculateStats(fullHeal: true);
                world.Grid.TryFindNearestWalkable(World.GridMap.WorldToCell(stand + new Vector2(i % 3, i / 3)), 4, out var cell);
                a.Mover.Stop();
                a.transform.position = World.GridMap.CellToWorld(cell);
            }
            AcceptAsParty(board, q, team);
            Services.Get<Fog.FogOfWar>().Reveal(svc.Lair, 20f);
            Services.Get<CameraControl.CameraController>().JumpTo(svc.Lair);
            return $"{q.Id}: {string.Join(", ", team.Select(a => $"{a.Data.name}({a.Data.cls} {a.Stats.MaxHp:0}hp {a.Stats.Attack:0.#}atk)"))} vs {dragon.DisplayName} {dragon.Hp:0}hp";
        }

        // ---------------- Flares (P4-94) ----------------

        /// <summary>Burning flares, flares shot so far, what each member carries and the stock.</summary>
        public static string Flares() => Services.TryGet<GuildMaster.Flares.FlareService>(out var f) ? f.Describe() : "no flare service";

        /// <summary>Every living member carries <paramref name="count"/> flares (and the stock gets as many more).</summary>
        public static string GiveFlares(int count = 2)
        {
            foreach (var a in Services.Get<GuildRoster>().Members) if (a.IsAlive) a.Data.flares = count;
            Services.Get<GuildTreasury>().AddStock(GuildMaster.Flares.FlareRules.ItemId, count);
            return Flares();
        }

        /// <summary>Shoots one over (x, y) — the camera's centre when left out — from nobody.</summary>
        public static string FireFlare(float x = -1f, float y = -1f)
        {
            var at = x < 0f ? (Vector2)Camera.main.transform.position : new Vector2(x, y);
            Services.Get<GuildMaster.Flares.FlareService>().Fire(null, at);
            return Flares();
        }

        // ---------------- Landforms (P4-101) ----------------

        static string PassLabel(World.PassKind k) => k switch { World.PassKind.Bridge => "다리", World.PassKind.Ford => "여울", _ => "숲길" };

        /// <summary>
        /// P4-102: this game's zones, real values — name, region, lie of the land, danger shift, distance, known to the guild,
        /// road, landmark, dens and the kinds holding them, the birth level at the heart now.
        /// </summary>
        public static string Zones()
        {
            var world = Services.Get<World.WorldService>();
            var map = world.Map;
            var known = Services.TryGet<World.ZoneDiscovery>(out var zd) ? zd : null;
            var eco = Services.TryGet<EcosystemManager>(out var e) ? e : null;
            var sb = new StringBuilder();
            foreach (var z in map.Zones.OrderBy(z => Vector2.Distance(z.Centre, map.TownCenter)))
            {
                var dens = eco == null ? new List<Nest>() : eco.Nests.Where(n => map.ZoneAt(n.Cell.x, n.Cell.y) == z).ToList();
                string kinds = string.Join(",", dens.Where(n => n.Species != null).GroupBy(n => n.Species.displayName).Select(g => $"{g.Key}{g.Count()}"));
                int level = eco != null ? eco.BirthLevelAt(World.GridMap.CellToWorld(z.Heart)) : 0;
                sb.AppendLine($"{z.Index} {z.Name} [{World.TerrainRegions.Label(z.Region)}·{z.Character}·{z.Kind}] {Vector2.Distance(z.Centre, map.TownCenter):0}칸 위험 {z.DangerShift:+0.00;-0.00} 태어남 Lv{level}"
                              + $"{(known != null && known.IsKnown(z) ? " · 앎" : "")}{(z.OnRoad ? " · 길" : "")}{(z.Landmark ? " · 랜드마크" : "")} · 둥지 {dens.Count} {kinds}");
            }
            sb.AppendLine($"마을 터 {map.SettlementSites.Count}곳");
            return sb.ToString();
        }

        /// <summary>The ways across the region barriers in this game: kind, borders, distance, road, the den holding it (real values).</summary>
        public static string Passes()
        {
            var world = Services.Get<World.WorldService>();
            var eco = Services.TryGet<EcosystemManager>(out var e) ? e : null;
            var sb = new StringBuilder();
            var town = world.Map.TownCenter;
            foreach (var p in world.Map.Passes.OrderBy(p => (p.Cell - town).sqrMagnitude))
            {
                string guard = "";
                if (p.GuardNest >= 0 && eco != null && p.GuardNest < eco.Nests.Count)
                {
                    var n = eco.Nests[p.GuardNest];
                    guard = $" · 지킴 {n.NestId} {(n.Species != null ? n.Species.displayName : "빈 둥지")} {n.Population}마리";
                }
                string border = p.A == p.B ? $"{World.TerrainRegions.Label(p.A)} 안" : $"{World.TerrainRegions.Label(p.A)}–{World.TerrainRegions.Label(p.B)}";
                sb.AppendLine($"{PassLabel(p.Kind)} {border} ({p.Cell.x}, {p.Cell.y}) {Vector2.Distance(p.Cell, town):0}칸{(p.OnRoad ? " · 길" : "")}{guard}");
            }
            sb.AppendLine($"모두 {world.Map.Passes.Count}곳, 지킴 둥지 {world.Map.Passes.Count(p => p.GuardNest >= 0)}");
            return sb.ToString();
        }

        /// <summary>
        /// Makes the map for <paramref name="seed"/> (the game's settings; works outside play) and saves a picture of it to
        /// <paramref name="file"/>: regions, ridges, rivers, landforms, dens (red, holding a pass: orange), ways across
        /// (magenta, cuts within a region: cyan). Reports the ground, the ways per border and how much the walk to a den bends.
        /// </summary>
        public static string MapPreview(int seed, string file, bool landforms = true, bool paths = true)
        {
            var balance = ScriptableObject.CreateInstance<GameBalance>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var map = World.MapGenerator.Generate(new World.MapGenSettings
            {
                Width = balance.mapWidth, Height = balance.mapHeight, Seed = seed, TownHalfSize = balance.townHalfSize,
                NestCount = balance.nestCount, MinNestDistanceFromTown = balance.minNestDistanceFromTown,
                InnerHalfSize = balance.innerHalfSize, InnerNestCount = balance.innerNestCount, Landforms = balance.landforms && landforms,
            });
            long ms = watch.ElapsedMilliseconds;
            Object.DestroyImmediate(balance);
            var sb = new StringBuilder($"시드 {seed}: 만들기 {ms}ms\n");
            foreach (var g in map.Tiles.GroupBy(t => t).OrderBy(g => g.Key))
                sb.Append($"{g.Key} {100f * g.Count() / map.Tiles.Length:0.0}% ");
            var town = map.TownCenter;
            var dist = World.MapGenerator.FloodDistances(map, town);
            // Steps over a walk with no obstacle (4 directions: |dx| + |dy|); 1 = straight.
            float Bend(Vector2Int c) => (float)dist[map.Index(c.x, c.y)] / (Mathf.Abs(c.x - town.x) + Mathf.Abs(c.y - town.y));
            var bends = map.Nests.Select(n => Bend(n.Cell)).ToList();
            sb.Append($"\n둥지 {map.Nests.Count}곳까지 걸음/직선: 평균 {bends.Average():0.000} · 최대 {bends.Max():0.000} · 1.2 넘는 둥지 {bends.Count(b => b > 1.2f)}");
            foreach (var g in map.Passes.GroupBy(p => p.A == p.B ? $"{World.TerrainRegions.Label(p.A)} 안" : $"{World.TerrainRegions.Label(p.A)}–{World.TerrainRegions.Label(p.B)}"))
                sb.Append($"\n  {g.Key}: {string.Join(", ", g.Select(p => PassLabel(p.Kind) + (p.OnRoad ? "(길)" : "") + (p.GuardNest >= 0 ? "(지킴)" : "")))}");

            var tex = new Texture2D(map.Width, map.SurfaceHeight);
            var rock = new Color32(0x5A, 0x55, 0x50, 255);
            var road = new Color32(0x9A, 0x6A, 0x3A, 255);
            for (int y = 0; y < map.SurfaceHeight; y++)
            for (int x = 0; x < map.Width; x++)
            {
                var pal = World.CartoonGroundPainter.PaletteOf(map.Regions[map.Index(x, y)]);
                tex.SetPixel(x, y, map[x, y] switch
                {
                    World.TileType.Forest => map.Deep[map.Index(x, y)] ? Color.Lerp(pal.ForestEdge, new Color(0.05f, 0.18f, 0.08f), 0.55f) : (Color)pal.ForestEdge,
                    World.TileType.Sand => pal.Sand,
                    World.TileType.Water => pal.Water,
                    World.TileType.Mountain => rock,
                    World.TileType.Road => road,
                    World.TileType.Grass => pal.Grass,
                    _ => Color.white,
                });
            }
            // Zone borders (P4-102) as a faint dark line; pockets of danger tinted red, havens blue.
            for (int y = 0; y < map.SurfaceHeight - 1; y++)
            for (int x = 0; x < map.Width - 1; x++)
            {
                int i = map.Index(x, y);
                var zone = map.ZoneAt(x, y);
                if (zone != null && zone.Kind != World.ZoneKind.Plain)
                    tex.SetPixel(x, y, Color.Lerp(tex.GetPixel(x, y), zone.Kind == World.ZoneKind.Pocket ? Color.red : Color.cyan, 0.22f));
                if (map.ZoneIds[i] == map.ZoneIds[i + 1] && map.ZoneIds[i] == map.ZoneIds[i + map.Width]) continue;
                tex.SetPixel(x, y, Color.Lerp(tex.GetPixel(x, y), Color.black, 0.35f));
            }
            var shares = map.Zones.GroupBy(z => z.Region).OrderBy(g => g.Key)
                .Select(g => $"{World.TerrainRegions.Label(g.Key)} {g.Count()}곳 {100f * g.Sum(z => z.Cells) / (map.Width * map.SurfaceHeight):0}%");
            sb.Append($"\n구역 {map.Zones.Count}: {string.Join(" · ", shares)}");
            foreach (var z in map.Zones.Where(z => !string.IsNullOrEmpty(z.Name)))
                sb.Append($"\n  {z.Index} {z.Name} ({World.TerrainRegions.Label(z.Region)}, {Vector2.Distance(z.Centre, town):0}칸, 위험 {z.DangerShift:+0.00;-0.00}{(z.Kind != World.ZoneKind.Plain ? ", " + z.Kind : "")}{(z.Landmark ? ", 랜드마크" : "")}{(z.OnRoad ? ", 길" : "")})");
            void Mark(Vector2Int c, int r, Color col, bool ring)
            {
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if ((!ring || Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) == r) && map.InBounds(c.x + dx, c.y + dy)) tex.SetPixel(c.x + dx, c.y + dy, col);
            }
            // The walk from the town to every den, as the pathfinder plans it knowing the ground (black), and what it cost.
            var world = new World.WorldService(map);
            var path = new List<Vector2Int>();
            int failed = 0;
            var expanded = new List<int>();
            float walked = 0f, straight = 0f;
            foreach (var n in map.Nests)
            {
                if (!world.Pathfinder.FindPath(town, n.Cell, path)) { failed++; continue; }
                expanded.Add(world.Pathfinder.LastExpanded);
                for (int i = 1; i < path.Count; i++) walked += Vector2Int.Distance(path[i - 1], path[i]);
                straight += Vector2Int.Distance(town, n.Cell);
                if (paths) foreach (var c in path) tex.SetPixel(c.x, c.y, Color.black);
            }
            if (expanded.Count > 0)
                sb.Append($"\n경로(A*): 실패 {failed} · 길이/직선 {walked / Mathf.Max(1f, straight):0.000} · 탐색 칸 평균 {expanded.Average():0} · 최대 {expanded.Max()}");
            var guarded = new HashSet<int>(map.Passes.Where(p => p.GuardNest >= 0).Select(p => p.GuardNest));
            for (int i = 0; i < map.Nests.Count; i++) Mark(map.Nests[i].Cell, 2, guarded.Contains(i) ? new Color(1f, 0.6f, 0f) : Color.red, false);
            foreach (var p in map.Passes) Mark(p.Cell, 4, p.A == p.B ? Color.cyan : Color.magenta, true);
            foreach (var site in map.SettlementSites) Mark(site, 3, Color.white, true);
            tex.Apply();
            System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return sb.ToString();
        }

        // ---------------- Thoughts and mood (P4-104c) ----------------

        /// <summary>Every member's mood, stress and what is on its mind (heaviest first, with the hours left).</summary>
        public static string Moods()
        {
            float now = Services.Get<GameClock>().TotalHours;
            var sb = new StringBuilder();
            foreach (var a in Services.Get<GuildRoster>().Members)
            {
                if (a == null || !a.IsAlive) continue;
                float mood = ThoughtRecorder.MoodOf(a);
                sb.Append($"{a.Data.name} 기분 {mood:0}({ThoughtRules.Label(ThoughtRules.LevelOf(mood))}) 스트레스 {a.Data.stress:0}: ");
                sb.AppendLine(string.Join(", ", ThoughtRecorder.OnItsMind(a).Select(t =>
                    $"{t.label}{(t.stack > 1 ? $"×{t.stack}" : "")} {ThoughtRules.Weight(t):+0;-0}{(t.until > 0f ? $"({t.until - now:0}h)" : "")}")));
            }
            return sb.ToString();
        }

        /// <summary>Gives the member at that roster index a thought by its id (<see cref="ThoughtRules.All"/>).</summary>
        public static string GiveThought(int index, string id, string about = null, string name = null)
        {
            var members = Services.Get<GuildRoster>().Members;
            if (index < 0 || index >= members.Count) return "no member " + index;
            var def = ThoughtRules.Find(id);
            if (def == null) return "unknown thought: " + id + " (" + string.Join(", ", ThoughtRules.All.Select(d => d.Id)) + ")";
            var t = ThoughtRecorder.Give(members[index], def, about, name);
            return t == null ? "not given" : $"{members[index].Data.name}: {t.label} {ThoughtRules.Weight(t):+0;-0} → 기분 {ThoughtRecorder.MoodOf(members[index]):0}";
        }

        // ---------------- Places worth going to (P4-105) ----------------

        /// <summary>Every place (real values): kind, cell, distance from town, known, gathered/climbed.</summary>
        public static string Places()
        {
            var svc = Services.Get<Places.PlaceService>();
            var town = Services.Get<World.WorldService>().TownCenterWorld;
            var sb = new StringBuilder($"places {svc.All.Count}, known {svc.Known().Count()}\n");
            foreach (var p in svc.All)
                sb.AppendLine($"#{p.Index} {p.Name()} {p.Cell} {Vector2.Distance(p.WorldPosition, town):0}칸 known={svc.IsKnown(p)} ready={svc.IsReady(p)} climbed={svc.IsClimbed(p)} · {QuestBoard.DescribeArea(p.WorldPosition)}");
            // Who is at one now, and who is blessed (P4-105b).
            float now = Services.Get<GameClock>().TotalHours;
            foreach (var a in Services.Get<GuildRoster>().Members)
            {
                if (a == null || !a.IsAlive) continue;
                bool visiting = a.Fsm.Current == a.PlaceVisitState;
                if (!visiting && a.Data.blessedUntil <= now) continue;
                sb.AppendLine($"{a.Data.name}: {a.StateLabel}{(visiting && a.PlaceVisitState.Place != null ? $" (#{a.PlaceVisitState.Place.Index})" : "")}"
                              + (a.Data.blessedUntil > now ? $" · 가호 {a.Data.blessedUntil - now:0.#}시간 HP {a.Stats.MaxHp:0}" : ""));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Sends the member at that roster index to a place now (P4-105b): by its index in <see cref="Places"/>, or -1 for the
        /// nearest known of <paramref name="kind"/> ("HotSpring", "Shrine", "OreVein", "HerbPatch") it would go to. <paramref name="learn"/>
        /// makes the guild know the place first.
        /// </summary>
        public static string PlaceVisit(int index, string kind = "HotSpring", int place = -1, bool learn = true)
        {
            var members = Services.Get<GuildRoster>().Members;
            if (index < 0 || index >= members.Count) return "no member " + index;
            var a = members[index];
            var svc = Services.Get<Places.PlaceService>();
            World.PlaceSite p;
            if (place >= 0)
            {
                if (place >= svc.All.Count) return "no place " + place;
                p = svc.All[place];
            }
            else
            {
                if (!System.Enum.TryParse<World.PlaceKind>(kind, out var k)) return "kind: " + string.Join(" | ", System.Enum.GetNames(typeof(World.PlaceKind)));
                p = Services.Get<Places.PlaceVisits>().Best(a, k);
                if (p == null) return $"{a.Data.name}: no known {kind} it would go to";
            }
            if (learn && !svc.IsKnown(p)) Services.Get<Fog.FogOfWar>().Reveal(p.WorldPosition, 3f);
            a.CurrentQuest = null;
            a.PlaceVisitState.Go(a, p);
            a.ChangeState(a.PlaceVisitState);
            return $"{a.Data.name} → #{p.Index} {p.Name()} {Vector2.Distance(a.Position, p.WorldPosition):0}칸: {a.StateLabel}";
        }

        // ---------------- Breaks and inspirations (P4-104d) ----------------

        /// <summary>Breaks the member at that roster index now: a kind by its enum name ("Berserk", "Brawl"…), or "" for what its mood and place pick.</summary>
        public static string Break(int index, string kind = "", string severity = "Extreme")
        {
            var members = Services.Get<GuildRoster>().Members;
            if (index < 0 || index >= members.Count) return "no member " + index;
            var a = members[index];
            if (!System.Enum.TryParse<BreakSeverity>(severity, out var sev)) return "severity: Minor | Major | Extreme";
            var forced = BreakKind.None;
            if (!string.IsNullOrEmpty(kind) && !System.Enum.TryParse(kind, out forced)) return "kind: " + string.Join(" | ", System.Enum.GetNames(typeof(BreakKind)));
            bool ok = Services.Get<MentalBreaks>().TryBreak(a, sev, forced);
            return ok ? $"{a.Data.name}: {a.StateLabel}" : $"{a.Data.name}: nothing fits here ({a.StateLabel})";
        }

        /// <summary>Inspires the member at that roster index now ("Combat", "Training" or "Social").</summary>
        public static string Inspire(int index, string kind = "Combat")
        {
            var members = Services.Get<GuildRoster>().Members;
            if (index < 0 || index >= members.Count) return "no member " + index;
            if (!System.Enum.TryParse<InspirationKind>(kind, out var k) || k == InspirationKind.None) return "kind: Combat | Training | Social";
            Services.Get<MentalBreaks>().Inspire(members[index], k);
            return $"{members[index].Data.name}: {BreakRules.Label(k)} (atk {members[index].Stats.Attack:0.0})";
        }
    }
}
