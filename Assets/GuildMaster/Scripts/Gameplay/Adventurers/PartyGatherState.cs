using GuildMaster.AI;
using GuildMaster.Core;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Party formation (P3-4): the leader and members who already joined mill about the plaza
    /// until every invitation is answered, then all switch to travelling at the same time.
    /// </summary>
    public class PartyGatherState : State<Adventurer>
    {
        float _wanderTimer;
        float _guardTimer;

        public override string Label => "동료 모집 중";

        public override void Enter() => _wanderTimer = 0.5f;

        public override void Tick(float dt)
        {
            var a = Owner;
            if (!AdvCtx.QuestStillValid(a)) return;
            if (TownGuardState.TryStart(a, this, ref _guardTimer, dt)) return;
            if (!Services.TryGet<PartyService>(out var party) || !party.IsGathering(a.CurrentQuest))
            {
                a.ChangeState(a.TravelState);
                return;
            }
            AdvCtx.Wander(a, ref _wanderTimer, dt);
        }
    }
}
