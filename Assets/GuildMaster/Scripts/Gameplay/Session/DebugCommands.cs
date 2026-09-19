using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Ecosystem;
using GuildMaster.Economy;
using GuildMaster.Monsters;
using GuildMaster.Quests;
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
            sb.AppendLine($"[{clock.FormatTime()}] gold={t.Gold} exp={t.Exp} stock={string.Join(",", t.Stock.Where(kv => kv.Value > 0).Select(kv => kv.Key + ":" + kv.Value))}");
            var eco = Services.Get<EcosystemManager>();
            sb.AppendLine("pop: " + string.Join(" ", eco.Species.Select(s => $"{s.id}={eco.GetPopulation(s)}{(eco.IsExtinct(s) ? "(X)" : "")}")));
            foreach (var a in Services.Get<GuildRoster>().Members)
                sb.AppendLine($"  {a.Data.id} {a.Data.name} {a.Data.cls} Lv{a.Data.level} hp={a.Hp:0}/{a.Stats.MaxHp:0} gold={a.Data.gold} fat={a.Data.fatigue:0} loot={a.Data.LootCount} pot={a.Data.potions} state={a.StateLabel} quest={a.CurrentQuest?.Id}");
            foreach (var q in Services.Get<QuestBoard>().Quests)
                sb.AppendLine($"  {q.Id} {q.Title} {q.Status} {q.ProgressText} assignees={string.Join(",", q.Assignees.Select(x => x.TakerName))}");
            foreach (var line in Services.Get<GameLog>().Recent(12)) sb.AppendLine("  > " + line);
            return sb.ToString();
        }

        public static string PostHunt(string speciesId, int count, int gold, int exp, int party = 1)
        {
            var eco = Services.Get<EcosystemManager>();
            var species = eco.Species.FirstOrDefault(s => s.id == speciesId);
            var ok = Services.Get<QuestBoard>().TryPost(new QuestDraft
            {
                Type = QuestType.Hunt, Species = species, Count = count, RewardGold = gold, RewardExp = exp, MaxParty = party,
            }, out var q, out var err);
            return ok ? q.Id : err;
        }

        public static void SetSpeed(int index) => Services.Get<GameClock>().SetSpeed(index);

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
            SkirmishMonitor.Ensure().ResetStats();
            return $"skirmish {species.id}x{count} at {site} (guild pulled: {pulled})";
        }

        /// <summary>Kills every living nest-less monster (leftovers from <see cref="SpawnSkirmish"/>).</summary>
        public static int ClearSkirmish()
        {
            var strays = Units.UnitRegistry.All.OfType<Monster>().Where(m => m.IsAlive && m.Home == null).ToList();
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

        // ---------------- Guild master & escort (P3-4a) ----------------

        public static string PostEscort(int goldPerHour, int expPerHour, int hours, int party)
        {
            var ok = Services.Get<QuestBoard>().TryPost(new QuestDraft
            {
                Type = QuestType.Escort, Hours = hours, RewardGold = goldPerHour * hours, RewardExp = expPerHour * hours,
                MaxParty = party, Point = Services.Get<Player.PlayerAvatar>().Position,
            }, out var q, out var err);
            return ok ? q.Id : err;
        }

        /// <summary>Makes up to 'count' idle members take the quest, skipping the decision step.</summary>
        public static string ForceAccept(string questId, int count)
        {
            var board = Services.Get<QuestBoard>();
            var q = board.FindById(questId);
            if (q == null) return "no quest " + questId;
            var names = new List<string>();
            foreach (var a in Services.Get<GuildRoster>().Members)
            {
                if (names.Count >= count) break;
                if (!a.IsAlive || a.CurrentQuest != null || !board.TryAccept(q, a)) continue;
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
            var sb = new StringBuilder($"master ({m.Position.x:0.0},{m.Position.y:0.0}) hp={m.Hp:0}/{m.Stats.MaxHp:0} inTown={world.Grid.IsInTown(m.Position)} moving={m.Mover.IsMoving} gold={t.Gold} exp={t.Exp}\n");
            foreach (var q in Services.Get<QuestBoard>().Quests.Concat(Services.Get<QuestBoard>().History).Where(q => q.Type == QuestType.Escort))
                sb.AppendLine($"  {q.Id} {q.Status} {q.ProgressText} escrow={q.RewardGold}/{q.RewardExp} pay={q.PayGold}/{q.PayExp} assignees={string.Join(",", q.Assignees.Select(x => x.TakerName))}");
            foreach (var a in Services.Get<GuildRoster>().Members)
                sb.AppendLine($"  {a.Data.name} {a.StateLabel} d={Vector2.Distance(a.Position, m.Position):0.0} quest={a.CurrentQuest?.Id} gold={a.Data.gold}");
            return sb.ToString();
        }

        /// <summary>Spawns monsters next to the guild master (they aggro on it).</summary>
        public static string SpawnNearMaster(string speciesId, int count, float distance = 4f)
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
                mon.ForceTarget(master);
                spawned++;
            }
            return $"spawned {spawned}";
        }

        public static string SkirmishReport()
            => SkirmishMonitor.Instance != null ? SkirmishMonitor.Instance.Report() : "no monitor (call SpawnSkirmish first)";

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
    }
}
