using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using GuildMaster.Units;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Incidents
{
    /// <summary>The rumour of an incident reached the town (its quest is up).</summary>
    public struct IncidentRumouredEvent { public Incident Incident; }
    /// <summary>A later rumour of where a moving incident (a horde) is now; its quest's area moved there.</summary>
    public struct IncidentRumourUpdatedEvent { public Incident Incident; }
    /// <summary>The guild saw an incident with its own eyes for the first time.</summary>
    public struct IncidentSightedEvent { public Incident Incident; }
    /// <summary>An incident is over. <see cref="Known"/>: the guild had heard of it or seen it (else the UI stays silent).</summary>
    public struct IncidentEndedEvent { public Incident Incident; public bool Known; }

    /// <summary>
    /// Sudden incidents (P4-85, user decisions 2026-10-18): something happens out there, the town hears a rumour a
    /// little later that puts it roughly somewhere, a client posts a quest for it (the client pays, the guild keeps its
    /// fee), and it ends by the quest, by someone else, or by running out of time — with no penalty beyond what the
    /// incident itself does. Built like the disasters (P4-72). The numbers are in <see cref="IncidentRules"/>.
    /// Stage a: the rare monster.
    /// </summary>
    public class IncidentService : MonoBehaviour, ISaveable, ISaveLink
    {
        /// <summary>Tests: off keeps incidents to the ones a test starts itself.</summary>
        public static bool Enabled = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Enabled = true;

        const float TickInterval = 0.5f;

        readonly List<Incident> _active = new();
        readonly List<Incident> _history = new();
        GameClock _clock;
        WorldService _world;
        EcosystemManager _eco;
        QuestBoard _board;
        FogOfWar _fog;
        float _timer;
        int _nextId = 1;

        public IReadOnlyList<Incident> Active => _active;
        public IReadOnlyList<Incident> History => _history;

        float Now => _clock.TotalHours;

        public void Init(GameClock clock, WorldService world, EcosystemManager eco, QuestBoard board, FogOfWar fog)
        {
            _clock = clock;
            _world = world;
            _eco = eco;
            _board = board;
            _fog = fog;
            BuildRuinLooks();
        }

        // Deaths are noticed on the next check (not on the death event): the board counts the kill on that event first,
        // whatever order the two were subscribed in, and a pooled body is not reused within a check (its corpse lingers).
        void OnEnable() => EventBus.Subscribe<HourPassedEvent>(OnHour);

        void OnDisable() => EventBus.Unsubscribe<HourPassedEvent>(OnHour);

        void OnHour(HourPassedEvent e)
        {
            if (!Enabled) return;
            var rng = GameRandom.Shared;
            if (e.Day >= IncidentRules.RareFirstDay && !_active.Any(i => i.Kind == IncidentKind.RareMonster)
                && rng.Value < IncidentRules.RareChancePerHour)
                StartRareMonster();
            if (e.Day >= IncidentRules.HordeFirstDay && !_active.Any(i => i.Kind == IncidentKind.Horde)
                && rng.Value < IncidentRules.HordeChancePerHour)
                StartHorde();
            if (e.Day >= IncidentRules.RescueFirstDay && !_active.Any(i => i.Kind == IncidentKind.Rescue)
                && rng.Value < IncidentRules.RescueChancePerHour)
                StartRescue();
            if (e.Day >= IncidentRules.RuinFirstDay && _active.Count(i => i.Kind == IncidentKind.Ruin) < IncidentRules.MaxOpenRuins
                && rng.Value < IncidentRules.RuinChancePerHour)
                StartRuin();
        }

        void Update()
        {
            // Paused: nothing moves (P4-80; a loaded game starts with the check due and would otherwise run it once).
            if (_active.Count == 0 && _stragglers.Count == 0 || Time.deltaTime <= 0f) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = TickInterval;
            foreach (var i in _active.ToList()) Tick(i);
            ScatterStragglers();
        }

        void Tick(Incident i)
        {
            switch (i.Kind)
            {
                case IncidentKind.RareMonster: TickRare(i); break;
                case IncidentKind.Horde: TickHorde(i); break;
                case IncidentKind.Rescue: TickRescue(i); break;
                case IncidentKind.Ruin: TickRuin(i); break;
            }
            if (!i.Over && !i.Rumoured && Now >= i.RumourAtHour) SpreadRumour(i);
        }

        // ---------------- Rare monster ----------------

        /// <summary>
        /// A rare variant appears out of the guild's sight: a kind that lives there, at the land's level + the bonus, clear of
        /// the dens and the villages. Null when no spot was found this time. <paramref name="at"/>, <paramref name="kind"/> and
        /// <paramref name="variant"/> are for tests and debug.
        /// </summary>
        public Incident StartRareMonster(Vector2? at = null, MonsterDefinition kind = null, int variant = -1)
        {
            var rng = GameRandom.Shared;
            Vector2 spot;
            if (at.HasValue)
            {
                spot = at.Value;
                kind ??= PickKind(GridMap.WorldToCell(spot), rng);
            }
            else if (!PickRareSpot(rng, out spot, out var picked)) return null;
            else kind ??= picked;
            if (kind == null) return null;
            if (variant < 0) variant = rng.Range(0, RareMonster.VariantCount);
            var m = Services.Get<PoolService>().Spawn<Monster>(Monster.PoolKey, spot);
            m.SetupRare(kind, variant, RareMonster.Level(_eco.BirthLevelAt(spot)));
            var i = new Incident
            {
                Id = _nextId++, Kind = IncidentKind.RareMonster, StartedHour = Now, Spot = spot,
                RumourAtHour = Now + IncidentRules.RumourDelay(rng), EndsAtHour = Now + IncidentRules.RareStayHours,
                Target = m, TargetName = m.DisplayName, TargetLevel = m.Level,
            };
            _active.Add(i);
            Debug.Log($"[Incident] #{i.Id} rare {m.DisplayName} Lv{m.Level} at ({spot.x:0},{spot.y:0}), {QuestBoard.DescribeArea(spot)}");
            return i;
        }

        bool PickRareSpot(GameRandom rng, out Vector2 spot, out MonsterDefinition kind)
        {
            spot = default;
            kind = null;
            var town = _world.TownCenterWorld;
            float inner = EcosystemManager.InnerEdge;
            float outer = Mathf.Max(_eco.OutermostDistance, inner + 1f);
            Services.TryGet<Villages.VillageService>(out var villages);
            for (int attempt = 0; attempt < 64; attempt++)
            {
                bool inside = rng.Value < IncidentRules.RareInnerShare;
                float d = inside ? rng.Range(IncidentRules.RareMinDistance, inner) : rng.Range(inner, outer);
                float a = rng.Range(0f, Mathf.PI * 2f);
                var cell = GridMap.WorldToCell(town + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d);
                var grid = _world.Grid;
                if (!grid.InBounds(cell) || !grid.IsWalkable(cell) || grid.IsInTown(cell) || Hazards.Blocks(grid.HazardAt(cell))) continue;
                var p = GridMap.CellToWorld(cell);
                if (_fog != null && _fog.IsVisibleRaw(p)) continue; // it does not pop up in plain sight
                if (_eco.Nests.Any(n => !n.IsEmpty && Vector2.Distance(n.WorldPosition, p) < n.TerritoryRadius + IncidentRules.RareNestMargin)) continue;
                if (villages != null && villages.Villages.Any(v => !v.IsDestroyed && Vector2.Distance(v.WorldPosition, p) < IncidentRules.RareVillageMargin)) continue;
                var k = PickKind(cell, rng);
                if (k == null) continue;
                spot = p;
                kind = k;
                return true;
            }
            return false;
        }

        /// <summary>
        /// A kind that lives at that cell, drawn by the weights of the map's first dens (no region lords); only one that goes
        /// for people when <paramref name="aggressive"/> (an ambush).
        /// </summary>
        MonsterDefinition PickKind(Vector2Int cell, GameRandom rng, bool aggressive = false)
        {
            var region = _world.Map.RegionOf(cell.x, cell.y);
            var biome = MapGenerator.ClassifyBiome(_world.Map, cell);
            var kinds = _eco.AllSpecies.Where(s => s != null && !s.isRegionLord && (!aggressive || s.aggressiveToHumans)
                                                   && EcosystemManager.Fit(s, region, biome) > 0f).ToList();
            if (kinds.Count == 0) return null;
            float total = kinds.Sum(s => EcosystemManager.Fit(s, region, biome));
            float roll = rng.Range(0f, total);
            foreach (var s in kinds)
            {
                roll -= EcosystemManager.Fit(s, region, biome);
                if (roll <= 0f) return s;
            }
            return kinds[^1];
        }

        void TickRare(Incident i)
        {
            var m = i.Target;
            if (m == null) return;
            if (!m.IsAlive)
            {
                RareFell(i);
                return;
            }
            bool visible = _fog != null && _fog.IsVisibleRaw(m.Position);
            if (visible)
            {
                i.LastSeen = m.Position;
                i.LastSeenHour = Now;
                i.TargetLevel = m.Level;
                bool first = !i.Sighted;
                i.Sighted = true;
                // Seen before the rumour came: the town hears it from the guild, at once and exactly.
                if (!i.Rumoured) SpreadRumour(i);
                if (i.Quest != null && i.Quest.IsActive)
                {
                    i.Quest.Point = m.Position;
                    i.Quest.Radius = IncidentRules.SightedRadius;
                }
                if (first) EventBus.Publish(new IncidentSightedEvent { Incident = i });
            }
            // Its time is up: it slips away — never before the guild's eyes, nor in the middle of a fight.
            if (Now >= i.EndsAtHour && !visible && !m.IsInFight())
            {
                i.Target = null;
                m.Perish();
                End(i, IncidentOutcome.Expired);
                _board.FailIncidentQuest(i.Quest, RareGoneReason);
            }
        }

        /// <summary>Why the board took a rare hunt down: it is gone.</summary>
        public const string RareGoneReason = "희귀 몬스터가 사라졌습니다";
        /// <summary>Why the board took a rare hunt down: someone else got it.</summary>
        public const string RareSlainReason = "다른 누군가가 쓰러뜨렸습니다";

        /// <summary>
        /// The rare one fell. By the hunt's takers the board has already marked the hunt done; by anyone else the hunt is
        /// done if someone was on it (as with a den that is gone) and comes down if not.
        /// </summary>
        void RareFell(Incident i)
        {
            i.Target = null; // the pooled body will be someone else
            bool byQuest = i.Quest != null && i.Quest.Status is QuestStatus.Completed or QuestStatus.TurnedIn;
            End(i, byQuest ? IncidentOutcome.Resolved : IncidentOutcome.SettledByOthers);
            SettleQuest(i, RareSlainReason);
        }

        /// <summary>The incident is over by other hands: a claimed quest is done, an unclaimed one comes down.</summary>
        void SettleQuest(Incident i, string reason)
        {
            if (i.Quest == null || !i.Quest.IsActive) return;
            if (i.Quest.Assignees.Count > 0) _board.SettleTownQuest(i.Quest, reason);
            else _board.FailIncidentQuest(i.Quest, reason);
        }

        // ---------------- Horde ----------------

        /// <summary>
        /// A horde sets out from a map edge for the empty site nearest the town that a raiding kind can live at (else any
        /// kind that lives there). Null when there is no such site. <paramref name="site"/>, <paramref name="kind"/> and
        /// <paramref name="size"/> are for tests and debug.
        /// </summary>
        public Incident StartHorde(Nest site = null, MonsterDefinition kind = null, int size = 0)
        {
            if (!Services.TryGet<MigrationService>(out var migration)) return null;
            var rng = GameRandom.Shared;
            if (site == null && !PickHordeSite(migration, out site, out kind)) return null;
            kind ??= PickHordeKind(site);
            if (kind == null) return null;
            if (size <= 0) size = rng.Range(IncidentRules.HordeMin, IncidentRules.HordeMax + 1);
            var band = migration.TryHorde(site, kind, size, IncidentRules.HordeLevelBonus, rng);
            if (band == null) return null;
            var i = new Incident
            {
                Id = _nextId++, Kind = IncidentKind.Horde, StartedHour = Now, RumourAtHour = Now + IncidentRules.RumourDelay(rng),
                EndsAtHour = float.MaxValue, Band = band, Site = site, HordeSize = band.Members.Count, SpeciesName = kind.displayName,
                HordeLevel = band.Members.Count > 0 ? band.Members.Max(m => m.Level) : 1,
            };
            i.Members.AddRange(band.Members);
            i.Spot = Centroid(i.Members);
            _active.Add(i);
            Debug.Log($"[Incident] #{i.Id} horde of {i.HordeSize} {kind.displayName} Lv{i.HordeLevel} from ({i.Spot.x:0},{i.Spot.y:0}) → {site.DisplayName} {QuestBoard.DescribeArea(site.WorldPosition)}");
            return i;
        }

        bool PickHordeSite(MigrationService migration, out Nest site, out MonsterDefinition kind)
        {
            var town = _world.TownCenterWorld;
            foreach (var n in _eco.Nests.Where(n => n.IsEmpty && !n.IsLair && !n.Sealed && !migration.IsTarget(n)
                                                    && Vector2.Distance(n.WorldPosition, town) >= IncidentRules.HordeMinDistance)
                                        .OrderBy(n => Vector2.Distance(n.WorldPosition, town)))
            {
                var k = PickHordeKind(n);
                if (k == null) continue;
                site = n;
                kind = k;
                return true;
            }
            site = null;
            kind = null;
            return false;
        }

        /// <summary>A kind that lives at the site — a raiding kind if one does (the threat), by the map's own weights.</summary>
        MonsterDefinition PickHordeKind(Nest site)
        {
            var kinds = _eco.AllSpecies.Where(s => s != null && !s.isRegionLord && site.Suits(s)).ToList();
            if (kinds.Any(s => s.canRaid)) kinds = kinds.Where(s => s.canRaid).ToList();
            if (kinds.Count == 0) return null;
            float total = kinds.Sum(s => EcosystemManager.Fit(s, site.Region, site.Biome));
            if (total <= 0f) return kinds[0];
            float roll = GameRandom.Shared.Range(0f, total);
            foreach (var s in kinds)
            {
                roll -= EcosystemManager.Fit(s, site.Region, site.Biome);
                if (roll <= 0f) return s;
            }
            return kinds[^1];
        }

        static Vector2 Centroid(List<Monster> members)
        {
            var sum = Vector2.zero;
            int n = 0;
            foreach (var m in members) if (m != null && m.IsAlive) { sum += m.Position; n++; }
            return n == 0 ? Vector2.zero : sum / n;
        }

        /// <summary>Why the board took a horde's quest down: it got where it was going.</summary>
        public const string HordeSettledReason = "무리가 마을 근처에 자리를 잡았습니다";
        /// <summary>Why the board took a horde's quest down: it is gone, by other hands.</summary>
        public const string HordeGoneReason = "무리가 흩어졌습니다";
        /// <summary>A horde is this spread out (the rumour's circle allows for it).</summary>
        const float HordeSpread = 6f;

        void TickHorde(Incident i)
        {
            i.Members.RemoveAll(m => m == null || !m.IsAlive);
            // The quest is done (enough of it brought down): the incident is over; what is left walks on as any band.
            if (i.Quest != null && i.Quest.Status is QuestStatus.Completed or QuestStatus.TurnedIn)
            {
                End(i, IncidentOutcome.Resolved);
                return;
            }
            if (i.Members.Count == 0)
            {
                // Gone to the last one: with a group on it, it was their fight (others only finished it).
                bool claimed = i.Quest != null && i.Quest.IsActive && i.Quest.Assignees.Count > 0;
                End(i, claimed ? IncidentOutcome.Resolved : IncidentOutcome.SettledByOthers);
                SettleQuest(i, HordeGoneReason);
                return;
            }
            i.Spot = Centroid(i.Members);
            // It got there (settled, or gave up walking and went "home" to its site all the same).
            if (i.Band == null || !Services.TryGet<MigrationService>(out var migration) || !migration.Bands.Contains(i.Band))
            {
                End(i, IncidentOutcome.Expired);
                _board.FailIncidentQuest(i.Quest, HordeSettledReason);
                return;
            }
            // Seen: the area follows the members in sight.
            var seen = Vector2.zero;
            int visible = 0;
            if (_fog != null)
                foreach (var m in i.Members)
                    if (_fog.IsVisibleRaw(m.Position)) { seen += m.Position; visible++; }
            if (visible > 0)
            {
                i.LastSeen = seen / visible;
                i.LastSeenHour = Now;
                bool first = !i.Sighted;
                i.Sighted = true;
                if (!i.Rumoured) SpreadRumour(i);
                if (i.Quest != null && i.Quest.IsActive)
                {
                    i.Quest.Point = i.LastSeen;
                    i.Quest.Radius = IncidentRules.SightedRadius;
                }
                // Rumours of it start again a while after the guild loses sight of it.
                i.NextRumourHour = Now + IncidentRules.HordeRumourEvery;
                if (first) EventBus.Publish(new IncidentSightedEvent { Incident = i });
                return;
            }
            // Unseen: every while a new rumour of where it is now (it is on the move).
            if (i.Rumoured && Now >= i.NextRumourHour && i.Quest != null && i.Quest.IsActive)
            {
                i.RumourCentre = IncidentRules.RumourCentre(i.Spot, IncidentRules.RumourOffset(HordeSpread), GameRandom.Shared);
                i.RumourRadius = IncidentRules.RumourRadius;
                i.NextRumourHour = Now + IncidentRules.HordeRumourEvery;
                i.Quest.Point = i.RumourCentre;
                i.Quest.Radius = i.RumourRadius;
                EventBus.Publish(new IncidentRumourUpdatedEvent { Incident = i });
            }
        }

        // ---------------- Rescue ----------------

        /// <summary>Ambushers left behind when a rescue ends, and when they may scatter (unseen).</summary>
        readonly List<(Monster monster, float hour)> _stragglers = new();

        /// <summary>
        /// A traveller on the road between a village and our town is ambushed out of the guild's sight, 40-90 cells out:
        /// a few of a kind that goes for people and lives there, at the land's level, fall on it; it crouches by its cart
        /// and holds out about <see cref="IncidentRules.RescueHoldHours"/>. Null when there is no road or no spot.
        /// <paramref name="at"/> is for tests and debug.
        /// </summary>
        public Incident StartRescue(Vector2? at = null, int ambushers = 0)
        {
            var rng = GameRandom.Shared;
            var town = _world.TownCenterWorld;
            Vector2 spot, destination;
            MonsterDefinition kind;
            if (at.HasValue)
            {
                spot = at.Value;
                destination = town;
                kind = PickKind(GridMap.WorldToCell(spot), rng, aggressive: true);
            }
            else if (!PickRescueSpot(rng, out spot, out destination, out kind)) return null;
            if (kind == null) return null;
            if (ambushers <= 0) ambushers = rng.Range(IncidentRules.AmbushMin, IncidentRules.AmbushMax + 1);

            int id = _nextId++;
            string name = IncidentRules.TravellerName(rng);
            var traveller = Traveller.Create($"TRAVELLER_{id}", name, spot, destination, transform);
            var i = new Incident
            {
                Id = id, Kind = IncidentKind.Rescue, StartedHour = Now, Spot = spot, RumourAtHour = Now + IncidentRules.RumourDelay(rng),
                EndsAtHour = float.MaxValue, Traveller = traveller, Destination = destination, TargetName = name, SpeciesName = kind.displayName,
            };
            int level = _eco.BirthLevelAt(spot);
            var pool = Services.Get<PoolService>();
            var blows = new List<(float, float)>();
            for (int k = 0; k < ambushers; k++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                var p = spot + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rng.Range(1.5f, 3f);
                if (!_world.Grid.TryFindNearestWalkable(GridMap.WorldToCell(p), 3, out var cell)) cell = GridMap.WorldToCell(spot);
                var m = pool.Spawn<Monster>(Monster.PoolKey, GridMap.CellToWorld(cell));
                m.Setup(kind, null);
                m.SetLevel(level);
                m.ForceTarget(traveller);
                i.Members.Add(m);
                blows.Add((m.Stats.Attack, m.Stats.AttackCooldown));
            }
            i.TargetLevel = level;
            traveller.HoldOut(IncidentRules.HoldOutHp(blows, IncidentRules.RescueHoldHours, _clock.RealSecondsPerGameHour));
            traveller.Arrived = t => { if (i.Traveller == t) i.Traveller = null; };
            _active.Add(i);
            Debug.Log($"[Incident] #{i.Id} rescue: {name} ambushed by {ambushers} {kind.displayName} Lv{level} at ({spot.x:0},{spot.y:0}) {QuestBoard.DescribeArea(spot)}, hp {traveller.Stats.MaxHp:0}");
            return i;
        }

        /// <summary>On the straight road between our town and a standing village, 40-90 cells out (and short of the village).</summary>
        bool PickRescueSpot(GameRandom rng, out Vector2 spot, out Vector2 destination, out MonsterDefinition kind)
        {
            spot = destination = default;
            kind = null;
            if (!Services.TryGet<Villages.VillageService>(out var villages)) return false;
            var town = _world.TownCenterWorld;
            var roads = villages.Standing.Where(v => Vector2.Distance(v.WorldPosition, town) > IncidentRules.RescueMinDistance + 15f).ToList();
            if (roads.Count == 0) return false;
            var grid = _world.Grid;
            for (int attempt = 0; attempt < 32; attempt++)
            {
                var v = roads[rng.Range(0, roads.Count)];
                float length = Vector2.Distance(v.WorldPosition, town);
                float d = rng.Range(IncidentRules.RescueMinDistance, Mathf.Min(IncidentRules.RescueMaxDistance, length - 15f));
                var p = town + (v.WorldPosition - town).normalized * d;
                if (!grid.TryFindNearestWalkable(GridMap.WorldToCell(p), 6, out var cell) || grid.IsInTown(cell) || Hazards.Blocks(grid.HazardAt(cell))) continue;
                var w = GridMap.CellToWorld(cell);
                if (_fog != null && _fog.IsVisibleRaw(w)) continue;
                if (_eco.Nests.Any(n => !n.IsEmpty && Vector2.Distance(n.WorldPosition, w) < n.TerritoryRadius + 4f)) continue;
                var k = PickKind(cell, rng, aggressive: true);
                if (k == null) continue;
                spot = w;
                kind = k;
                destination = rng.Value < IncidentRules.RescueToTownShare ? town : v.WorldPosition;
                return true;
            }
            return false;
        }

        /// <summary>Why the board took a rescue down.</summary>
        public const string RescueDiedReason = "여행자가 목숨을 잃었습니다";
        public const string RescueSavedReason = "여행자가 위기를 넘겼습니다";

        void TickRescue(Incident i)
        {
            i.Members.RemoveAll(m => m == null || !m.IsAlive);
            var t = i.Traveller;
            if (t == null || !t.IsAlive)
            {
                // Fallen: its cart spills, the ones who did it scatter after a while.
                if (t != null && Services.TryGet<Items.GroundItemService>(out var ground)) ground.Drop(t.Position, IncidentRules.Cargo);
                i.Traveller = null;
                End(i, IncidentOutcome.Expired);
                _board.FailIncidentQuest(i.Quest, RescueDiedReason);
                LeaveStragglers(i);
                return;
            }
            i.Spot = t.Position;
            if (i.Members.Count == 0)
            {
                // Nobody is on it any more: on its way. With a group on the quest it was their doing.
                t.GoOn();
                bool claimed = i.Quest != null && (i.Quest.Status is QuestStatus.Completed or QuestStatus.TurnedIn || i.Quest.IsActive && i.Quest.Assignees.Count > 0);
                End(i, claimed ? IncidentOutcome.Resolved : IncidentOutcome.SettledByOthers);
                SettleQuest(i, RescueSavedReason);
                return;
            }
            bool visible = _fog != null && (_fog.IsVisibleRaw(t.Position) || i.Members.Any(m => _fog.IsVisibleRaw(m.Position)));
            if (!visible) return;
            i.LastSeen = t.Position;
            i.LastSeenHour = Now;
            bool first = !i.Sighted;
            i.Sighted = true;
            if (!i.Rumoured) SpreadRumour(i);
            if (i.Quest != null && i.Quest.IsActive)
            {
                i.Quest.Point = t.Position;
                i.Quest.Radius = IncidentRules.SightedRadius;
            }
            if (first) EventBus.Publish(new IncidentSightedEvent { Incident = i });
        }

        void LeaveStragglers(Incident i)
        {
            foreach (var m in i.Members) if (m != null && m.IsAlive) _stragglers.Add((m, Now + IncidentRules.StragglerHours));
            i.Members.Clear();
        }

        /// <summary>Ambushers left behind go their ways once their time is up and nobody sees them.</summary>
        void ScatterStragglers()
        {
            for (int k = _stragglers.Count - 1; k >= 0; k--)
            {
                var (m, hour) = _stragglers[k];
                if (m == null || !m.IsAlive) { _stragglers.RemoveAt(k); continue; }
                if (Now < hour || m.IsInFight() || _fog != null && _fog.IsVisibleRaw(m.Position)) continue;
                _stragglers.RemoveAt(k);
                m.Perish();
            }
        }

        // ---------------- Ruin ----------------

        /// <summary>Ruins whose treasure the guild has taken (each is looted once, user decision: recommended "fixed, once").</summary>
        readonly HashSet<int> _looted = new();
        readonly Dictionary<int, SpriteRenderer> _chests = new();
        readonly Dictionary<int, SpriteRenderer> _entrances = new();

        /// <summary>A member of the quest this close to the chest may open it.</summary>
        public const float ChestReach = 1.4f;

        public bool IsLooted(int ruin) => _looted.Contains(ruin);

        /// <summary>The ruin of an incident, or null.</summary>
        public RuinSite RuinOf(Incident i) => i != null && i.RuinIndex >= 0 && i.RuinIndex < _world.Map.Ruins.Count ? _world.Map.Ruins[i.RuinIndex] : null;

        public Vector2 ChestOf(Incident i) => RuinOf(i) is { } r ? GridMap.CellToWorld(r.Chest) : i.Spot;

        /// <summary>The incident going on at a ruin, or null.</summary>
        public Incident ActiveAt(RuinSite ruin) => ruin == null ? null : _active.FirstOrDefault(i => i.Kind == IncidentKind.Ruin && i.RuinIndex == ruin.Index);

        /// <summary>
        /// Word of a ruin: one not looted and not already talked of; its halls fill with guardians — a kind that lives round
        /// the way down (one that goes for people if any), two levels above the land there — and the treasure waits in the
        /// last room. <paramref name="index"/> is for tests and debug.
        /// </summary>
        public Incident StartRuin(int index = -1)
        {
            var map = _world.Map;
            var free = map.Ruins.Where(r => !_looted.Contains(r.Index) && ActiveAt(r) == null).ToList();
            if (free.Count == 0) return null;
            var rng = GameRandom.Shared;
            var site = index >= 0 ? free.FirstOrDefault(r => r.Index == index) : free[rng.Range(0, free.Count)];
            if (site == null) return null;
            var kind = PickKind(site.Entrance, rng, aggressive: true) ?? PickKind(site.Entrance, rng);
            if (kind == null) return null;
            var entrance = GridMap.CellToWorld(site.Entrance);
            int level = Mathf.Min(MonsterLevel.MaxLevel, _eco.BirthLevelAt(entrance) + IncidentRules.GuardLevelBonus);
            var i = new Incident
            {
                Id = _nextId++, Kind = IncidentKind.Ruin, StartedHour = Now, Spot = entrance, RumourAtHour = Now + IncidentRules.RumourDelay(rng),
                EndsAtHour = float.MaxValue, RuinIndex = site.Index, TargetName = IncidentRules.RuinName(site), SpeciesName = kind.displayName,
                TargetLevel = level,
            };
            var pool = Services.Get<PoolService>();
            for (int r = 1; r < site.Rooms.Count; r++)
            {
                var room = site.Rooms[r];
                int n = r == site.Rooms.Count - 1 ? IncidentRules.TreasureGuards : IncidentRules.GuardsPerRoom;
                for (int k = 0; k < n; k++)
                {
                    var cell = new Vector2Int(rng.Range(room.xMin + 1, room.xMax - 1), rng.Range(room.yMin + 1, room.yMax - 1));
                    var m = pool.Spawn<Monster>(Monster.PoolKey, GridMap.CellToWorld(cell));
                    m.Setup(kind, null);
                    m.SetLevel(level);
                    i.Members.Add(m);
                }
            }
            _active.Add(i);
            ShowChest(site, open: false);
            Debug.Log($"[Incident] #{i.Id} ruin '{i.TargetName}' #{site.Index} at ({site.Entrance.x},{site.Entrance.y}) {QuestBoard.DescribeArea(entrance)}: {i.Members.Count} {kind.displayName} Lv{level}");
            return i;
        }

        void TickRuin(Incident i)
        {
            i.Members.RemoveAll(m => m == null || !m.IsAlive);
            var site = RuinOf(i);
            if (site == null || _fog == null || i.Sighted) return;
            var entrance = GridMap.CellToWorld(site.Entrance);
            if (!_fog.IsVisibleRaw(entrance)) return;
            i.Sighted = true;
            i.LastSeen = entrance;
            i.LastSeenHour = Now;
            if (!i.Rumoured) SpreadRumour(i);
            if (i.Quest != null && i.Quest.IsActive)
            {
                i.Quest.Point = entrance;
                i.Quest.Radius = IncidentRules.SightedRadius;
            }
            EventBus.Publish(new IncidentSightedEvent { Incident = i });
        }

        /// <summary>
        /// Someone on the ruin's quest at the chest, and no guardian close to it: it opens — relics and gear spill out, the
        /// quest is done, the ruin is looted for good, and the guardians left go their ways some hours on.
        /// </summary>
        public bool TryOpenChest(Incident i, Adventurer opener)
        {
            if (i == null || i.Over || i.ChestOpened || i.Kind != IncidentKind.Ruin || opener == null || !opener.IsAlive) return false;
            if (i.Quest == null || !i.Quest.IsAssigned(opener)) return false;
            var chest = ChestOf(i);
            if (Vector2.Distance(opener.Position, chest) > ChestReach + 0.2f) return false;
            if (i.Members.Any(m => m != null && m.IsAlive && Vector2.Distance(m.Position, chest) <= IncidentRules.ChestClearRadius)) return false;
            i.ChestOpened = true;
            var site = RuinOf(i);
            var loot = new List<ItemStack>();
            IncidentRules.AddTreasure(_eco.Depth(i.Spot) > 1f, i.TargetLevel, loot);
            if (Services.TryGet<Items.GroundItemService>(out var ground)) ground.Drop(chest + new Vector2(0f, -0.8f), loot);
            _looted.Add(i.RuinIndex);
            ShowChest(site, open: true);
            ShowEntrance(site);
            End(i, IncidentOutcome.Resolved);
            SettleQuest(i, RuinLootedReason);
            LeaveStragglers(i);
            return true;
        }

        public const string RuinLootedReason = "유적의 보물을 찾았습니다";

        // ---- Looks: the ways down on the surface, the steps up and the chests below ----

        void BuildRuinLooks()
        {
            foreach (var site in _world.Map.Ruins)
            {
                ShowEntrance(site);
                var up = Prop("RuinStairs", GridMap.CellToWorld(site.Inside) + new Vector2(0f, -0.4f), CartoonArt.RuinStairsUp());
                up.transform.localScale = Vector3.one;
            }
        }

        void ShowEntrance(RuinSite site)
        {
            if (!_entrances.TryGetValue(site.Index, out var sr) || sr == null)
                _entrances[site.Index] = sr = Prop("RuinEntrance", GridMap.CellToWorld(site.Entrance) + new Vector2(0f, -0.45f), null);
            sr.sprite = CartoonArt.RuinEntrance(_looted.Contains(site.Index));
        }

        void ShowChest(RuinSite site, bool open)
        {
            if (site == null) return;
            if (!_chests.TryGetValue(site.Index, out var sr) || sr == null)
                _chests[site.Index] = sr = Prop("RuinChest", GridMap.CellToWorld(site.Chest) + new Vector2(0f, -0.3f), null);
            sr.sprite = CartoonArt.TreasureChest(open);
        }

        SpriteRenderer Prop(string name, Vector2 at, Sprite sprite)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = CharacterVisual.BaseSortingOrder - Mathf.RoundToInt(at.y * 8f) + 2;
            return sr;
        }

        // ---------------- Rumour, end ----------------

        /// <summary>
        /// The rumour reaches the town: a rough circle (the true spot inside it — exact once the guild has seen it) and a
        /// quest from the incident's client.
        /// </summary>
        void SpreadRumour(Incident i)
        {
            i.Rumoured = true;
            var rng = GameRandom.Shared;
            if (i.Sighted)
            {
                i.RumourCentre = i.LastSeen;
                i.RumourRadius = IncidentRules.SightedRadius;
            }
            else
            {
                float roam = i.Kind switch { IncidentKind.Horde => HordeSpread, IncidentKind.Ruin => 0f, _ => IncidentRules.RareRoam };
                i.RumourCentre = IncidentRules.RumourCentre(i.Spot, IncidentRules.RumourOffset(roam), rng);
                i.RumourRadius = IncidentRules.RumourRadius;
            }
            i.NextRumourHour = Now + IncidentRules.HordeRumourEvery;
            i.Quest = PostQuest(i);
            EventBus.Publish(new IncidentRumouredEvent { Incident = i });
        }

        Quest PostQuest(Incident i)
        {
            switch (i.Kind)
            {
                case IncidentKind.RareMonster:
                {
                    var species = i.Target != null ? i.Target.Def : null;
                    if (species == null) return null;
                    i.Requirement = IncidentRules.RareRequirement(_eco.BirthLevelAt(i.RumourCentre));
                    int gold = IncidentRules.RareReward(Vector2.Distance(i.RumourCentre, _world.TownCenterWorld));
                    var draft = new QuestDraft
                    {
                        Type = QuestType.Hunt, Species = species, Count = 1, Point = i.RumourCentre, HasPoint = true, Radius = i.RumourRadius,
                        RewardGold = gold, RewardExp = QuestPricing.RewardExp(gold), MaxParty = IncidentRules.RareParty,
                    };
                    return _board.PostIncidentQuest(draft, i);
                }
                case IncidentKind.Rescue:
                {
                    var species = i.Members.Count > 0 ? i.Members[0].Def : null;
                    if (species == null) return null;
                    var danger = QuestBoard.HuntDanger(i.TargetLevel, i.Members.Count);
                    int gold = IncidentRules.RescueReward(danger, i.Members.Count, Vector2.Distance(i.RumourCentre, _world.TownCenterWorld));
                    var draft = new QuestDraft
                    {
                        Type = QuestType.Hunt, Species = species, Count = i.Members.Count, Point = i.RumourCentre, HasPoint = true, Radius = i.RumourRadius,
                        RewardGold = gold, RewardExp = QuestPricing.RewardExp(gold), MaxParty = QuestPricing.PartyFor(danger),
                    };
                    return _board.PostIncidentQuest(draft, i);
                }
                case IncidentKind.Ruin:
                {
                    var species = i.Members.Count > 0 ? i.Members[0].Def : null;
                    if (species == null) return null;
                    int guards = i.Members.Count;
                    i.Requirement = IncidentRules.RuinRequirement(i.TargetLevel, guards);
                    int gold = IncidentRules.RuinReward(i.TargetLevel, guards, Vector2.Distance(i.Spot, _world.TownCenterWorld));
                    var draft = new QuestDraft
                    {
                        Type = QuestType.Hunt, Species = species, Count = 1, Point = i.RumourCentre, HasPoint = true, Radius = i.RumourRadius,
                        RewardGold = gold, RewardExp = QuestPricing.RewardExp(gold), MaxParty = IncidentRules.RuinParty,
                    };
                    return _board.PostIncidentQuest(draft, i);
                }
                case IncidentKind.Horde:
                {
                    var species = i.Members.Count > 0 ? i.Members[0].Def : null;
                    if (species == null) return null;
                    i.Requirement = IncidentRules.HordeRequirement(i.HordeLevel, i.HordeSize);
                    int gold = IncidentRules.HordeReward(i.HordeLevel, i.HordeSize, Vector2.Distance(i.RumourCentre, _world.TownCenterWorld));
                    var draft = new QuestDraft
                    {
                        Type = QuestType.Hunt, Species = species, Count = IncidentRules.HordeKills(i.HordeSize), Point = i.RumourCentre, HasPoint = true,
                        Radius = i.RumourRadius, RewardGold = gold, RewardExp = QuestPricing.RewardExp(gold), MaxParty = IncidentRules.HordeParty,
                    };
                    return _board.PostIncidentQuest(draft, i);
                }
            }
            return null;
        }

        void End(Incident i, IncidentOutcome outcome)
        {
            if (i.Over) return;
            i.Outcome = outcome;
            i.EndedHour = Now;
            _active.Remove(i);
            _history.Add(i);
            Debug.Log($"[Incident] #{i.Id} {i.Kind} over: {outcome}");
            EventBus.Publish(new IncidentEndedEvent { Incident = i, Known = i.Rumoured || i.Sighted });
        }

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SavedIncident
        {
            public int id;
            public IncidentKind kind;
            public float started;
            public Vector2 spot;
            public float rumour_at;
            public float ends_at;
            public bool rumoured;
            public Vector2 rumour_centre;
            public float rumour_radius;
            public bool sighted;
            public Vector2 last_seen;
            public float last_seen_hour;
            public IncidentOutcome outcome;
            public float ended;
            public string quest;
            public string target;
            public string target_name;
            public int target_level;
            public float requirement;
            public List<string> members = new();
            public int horde_size;
            public string species_name;
            public int horde_level;
            public int band = -1;
            public int site = -1;
            public float next_rumour;
            public bool traveller;
            public Vector2 traveller_pos;
            public float traveller_hp;
            public float traveller_max;
            public Traveller.Phase traveller_phase;
            public Vector2 destination;
            public int ruin = -1;
            public bool chest_opened;
        }

        [System.Serializable]
        class SaveData
        {
            public int next_id = 1;
            public List<SavedIncident> active = new();
            public List<SavedIncident> history = new();
            public List<string> stragglers = new();
            public List<float> straggler_hours = new();
            public List<int> looted = new();
        }

        SaveData _restoring;
        readonly List<(Incident i, SavedIncident s)> _restored = new();

        public string SaveKey => "incidents";

        SavedIncident CaptureOne(Incident i)
        {
            var s = new SavedIncident
            {
                id = i.Id, kind = i.Kind, started = i.StartedHour, spot = i.Spot, rumour_at = i.RumourAtHour, ends_at = i.EndsAtHour,
                rumoured = i.Rumoured, rumour_centre = i.RumourCentre, rumour_radius = i.RumourRadius, sighted = i.Sighted,
                last_seen = i.LastSeen, last_seen_hour = i.LastSeenHour, outcome = i.Outcome, ended = i.EndedHour, quest = i.Quest?.Id,
                target = i.Target != null && i.Target.IsAlive ? i.Target.UnitId : null, target_name = i.TargetName, target_level = i.TargetLevel,
                requirement = i.Requirement, horde_size = i.HordeSize, species_name = i.SpeciesName, horde_level = i.HordeLevel,
                band = i.Band != null && !i.Over ? i.Band.Id : -1, site = i.Site != null ? _eco.Nests.IndexOf(i.Site) : -1, next_rumour = i.NextRumourHour,
                destination = i.Destination, ruin = i.RuinIndex, chest_opened = i.ChestOpened,
            };
            foreach (var m in i.Members) if (m != null && m.IsAlive) s.members.Add(m.UnitId);
            if (i.Traveller != null && i.Traveller.IsAlive)
            {
                s.traveller = true;
                s.traveller_pos = new Vector2(SaveTime.Hundredths(i.Traveller.Position.x), SaveTime.Hundredths(i.Traveller.Position.y));
                s.traveller_hp = SaveTime.Hundredths(i.Traveller.Hp);
                s.traveller_max = SaveTime.Hundredths(i.Traveller.Stats.MaxHp);
                s.traveller_phase = i.Traveller.Current;
            }
            return s;
        }

        public string Capture()
        {
            var s = new SaveData { next_id = _nextId };
            foreach (var i in _active) s.active.Add(CaptureOne(i));
            foreach (var i in _history) s.history.Add(CaptureOne(i));
            foreach (var (m, hour) in _stragglers)
                if (m != null && m.IsAlive) { s.stragglers.Add(m.UnitId); s.straggler_hours.Add(hour); }
            s.looted.AddRange(_looted);
            s.looted.Sort();
            return JsonUtility.ToJson(s);
        }

        /// <summary>Incidents are registered under their keys (quests point at them); their monsters and quests come in <see cref="Link"/>.</summary>
        public void Restore(string json, SaveContext ctx)
        {
            _restoring = JsonUtility.FromJson<SaveData>(json);
            _nextId = _restoring.next_id;
            _restored.Clear();
            foreach (var si in _restoring.active) _active.Add(RestoreOne(si, ctx));
            foreach (var si in _restoring.history) _history.Add(RestoreOne(si, ctx));
        }

        Incident RestoreOne(SavedIncident s, SaveContext ctx)
        {
            var i = new Incident
            {
                Id = s.id, Kind = s.kind, StartedHour = s.started, Spot = s.spot, RumourAtHour = s.rumour_at, EndsAtHour = s.ends_at,
                Rumoured = s.rumoured, RumourCentre = s.rumour_centre, RumourRadius = s.rumour_radius, Sighted = s.sighted,
                LastSeen = s.last_seen, LastSeenHour = s.last_seen_hour, Outcome = s.outcome, EndedHour = s.ended,
                TargetName = s.target_name, TargetLevel = s.target_level, Requirement = s.requirement,
                HordeSize = s.horde_size, SpeciesName = s.species_name, HordeLevel = s.horde_level, NextRumourHour = s.next_rumour,
                Site = s.site >= 0 && s.site < _eco.Nests.Count ? _eco.Nests[s.site] : null, Destination = s.destination,
                RuinIndex = s.ruin, ChestOpened = s.chest_opened,
            };
            if (s.traveller)
            {
                // Registered now: the monsters on it find it again when their fights are put back (Resume).
                var t = Traveller.Create($"TRAVELLER_{i.Id}", i.TargetName, s.traveller_pos, s.destination, transform);
                t.RestoreState(s.traveller_phase, s.traveller_max, s.traveller_hp);
                t.Arrived = arrived => { if (i.Traveller == arrived) i.Traveller = null; };
                i.Traveller = t;
                ctx.Register(t.UnitId, t);
            }
            ctx.Register(Incident.SaveKeyOf(i.Id), i);
            _restored.Add((i, s));
            return i;
        }

        public void Link(SaveContext ctx)
        {
            foreach (var (i, s) in _restored)
            {
                if (!string.IsNullOrEmpty(s.quest)) i.Quest = ctx.Find<Quest>(s.quest);
                if (!string.IsNullOrEmpty(s.target)) i.Target = ctx.Find<Monster>(s.target);
                foreach (var id in s.members) if (ctx.Find<Monster>(id) is { } m) i.Members.Add(m);
                // The march was put back by the migration service's own Link (registered before this one).
                if (s.band >= 0 && Services.TryGet<MigrationService>(out var migration)) i.Band = migration.BandById(s.band);
            }
            _stragglers.Clear();
            for (int k = 0; k < _restoring.stragglers.Count && k < _restoring.straggler_hours.Count; k++)
                if (ctx.Find<Monster>(_restoring.stragglers[k]) is { } m) _stragglers.Add((m, _restoring.straggler_hours[k]));
            // The ruins as they were: caved in and an open chest where looted, a closed one where explorers are awaited.
            _looted.Clear();
            foreach (int r in _restoring.looted) _looted.Add(r);
            foreach (var site in _world.Map.Ruins)
            {
                ShowEntrance(site);
                if (_looted.Contains(site.Index)) ShowChest(site, open: true);
                else if (ActiveAt(site) != null) ShowChest(site, open: false);
            }
            _restored.Clear();
            _restoring = null;
        }
    }
}
