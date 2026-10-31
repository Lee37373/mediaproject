using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    public struct VisitorLeftTownEvent { public Adventurer Visitor; }

    /// <summary>
    /// Visitor idling on the plaza until its stay ends (see <see cref="Adventurer.VisitorLeaveAtHour"/>).
    /// While talking with the guild master it stands still and faces them.
    /// </summary>
    public class VisitorWanderState : State<Adventurer>
    {
        float _wanderTimer;

        public override string Label => "방문 중";

        public override void Enter() => _wanderTimer = GameRandom.Shared.Range(0.5f, 2f);

        public override void Tick(float dt)
        {
            var a = Owner;
            if (a.InConversation)
            {
                if (a.Mover.IsMoving) a.Mover.Stop();
                if (a.ConversationPartner != null) a.Visual.FaceTowards(a.ConversationPartner.position);
                return;
            }
            if (AdvCtx.Clock.TotalHours >= a.VisitorLeaveAtHour)
            {
                a.ChangeState(a.VisitorLeaveState);
                return;
            }
            AdvCtx.Wander(a, ref _wanderTimer, dt);
        }
    }

    /// <summary>A refugee from another village (P4-84) reached our town: a visitor like any other from now on.</summary>
    public struct RefugeeArrivedEvent { public Adventurer Visitor; public string From; }

    /// <summary>
    /// A recruitment candidate of another village (P4-84), out in front of it while the guild master is there:
    /// keeps near its spot, and stands still facing the guild master while they talk. The village takes it back
    /// in when the guild master leaves (<see cref="Villages.VillageService"/>).
    /// </summary>
    public class VillageCandidateState : State<Adventurer>
    {
        /// <summary>Where it stands, set by the village before entering.</summary>
        public Vector2 Spot;
        float _idle;

        public override string Label => "영입 후보";

        public override void Enter() => _idle = GameRandom.Shared.Range(1f, 3f);

        public override void Tick(float dt)
        {
            var a = Owner;
            if (a.InConversation)
            {
                if (a.Mover.IsMoving) a.Mover.Stop();
                if (a.ConversationPartner != null) a.Visual.FaceTowards(a.ConversationPartner.position);
                return;
            }
            if (a.Mover.IsMoving) return;
            _idle -= dt;
            if (_idle > 0f) return;
            _idle = GameRandom.Shared.Range(2f, 5f);
            // A step or two about its spot, never further.
            var to = Vector2.Distance(a.Position, Spot) > 1.5f ? Spot : Spot + GameRandom.Shared.InsideUnitCircle() * 1.2f;
            if (Services.Get<WorldService>().Grid.IsWalkable(GridMap.WorldToCell(to))) a.Mover.SetDestination(to, 0.2f);
        }

        public override void Exit() => Owner.Mover.Stop();
    }

    /// <summary>
    /// A candidate fleeing a falling village (P4-84, user decision 2026-10-18): walks to our town — monsters on
    /// the way may fall on it (<see cref="Adventurer.ExposedToMonsters"/>) and it fights back — and once inside
    /// the walls it is a visitor the guild master can talk to.
    /// </summary>
    public class RefugeeState : State<Adventurer>
    {
        const float RepathSeconds = 3f;
        /// <summary>The village it fled, for the log.</summary>
        public string From;
        Vector2 _goal;
        float _repath;

        public override string Label => "피난 중";

        public override void Enter()
        {
            _goal = AdvCtx.Town.RandomPlazaSpot(GameRandom.Shared, 4);
            _repath = 0f;
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (a.InConversation)
            {
                if (a.Mover.IsMoving) a.Mover.Stop();
                if (a.ConversationPartner != null) a.Visual.FaceTowards(a.ConversationPartner.position);
                return;
            }
            // Turned the guild master down on the road: it goes its own way.
            if (AdvCtx.Clock.TotalHours >= a.VisitorLeaveAtHour)
            {
                a.ChangeState(a.VisitorLeaveState);
                return;
            }
            if (AdvCtx.Town.IsInTown(a.Position))
            {
                a.VisitorLeaveAtHour = AdvCtx.Clock.TotalHours + Recruitment.VisitorService.StayHours;
                a.ChangeState(a.VisitorWanderState);
                EventBus.Publish(new RefugeeArrivedEvent { Visitor = a, From = From });
                return;
            }
            if (a.HandleCombat(dt)) return;
            _repath -= dt;
            if (_repath > 0f && a.Mover.IsMoving) return;
            _repath = RepathSeconds;
            a.Mover.SetDestination(_goal, 0.8f);
        }

        public override string SaveState() => From;
        public override void ResumeState(string data, SaveContext ctx)
        {
            From = data;
            Enter();
        }

        public override void Exit() => Owner.Mover.Stop();
    }

    /// <summary>Walks to the nearest town gate, then leaves the map for good.</summary>
    public class VisitorLeaveState : State<Adventurer>
    {
        Vector2 _exit;

        public override string Label => "떠나는 중";

        public override void Enter()
        {
            var a = Owner;
            var world = Services.Get<WorldService>();
            float best = float.MaxValue;
            _exit = a.Position;
            foreach (var g in world.Map.Gates)
            {
                var p = GridMap.CellToWorld(g);
                float d = (p - a.Position).sqrMagnitude;
                if (d < best) { best = d; _exit = p; }
            }
            a.Mover.SetDestination(_exit, 0.5f);
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (a.InConversation)
            {
                // The guild master caught them on the way out: stop and talk.
                a.Mover.Stop();
                a.ChangeState(a.VisitorWanderState);
                return;
            }
            if (Vector2.Distance(a.Position, _exit) < 1.2f || (!a.Mover.IsMoving && TimeInState > 1f))
            {
                EventBus.Publish(new VisitorLeftTownEvent { Visitor = a });
                a.Vanish();
                Object.Destroy(a.gameObject);
            }
        }
    }
}
