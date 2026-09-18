using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Monsters
{
    /// <summary>Idle around the home nest, occasionally strolling to a random nearby cell.</summary>
    public class MonsterWanderState : State<Monster>
    {
        float _idleTimer;

        public override string Label => "배회";

        public override void Enter()
        {
            _idleTimer = GameRandom.Shared.Range(0.5f, 3f);
        }

        public override void Exit() => Owner.Mover.SpeedMultiplier = 1f;

        public override void Tick(float dt)
        {
            var m = Owner;
            if (Vector2.Distance(m.Position, m.HomePosition) > m.Def.leashRadius)
            {
                Machine.ChangeState(m.ReturnState);
                return;
            }
            var world = Services.Get<WorldService>();

            // Pack flocking: followers keep a formation slot around the leader.
            var leader = m.PackLeader;
            if (leader != null && leader != m)
            {
                int index = 0, count = 0;
                foreach (var member in m.Home.Members)
                {
                    if (!member.IsAlive) continue;
                    if (member == m) index = count;
                    count++;
                }
                float angle = index * Mathf.PI * 2f / Mathf.Max(1, count - 1) + leader.GetInstanceID() % 7;
                var slot = leader.Position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (0.9f + 0.25f * count);
                float off = Vector2.Distance(m.Position, slot);
                if (off > 1.2f && (!m.Mover.IsMoving || Vector2.Distance(m.Mover.Destination, slot) > 1f))
                {
                    m.Mover.SpeedMultiplier = off > 3f ? 1.25f : 1f;
                    if (world.Grid.IsWalkable(GridMap.WorldToCell(slot))) m.Mover.SetDestination(slot, 0.3f);
                    else m.Mover.SetDestination(leader.Position, 1f);
                }
                return;
            }

            if (m.Mover.IsMoving) return;
            _idleTimer -= dt;
            if (_idleTimer > 0f) return;
            _idleTimer = GameRandom.Shared.Range(2f, 6f);

            var home = GridMap.WorldToCell(m.HomePosition);
            if (world.Grid.TryRandomWalkableNear(home, Mathf.CeilToInt(m.Def.wanderRadius), GameRandom.Shared, out var cell)
                && !world.Grid.IsInTown(cell))
            {
                m.Mover.SetDestination(GridMap.CellToWorld(cell) + GameRandom.Shared.InsideUnitCircle() * 0.3f);
            }
        }
    }

    /// <summary>Chase and attack the current target until it dies, escapes, or we get leashed.</summary>
    public class MonsterEngageState : State<Monster>
    {
        public override string Label => "전투";

        public override void Tick(float dt)
        {
            var m = Owner;
            var target = m.Engage.Target;
            bool leashed = m.DefaultStateOverride == null
                           && Vector2.Distance(m.Position, m.HomePosition) > m.Def.leashRadius
                           && (target == null || !m.WasRecentlyAttackedBy(target, 3f));
            if (leashed)
            {
                m.Engage.Clear();
                Machine.ChangeState(m.ReturnState);
                return;
            }

            if (m.Engage.Tick(dt) == EngageStatus.Lost)
            {
                // Look for another target before giving up.
                var next = UnitRegistry.FindNearestHostile(m, m.Stats.SightRadius);
                if (next != null) m.Engage.SetTarget(next);
                else m.GoToDefaultState();
            }
        }

        public override void Exit() => Owner.Mover.Stop();
    }

    /// <summary>Run directly away from a predator until out of sight.</summary>
    public class MonsterFleeState : State<Monster>
    {
        public Unit From;
        float _repath;

        public override string Label => "도주";

        public override void Enter()
        {
            _repath = 0f;
            Owner.Mover.SpeedMultiplier = 1.25f;
        }

        public override void Tick(float dt)
        {
            var m = Owner;
            bool safe = From == null || !From.IsAlive || Vector2.Distance(m.Position, From.Position) > m.Stats.SightRadius + 3f;
            if (safe || TimeInState > 8f)
            {
                m.GoToDefaultState();
                return;
            }
            _repath -= dt;
            if (_repath > 0f && m.Mover.IsMoving) return;
            _repath = 1f;
            var away = (m.Position - From.Position).normalized;
            if (away == Vector2.zero) away = Random.insideUnitCircle.normalized;
            var world = Services.Get<WorldService>();
            var target = m.Position + away * 6f;
            if (world.Grid.TryFindNearestWalkable(GridMap.WorldToCell(target), 4, out var cell))
                m.Mover.SetDestination(GridMap.CellToWorld(cell));
        }

        public override void Exit() => Owner.Mover.SpeedMultiplier = 1f;
    }

    /// <summary>Walk back to the nest, regenerating health on the way.</summary>
    public class MonsterReturnState : State<Monster>
    {
        public override string Label => "귀소";

        public override void Enter()
        {
            if (!Owner.Mover.SetDestination(Owner.HomePosition, 1f)) Owner.GoToDefaultState();
        }

        public override void Tick(float dt)
        {
            var m = Owner;
            m.Heal(m.Stats.MaxHp * 0.05f * dt);
            if (!m.Mover.IsMoving) Machine.ChangeState(m.WanderState);
        }
    }
}
