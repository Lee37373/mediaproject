using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using GuildMaster.Units;
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
        }

        void Update()
        {
            // Paused: nothing moves (P4-80; a loaded game starts with the check due and would otherwise run it once).
            if (_active.Count == 0 || Time.deltaTime <= 0f) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = TickInterval;
            foreach (var i in _active.ToList()) Tick(i);
        }

        void Tick(Incident i)
        {
            switch (i.Kind)
            {
                case IncidentKind.RareMonster: TickRare(i); break;
                case IncidentKind.Horde: TickHorde(i); break;
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

        /// <summary>A kind that lives at that cell, drawn by the weights of the map's first dens (no region lords).</summary>
        MonsterDefinition PickKind(Vector2Int cell, GameRandom rng)
        {
            var region = _world.Map.RegionOf(cell.x, cell.y);
            var biome = MapGenerator.ClassifyBiome(_world.Map, cell);
            var kinds = _eco.AllSpecies.Where(s => s != null && !s.isRegionLord && EcosystemManager.Fit(s, region, biome) > 0f).ToList();
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
                float roam = i.Kind == IncidentKind.Horde ? HordeSpread : IncidentRules.RareRoam;
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
        }

        [System.Serializable]
        class SaveData
        {
            public int next_id = 1;
            public List<SavedIncident> active = new();
            public List<SavedIncident> history = new();
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
            };
            foreach (var m in i.Members) if (m != null && m.IsAlive) s.members.Add(m.UnitId);
            return s;
        }

        public string Capture()
        {
            var s = new SaveData { next_id = _nextId };
            foreach (var i in _active) s.active.Add(CaptureOne(i));
            foreach (var i in _history) s.history.Add(CaptureOne(i));
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
                Site = s.site >= 0 && s.site < _eco.Nests.Count ? _eco.Nests[s.site] : null,
            };
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
            _restored.Clear();
            _restoring = null;
        }
    }
}
