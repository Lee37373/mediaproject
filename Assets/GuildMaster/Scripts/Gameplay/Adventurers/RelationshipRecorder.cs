using System;
using GuildMaster.Core;
using GuildMaster.Quests;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Rule-based relationship changes from shared experiences (P3-5). Small talk changes
    /// feelings through the LLM in <see cref="BanterService"/>.
    /// </summary>
    public class RelationshipRecorder : IDisposable
    {
        public const int SharedRewardBonus = 3;
        public const int InviteAcceptedBonus = 3;
        public const int InviteRefusedPenalty = -2;
        public const int RetreatPenalty = -3;

        public RelationshipRecorder()
        {
            EventBus.Subscribe<QuestRewardPaidEvent>(OnRewardPaid);
            EventBus.Subscribe<PartyInviteAnsweredEvent>(OnInviteAnswered);
            EventBus.Subscribe<AdventurerRetreatedEvent>(OnRetreated);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<QuestRewardPaidEvent>(OnRewardPaid);
            EventBus.Unsubscribe<PartyInviteAnsweredEvent>(OnInviteAnswered);
            EventBus.Unsubscribe<AdventurerRetreatedEvent>(OnRetreated);
        }

        /// <summary>Each paid member warms to everyone else who worked the same quest.</summary>
        void OnRewardPaid(QuestRewardPaidEvent e)
        {
            if (e.Taker is not Adventurer me) return;
            foreach (var t in e.Quest.Assignees)
                if (t is Adventurer other && other != me)
                    Relationships.Add(me, other, SharedRewardBonus, "함께 의뢰 완수");
        }

        void OnInviteAnswered(PartyInviteAnsweredEvent e)
        {
            if (e.Accepted)
            {
                Relationships.Add(e.Leader, e.Invitee, InviteAcceptedBonus, "파티 합류");
                Relationships.Add(e.Invitee, e.Leader, InviteAcceptedBonus, "파티 합류");
            }
            else Relationships.Add(e.Leader, e.Invitee, InviteRefusedPenalty, "파티 거절");
        }

        /// <summary>Party mates think less of someone who runs off mid-quest.</summary>
        void OnRetreated(AdventurerRetreatedEvent e)
        {
            var q = e.Adventurer?.CurrentQuest;
            if (q == null) return;
            foreach (var t in q.Assignees)
                if (t is Adventurer other && other != e.Adventurer)
                    Relationships.Add(other, e.Adventurer, RetreatPenalty, "먼저 도망침");
        }
    }
}
