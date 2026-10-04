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
            if (MonsterLeash.IsStrayed(m))
            {
                Machine.ChangeState(m.ReturnState);
                return;
            }
            var world = Services.Get<WorldService>();

            // Pack flocking: followers keep a formation slot around the leader.
            // The leader comes from the pack, which may be a roaming one with no nest (P4-58), so the
            // slots are counted over the pack too: going by m.Home here threw for such a monster.
            var leader = m.PackLeader;
            var pack = m.PackMembers;
            if (leader != null && leader != m && pack != null)
            {
                int index = 0, count = 0;
                foreach (var member in pack)
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
            // Too far from home and nothing keeps it here (see MonsterLeash for the whole rule).
            bool leash;
            using (s_leashMarker.Auto()) leash = MonsterLeash.ShouldReturn(m);
            if (leash)
            {
                m.Engage.Clear();
                Machine.ChangeState(m.ReturnState);
                return;
            }

            // Veterans losing a fight drag the pursuer through another species' territory (P4-18).
            if (m.Tier >= CombatTier.Veteran && m.HpRatio < 0.35f && Time.time >= m.NextLureTime)
            using (s_lureMarker.Auto())
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

            // Skilled+ monsters losing a fight alone fall back to their pack and strike back together (P4-28).
            using (s_regroupMarker.Auto())
                if (m.TryStartRegroup()) return;

            EngageStatus status;
            using (s_engageMarker.Auto()) status = m.Engage.Tick(dt);
            if (status == EngageStatus.Lost)
            using (s_lostMarker.Auto())
            {
                // Chased someone back behind the walls (P4-39, user decision): the gate is what is
                // in the way, so break it.
                if (target != null && target.IsAlive && m.Engage.IsIgnoring(target) && m.TryBreakGateToward(target.Position, false)) return;
                // Look for another target before giving up.
                var next = m.FindTarget();
                if (next != null) m.Engage.SetTarget(next);
                else m.GoToDefaultState();
            }
        }

        public override void Exit() => Owner.Mover.Stop();

        static readonly Unity.Profiling.ProfilerMarker s_leashMarker = new("MonsterEngage.Leash"),
            s_regroupMarker = new("MonsterEngage.Regroup"), s_engageMarker = new("MonsterEngage.Engagement"),
            s_lureMarker = new("MonsterEngage.Lure"), s_lostMarker = new("MonsterEngage.Lost");
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

        [System.Serializable]
        class Saved
        {
            public string from;
            public Vector2 point;
        }

        public override string SaveState()
            => JsonUtility.ToJson(new Saved { from = From != null && From.IsAlive ? From.UnitId : null, point = FromPoint });

        public override void ResumeState(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<Saved>(json);
            From = ctx.Find<Unit>(s.from);
            FromPoint = s.point;
            Enter();
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

        [System.Serializable]
        class Saved { public Vector2 point; }

        public override string SaveState() => JsonUtility.ToJson(new Saved { point = Point });

        public override void ResumeState(string json, SaveContext ctx)
        {
            Point = JsonUtility.FromJson<Saved>(json).point;
            Enter();
        }

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
            // Home: its default life (wandering, or the pack's road if it roams), not always wandering.
            if (!m.Mover.IsMoving) m.GoToDefaultState();
        }
    }
}
