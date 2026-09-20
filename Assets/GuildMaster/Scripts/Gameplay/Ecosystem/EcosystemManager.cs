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
    /// <summary>A species wiped out earlier has settled again (P5-1: the last nest is protected).</summary>
    public struct SpeciesReturnedEvent { public MonsterDefinition Species; public Nest Nest; }
    public struct NestClearedEvent { public Nest Nest; public Unit Killer; }
    public struct NestDiscoveredEvent { public Nest Nest; }
    public struct NestRecolonizedEvent { public Nest Nest; }

    /// <summary>
    /// Monster ecosystem simulation:
    /// - logistic growth per nest each game hour,
    /// - predators' growth scales with prey availability; starving predators die off,
    /// - empty nests can be recolonized by surviving members of the species,
    /// - a species wiped out entirely is gone for a few days and then settles again
    ///   (P5-1, user decision 2026-09-20: extinction is not permanent).
    /// </summary>
    public class EcosystemManager : MonoBehaviour
    {
        readonly List<Nest> _nests = new();
        readonly Dictionary<MonsterDefinition, int> _population = new();
        readonly HashSet<MonsterDefinition> _extinct = new();
        /// <summary>Day each wiped-out species disappeared, so it can settle again (P5-1).</summary>
        readonly Dictionary<MonsterDefinition, int> _goneSinceDay = new();
        GameDatabase _db;
        int _day = 1;

        [SerializeField] float starvationChancePerHour = 0.03f;
        [SerializeField] float recolonizeChancePerDay = 0.35f;

        /// <summary>Days a wiped-out species stays away before it settles again (0 = never).</summary>
        public int SpeciesReturnDays { get; set; } = 3;

        /// <summary>
        /// Nests the town already knows about at the start (P5-1). Species are placed by distance,
        /// so the nearest ones are the weakest — this is the guild's first quest, not a spoiler.
        /// </summary>
        public const int StartingKnownNests = 2;

        /// <summary>Marks the closest nests as discovered so day one has something to hunt.</summary>
        void RevealNearestNests(MapData map, int count)
        {
            if (count <= 0) return;
            foreach (var nest in _nests
                         .Where(n => !n.IsEmpty)
                         .OrderBy(n => (n.Cell - map.TownCenter).sqrMagnitude)
                         .Take(count))
                nest.RevealAtStart();
        }

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

            RevealNearestNests(map, StartingKnownNests);

            EventBus.Subscribe<HourPassedEvent>(OnHourPassed);
            EventBus.Subscribe<DayPassedEvent>(OnDayPassed);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<HourPassedEvent>(OnHourPassed);
            EventBus.Unsubscribe<DayPassedEvent>(OnDayPassed);
        }

        /// <summary>
        /// How far outside its band a species may still settle (0..1 of the map's span). Keep this
        /// tight: at 0.45 the bands overlapped so much that goblins and goblin warriors shared the
        /// same ring and the walk out stopped getting harder.
        /// </summary>
        public const float BandWidth = 0.22f;
        /// <summary>Multiplier applied when the site's biome does not suit the species.</summary>
        public const float BiomeMismatch = 0.35f;
        /// <summary>Floor on the band term, so a species is unlikely — not impossible — off its ring.</summary>
        public const float OffBandFloor = 0.005f;

        /// <summary>
        /// How well a species suits a nest site: mostly its distance from town (P5-1, user decision
        /// 2026-09-20 — weak species ring the walls, dangerous ones live out at the edges), then the
        /// biome, then rarity. Pure, so the placement rule can be tested.
        /// </summary>
        public static float Fit(MonsterDefinition species, float distance01, Biome biome)
        {
            float band = Mathf.Max(OffBandFloor, 1f - Mathf.Abs(distance01 - species.distanceBand) / BandWidth);
            float biomeFit = species.biomes.Contains(biome) ? 1f : BiomeMismatch;
            return band * biomeFit * Rarity(species);
        }

        static float Rarity(MonsterDefinition s) => s.danger switch
        {
            DangerLevel.High => 0.15f,
            DangerLevel.Medium => 0.6f,
            _ => 1f,
        };

        /// <summary>
        /// Hands every nest site to a species. Each species gets its best free site first so none is
        /// missing from the world, then the rest go to whoever fits that distance best.
        /// </summary>
        public static List<MonsterDefinition> AssignSpecies(List<MonsterDefinition> species, List<NestSite> sites, GameRandom rng)
        {
            var result = new MonsterDefinition[sites.Count];
            if (sites.Count == 0 || species.Count == 0) return result.ToList();

            // Normalise the walk distances so the bands mean the same thing on any map.
            int minD = int.MaxValue, maxD = int.MinValue;
            foreach (var s in sites)
            {
                if (s.DistanceFromTown < minD) minD = s.DistanceFromTown;
                if (s.DistanceFromTown > maxD) maxD = s.DistanceFromTown;
            }
            float span = Mathf.Max(1, maxD - minD);
            var t = new float[sites.Count];
            for (int i = 0; i < sites.Count; i++) t[i] = (sites[i].DistanceFromTown - minD) / span;

            var order = Enumerable.Range(0, sites.Count).ToList();
            rng.Shuffle(order); // ties fall differently per seed

            // 1. Coverage: nearest-to-town species pick first, so the ring around the walls is filled
            //    by the weak ones even when there are fewer sites than species. Each takes its
            //    guaranteed share (minNests) — the early economy lives off the closest, weakest nests.
            foreach (var m in species.OrderBy(x => x.distanceBand))
            {
                for (int taken = 0; taken < Mathf.Max(1, m.minNests); taken++)
                {
                    int best = -1;
                    float bestFit = float.MinValue;
                    foreach (int i in order)
                    {
                        if (result[i] != null) continue;
                        // Rarity must not decide who *exists*, only how often; band and biome alone.
                        float fit = Fit(m, t[i], sites[i].Biome) / Mathf.Max(0.001f, Rarity(m));
                        if (fit > bestFit) { bestFit = fit; best = i; }
                    }
                    if (best < 0) break; // nothing free left
                    result[best] = m;
                }
            }

            // 2. The remaining sites go to a weighted draw among the species that suit that distance.
            foreach (int i in order)
            {
                if (result[i] != null) continue;
                float total = 0f;
                foreach (var m in species) total += Fit(m, t[i], sites[i].Biome);
                float roll = rng.Range(0f, total);
                foreach (var m in species)
                {
                    roll -= Fit(m, t[i], sites[i].Biome);
                    if (roll <= 0f) { result[i] = m; break; }
                }
                result[i] ??= species[^1];
            }
            return result.ToList();
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
                _goneSinceDay[species] = _day;
                Debug.Log($"[Ecosystem] {species.displayName} wiped out (day {_day})");
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
            _day = e.Day;
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
            _day = e.Day;
            BringBackWipedOutSpecies(e.Day);

            var rng = GameRandom.Shared;
            foreach (var nest in _nests)
            {
                if (!nest.IsEmpty || IsExtinct(nest.Species)) continue;
                // A single survivor is enough to repopulate an empty nest of its own kind.
                if (GetPopulation(nest.Species) < 1 || !rng.Chance(recolonizeChancePerDay)) continue;
                Spawn(nest);
                Spawn(nest);
                EventBus.Publish(new NestRecolonizedEvent { Nest = nest });
            }
        }

        /// <summary>
        /// Settles species that were wiped out entirely back into one of their empty nests
        /// (P5-1, user decision: extinction is not permanent — the last nest is protected).
        /// </summary>
        void BringBackWipedOutSpecies(int day)
        {
            if (SpeciesReturnDays <= 0 || _extinct.Count == 0) return;
            var returning = _extinct
                .Where(s => day - (_goneSinceDay.TryGetValue(s, out var d) ? d : day) >= SpeciesReturnDays)
                .ToList();
            foreach (var species in returning)
            {
                var nest = PickReturnNest(species);
                if (nest == null) continue; // no room right now; try again tomorrow
                if (nest.Species != species) nest.ChangeSpecies(species);
                _extinct.Remove(species);
                _goneSinceDay.Remove(species);
                Spawn(nest);
                Spawn(nest);
                Debug.Log($"[Ecosystem] {species.displayName} settled again at {nest.NestId} (day {day})");
                EventBus.Publish(new SpeciesReturnedEvent { Species = species, Nest = nest });
            }
        }

        /// <summary>An empty nest for a returning species: its own old ground first, else any free one.</summary>
        Nest PickReturnNest(MonsterDefinition species)
            => _nests.FirstOrDefault(n => n.IsEmpty && n.Species == species)
            ?? _nests.FirstOrDefault(n => n.IsEmpty && !_extinct.Contains(n.Species) && GetPopulation(n.Species) > 0)
            ?? _nests.FirstOrDefault(n => n.IsEmpty);

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
