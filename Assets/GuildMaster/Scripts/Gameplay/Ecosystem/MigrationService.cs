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
    /// Band migration (P4-56, user decisions 2026-09-22): part of a crowded nest leaves (the raid roll
    /// of <see cref="Town.WaveManager"/>) and moves its home a step towards the town — it walks to a
    /// nest site nearer the walls, fights whatever it meets on the way (and whoever lives there: the
    /// site is taken by force), rests where it stands when badly hurt, then goes on. Settled, the
    /// members belong to the new nest. So the strong kinds creep inwards over the days, and only a
    /// nest close to the town sends its band against the town itself (the raid).
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
        public bool TryGetBand(Monster m, out Band band) => _of.TryGetValue(m, out band);

        /// <summary>Distance from the town centre (cells).</summary>
        float FromTown(Nest n) => Vector2.Distance(n.WorldPosition, _world.TownCenterWorld);

        /// <summary>
        /// The next home: a site nearer the town by <see cref="MinStep"/>, within <see cref="MaxStep"/>,
        /// nearest first; an empty site or the band's own kind is easier than a fight.
        /// </summary>
        public Nest PickTarget(Nest from, MonsterDefinition species, ICollection<Nest> refused = null)
        {
            float home = FromTown(from);
            Nest best = null;
            float bestScore = float.MaxValue;
            foreach (var n in _eco.Nests)
            {
                if (n == from || refused != null && refused.Contains(n)) continue;
                if (FromTown(n) > home - MinStep) continue;
                float d = Vector2.Distance(n.WorldPosition, from.WorldPosition);
                if (d > MaxStep) continue;
                if (_bands.Any(b => b.Target == n)) continue;
                float score = d + (n.IsEmpty ? 0f : n.Species == species ? 5f : 10f + 2f * n.Population);
                if (score < bestScore) { bestScore = score; best = n; }
            }
            return best;
        }

        /// <summary>Sends <paramref name="count"/> members of the nest off to a nearer site. False if there is nowhere to go.</summary>
        public bool TryLaunch(Nest nest, int count)
        {
            if (_bands.Count >= MaxBands || IsMigrating(nest)) return false;
            var home = nest.Members.Where(m => m != null && m.IsAlive && m.DefaultStateOverride == null).ToList();
            if (home.Count == 0) return false;
            var target = PickTarget(nest, nest.Species);
            if (target == null) return false;
            var band = new Band
            {
                Id = ++_count, From = nest, Target = target, StartedAtHour = _clock.TotalHours,
                Species = nest.Species,
            };
            foreach (var m in home.OrderBy(_ => GameRandom.Shared.Value).Take(Mathf.Clamp(count, 1, home.Count)))
            {
                band.Members.Add(m);
                _of[m] = band;
                m.Died += OnMemberDied;
                m.DefaultStateOverride = m.MigrateState;
                m.GoToDefaultState();
            }
            _bands.Add(band);
            Debug.Log($"[Migration] band #{band.Id}: {band.Members.Count} {nest.Species.displayName} {nest.DisplayName} → {target.DisplayName}");
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
        }

        /// <summary>Beaten back on the way or at the target: rest, then try elsewhere (or go home).</summary>
        void OnRouted(IntrudersRoutedEvent e)
        {
            foreach (var band in _bands)
            {
                if (band.From != e.IntruderHome) continue;
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

            bool fighting = band.Members.Any(InFight);
            float hp = band.Members.Average(m => m.HpRatio);
            // Rest where they stand once the fighting is over (user decision: regroup in place).
            if (!fighting && hp < RestBelow) band.Resting = true;
            if (band.Resting && hp >= RestUntil) band.Resting = false;

            // The target changed hands while we walked: our own kind now, or emptied — both fine;
            // a stronger kind is fought for when we get there.
            if (fighting || band.Resting) return;
            var lead = band.Members.OrderBy(m => Vector2.Distance(m.Position, band.Target.WorldPosition)).First();
            if (Vector2.Distance(lead.Position, band.Target.WorldPosition) > ArriveRadius + band.Target.TerritoryRadius * 0.5f) return;
            if (!band.Target.IsEmpty && band.Target.Species != band.Species) return; // residents still hold it: the fight goes on
            Settle(band);
        }

        static bool InFight(Monster m)
            => m.CurrentTarget != null && m.CurrentTarget.IsAlive
               || m.LastAttacker != null && m.WasRecentlyAttackedBy(m.LastAttacker, 3f);

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
            EventBus.Publish(new BandSettledEvent { From = band.From, To = to, Species = band.Species, Count = band.Members.Count, Observed = observed });
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
                m.DefaultStateOverride = null;
                m.GoToReturn();
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
