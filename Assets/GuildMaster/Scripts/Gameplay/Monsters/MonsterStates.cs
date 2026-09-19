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
            Owner.Engage.Clear();
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
        /// <summary>Leash multiplier while chasing a live target.</summary>
        public const float PursuitLeashFactor = 2f;

        public override string Label => "전투";

        public override void Tick(float dt)
        {
            var m = Owner;
            // Far from home we only keep fighting while someone is still hitting us — any attacker,
            // not just the current target (intruders mobbed by a nest's residents fight back, user request).
            var attacker = m.LastAttacker;
            bool underAttack = attacker != null && attacker.IsAlive && m.WasRecentlyAttackedBy(attacker, 3f);
            // A live chase runs twice as far from home, so a pursuer can be lured into another pack's
            // territory (user report: it used to turn back first). Giving up on a faster target is
            // still handled by the engagement's chase stall.
            var target = m.Engage.Target;
            float leash = m.Def.leashRadius * (target != null && target.IsAlive ? PursuitLeashFactor : 1f);
            bool leashed = m.DefaultStateOverride == null
                           && Vector2.Distance(m.Position, m.HomePosition) > leash
                           && !underAttack;
            if (leashed)
            {
                m.Engage.Clear();
                Machine.ChangeState(m.ReturnState);
                return;
            }

            // Veterans losing a fight drag the pursuer through another species' territory (P4-18).
            if (m.Tier >= CombatTier.Veteran && m.HpRatio < 0.35f && Time.time >= m.NextLureTime)
            {
                m.NextLureTime = Time.time + 20f;
                var pursuer = Ecosystem.TerritoryTactics.FindPursuer(m);
                if (pursuer != null && Ecosystem.TerritoryTactics.FindLurePoint(m, pursuer, m.Position + (m.Position - pursuer.Position).normalized * 8f, false, out var point, out _))
                {
                    m.LureState.Point = point;
                    Machine.ChangeState(m.LureState);
                    return;
                }
            }

            if (m.Engage.Tick(dt) == EngageStatus.Lost)
            {
                // Look for another target before giving up.
                var next = m.FindTarget();
                if (next != null) m.Engage.SetTarget(next);
                else m.GoToDefaultState();
            }
        }

        public override void Exit() => Owner.Mover.Stop();
    }

    /// <summary>Run directly away from a predator until out of sight.</summary>
    public class MonsterFleeState : State<Monster>
    {
        /// <summary>Run from this unit, or (when null) from <see cref="FromPoint"/>.</summary>
        public Unit From;
        public Vector2 FromPoint;
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
            var threat = From != null ? From.Position : FromPoint;
            bool safe = From != null
                ? !From.IsAlive || Vector2.Distance(m.Position, threat) > m.Stats.SightRadius + 3f
                : Vector2.Distance(m.Position, threat) > m.Stats.SightRadius + 3f;
            if (safe || TimeInState > 8f)
            {
                m.GoToDefaultState();
                return;
            }
            _repath -= dt;
            if (_repath > 0f && m.Mover.IsMoving) return;
            _repath = 1f;
            var away = (m.Position - threat).normalized;
            if (away == Vector2.zero) away = Random.insideUnitCircle.normalized;
            var world = Services.Get<WorldService>();
            var target = m.Position + away * 6f;
            if (world.Grid.TryFindNearestWalkable(GridMap.WorldToCell(target), 4, out var cell))
                m.Mover.SetDestination(GridMap.CellToWorld(cell));
        }

        public override void Exit() => Owner.Mover.SpeedMultiplier = 1f;
    }

    /// <summary>
    /// Runs past another species' territory so the pursuer has to cross it (veteran monsters, P4-18),
    /// then resumes; the third-party wait in Engagement keeps it out of the brawl for a while.
    /// </summary>
    public class MonsterLureState : State<Monster>
    {
        const float Timeout = 10f;
        public Vector2 Point;

        public override string Label => "유인";

        public override void Enter()
        {
            var m = Owner;
            m.Engage.Clear();
            m.Mover.SpeedMultiplier = 1.2f;
            if (!m.Mover.SetDestination(Point, 0.5f)) m.GoToDefaultState();
        }

        public override void Tick(float dt)
        {
            var m = Owner;
            if (Vector2.Distance(m.Position, Point) < 0.8f || !m.Mover.IsMoving || TimeInState > Timeout)
                m.GoToDefaultState();
        }

        public override void Exit() => Owner.Mover.SpeedMultiplier = 1f;
    }

    /// <summary>Walk back to the nest, regenerating health on the way.</summary>
    public class MonsterReturnState : State<Monster>
    {
        public override string Label => "귀소";

        public override void Enter()
        {
            Owner.Engage.Clear();   // no stale target (it would count as piling on in TargetSelector)
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
