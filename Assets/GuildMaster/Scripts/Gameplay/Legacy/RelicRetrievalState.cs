using GuildMaster.AI;
using GuildMaster.Adventurers;
using UnityEngine;

namespace GuildMaster.Legacy
{
    /// <summary>
    /// A member sent out alone (from the memorial window) to bring a fallen comrade's relics home (F-6): walks to
    /// the spot where they fell, fights what is there, picks the items up (the ordinary loot pickup does the taking)
    /// and heads back. Gives up when nothing is left, when hurt, or after <see cref="LegacyRules.RetrievalHours"/>.
    /// </summary>
    public class RelicRetrievalState : State<Adventurer>
    {
        /// <summary>Real seconds to look around at the spot before giving up on what is not there.</summary>
        const float LingerSeconds = 2.5f;
        const float ArriveDistance = 1.0f;

        public FallenRecord Record { get; private set; }
        Vector2 _target;
        float _linger;
        float _timeout;
        bool _cannotReach;

        public override string Label => "유품 회수";

        /// <summary>Set before switching to this state (the owner is only bound when the state is entered).</summary>
        public void Begin(FallenRecord record, Vector2 target)
        {
            Record = record;
            _target = target;
        }

        public override void Enter()
        {
            var a = Owner;
            _linger = 0f;
            _cannotReach = false;
            _timeout = AdvCtx.Clock.HoursToSeconds(LegacyRules.RetrievalHours);
            a.Say(LegacyTexts.RetrieverLine(Record != null ? Record.Name : "동료"));
            if (!a.Mover.SetDestination(_target, 0.8f)) _cannotReach = true;
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (a.CheckRetreat()) return;
            if (_cannotReach || Record == null || !Record.HasLying || TimeInState > _timeout)
            {
                Finish();
                return;
            }
            if (a.HandleCombat(dt)) return;
            if (a.Looting.Tick(dt)) return;

            var next = Record.FirstLying();
            if (next != null) _target = next.Position;

            if (Vector2.Distance(a.Position, _target) > ArriveDistance)
            {
                _linger = 0f;
                if (!a.Mover.IsMoving && !a.Mover.SetDestination(_target, 0.5f)) Finish();
                return;
            }

            // At the spot with nothing left to pick up: whatever remains is out of reach — or gone,
            // which the guild learns now that someone is standing here (P4-69).
            _linger += dt;
            if (_linger < LingerSeconds) return;
            if (Core.Services.TryGet<GuildLegacy>(out var legacy)) legacy.CheckRelicsInSight();
            Finish();
        }

        void Finish() => Owner.ChangeState(Owner.ReturnState);

        public override void Exit()
        {
            Owner.Mover.SpeedMultiplier = 1f;
        }
    }
}
