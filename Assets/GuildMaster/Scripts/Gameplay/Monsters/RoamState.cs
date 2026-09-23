using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Ecosystem;
using UnityEngine;

namespace GuildMaster.Monsters
{
    /// <summary>
    /// A member of a roaming pack (P4-58), used as the monster's default state: walks with the pack
    /// to its next stop, spread round it, and idles and heals there while the pack rests. Fights come
    /// through Monster.Sense → EngageState as usual; afterwards it falls back into step here.
    /// </summary>
    public class RoamState : State<Monster>
    {
        const float RepathSeconds = 1.5f;
        const float Spread = 2f;

        float _repath;

        public override string Label => "떠돌이";

        public override void Enter() => _repath = 0f;

        public override void Tick(float dt)
        {
            var m = Owner;
            var pack = m.Roaming;
            if (pack == null || !Services.TryGet<RoamingPackService>(out var svc))
            {
                // The pack settled or is gone: back to ordinary life.
                m.DefaultStateOverride = null;
                m.GoToReturn();
                return;
            }
            // Each member keeps its own spot round the pack's stop (stable per monster).
            float angle = (Mathf.Abs(m.UnitId.GetHashCode()) % 360) * Mathf.Deg2Rad;
            var goal = (pack.Moving ? pack.Goal : pack.Stop) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Spread;
            if (!pack.Moving)
            {
                svc.Rest(m, dt);
                if (Vector2.Distance(m.Position, goal) < 1.5f) { m.Mover.Stop(); return; }
            }
            _repath -= dt;
            if (_repath > 0f && m.Mover.IsMoving) return;
            _repath = RepathSeconds;
            // The pack walks one route (P4-63): woodland kinds keep to the trees on the road.
            m.Mover.SetDestination(goal, 1f, Units.RouteChoice.ForPack(m));
        }

        public override void Exit() => Owner.Mover.Stop();
    }
}
