using System.Linq;
using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Town;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Monsters
{
    /// <summary>
    /// Raid wave behaviour (P3-6), used as the monster's default state: fights any guild unit it
    /// can see (via Monster.Sense → EngageState), otherwise marches on the nearest standing
    /// building — the town hall weighted as closer — and smashes it. Walls are unwalkable, so
    /// the path goes through the gates.
    /// </summary>
    public class RaidState : State<Monster>
    {
        /// <summary>The town hall counts as this much closer when choosing a target.</summary>
        const float TownHallWeight = 0.6f;
        const float RepathSeconds = 1f;

        /// <summary>
        /// Share of a raider's attack that a building takes (P5-1, <see cref="Session.GameBalance"/>).
        /// Stone takes hits far better than flesh; without this a dozen raiders flattened the town
        /// hall in seconds and nobody could walk back in time to defend it.
        /// </summary>
        public static float BuildingDamageMul { get; set; } = 0.4f;

        Building _target;
        Vector2 _spot;
        float _repath;

        public override string Label => "침공";

        public override void Enter()
        {
            _target = null;
            _repath = 0f;
        }

        public override void Tick(float dt)
        {
            var m = Owner;
            if (!Services.TryGet<TownService>(out var town)) return;
            if (_target == null || _target.IsDestroyed)
            {
                _target = PickTarget(town, m.Position);
                if (_target == null) return; // nothing left standing
                _repath = 0f;
            }

            // Close enough to the wall of the building: hit it.
            var edge = _target.ClosestPoint(m.Position);
            if (Vector2.Distance(m.Position, edge) <= m.Stats.AttackRange + m.Radius + 0.9f)
            {
                m.Mover.Stop();
                if (Time.time >= m.NextAttackTime)
                {
                    m.NextAttackTime = Time.time + m.Stats.AttackCooldown;
                    _target.Damage(m.Stats.Attack * BuildingDamageMul);
                    m.Visual.Flash();
                }
                return;
            }

            _repath -= dt;
            if (_repath > 0f && m.Mover.IsMoving) return;
            _repath = RepathSeconds;
            var world = Services.Get<WorldService>();
            if (world.Grid.TryFindNearestWalkable(GridMap.WorldToCell(edge), 3, out var cell))
            {
                _spot = GridMap.CellToWorld(cell);
                if (!m.Mover.SetDestination(_spot, 0.4f))
                {
                    // Behind a standing gate (P4-39): break it first, then come back to the building.
                    if (m.TryBreakGateToward(_spot, true)) return;
                    _target = null; // unreachable: try another
                }
            }
        }

        public override void Exit() => Owner.Mover.Stop();

        static Building PickTarget(TownService town, Vector2 from)
            => town.Buildings.Where(b => !b.IsDestroyed)
                   .OrderBy(b => Vector2.Distance(from, b.ClosestPoint(from)) * (b.Type == BuildingType.TownHall ? TownHallWeight : 1f))
                   .FirstOrDefault();
    }
}
