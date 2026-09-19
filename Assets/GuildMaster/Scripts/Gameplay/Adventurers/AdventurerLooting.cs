using System;
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
    /// states call <see cref="Tick"/> once combat is idle: with no hostile in sight, the adventurer walks
    /// to the nearest item it can see (within its sight + 2) and puts it in its bag.
    /// </summary>
    public class AdventurerLooting
    {
        /// <summary>Minimum search radius; adventurers look as far as their sight + 2 (archers kill from afar).</summary>
        public const float SearchRadius = 6f;
        const float CheckInterval = 0.5f;
        const float Timeout = 8f;
        const float ReachDistance = 0.6f;

        readonly Adventurer _owner;
        GroundItem _target;
        float _timer;
        float _startedAt;

        public AdventurerLooting(Adventurer owner) { _owner = owner; }

        public bool IsBusy => _target != null;

        /// <summary>
        /// Returns true while walking to an item (the caller skips its own movement that frame).
        /// <paramref name="allowed"/> limits where items may be taken (e.g. inside town).
        /// </summary>
        public bool Tick(float dt, Func<Vector2, bool> allowed = null)
        {
            var a = _owner;
            if (!Services.TryGet<GroundItemService>(out var ground)) return false;

            if (_target != null)
            {
                if (!ground.Contains(_target) || Time.time - _startedAt > Timeout) { Cancel(); return false; }
                if (Vector2.Distance(a.Position, _target.Position) <= ReachDistance)
                {
                    var stack = ground.Take(_target, a);
                    _target = null;
                    if (stack.count > 0) a.Data.AddLoot(stack.itemId, stack.count);
                    return false;
                }
                if (!a.Mover.IsMoving) a.Mover.SetDestination(_target.Position, 0.2f);
                return true;
            }

            _timer -= dt;
            if (_timer > 0f) return false;
            _timer = CheckInterval;
            if (ground.Items.Count == 0 || EnemyInSight(a)) return false;
            Services.TryGet<FogOfWar>(out var fog);
            Services.TryGet<WorldService>(out var world);
            float radius = Mathf.Max(SearchRadius, a.Stats.SightRadius + 2f);
            var item = ground.FindNearest(a.Position, radius, g =>
                g.IsFreeFor(a) && (allowed == null || allowed(g.Position))
                && (fog == null || fog.IsVisibleRaw(g.Position))
                && (world == null || world.HasLineOfSight(a.Position, g.Position, 2)));
            if (item == null || !item.Reserve(a) || !a.Mover.SetDestination(item.Position, 0.2f))
            {
                item?.Release(a);
                return false;
            }
            _target = item;
            _startedAt = Time.time;
            return true;
        }

        /// <summary>Drops the current target (state change, death).</summary>
        public void Cancel()
        {
            _target?.Release(_owner);
            _target = null;
        }

        static bool EnemyInSight(Adventurer a)
            => UnitRegistry.FindNearest(a.Position, a.Stats.SightRadius, u => u is Monster m && m.IsAlive && m.IsHostileTo(a) && a.CanSee(u)) != null;
    }
}
