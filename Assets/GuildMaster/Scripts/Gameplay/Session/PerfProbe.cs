using System.Collections.Generic;
using System.Text;
using GuildMaster.Core;
using GuildMaster.Units;
using Unity.Profiling;
using UnityEngine;

namespace GuildMaster.Session
{
    /// <summary>
    /// Debug-only performance probe (P5-2): frame times (unscaled), managed allocations per frame,
    /// live unit and projectile counts. Optionally keeps a minimum number of harmless projectiles in
    /// flight between live units so the projectile budget can be measured on its own.
    /// Created on demand by <see cref="DebugCommands.PerfStress"/> / <see cref="DebugCommands.PerfStart"/>.
    /// </summary>
    public class PerfProbe : MonoBehaviour
    {
        public static PerfProbe Instance { get; private set; }

        readonly List<float> _frameMs = new(4096);
        readonly List<long> _allocBytes = new(4096);
        ProfilerRecorder _gcAlloc;
        ProfilerRecorder _gcCount;
        int _maxUnits, _maxProjectiles;
        long _unitSum, _projectileSum;
        float _started;

        /// <summary>Projectiles kept in flight between live units (0 = off). Impacts do nothing.</summary>
        public int ProjectileFloor;

        public static PerfProbe Ensure()
        {
            if (Instance != null) return Instance;
            Instance = new GameObject("PerfProbe").AddComponent<PerfProbe>();
            return Instance;
        }

        void OnEnable()
        {
            _gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _gcCount = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count");
        }

        void OnDisable()
        {
            _gcAlloc.Dispose();
            _gcCount.Dispose();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public void ResetStats()
        {
            _frameMs.Clear();
            _allocBytes.Clear();
            _maxUnits = _maxProjectiles = 0;
            _unitSum = _projectileSum = 0;
            _started = Time.realtimeSinceStartup;
        }

        void Update()
        {
            TopUpProjectiles();
            if (_started <= 0f) return;
            _frameMs.Add(Time.unscaledDeltaTime * 1000f);
            _allocBytes.Add(_gcAlloc.Valid ? _gcAlloc.LastValue : 0);
            int units = 0;
            foreach (var u in UnitRegistry.All) if (u.IsAlive) units++;
            int projectiles = Services.TryGet<PoolService>(out var pool) ? pool.CountActive(ProjectileService.PoolKey) : 0;
            _maxUnits = Mathf.Max(_maxUnits, units);
            _maxProjectiles = Mathf.Max(_maxProjectiles, projectiles);
            _unitSum += units;
            _projectileSum += projectiles;
        }

        readonly List<Unit> _shooters = new();

        void TopUpProjectiles()
        {
            if (ProjectileFloor <= 0 || !Services.TryGet<PoolService>(out var pool) || !Services.TryGet<ProjectileService>(out var shots)) return;
            int missing = ProjectileFloor - pool.CountActive(ProjectileService.PoolKey);
            if (missing <= 0) return;
            _shooters.Clear();
            foreach (var u in UnitRegistry.All) if (u.IsAlive) _shooters.Add(u);
            if (_shooters.Count < 2) return;
            var rng = GameRandom.Shared;
            var spec = ProjectileSpec.Arrow(new Color(1f, 0.4f, 0.9f));
            spec.OnImpact = (_, _) => { };
            for (int i = 0; i < missing; i++)
            {
                var a = _shooters[rng.Range(0, _shooters.Count)];
                // Nearest other unit within 8 so the arrows fly over the battlefield, not the map.
                Unit b = null;
                float best = 64f;
                foreach (var o in _shooters)
                {
                    if (o == a) continue;
                    float sq = (o.Position - a.Position).sqrMagnitude;
                    if (sq < best && sq > 4f) { best = sq; b = o; }
                }
                if (b != null) shots.Launch(a, b, spec);
            }
        }

        public string Report()
        {
            int n = _frameMs.Count;
            if (n == 0) return "no samples";
            var sorted = new List<float>(_frameMs);
            sorted.Sort();
            float sum = 0f;
            foreach (var f in _frameMs) sum += f;
            long allocSum = 0, allocMax = 0;
            int allocFrames = 0;
            foreach (var b in _allocBytes)
            {
                allocSum += b;
                if (b > allocMax) allocMax = b;
                if (b > 0) allocFrames++;
            }
            float P(float q) => sorted[Mathf.Clamp(Mathf.RoundToInt(q * (n - 1)), 0, n - 1)];
            var sb = new StringBuilder();
            sb.Append($"frames {n} over {Time.realtimeSinceStartup - _started:0.0}s");
            sb.Append($" | ms avg {sum / n:0.00} p50 {P(0.5f):0.00} p95 {P(0.95f):0.00} p99 {P(0.99f):0.00} max {sorted[n - 1]:0.00}");
            sb.Append($" | fps avg {1000f * n / sum:0.0}");
            sb.Append($" | GC/frame avg {allocSum / n / 1024f:0.0}KB max {allocMax / 1024f:0.0}KB ({allocFrames * 100 / n}% frames alloc)");
            sb.Append($" | units avg {_unitSum / n} max {_maxUnits} | projectiles avg {_projectileSum / n} max {_maxProjectiles}");
            return sb.ToString();
        }
    }
}
