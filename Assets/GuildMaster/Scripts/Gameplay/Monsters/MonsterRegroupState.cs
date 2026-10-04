using GuildMaster.AI;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Monsters
{
    /// <summary>
    /// Skilled+ monsters losing a fight alone fall back to a pack mate (following it as it wanders) or
    /// to the nest, then turn on the enemy together with the mates gathered there (P4-28). Gives up
    /// after <see cref="Timeout"/> seconds.
    /// </summary>
    public class MonsterRegroupState : State<Monster>
    {
        const float Timeout = 8f;
        const float ArriveDistance = 2f;
        const float RallyRadius = 6f;
        const float RepathSeconds = 0.5f;

        /// <summary>Pack mate to fall back to (null: go to <see cref="Point"/>, the nest).</summary>
        public Monster RallyMate;
        public Vector2 Point;
        public Unit Enemy;
        float _repath;

        public override string Label => "재집결";

        [System.Serializable]
        class Saved
        {
            public string rally;
            public Vector2 point;
            public string enemy;
        }

        public override string SaveState() => JsonUtility.ToJson(new Saved
        {
            rally = RallyMate != null && RallyMate.IsAlive ? RallyMate.UnitId : null, point = Point,
            enemy = Enemy != null && Enemy.IsAlive ? Enemy.UnitId : null,
        });

        public override void ResumeState(string json, Core.SaveContext ctx)
        {
            var s = JsonUtility.FromJson<Saved>(json);
            RallyMate = ctx.Find<Monster>(s.rally);
            Point = s.point;
            Enemy = ctx.Find<Units.Unit>(s.enemy);
            Enter();
        }

        Vector2 Destination => RallyMate != null && RallyMate.IsAlive ? RallyMate.Position : Point;

        public override void Enter()
        {
            var m = Owner;
            m.Engage.Clear();
            m.Mover.SpeedMultiplier = 1.2f;
            _repath = RepathSeconds;
            if (!m.Mover.SetDestination(Destination, 0.5f)) m.GoToDefaultState();
        }

        public override void Tick(float dt)
        {
            var m = Owner;
            var dest = Destination;
            bool arrived = Vector2.Distance(m.Position, dest) <= ArriveDistance;
            if (!arrived && TimeInState < Timeout)
            {
                // The mate wanders on: keep heading for where it is now.
                _repath -= dt;
                if (_repath <= 0f || !m.Mover.IsMoving)
                {
                    _repath = RepathSeconds;
                    if (Vector2.Distance(m.Mover.Destination, dest) > 1f || !m.Mover.IsMoving) m.Mover.SetDestination(dest, 0.5f);
                }
                return;
            }

            // Rally: strike back together with the mates here (at least one besides us); alone, carry on.
            bool canStrike = Enemy != null && Enemy.IsAlive && Enemy.IsTargetableBy(m);
            int mates = 0;
            foreach (var u in UnitRegistry.All)
                if (u is Monster o && m.IsPackMate(o) && (o.Position - m.Position).sqrMagnitude <= RallyRadius * RallyRadius) mates++;
            if (canStrike && mates >= 1)
            {
                foreach (var u in UnitRegistry.All)
                    if (u is Monster o && m.IsPackMate(o) && (o.Position - m.Position).sqrMagnitude <= RallyRadius * RallyRadius)
                        o.ForceTarget(Enemy);
                m.ForceTarget(Enemy);
                return;
            }
            m.GoToDefaultState();
        }

        public override void Exit() => Owner.Mover.SpeedMultiplier = 1f;
    }
}
