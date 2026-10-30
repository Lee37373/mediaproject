using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Monsters;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Ecosystem
{
    public struct SpeciesPopulationChangedEvent { public MonsterDefinition Species; public int Population; }
    public struct NestClearedEvent { public Nest Nest; public Unit Killer; }
    public struct NestDiscoveredEvent { public Nest Nest; }
    public struct NestRecolonizedEvent { public Nest Nest; }
    /// <summary>The far land's young are born another level stronger (P4-44): word of it reaches the town.</summary>
    public struct WildsStirredEvent { public int OutermostBonus; }

    /// <summary>
    /// Monster ecosystem simulation:
    /// - logistic growth per nest each game hour,
    /// - predators' growth scales with prey availability; starving predators die off,
    /// - an emptied nest stays empty until a band moves in: one migrating inwards, or (at the
    ///   outer sites) a new band from beyond the map edge (<see cref="MigrationService"/>, user
    ///   decision 2026-09-22; the old six-hour refill by the same kind is gone),
    /// - the far land's young are born stronger as the days go by (<see cref="WildsPressure"/>, P4-44).
    /// A kind may vanish for a while (the P4-43 "no extinction" guarantee went with the refill):
    /// newcomers from beyond the edge are of any kind and bring it back.
    /// </summary>
    public class EcosystemManager : MonoBehaviour, ISaveable, ISaveLink, ISaveResume
    {
        readonly List<Nest> _nests = new();
        readonly Dictionary<MonsterDefinition, int> _population = new();
        GameDatabase _db;

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SavedNest
        {
            public string species;
            public float growth;
            public int empty_hours;
            public bool discovered;
            public List<Vector2Int> known_gates = new();
            /// <summary>In the pack's order: the first one alive leads it.</summary>
            public List<Monster.MonsterSave> members = new();
        }

        [System.Serializable]
        class SaveData
        {
            public int next_id;
            public List<SavedNest> nests = new();
            /// <summary>Monsters with no nest (roaming packs, strays), by id.</summary>
            public List<Monster.MonsterSave> loose = new();
            public List<string> population_species = new();
            public List<int> population = new();
        }

        readonly List<(Monster monster, Monster.MonsterSave saved)> _restoring = new();

        public string SaveKey => "ecosystem";

        /// <summary>Every living monster (nest members in pack order, then the nest-less), the nests and the head counts.</summary>
        public string Capture()
        {
            var s = new SaveData { next_id = Monster.IdCounter };
            foreach (var nest in _nests)
            {
                var n = new SavedNest
                {
                    species = nest.Species.id, growth = nest.GrowthAccumulator, empty_hours = nest.EmptyHours, discovered = nest.Discovered,
                };
                n.known_gates.AddRange(nest.KnownOpenGates);
                n.known_gates.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
                foreach (var m in nest.MemberList) if (m != null && m.IsAlive) n.members.Add(m.CaptureSave());
                s.nests.Add(n);
            }
            var loose = new List<Monster>();
            foreach (var u in UnitRegistry.All) if (u is Monster m && m.IsAlive && m.Home == null) loose.Add(m);
            loose.Sort((a, b) => string.CompareOrdinal(a.UnitId, b.UnitId));
            foreach (var m in loose) s.loose.Add(m.CaptureSave());
            foreach (var species in _db.monsters)
            {
                s.population_species.Add(species.id);
                s.population.Add(GetPopulation(species));
            }
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            _restoring.Clear();
            if (s.nests.Count != _nests.Count) ctx.Warnings.Add($"ecosystem: saved {s.nests.Count} nests, map has {_nests.Count}");
            for (int i = 0; i < _nests.Count && i < s.nests.Count; i++)
            {
                var nest = _nests[i];
                var saved = s.nests[i];
                nest.RestoreSpecies(_db.Monster(saved.species));
                nest.GrowthAccumulator = saved.growth;
                nest.EmptyHours = saved.empty_hours;
                nest.RestoreDiscovered(saved.discovered);
                nest.KnownOpenGates.Clear();
                foreach (var g in saved.known_gates) nest.KnownOpenGates.Add(g);
                foreach (var ms in saved.members) RestoreMonster(ms, nest, ctx);
            }
            foreach (var ms in s.loose) RestoreMonster(ms, null, ctx);
            for (int i = 0; i < s.population_species.Count && i < s.population.Count; i++)
                if (_db.Monster(s.population_species[i]) is { } species) _population[species] = s.population[i];
            Monster.IdCounter = Mathf.Max(Monster.IdCounter, s.next_id);
        }

        void RestoreMonster(Monster.MonsterSave ms, Nest nest, SaveContext ctx)
        {
            var def = _db.Monster(ms.species);
            if (def == null) { ctx.Warnings.Add("ecosystem: unknown species " + ms.species); return; }
            var m = Monster.RestoreSave(ms, def, nest, ctx);
            nest?.RestoreMember(m);
            _restoring.Add((m, ms));
        }

        public void Link(SaveContext ctx)
        {
            foreach (var (m, saved) in _restoring) m.LinkSave(saved, ctx);
        }

        public void Resume(SaveContext ctx)
        {
            foreach (var (m, saved) in _restoring) m.ResumeSave(saved, ctx);
            _restoring.Clear();
        }

        [SerializeField] float starvationChancePerHour = 0.03f;

        // P5-1 handed the guild its two nearest nests so day one was not pure fog. Removed on
        // 2026-09-20 (user decision): an orange minimap dot on ground nobody had walked broke the
        // information rule. Every nest is found by looking at it now.

        /// <summary>Breeding rate at the farthest nest site, relative to the ring by the town.</summary>
        public const float EdgeGrowthFactor = 0.4f;

        /// <summary>
        /// Breeding slows the farther a site lies from the town (user decision 2026-09-22): the near
        /// ring is what a growing guild hunts, the strong kinds out at the edge fill up slowly and
        /// creep in over the days (P4-56). A strong band that settles close in breeds fast there —
        /// by then the guild has grown into it. 1 at the innermost site, <see cref="EdgeGrowthFactor"/>
        /// at <paramref name="outermost"/> and beyond (pure, tested; the game passes <see cref="InnerEdge"/>, P4-81).
        /// </summary>
        public static float GrowthFactor(float distance, float innermost, float outermost)
            => Mathf.Lerp(1f, EdgeGrowthFactor, Mathf.InverseLerp(innermost, outermost, distance));

        /// <summary>
        /// P4-81 (user decision 2026-10-16): the inner land keeps the 256 map's rules out to this distance
        /// from the town (about its farthest nest, where the slime king lived): levels 1 → 15, breeding
        /// 1 → <see cref="EdgeGrowthFactor"/>. The 512 map's land beyond is new: levels go on rising to the
        /// outermost site, breeding stays at the edge rate.
        /// </summary>
        public const float InnerEdge = 180f;

        float _innermost, _outermost;
        Vector2 _town;

        /// <summary>Breeding factor of a site at this position (the rule is public; the guild may use it).</summary>
        public float GrowthFactorAt(Vector2 p) => GrowthFactor(Vector2.Distance(p, _town), _innermost, InnerEdge);

        /// <summary>Farthest nest site from the town (cells).</summary>
        public float OutermostDistance => _outermost;

        /// <summary>
        /// How deep into the wilds a spot lies: 0 at the innermost nest site's distance from the town,
        /// 1 at <see cref="InnerEdge"/>, 2 at the outermost site (pure rule, <see cref="DepthAt"/>), plus its zone's own
        /// danger (P4-102, user decision 2026-10-24: by zone and by distance — <see cref="ZoneSite.DangerShift"/>).
        /// </summary>
        public float Depth(Vector2 p) => Mathf.Clamp(DepthAt(Vector2.Distance(p, _town), _innermost, _outermost) + ZoneShift(p), 0f, WildsPressure.FullDepth);

        MapData _map;

        float ZoneShift(Vector2 p)
        {
            if (_map == null) return 0f;
            var c = GridMap.WorldToCell(p);
            // The deep woods breed strong (P4-103: their dens ambush whoever cuts through).
            float deep = _map.IsDeep(c.x, c.y) ? DeepWoodRules.DepthShift : 0f;
            return (_map.ZoneAt(c.x, c.y)?.DangerShift ?? 0f) + deep;
        }

        public static float DepthAt(float distance, float innermost, float outermost)
        {
            if (distance <= InnerEdge) return Mathf.InverseLerp(innermost, InnerEdge, distance);
            return 1f + Mathf.InverseLerp(InnerEdge, Mathf.Max(outermost, InnerEdge + 1f), distance);
        }

        /// <summary>
        /// The level monsters are born at around this spot (P4-58: the level is the strength), the far land the higher
        /// the longer the game has run (P4-44). Distance and time are no secret: the guild reckons with it too.
        /// </summary>
        public int BirthLevelAt(Vector2 p) => WildsPressure.BirthLevel(Depth(p), Days);

        /// <summary>The same by distance from the town, whichever way (P4-99: the near ring a newcomer is measured by).</summary>
        public int BirthLevelAtDistance(float distance) => WildsPressure.BirthLevel(DepthAt(distance, _innermost, _outermost), Days);

        /// <summary>Nearest nest site to the town (cells).</summary>
        public float InnermostDistance => _innermost;

        /// <summary>Game days gone (fractional), which the wilds' pressure follows.</summary>
        public static float Days => Services.TryGet<GameClock>(out var clock) ? clock.TotalHours / 24f : 0f;

        /// <summary>The outermost sites' bonus last told to the guild (-1: not yet this session — a loaded game says nothing).</summary>
        int _wildsTold = -1;

        /// <summary>
        /// Every site breeds at today's level and its residents grow towards it; a level more out at the edge is news (P4-44).
        /// </summary>
        void StirTheWilds()
        {
            var rng = GameRandom.Shared;
            foreach (var nest in _nests)
            {
                nest.BirthLevel = BirthLevelAt(nest.WorldPosition);
                // Rolled only for those behind (the shared random draws stay as they were while nobody is).
                foreach (var m in nest.Members)
                    if (m != null && m.IsAlive && m.Level < nest.BirthLevel && WildsPressure.GrowsThisHour(m.Level, nest.BirthLevel, rng.Value)) m.GrowWithTheLand();
            }
            int outermost = WildsPressure.LevelBonus(WildsPressure.FullDepth, Days);
            if (_wildsTold < 0) _wildsTold = outermost;
            else if (outermost > _wildsTold)
            {
                _wildsTold = outermost;
                EventBus.Publish(new WildsStirredEvent { OutermostBonus = outermost });
            }
        }

        /// <summary>Species that exist in the game (newcomers from beyond the edge are drawn from these).</summary>
        internal IReadOnlyList<MonsterDefinition> AllSpecies => _db.monsters;

        public ListView<Nest> Nests => new(_nests);
        public IEnumerable<MonsterDefinition> Species => _db.monsters;
        public int TotalMonsters => _population.Values.Sum();

        public int GetPopulation(MonsterDefinition species) => species != null && _population.TryGetValue(species, out var n) ? n : 0;

        public void Init(GameDatabase db, MapData map, GameRandom rng)
        {
            _db = db;
            _map = map;
            Services.Get<PoolService>().Register(Monster.PoolKey, Monster.CreatePooledInstance, prewarm: 32, maxSize: 400);
            foreach (var s in db.monsters) _population[s] = 0;

            _town = GridMap.CellToWorld(map.TownCenter);
            _innermost = map.Nests.Count == 0 ? 0f : map.Nests.Min(s => Vector2.Distance(GridMap.CellToWorld(s.Cell), _town));
            _outermost = map.Nests.Count == 0 ? 1f : map.Nests.Max(s => Vector2.Distance(GridMap.CellToWorld(s.Cell), _town));
            var assignment = AssignSpecies(db.monsters, map.Nests, rng);
            // Region lords (P4-83): the farthest site of each outer region is a lord's lair, held by its guards' kind.
            var lairs = new Dictionary<int, MonsterDefinition>();
            foreach (var kv in RegionLords.PickLairs(map.Nests, _town, InnerEdge))
            {
                var lord = db.monsters.FirstOrDefault(m => m.isRegionLord && m.lordRegion == kv.Key && m.lairEscort != null);
                if (lord == null) continue;
                lairs[kv.Value] = lord;
                assignment[kv.Value] = lord.lairEscort;
            }
            for (int i = 0; i < map.Nests.Count; i++)
            {
                var go = new GameObject();
                go.transform.SetParent(transform, false);
                var nest = go.AddComponent<Nest>();
                nest.Init(this, $"N_{i + 1:00}", assignment[i], map.Nests[i]);
                nest.GrowthFactor = GrowthFactorAt(nest.WorldPosition);
                nest.BirthLevel = BirthLevelAt(nest.WorldPosition);
                lairs.TryGetValue(i, out var lairLord);
                if (lairLord != null) nest.MakeLair(lairLord);
                _nests.Add(nest);
                // A loaded game brings its own monsters back (P5-4).
                if (Session.GameSettings.RestoresSection(SaveKey)) continue;
                if (lairLord != null)
                {
                    // The lord leads its guards (it is their boss, so the first one born).
                    Spawn(nest, null, true, lairLord);
                    for (int k = 0; k < RegionLords.InitialEscorts; k++) Spawn(nest);
                    continue;
                }
                // A founded pack may have a boss (P4-57): the first one born.
                bool boss = GameRandom.Shared.Chance(MonsterBoss.Chance);
                for (int k = 0; k < assignment[i].initialPopulation; k++) Spawn(nest, null, boss && k == 0);
            }

            EventBus.Subscribe<HourPassedEvent>(OnHourPassed);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<HourPassedEvent>(OnHourPassed);
        }

        /// <summary>Multiplier applied when the site's biome is not one the species prefers.</summary>
        public const float BiomeMismatch = 0.35f;

        /// <summary>
        /// How well a species suits a nest site (P4-58, user decision 2026-09-22: by terrain only —
        /// how strong its young are is the site's distance, via the birth level). Zero where it does
        /// not live; then the preferred biome, then rarity. Pure, so the placement rule can be tested.
        /// </summary>
        public static float Fit(MonsterDefinition species, Region region, Biome biome)
        {
            if (!species.LivesAt(region, biome)) return 0f;
            float biomeFit = species.biomes.Count == 0 || species.biomes.Contains(biome) ? 1f : BiomeMismatch;
            return biomeFit * Rarity(species) * species.nestWeight;
        }

        static float Rarity(MonsterDefinition s) => s.danger switch
        {
            DangerLevel.High => 0.15f,
            DangerLevel.Medium => 0.6f,
            _ => 1f,
        };

        /// <summary>
        /// Hands every nest site to a species that lives there. Each species first takes its
        /// guaranteed share (minNests) at the sites of its habitat nearest the town — the early
        /// economy lives off the slimes by the walls — then the rest go by a weighted draw among the
        /// species living at each site.
        /// </summary>
        public static List<MonsterDefinition> AssignSpecies(List<MonsterDefinition> species, List<NestSite> sites, GameRandom rng)
        {
            var result = new MonsterDefinition[sites.Count];
            if (sites.Count == 0 || species.Count == 0) return result.ToList();

            var order = Enumerable.Range(0, sites.Count).ToList();
            rng.Shuffle(order); // ties fall differently per seed
            var byDistance = order.OrderBy(i => sites[i].DistanceFromTown).ToList();

            // P4-102: the kinds already holding dens in each zone.
            var inZone = new Dictionary<int, HashSet<MonsterDefinition>>();
            void Hold(int i, MonsterDefinition m)
            {
                result[i] = m;
                if (!inZone.TryGetValue(sites[i].Zone, out var set)) inZone[sites[i].Zone] = set = new HashSet<MonsterDefinition>();
                set.Add(m);
            }
            float Weight(MonsterDefinition m, int i)
            {
                float fit = Fit(m, sites[i].Region, sites[i].Biome);
                // The town's own fields (zone 0, also every site of a hand-made map) keep their mix.
                return fit > 0f && sites[i].Zone > 0 && inZone.TryGetValue(sites[i].Zone, out var set) && set.Contains(m) ? fit * SameZoneBias : fit;
            }

            // 1. Coverage: nobody is missing from the world.
            foreach (var m in species)
                for (int taken = 0; taken < Mathf.Max(1, m.minNests); taken++)
                {
                    int best = -1;
                    foreach (int i in byDistance)
                        if (result[i] == null && m.LivesAt(sites[i].Region, sites[i].Biome)) { best = i; break; }
                    if (best < 0) break; // no free site of its habitat left
                    Hold(best, m);
                }

            // 2. The remaining sites go to a weighted draw among the species that live there, a zone's own kind first.
            foreach (int i in order)
            {
                if (result[i] != null) continue;
                float total = 0f;
                foreach (var m in species) total += Weight(m, i);
                if (total <= 0f) { Hold(i, species[rng.Range(0, species.Count)]); continue; } // nobody's habitat: anyone
                float roll = rng.Range(0f, total);
                MonsterDefinition pick = null;
                foreach (var m in species)
                {
                    roll -= Weight(m, i);
                    if (roll <= 0f) { pick = m; break; }
                }
                Hold(i, pick ?? species[^1]);
            }
            return result.ToList();
        }

        /// <summary>A kind already holding a den in the zone weighs this much more for the zone's other dens (P4-102: a zone has its own kind).</summary>
        public const float SameZoneBias = 4f;

        /// <summary>
        /// Adds one member to the nest and updates the population count (also used by debug tools). <paramref name="kind"/>:
        /// another kind than the nest's own (a lair's lord, P4-83); counts are kept by each monster's own kind.
        /// </summary>
        internal Monster Spawn(Nest nest, Vector2? at = null, bool boss = false, MonsterDefinition kind = null)
        {
            var m = nest.SpawnMember(at, boss, kind);
            ChangePopulation(m.Def, +1);
            return m;
        }

        /// <summary>
        /// A monster with no nest at all (P4-44: newcomers from beyond the edge bound for a held site), born at
        /// <paramref name="level"/>; it is counted like any other and leaves the count through <see cref="NotifyRoamerDied"/>.
        /// </summary>
        internal Monster SpawnHomeless(MonsterDefinition species, Vector2 at, int level, bool boss = false)
        {
            var world = Services.Get<WorldService>();
            world.Grid.TryRandomWalkableNear(GridMap.WorldToCell(at), 2, GameRandom.Shared, out var cell);
            var m = Services.Get<PoolService>().Spawn<Monster>(Monster.PoolKey, GridMap.CellToWorld(cell) + GameRandom.Shared.InsideUnitCircle() * 0.3f);
            m.Setup(species, null, boss);
            m.SetLevel(Mathf.Min(MonsterLevel.MaxLevel, level + (boss ? MonsterBoss.LevelBonus : 0)));
            ChangePopulation(species, +1);
            return m;
        }

        /// <summary>A member of a roaming pack (no nest) died (P4-58).</summary>
        internal void NotifyRoamerDied(MonsterDefinition species) => ChangePopulation(species, -1);

        internal void NotifyMemberDied(Nest nest, Monster m, Unit killer)
        {
            ChangePopulation(m.Def, -1);
            if (nest.IsEmpty) EventBus.Publish(new NestClearedEvent { Nest = nest, Killer = killer });
        }

        void ChangePopulation(MonsterDefinition species, int delta)
        {
            int pop = Mathf.Max(0, GetPopulation(species) + delta);
            _population[species] = pop;
            EventBus.Publish(new SpeciesPopulationChangedEvent { Species = species, Population = pop });
        }

        /// <summary>0..1+ food availability for a predator species (1 = plenty of prey).</summary>
        public float FoodFactor(MonsterDefinition species)
        {
            if (species.prey.Count == 0) return 1f;
            int prey = species.prey.Sum(GetPopulation);
            int predators = Mathf.Max(1, GetPopulation(species));
            return Mathf.Clamp01(prey / (predators * 1.5f));
        }

        void OnHourPassed(HourPassedEvent e)
        {
            StirTheWilds();
            var rng = GameRandom.Shared;
            foreach (var nest in _nests)
            {
                if (nest.IsEmpty)
                {
                    nest.EmptyHours++;
                    continue;
                }
                nest.EmptyHours = 0;
                // Cleansed land breeds nothing (P4-83); a lair breeds its guards back only while its lord lives.
                if (nest.Sealed || nest.IsLair && nest.LivingLord == null) continue;
                var s = nest.Species;
                int pop = nest.Population;
                float food = FoodFactor(s);

                // Logistic growth; lone survivors breed slowly.
                float rate = s.growthPerHour * nest.GrowthFactor * pop * (1f - pop / (float)nest.Capacity);
                if (pop < 2) rate *= 0.35f;
                if (s.prey.Count > 0) rate *= 0.25f + 0.75f * food;
                nest.GrowthAccumulator += Mathf.Max(0f, rate);
                while (nest.GrowthAccumulator >= 1f && nest.Population < nest.Capacity)
                {
                    nest.GrowthAccumulator -= 1f;
                    Spawn(nest);
                }
                if (nest.Population >= nest.Capacity) nest.GrowthAccumulator = 0f;

                // Starvation for predators without prey.
                if (s.prey.Count > 0 && food < 0.2f && rng.Chance(starvationChancePerHour * pop))
                {
                    var victim = nest.Members[rng.Range(0, nest.Population)];
                    victim.Perish();
                }
            }
        }

        public IEnumerable<Nest> NestsOf(MonsterDefinition species) => _nests.Where(n => n.Species == species);

        /// <summary>
        /// Real (not observed) population fill 0..1 of the species that drop an item, or null if no
        /// species drops it. For the outside market only — guild-facing code must use GuildIntel.
        /// </summary>
        public float? LootAbundance(ItemDefinition item)
        {
            if (item == null) return null;
            int pop = 0, cap = 0;
            bool any = false;
            foreach (var s in _db.monsters)
            {
                if (s == null || !s.CanDrop(item)) continue;
                any = true;
                pop += GetPopulation(s);
                cap += Mathf.Max(1, NestsOf(s).Sum(n => n.Capacity));
            }
            return any ? Mathf.Clamp01(pop / (float)cap) : (float?)null;
        }

        public Nest FindNearestNest(Vector2 from, MonsterDefinition species, bool requireMembers)
        {
            Nest best = null;
            float bestSq = float.MaxValue;
            foreach (var n in _nests)
            {
                if (n.Species != species || (requireMembers && n.IsEmpty)) continue;
                float sq = (n.WorldPosition - from).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = n; }
            }
            return best;
        }

        public Nest FindNest(string id) => _nests.FirstOrDefault(n => n.NestId == id);
    }
}
