using System.Collections.Generic;
using System.Linq;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Ecosystem;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>
    /// A raid was first seen by the guild (any vision: town watch, members, guild master).
    /// There is no system warning: the guild only learns about a raid by observing it.
    /// </summary>
    public struct RaidSightedEvent { public int RaidId; public string Direction; public Vector2 Position; }
    /// <summary>Raid over. Sighted=false means the guild never saw it (UI/log stay silent).</summary>
    public struct RaidEndedEvent { public int RaidId; public int Killed; public int Withdrawn; public bool Sighted; }

    /// <summary>Where one sighted raid is pressing now: the gate it heads for (or the breach inside).</summary>
    public struct RaidFront
    {
        public int RaidId;
        /// <summary>Hold point just inside the gate the raid is heading for.</summary>
        public Vector2 Point;
        /// <summary>Living raiders in it (defenders are shared out by this).</summary>
        public int Strength;
    }

    public struct RaidSettings
    {
        public float ChancePerLevelPerHour;
        public float SendFraction;
        public int MinParty;
        public int MaxHours;
    }

    /// <summary>
    /// Raids (P3-6, redesigned 2026-09-19): each raid-capable nest is its own count. Once a nest
    /// grows past its species' threshold it reaches danger level 1, and every further step raises
    /// the level; each game hour the nest raids with chance level × ChancePerLevelPerHour. The
    /// nest's own members march on the town (the nest really shrinks when they die) and go home
    /// when they give up. Nothing is announced until a raider is seen (information hiding;
    /// watchtowers will extend that sight, P4-13). Several nests may raid at once.
    /// </summary>
    public class WaveManager : MonoBehaviour, ISaveable, ISaveLink
    {
        const float SightCheckInterval = 0.25f;

        class Raid
        {
            public int Id;
            public Nest Nest;
            public readonly List<Monster> Members = new();
            public float StartedAtHour;
            public bool Sighted;
            public int Killed;
            public Vector2Int FrontGate;
            public Vector2 Front;
        }

        RaidSettings _cfg;
        GameClock _clock;
        EcosystemManager _eco;
        WorldService _world;

        readonly List<Raid> _raids = new();
        readonly List<Monster> _seenRaiders = new();
        float _sightTimer;

        /// <summary>Raids launched so far (shown on the game-over screen).</summary>
        public int RaidCount { get; private set; }
        /// <summary>Some raid is on the map (the guild may not know yet).</summary>
        public bool RaidActive => _raids.Count > 0;
        /// <summary>The guild has seen at least one ongoing raid: defenders respond only then.</summary>
        public bool RaidSighted => _raids.Any(r => r.Sighted);
        /// <summary>Living raiders of the raids the guild has seen.</summary>
        public IReadOnlyList<Monster> Raiders => _seenRaiders;
        /// <summary>
        /// One front per sighted raid, following it (user report 2026-09-22: a single rally point
        /// at the latest sighting left earlier raids undefended, and stayed at the first gate
        /// seen while the raid went round to a breach).
        /// </summary>
        public IReadOnlyList<RaidFront> Fronts => _fronts;
        readonly List<RaidFront> _fronts = new();
        /// <summary>A new gate must be this much closer to the vanguard before a front moves (no flicker).</summary>
        const float FrontSwitchRatio = 0.8f;

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SavedRaid
        {
            public int id;
            public int nest = -1;
            public List<string> members = new();
            public float started;
            public bool sighted;
            public int killed;
            public Vector2Int front_gate;
            public Vector2 front;
        }

        [System.Serializable]
        class SaveData
        {
            public int raid_count;
            public List<SavedRaid> raids = new();
            public List<string> seen = new();
        }

        SaveData _restoring;

        public string SaveKey => "raids";

        /// <summary>The fronts are worked out again every check, so only the raids and who was seen are saved.</summary>
        public string Capture()
        {
            var s = new SaveData { raid_count = RaidCount };
            foreach (var r in _raids)
            {
                var sr = new SavedRaid
                {
                    id = r.Id, nest = r.Nest != null ? _eco.Nests.IndexOf(r.Nest) : -1, started = r.StartedAtHour, sighted = r.Sighted,
                    killed = r.Killed, front_gate = r.FrontGate, front = r.Front,
                };
                foreach (var m in r.Members) if (m != null && m.IsAlive) sr.members.Add(m.UnitId);
                s.raids.Add(sr);
            }
            foreach (var m in _seenRaiders) if (m != null && m.IsAlive) s.seen.Add(m.UnitId);
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            _restoring = JsonUtility.FromJson<SaveData>(json);
            RaidCount = _restoring.raid_count;
        }

        public void Link(SaveContext ctx)
        {
            foreach (var sr in _restoring.raids)
            {
                var r = new Raid
                {
                    Id = sr.id, Nest = sr.nest >= 0 && sr.nest < _eco.Nests.Count ? _eco.Nests[sr.nest] : null, StartedAtHour = sr.started,
                    Sighted = sr.sighted, Killed = sr.killed, FrontGate = sr.front_gate, Front = sr.front,
                };
                foreach (var id in sr.members)
                {
                    if (ctx.Find<Monster>(id) is not { } m) continue;
                    r.Members.Add(m);
                    // As Launch: a raider's death counts, and the last one's ends the raid (without
                    // this a loaded raid stayed "on" until MaxHours, the defenders held at the gate).
                    m.Died += OnRaiderDied;
                }
                _raids.Add(r);
            }
            foreach (var id in _restoring.seen) if (ctx.Find<Monster>(id) is { } m) _seenRaiders.Add(m);
            _restoring = null;
        }

        public void Init(RaidSettings cfg, GameClock clock, EcosystemManager eco, WorldService world)
        {
            _cfg = cfg;
            _clock = clock;
            _eco = eco;
            _world = world;
            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<IntrudersRoutedEvent>(OnRouted);
            EventBus.Subscribe<GateDamagedEvent>(OnGateDamaged);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            EventBus.Unsubscribe<IntrudersRoutedEvent>(OnRouted);
            EventBus.Unsubscribe<GateDamagedEvent>(OnGateDamaged);
        }

        /// <summary>
        /// Raiders battering a gate (P4-39) are heard even where nobody can see them: the gate is
        /// outside every sight line from the plaza, and the first measured raid broke it and was
        /// half through the town hall before anyone came.
        /// </summary>
        void OnGateDamaged(GateDamagedEvent e)
        {
            if (e.Attacker is not Monster m) return;
            foreach (var raid in _raids)
                if (!raid.Sighted && raid.Members.Contains(m)) Sight(raid, m);
        }

        /// <summary>A raid ran into a much stronger nest on the way (P4-18): it turns back home.</summary>
        void OnRouted(IntrudersRoutedEvent e)
        {
            foreach (var raid in _raids.ToList())
                if (raid.Nest == e.IntruderHome) GiveUp(raid);
        }

        // ---------------- Danger level (pure, tested) ----------------

        /// <summary>0 below the threshold, then 1 + one per full step above it.</summary>
        public static int DangerLevel(int population, int threshold, int step)
            => population < threshold ? 0 : 1 + (population - threshold) / Mathf.Max(1, step);

        /// <summary>How many members march: SendFraction of the nest, at least MinParty (never more than it has).</summary>
        public static int PartySize(int population, float sendFraction, int minParty)
            => Mathf.Min(population, Mathf.Max(minParty, Mathf.RoundToInt(population * sendFraction)));

        /// <summary>Members at home (not already raiding).</summary>
        static IEnumerable<Monster> HomeMembers(Nest n) => n.Members.Where(m => m != null && m.IsAlive && m.DefaultStateOverride == null);

        public int LevelOf(Nest n) => n.Species.canRaid ? DangerLevel(HomeMembers(n).Count(), n.Species.raidThreshold, n.Species.raidStep) : 0;

        public bool IsRaiding(Nest n) => _raids.Any(r => r.Nest == n);

        // ---------------- Hourly roll ----------------

        void OnHour(HourPassedEvent e)
        {
            foreach (var raid in _raids.ToList())
                if (_clock.TotalHours - raid.StartedAtHour >= _cfg.MaxHours) GiveUp(raid);

            Services.TryGet<MigrationService>(out var migration);
            foreach (var nest in _eco.Nests)
            {
                if (!nest.Species.canRaid || nest.IsEmpty || IsRaiding(nest)) continue;
                if (migration != null && migration.IsMigrating(nest)) continue;
                int level = LevelOf(nest);
                if (level <= 0 || GameRandom.Shared.Value >= level * _cfg.ChancePerLevelPerHour) continue;
                // Only a nest by the town sends its band against the walls; the others move their
                // home a step closer instead (P4-56, user decision 2026-09-22).
                if (migration == null || IsByTheTown(nest)) Launch(nest);
                else migration.TryLaunch(nest); // the whole nest moves (user decision 2026-09-22)
            }
        }

        /// <summary>Nests this close to the town (cells) raid it; further out they migrate inwards.</summary>
        public const float RaidRadius = 45f;

        public bool IsByTheTown(Nest n) => Vector2.Distance(n.WorldPosition, _world.TownCenterWorld) <= RaidRadius;

        /// <summary>Sends part of the nest against the town now (also used by debug tools). Returns raiders sent.</summary>
        public int Launch(Nest nest)
        {
            var home = HomeMembers(nest).ToList();
            if (home.Count == 0) return 0;
            int n = PartySize(home.Count, _cfg.SendFraction, _cfg.MinParty);
            var raid = new Raid { Id = ++RaidCount, Nest = nest, StartedAtHour = _clock.TotalHours };
            foreach (var m in home.OrderBy(_ => GameRandom.Shared.Value).Take(n))
            {
                m.DefaultStateOverride = m.RaidState;
                m.GoToDefaultState();
                m.Died += OnRaiderDied;
                raid.Members.Add(m);
            }
            _raids.Add(raid);
            return raid.Members.Count;
        }

        void OnRaiderDied(Units.Unit u)
        {
            u.Died -= OnRaiderDied;
            if (u is not Monster m) return;
            _seenRaiders.Remove(m);
            foreach (var raid in _raids.ToList())
            {
                if (!raid.Members.Remove(m)) continue;
                raid.Killed++;
                if (raid.Members.Count == 0) End(raid, 0);
            }
        }

        /// <summary>Survivors give up and walk back to their nest (they are still its members).</summary>
        void GiveUp(Raid raid)
        {
            int withdrawn = 0;
            foreach (var m in raid.Members)
            {
                if (m == null) continue;
                m.Died -= OnRaiderDied;
                _seenRaiders.Remove(m);
                if (!m.IsAlive) continue;
                withdrawn++;
                m.EndMarch();
            }
            End(raid, withdrawn);
        }

        void End(Raid raid, int withdrawn)
        {
            _raids.Remove(raid);
            _fronts.RemoveAll(f => f.RaidId == raid.Id);
            EventBus.Publish(new RaidEndedEvent { RaidId = raid.Id, Killed = raid.Killed, Withdrawn = withdrawn, Sighted = raid.Sighted });
        }

        // ---------------- Sighting ----------------

        void Update()
        {
            if (_raids.Count == 0) return;
            _sightTimer -= Time.deltaTime;
            if (_sightTimer > 0f) return;
            _sightTimer = SightCheckInterval;
            CheckSighting();
            UpdateFronts();
        }

        /// <summary>
        /// Each sighted raid's front follows its vanguard: the raider inside the walls or battering a
        /// gate if any, else the one closest to the town; the front is the gate nearest to it.
        /// </summary>
        void UpdateFronts()
        {
            _fronts.Clear();
            var center = _world.TownCenterWorld;
            foreach (var raid in _raids)
            {
                if (!raid.Sighted) continue;
                Monster lead = null;
                float best = float.MaxValue;
                int living = 0;
                foreach (var m in raid.Members)
                {
                    if (m == null || !m.IsAlive) continue;
                    living++;
                    // Inside the walls or at a gate counts as right at the town.
                    float d = _world.Grid.IsInTown(m.Position) || m.IsBreakingGate ? 0f : Vector2.Distance(m.Position, center);
                    if (d < best) { best = d; lead = m; }
                }
                if (lead == null) continue;
                var gate = NearestGate(lead.Position);
                if (gate != raid.FrontGate)
                {
                    float now = Vector2.Distance(GridMap.CellToWorld(raid.FrontGate), lead.Position);
                    float next = Vector2.Distance(GridMap.CellToWorld(gate), lead.Position);
                    if (next < now * FrontSwitchRatio || lead.IsBreakingGate) SetFront(raid, gate);
                }
                _fronts.Add(new RaidFront { RaidId = raid.Id, Point = raid.Front, Strength = living });
            }
        }

        void SetFront(Raid raid, Vector2Int gate)
        {
            raid.FrontGate = gate;
            raid.Front = RallyInside(gate);
        }

        /// <summary>The first raider of a raid inside the guild's current vision raises the alarm.</summary>
        void CheckSighting()
        {
            if (!Services.TryGet<FogOfWar>(out var fog)) return;
            foreach (var raid in _raids)
            {
                if (raid.Sighted) continue;
                var seen = raid.Members.FirstOrDefault(m => m != null && m.IsAlive && fog.IsVisibleRaw(m.Position));
                if (seen != null) Sight(raid, seen);
            }
        }

        /// <summary>Raises the alarm for a raid: the defenders rally at the gate nearest to <paramref name="seen"/>.</summary>
        void Sight(Raid raid, Monster seen)
        {
            raid.Sighted = true;
            _seenRaiders.AddRange(raid.Members.Where(m => m != null && m.IsAlive));
            SetFront(raid, NearestGate(seen.Position));
            UpdateFronts();
            EventBus.Publish(new RaidSightedEvent { RaidId = raid.Id, Direction = DirectionFromTown(seen.Position), Position = seen.Position });
            if (Services.TryGet<CameraController>(out var cam)) cam.Shake(0.7f);
        }

        /// <summary>동/서/남/북 of the town centre (dominant axis).</summary>
        string DirectionFromTown(Vector2 p)
        {
            var d = p - _world.TownCenterWorld;
            return Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? (d.x >= 0 ? "동" : "서") : (d.y >= 0 ? "북" : "남");
        }

        Vector2Int NearestGate(Vector2 origin)
        {
            var gates = _world.Map.Gates;
            return gates.Count == 0 ? GridMap.WorldToCell(_world.TownCenterWorld)
                : gates.OrderBy(g => (GridMap.CellToWorld(g) - origin).sqrMagnitude).First();
        }

        /// <summary>A walkable spot a few cells inside a gate.</summary>
        Vector2 RallyInside(Vector2Int gateCell)
        {
            var center = _world.TownCenterWorld;
            var gate = GridMap.CellToWorld(gateCell);
            if ((gate - center).sqrMagnitude < 1f) return center;
            var inside = gate + (center - gate).normalized * 3f;
            return _world.Grid.TryFindNearestWalkable(GridMap.WorldToCell(inside), 3, out var c) ? GridMap.CellToWorld(c) : center;
        }
    }
}
