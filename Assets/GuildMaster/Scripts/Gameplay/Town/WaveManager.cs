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
    public class WaveManager : MonoBehaviour
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
        /// <summary>Where defenders gather: just inside the gate facing the latest sighting.</summary>
        public Vector2 RallyPoint { get; private set; }

        public void Init(RaidSettings cfg, GameClock clock, EcosystemManager eco, WorldService world)
        {
            _cfg = cfg;
            _clock = clock;
            _eco = eco;
            _world = world;
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        void OnDestroy() => EventBus.Unsubscribe<HourPassedEvent>(OnHour);

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

            foreach (var nest in _eco.Nests)
            {
                if (!nest.Species.canRaid || nest.IsEmpty || IsRaiding(nest)) continue;
                int level = LevelOf(nest);
                if (level > 0 && GameRandom.Shared.Value < level * _cfg.ChancePerLevelPerHour) Launch(nest);
            }
        }

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
                m.DefaultStateOverride = null;
                m.GoToReturn();
            }
            End(raid, withdrawn);
        }

        void End(Raid raid, int withdrawn)
        {
            _raids.Remove(raid);
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
        }

        /// <summary>The first raider of a raid inside the guild's current vision raises the alarm.</summary>
        void CheckSighting()
        {
            if (!Services.TryGet<FogOfWar>(out var fog)) return;
            foreach (var raid in _raids)
            {
                if (raid.Sighted) continue;
                var seen = raid.Members.FirstOrDefault(m => m != null && m.IsAlive && fog.IsVisibleRaw(m.Position));
                if (seen == null) continue;
                raid.Sighted = true;
                _seenRaiders.AddRange(raid.Members.Where(m => m != null && m.IsAlive));
                RallyPoint = RallyInside(seen.Position);
                EventBus.Publish(new RaidSightedEvent { RaidId = raid.Id, Direction = DirectionFromTown(seen.Position), Position = seen.Position });
                if (Services.TryGet<CameraController>(out var cam)) cam.Shake(0.7f);
            }
        }

        /// <summary>동/서/남/북 of the town centre (dominant axis).</summary>
        string DirectionFromTown(Vector2 p)
        {
            var d = p - _world.TownCenterWorld;
            return Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? (d.x >= 0 ? "동" : "서") : (d.y >= 0 ? "북" : "남");
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
    }
}
