using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Ecosystem
{
    /// <summary>The guild saw a roaming pack for the first time (P4-58).</summary>
    public struct RoamingPackSightedEvent { public RoamingPack Pack; }
    /// <summary>A roaming pack moved into an empty nest of its habitat. Observed = the guild saw it happen.</summary>
    public struct RoamingPackSettledEvent { public RoamingPack Pack; public Nest Nest; public bool Observed; }

    /// <summary>A pack without a nest, walking its habitat as one (P4-58).</summary>
    public class RoamingPack
    {
        public int Id;
        public MonsterDefinition Species;
        public readonly List<Monster> Members = new();
        /// <summary>Where the pack is now (the members' centre).</summary>
        public Vector2 Center;
        /// <summary>Where it rests between walks.</summary>
        public Vector2 Stop;
        /// <summary>Where it is walking to, while <see cref="Moving"/>.</summary>
        public Vector2 Goal;
        public bool Moving;
        public float MovingSinceHour;
        public float RestUntilHour;
        /// <summary>The guild has seen it (logged once).</summary>
        public bool Sighted;
        /// <summary>The nest it was driven from, and when (not moved back into for a while).</summary>
        public Nest LeftNest;
        public float LeftAtHour;
        internal RoamingPackService Service;

        internal void OnMemberDied(Monster m) => Service?.OnMemberDied(this, m);
    }

    /// <summary>
    /// Roaming packs (P4-58, user decisions 2026-09-22): a pack is one — it never splits and never
    /// merges with another. A pack driven from its nest with no empty nest of its habitat to go to
    /// becomes a roaming pack: it walks the empty land of its habitat together (away from other
    /// packs' territories and the town), rests a few hours at each stop and heals, and moves into
    /// the first empty nest of its habitat it comes near. It does not breed on the road.
    /// </summary>
    public class RoamingPackService : MonoBehaviour, ISaveable, ISaveLink
    {
        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SavedPack
        {
            public int id;
            public string species;
            public List<string> members = new();
            public Vector2 center;
            public Vector2 stop;
            public Vector2 goal;
            public bool moving;
            public float moving_since;
            public float rest_until;
            public bool sighted;
            public int left_nest = -1;
            public float left_at;
        }

        [System.Serializable]
        class SaveData
        {
            public int count;
            public List<SavedPack> packs = new();
        }

        SaveData _restoring;

        public string SaveKey => "roaming";

        public string Capture()
        {
            var s = new SaveData { count = _count };
            foreach (var p in _packs)
            {
                var sp = new SavedPack
                {
                    id = p.Id, species = p.Species != null ? p.Species.id : null, center = p.Center, stop = p.Stop, goal = p.Goal,
                    moving = p.Moving, moving_since = p.MovingSinceHour, rest_until = p.RestUntilHour, sighted = p.Sighted,
                    left_nest = p.LeftNest != null ? _eco.Nests.IndexOf(p.LeftNest) : -1, left_at = p.LeftAtHour,
                };
                foreach (var m in p.Members) if (m != null && m.IsAlive) sp.members.Add(m.UnitId);
                s.packs.Add(sp);
            }
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            _restoring = JsonUtility.FromJson<SaveData>(json);
            _count = _restoring.count;
        }

        /// <summary>The packs, with their members (restored by the ecosystem) back on the road with them.</summary>
        public void Link(SaveContext ctx)
        {
            var db = Services.Get<GameDatabase>();
            foreach (var sp in _restoring.packs)
            {
                var pack = new RoamingPack
                {
                    Id = sp.id, Species = db.Monster(sp.species), Service = this, Center = sp.center, Stop = sp.stop, Goal = sp.goal,
                    Moving = sp.moving, MovingSinceHour = sp.moving_since, RestUntilHour = sp.rest_until, Sighted = sp.sighted,
                    LeftNest = sp.left_nest >= 0 && sp.left_nest < _eco.Nests.Count ? _eco.Nests[sp.left_nest] : null, LeftAtHour = sp.left_at,
                };
                foreach (var id in sp.members)
                {
                    if (ctx.Find<Monster>(id) is not { } m) continue;
                    pack.Members.Add(m);
                    m.RestoreRoaming(pack);
                }
                _packs.Add(pack);
            }
            _restoring = null;
        }

        /// <summary>Distance (cells) of each walk.</summary>
        public const float MinStep = 20f, MaxStep = 45f;
        /// <summary>Game hours of rest at each stop.</summary>
        public const float RestHoursMin = 2f, RestHoursMax = 6f;
        /// <summary>A walk that has not arrived after this many game hours ends where the pack is.</summary>
        public const float GiveUpWalkHours = 6f;
        /// <summary>An empty nest of its habitat this close is moved into.</summary>
        public const float SettleRadius = 20f;
        /// <summary>Game hours before a pack would move back into the nest it was driven from.</summary>
        public const float ReturnAfterHours = 12f;
        /// <summary>Stops keep this far clear of other packs' territories.</summary>
        public const float TerritoryMargin = 6f;
        /// <summary>Stops keep this far from the town centre, and as much again as the walls have moved out (P4-86).</summary>
        public const float TownMargin = 30f;
        /// <summary>Average HP a pack wants back before walking on.</summary>
        public const float WalkOnHp = 0.8f;
        /// <summary>Share of max HP healed per game hour at rest.</summary>
        public const float RestHealPerHour = 0.3f;
        const float TickInterval = 0.5f;

        EcosystemManager _eco;
        WorldService _world;
        GameClock _clock;
        readonly List<RoamingPack> _packs = new();
        float _timer;
        int _count;

        public IReadOnlyList<RoamingPack> Packs => _packs;

        public void Init(EcosystemManager eco, WorldService world, GameClock clock)
        {
            _eco = eco;
            _world = world;
            _clock = clock;
        }

        /// <summary>
        /// The whole pack of <paramref name="from"/> leaves it for the road, heading away from
        /// <paramref name="awayFrom"/>. Null when nobody is left to go.
        /// </summary>
        public RoamingPack SendOnTheRoad(Nest from, Vector2? awayFrom)
        {
            var pack = new RoamingPack { Id = ++_count, Species = from.Species, Service = this, LeftNest = from, LeftAtHour = _clock.TotalHours };
            foreach (var m in from.Members.ToList())
            {
                if (m == null || !m.IsAlive) continue;
                from.ReleaseMember(m);
                pack.Members.Add(m);
            }
            if (pack.Members.Count == 0) return null;
            pack.Center = pack.Stop = Centroid(pack);
            foreach (var m in pack.Members) m.StartRoaming(pack);
            _packs.Add(pack);
            if (!PickGoal(pack, awayFrom)) Rest(pack);
            Debug.Log($"[Roaming] pack #{pack.Id}: {pack.Members.Count} {pack.Species.displayName} left {from.DisplayName} with nowhere to go");
            return pack;
        }

        internal void OnMemberDied(RoamingPack pack, Monster m)
        {
            if (!pack.Members.Remove(m)) return;
            _eco.NotifyRoamerDied(pack.Species);
            if (pack.Members.Count == 0) _packs.Remove(pack);
        }

        /// <summary>Heals a resting member (called by <see cref="RoamState"/>).</summary>
        internal void Rest(Monster m, float dt)
        {
            float hours = dt / Mathf.Max(0.01f, _clock.RealSecondsPerGameHour);
            m.Heal(m.Stats.MaxHp * RestHealPerHour * hours);
        }

        void Update()
        {
            if (_packs.Count == 0) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = TickInterval;
            foreach (var pack in _packs.ToList()) Tick(pack);
        }

        void Tick(RoamingPack pack)
        {
            pack.Members.RemoveAll(m => m == null || !m.IsAlive || m.Roaming != pack);
            if (pack.Members.Count == 0) { _packs.Remove(pack); return; }
            pack.Center = Centroid(pack);
            // Back from a raid (or anything else that took it off the road): in step again.
            foreach (var m in pack.Members)
                if (m.DefaultStateOverride == null) m.StartRoaming(pack);

            if (!pack.Sighted && Services.TryGet<FogOfWar>(out var fog) && pack.Members.Any(m => fog.IsVisibleRaw(m.Position)))
            {
                pack.Sighted = true;
                EventBus.Publish(new RoamingPackSightedEvent { Pack = pack });
            }

            var home = FindHome(pack);
            if (home != null) { Settle(pack, home); return; }

            float now = _clock.TotalHours;
            if (pack.Moving)
            {
                if (Vector2.Distance(pack.Center, pack.Goal) < 3f || now - pack.MovingSinceHour > GiveUpWalkHours) Rest(pack);
                return;
            }
            if (now < pack.RestUntilHour || pack.Members.Any(m => m.IsInFight()) || pack.Members.Average(m => m.HpRatio) < WalkOnHp) return;
            if (!PickGoal(pack, null)) Rest(pack);
        }

        void Rest(RoamingPack pack)
        {
            pack.Moving = false;
            pack.Stop = pack.Center;
            pack.RestUntilHour = _clock.TotalHours + GameRandom.Shared.Range(RestHoursMin, RestHoursMax);
        }

        /// <summary>The nearest empty nest of its habitat in reach that no migrating band is heading for.</summary>
        Nest FindHome(RoamingPack pack)
        {
            Services.TryGet<MigrationService>(out var migration);
            Nest best = null;
            float bestD = SettleRadius;
            foreach (var n in _eco.Nests)
            {
                if (!n.IsEmpty || !n.Suits(pack.Species) || migration != null && migration.IsTarget(n)) continue;
                // Not straight back into the nest it was driven from: whoever drove it out is still about.
                if (n == pack.LeftNest && _clock.TotalHours - pack.LeftAtHour < ReturnAfterHours) continue;
                float d = Vector2.Distance(n.WorldPosition, pack.Center);
                if (d <= bestD) { bestD = d; best = n; }
            }
            return best;
        }

        void Settle(RoamingPack pack, Nest nest)
        {
            if (nest.Species != pack.Species) nest.ChangeSpecies(pack.Species);
            bool observed = Services.TryGet<FogOfWar>(out var fog) && fog.IsVisible(nest.WorldPosition);
            foreach (var m in pack.Members)
            {
                m.Roaming = null;
                if (m.DefaultStateOverride == m.RoamState) m.DefaultStateOverride = null;
                nest.AdoptMember(m);
            }
            _packs.Remove(pack);
            Debug.Log($"[Roaming] pack #{pack.Id}: {pack.Members.Count} {pack.Species.displayName} settled {nest.DisplayName}");
            EventBus.Publish(new RoamingPackSettledEvent { Pack = pack, Nest = nest, Observed = observed });
        }

        /// <summary>
        /// Next stop: open ground of its habitat, clear of other packs' territories and the town,
        /// <see cref="MinStep"/>..<see cref="MaxStep"/> away (heading away from a threat first).
        /// </summary>
        bool PickGoal(RoamingPack pack, Vector2? awayFrom)
        {
            var rng = GameRandom.Shared;
            var grid = _world.Grid;
            var town = _world.TownCenterWorld;
            float away = awayFrom.HasValue ? Mathf.Atan2(pack.Center.y - awayFrom.Value.y, pack.Center.x - awayFrom.Value.x) : 0f;
            for (int attempt = 0; attempt < 48; attempt++)
            {
                float angle = awayFrom.HasValue && attempt < 24 ? away + rng.Range(-1f, 1f) : rng.Range(0f, Mathf.PI * 2f);
                var p = pack.Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * rng.Range(MinStep, MaxStep);
                var cell = GridMap.WorldToCell(p);
                if (!grid.InBounds(cell) || !grid.IsWalkable(cell) || grid.IsInTown(cell)) continue;
                if (Vector2.Distance(p, town) < TownMargin + _world.TownGrowth) continue;
                if (!Habitable(pack.Species, cell)) continue;
                if (_eco.Nests.Any(n => !n.IsEmpty && Vector2.Distance(n.WorldPosition, p) < n.TerritoryRadius + TerritoryMargin)) continue;
                pack.Goal = GridMap.CellToWorld(cell);
                pack.Moving = true;
                pack.MovingSinceHour = _clock.TotalHours;
                return true;
            }
            return false;
        }

        /// <summary>Open ground the species lives on: its regions, or woods for a woodland kind.</summary>
        bool Habitable(MonsterDefinition species, Vector2Int cell)
            => species.LivesAt(_world.Map.RegionOf(cell.x, cell.y), _world.Grid.IsForest(cell) ? Biome.Forest : Biome.Plains);

        /// <summary>Centre of the members on the road (one off on a raid does not pull it towards the town).</summary>
        static Vector2 Centroid(RoamingPack pack)
        {
            var sum = Vector2.zero;
            int n = 0;
            foreach (var m in pack.Members)
                if (m != null && m.IsAlive && (m.DefaultStateOverride == null || m.DefaultStateOverride == m.RoamState)) { sum += m.Position; n++; }
            return n == 0 ? pack.Center : sum / n;
        }
    }
}
