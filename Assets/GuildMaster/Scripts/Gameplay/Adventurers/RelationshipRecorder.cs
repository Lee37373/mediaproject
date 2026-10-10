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
            EventBus.Subscribe<QuestCompletedEvent>(OnCompleted);
            EventBus.Subscribe<PartyInviteAnsweredEvent>(OnInviteAnswered);
            EventBus.Subscribe<AdventurerRetreatedEvent>(OnRetreated);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<QuestCompletedEvent>(OnCompleted);
            EventBus.Unsubscribe<PartyInviteAnsweredEvent>(OnInviteAnswered);
            EventBus.Unsubscribe<AdventurerRetreatedEvent>(OnRetreated);
        }

        /// <summary>
        /// Everyone who worked the quest warms to everyone else on it. Counted when it is done, while they
        /// are all still on it: it used to be counted as each was paid, and the board takes a member off the
        /// quest as it pays them, so the first to report warmed to the rest and the last to nobody (P4-77).
        /// </summary>
        void OnCompleted(QuestCompletedEvent e)
        {
            var takers = e.Quest.Assignees;
            for (int i = 0; i < takers.Count; i++)
            {
                if (takers[i] is not Adventurer me || !me.IsAlive) continue;
                for (int j = 0; j < takers.Count; j++)
                    if (j != i && takers[j] is Adventurer other && other.IsAlive)
                        Relationships.Add(me, other, SharedRewardBonus, "함께 의뢰 완수");
            }
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

        /// <summary>
        /// Party mates think less of someone who runs off mid-quest — not of one whose wounds made the whole
        /// party pull back together (P4-71: nobody ran off; P4-77: it still cost the hurt one their regard).
        /// </summary>
        void OnRetreated(AdventurerRetreatedEvent e)
        {
            if (e.WithParty) return;
            var q = e.Adventurer?.CurrentQuest;
            if (q == null) return;
            foreach (var t in q.Assignees)
                if (t is Adventurer other && other != e.Adventurer)
                    Relationships.Add(other, e.Adventurer, RetreatPenalty, "먼저 도망침");
        }
    }
}
