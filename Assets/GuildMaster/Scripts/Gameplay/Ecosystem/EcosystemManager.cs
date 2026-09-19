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
    public struct SpeciesExtinctEvent { public MonsterDefinition Species; }
    public struct NestClearedEvent { public Nest Nest; public Unit Killer; }
    public struct NestDiscoveredEvent { public Nest Nest; }
    public struct NestRecolonizedEvent { public Nest Nest; }

    /// <summary>
    /// Monster ecosystem simulation:
    /// - logistic growth per nest each game hour,
    /// - predators' growth scales with prey availability; starving predators die off,
    /// - empty nests can be recolonized by surviving members of the species,
    /// - a species whose global population hits zero is extinct and never respawns.
    /// </summary>
    public class EcosystemManager : MonoBehaviour
    {
        readonly List<Nest> _nests = new();
        readonly Dictionary<MonsterDefinition, int> _population = new();
        readonly HashSet<MonsterDefinition> _extinct = new();
        GameDatabase _db;

        [SerializeField] float starvationChancePerHour = 0.03f;
        [SerializeField] float recolonizeChancePerDay = 0.35f;

        public IReadOnlyList<Nest> Nests => _nests;
        public IEnumerable<MonsterDefinition> Species => _db.monsters;
        public int TotalMonsters => _population.Values.Sum();

        public int GetPopulation(MonsterDefinition species) => species != null && _population.TryGetValue(species, out var n) ? n : 0;
        public bool IsExtinct(MonsterDefinition species) => _extinct.Contains(species);

        public void Init(GameDatabase db, MapData map, GameRandom rng)
        {
            _db = db;
            Services.Get<PoolService>().Register(Monster.PoolKey, Monster.CreatePooledInstance, prewarm: 32, maxSize: 400);
            foreach (var s in db.monsters) _population[s] = 0;

            var assignment = AssignSpecies(db.monsters, map.Nests, rng);
            for (int i = 0; i < map.Nests.Count; i++)
            {
                var go = new GameObject();
                go.transform.SetParent(transform, false);
                var nest = go.AddComponent<Nest>();
                nest.Init(this, $"N_{i + 1:00}", assignment[i], map.Nests[i]);
                _nests.Add(nest);
                for (int k = 0; k < assignment[i].initialPopulation; k++) Spawn(nest);
            }

            EventBus.Subscribe<HourPassedEvent>(OnHourPassed);
            EventBus.Subscribe<DayPassedEvent>(OnDayPassed);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<HourPassedEvent>(OnHourPassed);
            EventBus.Unsubscribe<DayPassedEvent>(OnDayPassed);
        }

        /// <summary>Every species gets at least one biome-appropriate nest; the rest are weighted random.</summary>
        static List<MonsterDefinition> AssignSpecies(List<MonsterDefinition> species, List<NestSite> sites, GameRandom rng)
        {
            var result = new MonsterDefinition[sites.Count];
            var order = Enumerable.Range(0, sites.Count).ToList();
            rng.Shuffle(order);

            // Guarantee coverage, rarest (fewest biomes / smallest capacity) first.
            foreach (var s in species.OrderBy(s => s.biomes.Count).ThenBy(s => s.nestCapacity))
            {
                int idx = order.FindIndex(i => result[i] == null && s.biomes.Contains(sites[i].Biome));
                if (idx < 0) idx = order.FindIndex(i => result[i] == null);
                if (idx >= 0) idx = order[idx];
                if (idx >= 0) result[idx] = s;
            }
            // Fill the rest; strong species are rarer.
            foreach (int i in order)
            {
                if (result[i] != null) continue;
                var candidates = species.Where(s => s.biomes.Contains(sites[i].Biome)).ToList();
                if (candidates.Count == 0) candidates = species;
                float total = candidates.Sum(Weight);
                float roll = rng.Range(0f, total);
                foreach (var c in candidates)
                {
                    roll -= Weight(c);
                    if (roll <= 0f) { result[i] = c; break; }
                }
                result[i] ??= candidates[^1];
            }
            return result.ToList();

            static float Weight(MonsterDefinition s) => s.danger switch
            {
                DangerLevel.High => 0.15f,
                DangerLevel.Medium => 0.6f,
                _ => 1f,
            };
        }

        /// <summary>Adds one member to the nest and updates the population count (also used by debug tools).</summary>
        internal Monster Spawn(Nest nest)
        {
            var m = nest.SpawnMember();
            ChangePopulation(nest.Species, +1);
            return m;
        }

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
            if (pop == 0 && delta < 0 && _extinct.Add(species))
            {
                Debug.Log($"[Ecosystem] {species.displayName} extinct");
                EventBus.Publish(new SpeciesExtinctEvent { Species = species });
            }
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
                if (nest.IsEmpty || IsExtinct(nest.Species)) continue;
                var s = nest.Species;
                int pop = nest.Population;
                float food = FoodFactor(s);

                // Logistic growth; lone survivors breed slowly.
                float rate = s.growthPerHour * pop * (1f - pop / (float)nest.Capacity);
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

        void OnDayPassed(DayPassedEvent e)
        {
            var rng = GameRandom.Shared;
            foreach (var nest in _nests)
            {
                if (!nest.IsEmpty || IsExtinct(nest.Species)) continue;
                if (GetPopulation(nest.Species) < 2 || !rng.Chance(recolonizeChancePerDay)) continue;
                Spawn(nest);
                Spawn(nest);
                EventBus.Publish(new NestRecolonizedEvent { Nest = nest });
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
                if (s == null || !s.loot.Any(l => l.item == item)) continue;
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
