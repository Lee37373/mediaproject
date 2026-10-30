using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Units;
using GuildMaster.Villages;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Monsters
{
    /// <summary>
    /// A raid on another village (P4-84), used as the monster's default state like <see cref="RaidState"/>:
    /// marches on the village and batters its palisade. Fights still come through Monster.Sense → EngageState
    /// (the guild's members who come to its defence), and afterwards it goes back to the village.
    /// <see cref="VillageRaids"/> sets <see cref="Target"/> and ends the raid.
    /// </summary>
    public class VillageRaidState : State<Monster>
    {
        const float RepathSeconds = 1f;
        const float FailedRepathSeconds = 5f;

        /// <summary>The village under attack (set by <see cref="VillageRaids"/>, also on load).</summary>
        public Village Target;
        float _repath;

        public override string Label => "마을 습격";

        public override void Enter() => _repath = 0f;

        public override void Tick(float dt)
        {
            var m = Owner;
            var v = Target;
            if (v == null || v.IsDestroyed)
            {
                // Nothing left to batter: the raid is being wound up (VillageRaids hands it back).
                m.Mover.Stop();
                return;
            }
            var world = Services.Get<WorldService>();
            var edge = v.ClosestPoint(m.Position);
            if (Vector2.Distance(m.Position, edge) <= m.Stats.AttackRange + m.Radius + 0.9f && world.HasLineOfSight(m.Position, edge, 2))
            {
                m.Mover.Stop();
                m.Visual.FaceTowards(edge);
                if (Time.time >= m.NextAttackTime)
                {
                    m.NextAttackTime = Time.time + m.AttackCooldownNow;
                    v.Damage(VillageRules.Blow(m.Stats.Attack), m, Services.Get<GameClock>().TotalHours);
                    m.Visual.Flash();
                    m.Visual.PlayAttack();
                }
                return;
            }
            _repath -= dt;
            if (_repath > 0f && m.Mover.IsMoving) return;
            _repath = RepathSeconds;
            // Each raider takes its own side of the palisade, so they do not queue at one corner.
            float angle = (Mathf.Abs(m.UnitId.GetHashCode()) % 360) * Mathf.Deg2Rad;
            var aim = v.WorldPosition + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (VillageRules.Size * 0.5f + 0.8f);
            if (!world.Grid.TryFindNearestWalkable(GridMap.WorldToCell(aim), 3, out var cell)) return;
            if (!m.Mover.SetDestination(GridMap.CellToWorld(cell), 0.4f, RouteChoice.ForPack(m))) _repath = FailedRepathSeconds;
        }

        public override void Exit() => Owner.Mover.Stop();
    }
}
