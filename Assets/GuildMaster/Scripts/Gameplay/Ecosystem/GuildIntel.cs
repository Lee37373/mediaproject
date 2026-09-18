using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Ecosystem
{
    /// <summary>
    /// What the guild actually knows: species and nests it has seen, and the last observed
    /// nest populations. UI, adventurer navigation, logs and LLM prompts read this instead of
    /// the real simulation (<see cref="EcosystemManager"/>), so nothing outside vision leaks.
    /// </summary>
    public class GuildIntel : MonoBehaviour
    {
        const float ScanInterval = 0.5f;

        public class NestIntel
        {
            public bool Observed;
            public int LastCount;
            public float LastSeenHour;
            public bool VisibleNow;
        }

        readonly HashSet<MonsterDefinition> _known = new();
        readonly Dictionary<Nest, NestIntel> _nests = new();
        EcosystemManager _eco;
        FogOfWar _fog;
        GameClock _clock;
        WorldService _world;
        float _timer;

        /// <summary>Living monsters currently inside the guild's vision.</summary>
        public int VisibleMonsters { get; private set; }

        public void Init(EcosystemManager eco, FogOfWar fog, GameClock clock, WorldService world)
        {
            _eco = eco;
            _fog = fog;
            _clock = clock;
            _world = world;
        }

        void Update()
        {
            if (_eco == null) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = ScanInterval;
            Scan();
        }

        void Scan()
        {
            int visible = 0;
            foreach (var u in UnitRegistry.All)
            {
                if (u is not Monster m || !m.IsAlive || !_fog.IsVisibleRaw(m.Position)) continue;
                visible++;
                _known.Add(m.Def);
            }
            VisibleMonsters = visible;

            foreach (var n in _eco.Nests)
            {
                if (!n.Discovered) continue;
                _known.Add(n.Species);
                var ni = Get(n);
                ni.VisibleNow = _fog.IsVisibleRaw(n.Cell);
                if (!ni.VisibleNow) continue;
                ni.Observed = true;
                ni.LastCount = n.Population;
                ni.LastSeenHour = _clock.TotalHours;
            }
        }

        public bool IsKnown(MonsterDefinition species) => species != null && _known.Contains(species);

        /// <summary>Known species in database order.</summary>
        public IEnumerable<MonsterDefinition> KnownSpecies => _eco.Species.Where(_known.Contains);

        public IEnumerable<Nest> KnownNests => _eco.Nests.Where(n => n.Discovered);

        public NestIntel Get(Nest n)
        {
            if (!_nests.TryGetValue(n, out var ni)) _nests[n] = ni = new NestIntel();
            return ni;
        }

        /// <summary>Sum of the last observed counts of the species' discovered nests.</summary>
        public int ObservedPopulation(MonsterDefinition species)
            => KnownNests.Where(n => n.Species == species).Sum(n => Get(n).LastCount);

        public int KnownNestCount(MonsterDefinition species) => KnownNests.Count(n => n.Species == species);

        /// <summary>Observed count shown to the player: live when in view, otherwise the last sighting.</summary>
        public string DescribeNest(Nest n)
        {
            var ni = Get(n);
            if (!ni.Observed) return "관측 기록 없음";
            string count = ni.LastCount > 0 ? $"{ni.LastCount}마리" : "비어 있음";
            if (ni.VisibleNow) return $"{count} (지금 보임)";
            int day = Mathf.FloorToInt(ni.LastSeenHour / 24f) + 1;
            return $"마지막 관측 {count} ({day}일 {Mathf.FloorToInt(ni.LastSeenHour % 24f)}시)";
        }

        /// <summary>
        /// Nearest discovered nest of the species that was not last seen empty (never-observed
        /// discovered nests count as possible). Null when the guild knows none.
        /// </summary>
        public Nest FindNearestKnownNest(Vector2 from, MonsterDefinition species)
        {
            Nest best = null;
            float bestSq = float.MaxValue;
            foreach (var n in KnownNests)
            {
                if (n.Species != species) continue;
                var ni = Get(n);
                if (ni.Observed && ni.LastCount <= 0) continue;
                float sq = (n.WorldPosition - from).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = n; }
            }
            return best;
        }

        /// <summary>
        /// A walkable spot worth searching: prefers unexplored ground outside town at a moderate
        /// distance. Used when the guild does not know where a quest target lives.
        /// </summary>
        public Vector2 FindSearchPoint(Vector2 from, GameRandom rng)
        {
            var grid = _world.Grid;
            Vector2 best = from;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < 24; i++)
            {
                var p = from + rng.InsideUnitCircle().normalized * rng.Range(10f, 36f);
                var cell = GridMap.WorldToCell(p);
                if (!grid.InBounds(cell) || !grid.IsWalkable(cell) || grid.IsInTown(cell)) continue;
                float unexplored = 1f - _fog.ExploredFraction(p, 4f);
                float score = unexplored * 10f - Vector2.Distance(from, p) * 0.05f + rng.Range(0f, 1f);
                if (score > bestScore) { bestScore = score; best = GridMap.CellToWorld(cell); }
            }
            return best;
        }
    }
}
