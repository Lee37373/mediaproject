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
                Object.Destroy(a.gameObject);
            }
        }
    }
}
