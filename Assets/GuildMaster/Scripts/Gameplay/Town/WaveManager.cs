using System.Collections.Generic;
using System.Linq;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Monsters;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>Raid coming in a few hours. Only the direction is known (information hiding).</summary>
    public struct RaidWarningEvent { public int Wave; public string Direction; public int HoursLeft; }
    public struct RaidStartedEvent { public int Wave; public string Direction; }
    public struct RaidEndedEvent { public int Wave; public int Killed; public int Withdrawn; }

    public struct RaidSettings
    {
        public int FirstDay, IntervalDays, Hour, WarningHours, MaxHours;
    }

    /// <summary>
    /// Raid waves (P3-6): from day 3, every 3 days at 21:00 a wave of raid-capable species comes
    /// from one map edge, 2 hours after a warning. Size follows the ecosystem (Q3): extinct
    /// species never come, abundant ones come in numbers. Survivors withdraw at dawn.
    /// </summary>
    public class WaveManager : MonoBehaviour
    {
        static readonly string[] SideNames = { "동", "서", "북", "남" };

        RaidSettings _cfg;
        GameClock _clock;
        EcosystemManager _eco;
        WorldService _world;
        PoolService _pool;

        int _warnedWave, _startedWave;
        int _plannedSide = -1;
        float _startedAtHour;
        int _killed;
        readonly List<Monster> _raiders = new();
        readonly List<Monster> _withdrawing = new();

        public int Wave { get; private set; }
        public bool RaidActive { get; private set; }
        public IReadOnlyList<Monster> Raiders => _raiders;
        /// <summary>Where defenders gather: just inside the gate facing the raid (set when a wave starts).</summary>
        public Vector2 RallyPoint { get; private set; }

        public void Init(RaidSettings cfg, GameClock clock, EcosystemManager eco, WorldService world, PoolService pool)
        {
            _cfg = cfg;
            _clock = clock;
            _eco = eco;
            _world = world;
            _pool = pool;
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        void OnDestroy() => EventBus.Unsubscribe<HourPassedEvent>(OnHour);

        /// <summary>Wave number due on this day at the raid hour (0 = none).</summary>
        public static int WaveOnDay(int day, int firstDay, int intervalDays)
            => day >= firstDay && (day - firstDay) % intervalDays == 0 ? (day - firstDay) / intervalDays + 1 : 0;

        void OnHour(HourPassedEvent e)
        {
            // Warning N hours before (may fall on the same day).
            int warnHour = _cfg.Hour - _cfg.WarningHours;
            int wave = WaveOnDay(e.Day, _cfg.FirstDay, _cfg.IntervalDays);
            if (wave > 0 && e.Hour == warnHour && _warnedWave < wave) Warn(wave);
            if (wave > 0 && e.Hour == _cfg.Hour && _startedWave < wave) StartWave(wave);
            if (RaidActive && _clock.TotalHours - _startedAtHour >= _cfg.MaxHours) Withdraw();
        }

        void Warn(int wave)
        {
            _warnedWave = wave;
            _plannedSide = PickSide();
            EventBus.Publish(new RaidWarningEvent { Wave = wave, Direction = SideName(_plannedSide), HoursLeft = _cfg.WarningHours });
            if (Services.TryGet<CameraController>(out var cam)) cam.Shake(0.6f);
        }

        /// <summary>Spawns the wave now (also used by debug tools).</summary>
        public int StartWave(int wave)
        {
            if (_warnedWave < wave) Warn(wave);
            _startedWave = wave;
            Wave = wave;
            var species = Compose(wave, RaidPool(), GameRandom.Shared);
            var cells = SpawnCells(_plannedSide < 0 ? PickSide() : _plannedSide, species.Count);
            RallyPoint = cells.Count > 0 ? RallyInside(GridMap.CellToWorld(cells[0])) : _world.TownCenterWorld;
            _killed = 0;
            for (int i = 0; i < species.Count && i < cells.Count; i++)
            {
                var m = _pool.Spawn<Monster>(Monster.PoolKey, GridMap.CellToWorld(cells[i]) + GameRandom.Shared.InsideUnitCircle() * 0.3f);
                m.Setup(species[i], null);
                m.DefaultStateOverride = m.RaidState;
                m.GoToDefaultState();
                m.Died += OnRaiderDied;
                _raiders.Add(m);
            }
            RaidActive = _raiders.Count > 0;
            _startedAtHour = _clock.TotalHours;
            EventBus.Publish(new RaidStartedEvent { Wave = wave, Direction = SideName(_plannedSide) });
            if (Services.TryGet<CameraController>(out var cam)) cam.Shake(0.8f);
            if (!RaidActive) End(0);
            _plannedSide = -1;
            return _raiders.Count;
        }

        void OnRaiderDied(Units.Unit u)
        {
            u.Died -= OnRaiderDied;
            if (u is Monster m && _raiders.Remove(m)) _killed++;
            if (RaidActive && _raiders.Count == 0) End(0);
        }

        /// <summary>Dawn: survivors walk back to the edge they came from and vanish there.</summary>
        void Withdraw()
        {
            int n = _raiders.Count;
            foreach (var m in _raiders)
            {
                m.Died -= OnRaiderDied;
                m.DefaultStateOverride = null;
                m.GoToReturn();
                _withdrawing.Add(m);
            }
            _raiders.Clear();
            End(n);
        }

        void End(int withdrawn)
        {
            RaidActive = false;
            EventBus.Publish(new RaidEndedEvent { Wave = Wave, Killed = _killed, Withdrawn = withdrawn });
        }

        void Update()
        {
            for (int i = _withdrawing.Count - 1; i >= 0; i--)
            {
                var m = _withdrawing[i];
                if (m == null || !m.IsAlive) { _withdrawing.RemoveAt(i); continue; }
                if (Vector2.Distance(m.Position, m.HomePosition) > 1.5f) continue;
                _withdrawing.RemoveAt(i);
                _pool.Despawn(m.gameObject);
            }
        }

        // ---------------- Composition (pure, tested) ----------------

        /// <summary>Raid-capable, non-extinct species with their current population.</summary>
        List<(MonsterDefinition def, int pop)> RaidPool()
            => _eco.Species.Where(s => s.canRaid && !_eco.IsExtinct(s)).Select(s => (s, _eco.GetPopulation(s))).ToList();

        /// <summary>
        /// Wave composition (Q3): size = 4 + 2×wave + (population of the regular raid species)/6, drawn
        /// weighted by population; high-danger species (ogres) add one each from wave 4.
        /// </summary>
        public static List<MonsterDefinition> Compose(int wave, IReadOnlyList<(MonsterDefinition def, int pop)> pool, GameRandom rng)
        {
            var result = new List<MonsterDefinition>();
            var regular = pool.Where(p => p.def.danger < DangerLevel.High && p.pop > 0).ToList();
            int total = regular.Sum(p => p.pop);
            if (total > 0)
            {
                int size = 4 + 2 * wave + total / 6;
                for (int i = 0; i < size; i++)
                {
                    int roll = rng.Range(0, total);
                    foreach (var p in regular)
                    {
                        if (roll < p.pop) { result.Add(p.def); break; }
                        roll -= p.pop;
                    }
                }
            }
            if (wave >= 4)
                foreach (var p in pool)
                    if (p.def.danger >= DangerLevel.High && p.pop > 0) result.Add(p.def);
            return result;
        }

        /// <summary>A walkable spot a few cells inside the gate closest to where the raid comes from.</summary>
        Vector2 RallyInside(Vector2 origin)
        {
            var center = _world.TownCenterWorld;
            var gates = _world.Map.Gates;
            if (gates.Count == 0) return center;
            var gate = GridMap.CellToWorld(gates.OrderBy(g => (GridMap.CellToWorld(g) - origin).sqrMagnitude).First());
            var inside = gate + (center - gate).normalized * 3f;
            return _world.Grid.TryFindNearestWalkable(GridMap.WorldToCell(inside), 3, out var c) ? GridMap.CellToWorld(c) : center;
        }

        // ---------------- Spawn side ----------------

        static string SideName(int side) => side >= 0 && side < SideNames.Length ? SideNames[side] : "?";

        int SideOf(Vector2Int c)
        {
            var map = _world.Map;
            int[] d = { map.Width - 1 - c.x, c.x, map.Height - 1 - c.y, c.y };
            int best = 0;
            for (int i = 1; i < 4; i++) if (d[i] < d[best]) best = i;
            return best;
        }

        int PickSide()
        {
            var sides = _world.Map.EdgeSpawnCells.Select(SideOf).Distinct().ToList();
            return sides.Count == 0 ? 0 : sides[GameRandom.Shared.Range(0, sides.Count)];
        }

        /// <summary>Edge cells on one side, clustered around a random anchor cell.</summary>
        List<Vector2Int> SpawnCells(int side, int count)
        {
            var onSide = _world.Map.EdgeSpawnCells.Where(c => SideOf(c) == side).ToList();
            if (onSide.Count == 0) onSide = _world.Map.EdgeSpawnCells.ToList();
            if (onSide.Count == 0) return new List<Vector2Int>();
            var anchor = onSide[GameRandom.Shared.Range(0, onSide.Count)];
            var sorted = onSide.OrderBy(c => (c - anchor).sqrMagnitude).ToList();
            var cells = new List<Vector2Int>();
            for (int i = 0; i < count; i++) cells.Add(sorted[i % sorted.Count]);
            return cells;
        }
    }
}
