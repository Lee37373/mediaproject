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
            foreach (var b in Services.Get<Town.TownService>().Buildings)
                sb.Append($"  {b.DisplayName} {b.Hp:0}/{b.MaxHp:0}\n");
            foreach (var a in Services.Get<GuildRoster>().Members)
                sb.Append($"  {a.Data.name} {a.StateLabel} hp={a.Hp:0}/{a.Stats.MaxHp:0}\n");
            return sb.ToString();
        }

        public static string BanterStatus()
        {
            var b = Services.Get<BanterService>();
            return $"attempts={b.Attempts} llm={b.LlmBanters} fixed={b.FixedBanters}";
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
                sb.Append($"  {kv.Key} x{kv.Value} supply={s.Market.Supply(kv.Key):0.0} demand={s.Market.Demand(kv.Key):0.00} price={s.Market.Price(item)} base={item?.baseValue}\n");
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
