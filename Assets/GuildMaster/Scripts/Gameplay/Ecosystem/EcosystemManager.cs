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

    /// <summary>
    /// Monster ecosystem simulation:
    /// - logistic growth per nest each game hour,
    /// - predators' growth scales with prey availability; starving predators die off,
    /// - an emptied nest stays empty until a band moves in: one migrating inwards, or (at the
    ///   outer sites) a new band from beyond the map edge (<see cref="MigrationService"/>, user
    ///   decision 2026-09-22; the old six-hour refill by the same kind is gone).
    /// A kind may vanish for a while (the P4-43 "no extinction" guarantee went with the refill):
    /// newcomers from beyond the edge are of any kind and bring it back.
    /// </summary>
    public class EcosystemManager : MonoBehaviour
    {
        readonly List<Nest> _nests = new();
        readonly Dictionary<MonsterDefinition, int> _population = new();
        GameDatabase _db;

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
        /// at the outermost (pure, tested).
        /// </summary>
        public static float GrowthFactor(float distance, float innermost, float outermost)
            => Mathf.Lerp(1f, EdgeGrowthFactor, Mathf.InverseLerp(innermost, outermost, distance));

        float _innermost, _outermost;
        Vector2 _town;

        /// <summary>Breeding factor of a site at this position (the rule is public; the guild may use it).</summary>
        public float GrowthFactorAt(Vector2 p) => GrowthFactor(Vector2.Distance(p, _town), _innermost, _outermost);

        /// <summary>0 at the innermost nest site's distance from the town, 1 at the outermost.</summary>
        public float Distance01(Vector2 p) => Mathf.InverseLerp(_innermost, _outermost, Vector2.Distance(p, _town));

        /// <summary>The level monsters are born at around this spot (P4-58: the level is the strength).</summary>
        public int BirthLevelAt(Vector2 p) => MonsterLevel.BirthLevel(Distance01(p));

        /// <summary>Species that exist in the game (newcomers from beyond the edge are drawn from these).</summary>
        internal IReadOnlyList<MonsterDefinition> AllSpecies => _db.monsters;

        public IReadOnlyList<Nest> Nests => _nests;
        public IEnumerable<MonsterDefinition> Species => _db.monsters;
        public int TotalMonsters => _population.Values.Sum();

        public int GetPopulation(MonsterDefinition species) => species != null && _population.TryGetValue(species, out var n) ? n : 0;

        public void Init(GameDatabase db, MapData map, GameRandom rng)
        {
            _db = db;
            Services.Get<PoolService>().Register(Monster.PoolKey, Monster.CreatePooledInstance, prewarm: 32, maxSize: 400);
            foreach (var s in db.monsters) _population[s] = 0;

            _town = GridMap.CellToWorld(map.TownCenter);
            _innermost = map.Nests.Count == 0 ? 0f : map.Nests.Min(s => Vector2.Distance(GridMap.CellToWorld(s.Cell), _town));
            _outermost = map.Nests.Count == 0 ? 1f : map.Nests.Max(s => Vector2.Distance(GridMap.CellToWorld(s.Cell), _town));
            var assignment = AssignSpecies(db.monsters, map.Nests, rng);
            for (int i = 0; i < map.Nests.Count; i++)
            {
                var go = new GameObject();
                go.transform.SetParent(transform, false);
                var nest = go.AddComponent<Nest>();
                nest.Init(this, $"N_{i + 1:00}", assignment[i], map.Nests[i]);
                nest.GrowthFactor = GrowthFactorAt(nest.WorldPosition);
                nest.BirthLevel = MonsterLevel.BirthLevel(Distance01(nest.WorldPosition));
                _nests.Add(nest);
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
            return biomeFit * Rarity(species);
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

            // 1. Coverage: nobody is missing from the world.
            foreach (var m in species)
                for (int taken = 0; taken < Mathf.Max(1, m.minNests); taken++)
                {
                    int best = -1;
                    foreach (int i in byDistance)
                        if (result[i] == null && m.LivesAt(sites[i].Region, sites[i].Biome)) { best = i; break; }
                    if (best < 0) break; // no free site of its habitat left
                    result[best] = m;
                }

            // 2. The remaining sites go to a weighted draw among the species that live there.
            foreach (int i in order)
            {
                if (result[i] != null) continue;
                float total = 0f;
                foreach (var m in species) total += Fit(m, sites[i].Region, sites[i].Biome);
                if (total <= 0f) { result[i] = species[rng.Range(0, species.Count)]; continue; } // nobody's habitat: anyone
                float roll = rng.Range(0f, total);
                foreach (var m in species)
                {
                    roll -= Fit(m, sites[i].Region, sites[i].Biome);
                    if (roll <= 0f) { result[i] = m; break; }
                }
                result[i] ??= species[^1];
            }
            return result.ToList();
        }

        /// <summary>Adds one member to the nest and updates the population count (also used by debug tools).</summary>
        internal Monster Spawn(Nest nest, Vector2? at = null, bool boss = false)
        {
            var m = nest.SpawnMember(at, boss);
            ChangePopulation(nest.Species, +1);
            return m;
        }

        /// <summary>A member of a roaming pack (no nest) died (P4-58).</summary>
        internal void NotifyRoamerDied(MonsterDefinition species) => ChangePopulation(species, -1);

        internal void NotifyMemberDied(Nest nest, Monster m, Unit killer)
        {
            ChangePopulation(nest.Species, -1);
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
            var rng = GameRandom.Shared;
            foreach (var nest in _nests)
            {
                if (nest.IsEmpty)
                {
                    nest.EmptyHours++;
                    continue;
                }
                nest.EmptyHours = 0;
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
