using System;
using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Items;
using GuildMaster.Monsters;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Picking loot off the ground (P4-21, Q-C: every kill's loot drops to the ground). Out-of-town
    /// states call <see cref="Tick"/> once combat is idle.
    /// <para>
    /// P4-33: the adventurer plans a round of everything it can see and walks it in one pass,
    /// instead of re-picking the nearest item every half second — that made it set off for home,
    /// notice another drop and turn back, over and over. A fight always interrupts the round.
    /// </para>
    /// </summary>
    public class AdventurerLooting
    {
        /// <summary>Minimum search radius; adventurers look as far as their sight + 2 (archers kill from afar).</summary>
        public const float SearchRadius = 6f;
        /// <summary>At most this many stops per round, so nobody spends the day tidying up.</summary>
        public const int MaxRouteItems = 6;
        const float CheckInterval = 0.5f;
        const float EnemyCheckInterval = 0.25f;
        /// <summary>Give up on the current stop if it takes this long (blocked path, moved item).</summary>
        const float StopTimeout = 8f;
        const float ReachDistance = 0.6f;

        static readonly List<GroundItem> s_candidates = new();
        static readonly List<Vector2> s_points = new();

        readonly Adventurer _owner;
        readonly List<GroundItem> _route = new();
        float _timer;
        float _enemyTimer;
        float _stopStartedAt;

        public AdventurerLooting(Adventurer owner) { _owner = owner; }

        public bool IsBusy => _route.Count > 0;
        /// <summary>Stops still to make on the current round (0 when not looting).</summary>
        public int RouteLength => _route.Count;

        /// <summary>
        /// Returns true while walking the round (the caller skips its own movement that frame).
        /// <paramref name="allowed"/> limits where items may be taken (e.g. inside town).
        /// </summary>
        public bool Tick(float dt, Func<Vector2, bool> allowed = null)
        {
            var a = _owner;
            if (!Services.TryGet<GroundItemService>(out var ground)) return false;

            // Fighting comes first (2026-09-20 user decision): an enemy in sight ends the round.
            _enemyTimer -= dt;
            if (_enemyTimer <= 0f)
            {
                _enemyTimer = EnemyCheckInterval;
                if (EnemyInSight(a)) { Cancel(); return false; }
            }

            if (_route.Count > 0) return WalkRoute(a, ground);

            _timer -= dt;
            if (_timer > 0f) return false;
            _timer = CheckInterval;
            if (ground.Items.Count == 0 || EnemyInSight(a)) return false;
            BuildRoute(ground, allowed);
            if (_route.Count == 0) return false;
            _stopStartedAt = Time.time;
            if (a.Mover.SetDestination(_route[0].Position, 0.2f)) return true;
            Cancel();
            return false;
        }

        bool WalkRoute(Adventurer a, GroundItemService ground)
        {
            var target = _route[0];
            // Someone else got there first, or the way is blocked: skip that stop, keep the round.
            if (target == null || !ground.Contains(target) || Time.time - _stopStartedAt > StopTimeout)
                return NextStop(a, drop: true);

            if (Vector2.Distance(a.Position, target.Position) <= ReachDistance)
            {
                var stack = ground.Take(target, a);
                if (stack.count > 0) a.Data.AddLoot(stack.itemId, stack.count);
                return NextStop(a, drop: false);
            }
            if (!a.Mover.IsMoving) a.Mover.SetDestination(target.Position, 0.2f);
            return true;
        }

        /// <summary>Moves on to the next stop; false (round over) when there is none.</summary>
        bool NextStop(Adventurer a, bool drop)
        {
            if (drop) _route[0]?.Release(a);
            _route.RemoveAt(0);
            _stopStartedAt = Time.time;
            if (_route.Count == 0) return false;
            if (a.Mover.SetDestination(_route[0].Position, 0.2f)) return true;
            Cancel();
            return false;
        }

        /// <summary>Reserves everything in sight and puts it in walking order.</summary>
        void BuildRoute(GroundItemService ground, Func<Vector2, bool> allowed)
        {
            var a = _owner;
            Services.TryGet<FogOfWar>(out var fog);
            Services.TryGet<WorldService>(out var world);
            float radius = Mathf.Max(SearchRadius, a.Stats.SightRadius + 2f);

            s_candidates.Clear();
            s_points.Clear();
            foreach (var g in ground.Items)
            {
                if (g == null || !g.IsFreeFor(a)) continue;
                if (Vector2.Distance(a.Position, g.Position) > radius) continue;
                if (allowed != null && !allowed(g.Position)) continue;
                if (fog != null && !fog.IsVisibleRaw(g.Position)) continue;
                if (world != null && !world.HasLineOfSight(a.Position, g.Position, 2)) continue;
                s_candidates.Add(g);
                s_points.Add(g.Position);
            }
            if (s_candidates.Count == 0) return;

            foreach (int i in RouteOrder(a.Position, s_points, MaxRouteItems))
            {
                var item = s_candidates[i];
                if (item.Reserve(a)) _route.Add(item);
            }
        }

        /// <summary>
        /// Greedy nearest-neighbour round: start where the walker stands, then always take the
        /// closest point to the last one picked. Returns indices into <paramref name="points"/>.
        /// Pure, so the ordering can be tested.
        /// </summary>
        public static List<int> RouteOrder(Vector2 from, IReadOnlyList<Vector2> points, int max)
        {
            var order = new List<int>();
            if (points == null || points.Count == 0 || max <= 0) return order;
            var taken = new bool[points.Count];
            var at = from;
            int limit = Mathf.Min(max, points.Count);
            for (int step = 0; step < limit; step++)
            {
                int best = -1;
                float bestSq = float.MaxValue;
                for (int i = 0; i < points.Count; i++)
                {
                    if (taken[i]) continue;
                    float sq = (points[i] - at).sqrMagnitude;
                    if (sq >= bestSq) continue;
                    bestSq = sq;
                    best = i;
                }
                if (best < 0) break;
                taken[best] = true;
                order.Add(best);
                at = points[best];
            }
            return order;
        }

        /// <summary>Drops the whole round (state change, a fight, death).</summary>
        public void Cancel()
        {
            foreach (var g in _route) g?.Release(_owner);
            _route.Clear();
        }

        /// <summary>
        /// A monster in sight that is a fight right now: hostile to us, or in a fight with the guild
        /// either way — hitting one of us, targeted by one of us, or hit by one of us lately. Only
        /// "hostile to us" used to count, so while a comrade fought a slime (which never starts
        /// fights) the others went shopping for loot (user report 2026-09-22).
        /// </summary>
        static bool EnemyInSight(Adventurer a)
            => UnitRegistry.FindNearest(a.Position, a.Stats.SightRadius,
                   u => u is Monster m && m.IsAlive && (m.IsHostileTo(a) || InFightWithGuild(m)) && a.CanSee(u)) != null;

        static bool InFightWithGuild(Monster m)
        {
            if (m.CombatEngagement?.Target is Unit t && t.Faction == Faction.Guild) return true;
            if (m.LastAttacker != null && m.LastAttacker.Faction == Faction.Guild && Time.time - m.LastAttackedTime < 4f) return true;
            return UnitRegistry.FindNearest(m.Position, 12f,
                       u => u.Faction == Faction.Guild && u.IsAlive && u.CombatEngagement?.Target == m) != null;
        }
    }
}
