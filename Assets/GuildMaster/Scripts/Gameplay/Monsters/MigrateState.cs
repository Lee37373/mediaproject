using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Ecosystem;
using UnityEngine;

namespace GuildMaster.Monsters
{
    /// <summary>
    /// A member of a migrating band (P4-56), used as the monster's default state: walks with the band
    /// to its new home, spread round the target so they do not queue in single file, and stands still
    /// to heal while the band rests. Fights come through Monster.Sense → EngageState as usual (a
    /// migrant takes on any other kind it meets, unlike a raider on the march).
    /// </summary>
    public class MigrateState : State<Monster>
    {
        const float RepathSeconds = 1.5f;
        const float Spread = 2f;

        float _repath;

        public override string Label => "이주";

        public override void Enter() => _repath = 0f;

        public override void Tick(float dt)
        {
            var m = Owner;
            if (!Services.TryGet<MigrationService>(out var svc) || !svc.TryGetBand(m, out var band) || band.Target == null)
            {
                // The band is gone (settled, gave up): back to ordinary life.
                m.DefaultStateOverride = null;
                m.GoToReturn();
                return;
            }
            if (band.Resting)
            {
                m.Mover.Stop();
                svc.Rest(m, dt);
                return;
            }
            _repath -= dt;
            if (_repath > 0f && m.Mover.IsMoving) return;
            _repath = RepathSeconds;
            // Each member aims at its own spot around the target (stable per monster).
            float angle = (Mathf.Abs(m.UnitId.GetHashCode()) % 360) * Mathf.Deg2Rad;
            var goal = band.Target.WorldPosition + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Spread;
            m.Mover.SetDestination(goal, 1f);
        }

        public override void Exit() => Owner.Mover.Stop();
    }
}
