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
    /// <summary>A migrating band took over a nest site closer to the town. Observed = the guild saw it happen.</summary>
    public struct BandSettledEvent { public Nest From; public Nest To; public MonsterDefinition Species; public int Count; public bool Observed; }

    /// <summary>
    /// Band migration (P4-56, user decisions 2026-09-22): a crowded nest (the raid roll of
    /// <see cref="Town.WaveManager"/>) moves as a whole — no split packs that stayed friends — a
    /// step towards the town: it walks to a nest site nearer the walls, fights whatever it meets on
    /// the way (and whoever lives there: the site is taken by force), rests where it stands when
    /// badly hurt, then goes on. The nest it left stands empty; empty outer sites are taken by
    /// newcomers from beyond the map edge (<see cref="TryArrive"/>). So the strong kinds creep
    /// inwards over the days, and only a nest close to the town sends its band against the town
    /// itself (the raid).
    /// </summary>
    public class MigrationService : MonoBehaviour
    {
        /// <summary>A target site must be at least this much nearer the town than the band's nest.</summary>
        public const float MinStep = 15f;
        /// <summary>How far (cells) a band looks for its next home.</summary>
        public const float MaxStep = 55f;
        /// <summary>Close enough to the target to move in.</summary>
        const float ArriveRadius = 3f;
        /// <summary>Average HP below which a band stops to rest (after a fight, never mid-fight).</summary>
        public const float RestBelow = 0.7f;
        /// <summary>Average HP at which a resting band moves on.</summary>
        public const float RestUntil = 0.9f;
        /// <summary>Share of max HP healed per game hour while resting.</summary>
        public const float RestHealPerHour = 0.3f;
        /// <summary>A band that has not settled by then turns back home.</summary>
        public const float MaxHours = 30f;
        /// <summary>At most this many bands on the move at once.</summary>
        public const int MaxBands = 3;
        /// <summary>Newcomers from beyond the map edge (user decision 2026-09-22): chance per game hour while an outer site stands empty.</summary>
        public const float ArrivalChancePerHour = 0.6f; // 0.3 left 17 of 64 sites empty after five days (user: more)
        /// <summary>At most this many newcomer bands on their way in at once.</summary>
        public const int MaxArrivals = 3;
        /// <summary>Sites at least this far out (0 innermost .. 1 outermost) count as outer: newcomers settle there.</summary>
        public const float OuterSites = 0.5f;
        const float TickInterval = 0.5f;

        public class Band
        {
            public int Id;
            public Nest From;
            public Nest Target;
            public readonly List<Monster> Members = new();
            public readonly HashSet<Nest> Refused = new();
            public float StartedAtHour;
            public bool Resting;
            public MonsterDefinition Species;
            /// <summary>Newcomers from beyond the edge heading for an empty outer site (their home already).</summary>
            public bool Arrival;
        }

        EcosystemManager _eco;
        WorldService _world;
        GameClock _clock;
        readonly List<Band> _bands = new();
        readonly Dictionary<Monster, Band> _of = new();
        float _timer;
        int _count;

        public IReadOnlyList<Band> Bands => _bands;

        public void Init(EcosystemManager eco, WorldService world, GameClock clock)
        {
            _eco = eco;
            _world = world;
            _clock = clock;
            EventBus.Subscribe<IntrudersRoutedEvent>(OnRouted);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<IntrudersRoutedEvent>(OnRouted);
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
        }

        public bool IsMigrating(Nest n) => _bands.Any(b => b.From == n);
        /// <summary>A band is on its way to take this site.</summary>
        public bool IsTarget(Nest n) => _bands.Any(b => b.Target == n);
        public bool TryGetBand(Monster m, out Band band) => _of.TryGetValue(m, out band);

        /// <summary>Distance from the town centre (cells).</summary>
        float FromTown(Nest n) => Vector2.Distance(n.WorldPosition, _world.TownCenterWorld);

        /// <summary>
        /// The next home: a site nearer the town by <see cref="MinStep"/>, within <see cref="MaxStep"/>,
        /// nearest first, within its habitat (P4-58); an empty site is easier than a fight, and a site
        /// held by another pack — of its own kind too, packs never merge — is fought for.
        /// </summary>
        public Nest PickTarget(Nest from, MonsterDefinition species, ICollection<Nest> refused = null)
        {
            float home = FromTown(from);
            Nest best = null;
            float bestScore = float.MaxValue;
            foreach (var n in _eco.Nests)
            {
                if (n == from || refused != null && refused.Contains(n)) continue;
                if (!n.Suits(species)) continue;
                if (FromTown(n) > home - MinStep) continue;
                float d = Vector2.Distance(n.WorldPosition, from.WorldPosition);
                if (d > MaxStep) continue;
                if (_bands.Any(b => b.Target == n)) continue;
                float score = d + (n.IsEmpty ? 0f : 10f + 2f * n.Population);
                if (score < bestScore) { bestScore = score; best = n; }
            }
            return best;
        }

        /// <summary>
        /// Sends the whole pack off to a nearer site. False if there is nowhere to go, or while some
        /// of it is away (on a raid): a pack moves as one (P4-58, user decision).
        /// </summary>
        public bool TryLaunch(Nest nest)
        {
            if (_bands.Count >= MaxBands || IsMigrating(nest)) return false;
            var home = nest.Members.Where(m => m != null && m.IsAlive).ToList();
            if (home.Any(m => m.DefaultStateOverride != null)) return false;
            if (home.Count == 0) return false;
            var target = PickTarget(nest, nest.Species);
            if (target == null) return false;
            var band = new Band
            {
                Id = ++_count, From = nest, Target = target, StartedAtHour = _clock.TotalHours,
                Species = nest.Species,
            };
            foreach (var m in home) Enlist(band, m);
            _bands.Add(band);
            Debug.Log($"[Migration] band #{band.Id}: {band.Members.Count} {nest.Species.displayName} {nest.DisplayName} → {target.DisplayName}");
            return true;
        }

        void Enlist(Band band, Monster m)
        {
            band.Members.Add(m);
            _of[m] = band;
            m.Died += OnMemberDied;
            m.DefaultStateOverride = m.MigrateState;
            m.GoToDefaultState();
        }

        /// <summary>
        /// A new band walks in from beyond the map edge towards an empty outer site (user decision
        /// 2026-09-22: of any kind, born to that site's level). The site is theirs from the start;
        /// they fight their way there like any band.
        /// </summary>
        public bool TryArrive(Nest target = null, MonsterDefinition species = null)
        {
            if (_bands.Count(b => b.Arrival) >= MaxArrivals || _world.Map.EdgeSpawnCells.Count == 0) return false;
            if (target == null)
            {
                var free = _eco.Nests.Where(n => n.IsEmpty && _eco.Distance01(n.WorldPosition) >= OuterSites && !_bands.Any(b => b.Target == n)).ToList();
                if (free.Count == 0) return false;
                target = free[GameRandom.Shared.Range(0, free.Count)];
            }
            if (!target.IsEmpty) return false;
            if (species == null)
            {
                // A kind that lives there (P4-58: habitat by terrain).
                var kinds = _eco.AllSpecies.Where(target.Suits).ToList();
                if (kinds.Count == 0) return false;
                species = kinds[GameRandom.Shared.Range(0, kinds.Count)];
            }
            target.ChangeSpecies(species);
            var edge = _world.Map.EdgeSpawnCells.OrderBy(c => (c - target.Cell).sqrMagnitude).First();
            var band = new Band { Id = ++_count, From = target, Target = target, StartedAtHour = _clock.TotalHours, Species = species, Arrival = true };
            int size = Mathf.Max(3, species.nestCapacity / 3);
            bool boss = GameRandom.Shared.Chance(MonsterBoss.Chance); // a new pack may have a boss (P4-57)
            for (int i = 0; i < size; i++) Enlist(band, _eco.Spawn(target, GridMap.CellToWorld(edge), boss && i == 0));
            _bands.Add(band);
            Debug.Log($"[Migration] newcomers #{band.Id}: {size} {species.displayName} from the edge {edge} → {target.DisplayName}");
            return true;
        }

        void OnMemberDied(Units.Unit u)
        {
            u.Died -= OnMemberDied;
            if (u is not Monster m || !_of.Remove(m, out var band)) return;
            band.Members.Remove(m);
            if (band.Members.Count == 0) _bands.Remove(band);
        }

        void OnHour(HourPassedEvent e)
        {
            foreach (var band in _bands.ToList())
                if (_clock.TotalHours - band.StartedAtHour >= MaxHours) GoHome(band);
            if (GameRandom.Shared.Value < ArrivalChancePerHour) TryArrive();
        }

        /// <summary>Beaten back on the way or at the target: rest, then try elsewhere (or go home).</summary>
        void OnRouted(IntrudersRoutedEvent e)
        {
            foreach (var band in _bands)
            {
                if (band.From != e.IntruderHome) continue;
                if (band.Arrival)
                {
                    // Newcomers have nowhere else: back off, rest, and try their site again.
                    foreach (var m in band.Members) if (m != null && m.IsAlive && e.Territory != null) m.FleeFrom(e.Territory.WorldPosition);
                    band.Resting = true;
                    continue;
                }
                // Back off from the stronger residents, then rest out of their way.
                foreach (var m in band.Members) if (m != null && m.IsAlive && e.Territory != null) m.FleeFrom(e.Territory.WorldPosition);
                if (e.Territory != null) band.Refused.Add(e.Territory);
                if (band.Target == e.Territory || band.Target == null)
                    band.Target = PickTarget(band.From, band.Species, band.Refused);
                band.Resting = true;
            }
        }

        void Update()
        {
            if (_bands.Count == 0) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = TickInterval;
            foreach (var band in _bands.ToList()) TickBand(band);
        }

        void TickBand(Band band)
        {
            band.Members.RemoveAll(m => m == null || !m.IsAlive);
            if (band.Members.Count == 0) { _bands.Remove(band); return; }
            if (band.Target == null) { GoHome(band); return; }

            bool fighting = band.Members.Any(m => m.IsInFight());
            float hp = band.Members.Average(m => m.HpRatio);
            // Rest where they stand once the fighting is over (user decision: regroup in place).
            if (!fighting && hp < RestBelow) band.Resting = true;
            if (band.Resting && hp >= RestUntil) band.Resting = false;

            // The target changed hands while we walked: emptied is fine; anyone holding it — our own
            // kind too (P4-58: packs never merge) — is fought for when we get there.
            if (fighting || band.Resting) return;
            var lead = band.Members.OrderBy(m => Vector2.Distance(m.Position, band.Target.WorldPosition)).First();
            if (Vector2.Distance(lead.Position, band.Target.WorldPosition) > ArriveRadius + band.Target.TerritoryRadius * 0.5f) return;
            // Residents other than the band itself (newcomers belong to their site from the start)
            // still hold it: the fight goes on.
            if (band.Target.Members.Any(m => m.IsAlive && !band.Members.Contains(m))) return;
            Settle(band);
        }

        void Settle(Band band)
        {
            var to = band.Target;
            if (to.IsEmpty && to.Species != band.Species) to.ChangeSpecies(band.Species);
            bool observed = Services.TryGet<FogOfWar>(out var fog) && fog.IsVisible(to.WorldPosition);
            foreach (var m in band.Members)
            {
                m.Died -= OnMemberDied;
                _of.Remove(m);
                m.DefaultStateOverride = null;
                m.Home?.TransferMember(m, to);
            }
            _bands.Remove(band);
            Debug.Log($"[Migration] band #{band.Id}: {band.Members.Count} {band.Species.displayName} settled {to.DisplayName}");
            if (band.Arrival) EventBus.Publish(new NestRecolonizedEvent { Nest = to });
            else EventBus.Publish(new BandSettledEvent { From = band.From, To = to, Species = band.Species, Count = band.Members.Count, Observed = observed });
        }

        /// <summary>Gives up: back to the nest they came from.</summary>
        void GoHome(Band band)
        {
            foreach (var m in band.Members)
            {
                if (m == null) continue;
                m.Died -= OnMemberDied;
                _of.Remove(m);
                if (!m.IsAlive) continue;
                m.EndMarch();
            }
            _bands.Remove(band);
        }

        /// <summary>Heals a resting member (called by <see cref="MigrateState"/>).</summary>
        internal void Rest(Monster m, float dt)
        {
            float hours = dt / Mathf.Max(0.01f, _clock.RealSecondsPerGameHour);
            m.Heal(m.Stats.MaxHp * RestHealPerHour * hours);
        }
    }
}
