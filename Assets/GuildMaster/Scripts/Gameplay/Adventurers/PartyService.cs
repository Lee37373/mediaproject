using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Quests;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    public struct PartyInviteAnsweredEvent
    {
        public Quest Quest;
        public Adventurer Leader;
        public Adventurer Invitee;
        public bool Accepted;
    }

    /// <summary>Invitations are over; everyone who joined leaves town together.</summary>
    public struct PartyFormedEvent
    {
        public Quest Quest;
        public Adventurer Leader;
        public IReadOnlyList<Adventurer> Members;
    }

    /// <summary>
    /// Party formation (P3-4). When the first adventurer takes a quest for 2+ people, the best-suited idle members in
    /// town are invited; each answers from its own point of view — by values, traits and feeling towards the leader
    /// (P4-91, user decision 2026-10-19: the rules decide, the model no longer does). The leader and joiners wait in
    /// <see cref="PartyGatherState"/> until every answer is in, then depart together.
    /// </summary>
    public class PartyService : IDisposable
    {
        /// <summary>Real seconds before stragglers are dropped and the party leaves anyway.</summary>
        public const float GatherTimeoutRealSeconds = 60f;
        /// <summary>The leader does not invite members they feel this badly about (적대).</summary>
        public const int HostileThreshold = -40;

        class Gathering
        {
            public Quest Quest;
            public Adventurer Leader;
            public readonly List<Adventurer> Invited = new();
            public readonly List<Adventurer> Joined = new();
            public readonly HashSet<Adventurer> Answered = new();
            public int Pending;
            public float StartRealTime;
        }

        readonly QuestBoard _board;
        readonly GuildRoster _roster;
        readonly Dictionary<Quest, Gathering> _gatherings = new();

        public PartyService(QuestBoard board, GuildRoster roster)
        {
            _board = board;
            _roster = roster;
            EventBus.Subscribe<QuestAcceptedEvent>(OnAccepted);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<QuestAcceptedEvent>(OnAccepted);
            _hunts.Clear();
            _gatherings.Clear();
        }

        /// <summary>What the leader says asking someone along on a quest.</summary>
        public static string InviteLine(Quest q) => $"'{q.Title}' 같이 가지 않을래?";

        /// <summary>What the leader says asking someone along on a free hunt (P4-51).</summary>
        public static string HuntInviteLine() => "사냥 나가는데, 같이 갈래?";

        /// <summary>
        /// Odds an invitee says yes (spec P3-4): values 동료 0.8, loner-type traits 0.2, else 0.55; plus feeling towards
        /// the leader / 200 (P3-5), kept within 0.05..0.95.
        /// </summary>
        public static float RuleAcceptChance(string values, float partyAffinity, int feelingToLeader = 0)
        {
            float c = values == "동료" ? 0.8f : partyAffinity < 1f ? 0.2f : 0.55f;
            return Math.Max(0.05f, Math.Min(0.95f, c + feelingToLeader / 200f));
        }

        /// <summary>True while invitations for this quest are still being answered.</summary>
        public bool IsGathering(Quest q)
        {
            if (q == null || !_gatherings.TryGetValue(q, out var g)) return false;
            if (g.Pending > 0 && Time.unscaledTime - g.StartRealTime < GatherTimeoutRealSeconds && q.IsActive) return true;
            Finish(g);
            return false;
        }

        /// <summary>Invited members skip their own decisions until they have answered.</summary>
        public bool IsInvited(Adventurer a)
            => _gatherings.Values.Any(g => g.Invited.Contains(a) && !g.Answered.Contains(a))
               || _hunts.Values.Any(g => g.Invited.Contains(a) && !g.Answered.Contains(a));

        // ---------------- Free-hunt invitations (P4-51) ----------------

        /// <summary>Invitees for a hunt must be this healthy and this rested (they are not asked otherwise).</summary>
        const float HuntMinHp = 0.6f;
        const float HuntMaxFatigue = 60f;

        class HuntGathering
        {
            public HuntBand Band;
            public Adventurer Leader;
            public readonly List<Adventurer> Invited = new();
            public readonly HashSet<Adventurer> Answered = new();
            public int Pending;
            public float StartRealTime;
        }

        readonly Dictionary<HuntBand, HuntGathering> _hunts = new();

        /// <summary>True while hunt invitations for this band are still being answered (it waits at the guild).</summary>
        public bool IsGathering(HuntBand b)
        {
            if (b == null || !_hunts.TryGetValue(b, out var g)) return false;
            if (g.Pending > 0 && Time.unscaledTime - g.StartRealTime < GatherTimeoutRealSeconds && b.Mustering && g.Leader.IsAlive) return true;
            FinishHunt(g);
            return false;
        }

        /// <summary>
        /// A member going hunting asks others in town to come (P4-51, user request: hunts are joined by
        /// invitation too): free, not in someone else's party, not hostile to it, fit enough; the ones it
        /// likes most first. Each answers from its own point of view (<see cref="RuleDecide"/>).
        /// </summary>
        public void InviteToHunt(Adventurer leader, HuntBand band, int slots)
        {
            if (leader == null || band == null || slots <= 0 || _hunts.ContainsKey(band)) return;
            Services.TryGet<GuildParties>(out var parties);
            var candidates = _roster.Members
                .Where(a => a != leader && FreeInTown(a) && !band.Has(a) && !IsInvited(a)
                            && (parties == null || parties.PartyOf(a) == null)
                            && a.HpRatio >= HuntMinHp && a.Data.fatigue < HuntMaxFatigue
                            && Relationships.Get(leader, a) > HostileThreshold)
                .OrderByDescending(a => Relationships.Get(leader, a))
                .Take(slots)
                .ToList();
            if (candidates.Count == 0) return;

            var g = new HuntGathering { Band = band, Leader = leader, StartRealTime = Time.unscaledTime };
            g.Invited.AddRange(candidates);
            g.Pending = candidates.Count;
            _hunts[band] = g;

            leader.Say(HuntInviteLine());
            foreach (var c in candidates)
            {
                if (c.Fsm.Current == c.DecidingState) c.ChangeState(c.IdleState);
                var (accept, say) = RuleDecide(c, leader);
                AnswerHunt(g, c, accept, say);
            }
        }

        void AnswerHunt(HuntGathering g, Adventurer c, bool accept, string say)
        {
            if (!_hunts.TryGetValue(g.Band, out var current) || current != g) return; // timed out or the band left
            g.Pending--;
            g.Answered.Add(c);
            if (c == null || !c.IsAlive) return;
            // Already came along on its own (joined the band while the answer was being written): nothing to say.
            if (g.Band.Has(c)) return;
            // Still free, and the band still waiting for company.
            if (accept && (!FreeInTown(c) || !g.Band.Mustering)) accept = false;
            if (!string.IsNullOrEmpty(say)) c.Say(say);
            EventBus.Publish(new PartyInviteAnsweredEvent { Quest = null, Leader = g.Leader, Invitee = c, Accepted = accept });
            if (accept)
            {
                g.Band.Members.Add(c); // before the state change, so its Enter finds this band
                c.ChangeState(c.FreeHuntState);
            }
        }

        void FinishHunt(HuntGathering g) => _hunts.Remove(g.Band);

        void OnAccepted(QuestAcceptedEvent e)
        {
            var q = e.Quest;
            if (q.MaxParty <= 1 || q.Assignees.Count != 1 || e.Taker is not Adventurer leader || _gatherings.ContainsKey(q)) return;
            // The leader's standing party comes along without being asked (P4-50): those in town
            // and free take the quest with it; invitations only fill the slots left.
            var comrades = PartyComrades(q, leader);
            var candidates = PickCandidates(q, leader);
            if (candidates.Count == 0 && comrades.Count == 0)
            {
                // Party work nobody can be asked along for (P4-85): not walked into alone.
                if (QuestRisk.TooFewFor(q, AdventurerStats.Power(leader.Data), q.Assignees.Count)) GiveUp(q, leader, comrades);
                return;
            }

            var g = new Gathering { Quest = q, Leader = leader, StartRealTime = Time.unscaledTime };
            g.Joined.AddRange(comrades);
            g.Invited.AddRange(candidates);
            g.Pending = candidates.Count;
            _gatherings[q] = g;
            // After the gathering exists, so they wait in PartyGatherState with the rest; with no
            // invitations left to answer IsGathering finishes it at once and all leave together.
            foreach (var m in comrades)
            {
                m.CurrentQuest = q;
                m.Say("가자, 리더!");
                m.ChangeState(m.TravelState);
            }
            if (candidates.Count == 0) return;

            leader.Say(InviteLine(q));
            foreach (var c in candidates)
            {
                // The invitation replaces a decision in progress (no race where the member takes the quest on its
                // own while answering the invite).
                if (c.Fsm.Current == c.DecidingState) c.ChangeState(c.IdleState);
                var (accept, say) = RuleDecide(c, leader, q);
                Answer(g, c, accept, say);
            }
        }

        /// <summary>
        /// Idle in town and fit to go out: the only members asked along, and the only ones an answer
        /// can still take (P4-69: on leave, wounded or breaking, a member stays home whatever it says).
        /// </summary>
        static bool FreeInTown(Adventurer a)
            => a.IsAlive && !a.IsVisitor && a.CurrentQuest == null && a.IsIdleInTown && Welfare.GuildWelfare.MayGoOut(a);

        /// <summary>The leader's party members free in town, in party order, as many as the quest takes (they accept it here).</summary>
        List<Adventurer> PartyComrades(Quest q, Adventurer leader)
        {
            var list = new List<Adventurer>();
            if (!Services.TryGet<GuildParties>(out var parties) || parties.PartyOf(leader) is not { } party || party.Leader != leader) return list;
            foreach (var m in party.Members)
            {
                if (m == leader || !FreeInTown(m) || IsInvited(m) || !q.HasFreeSlot) continue;
                if (_board.TryAccept(q, m, invited: true)) list.Add(m);
            }
            return list;
        }

        /// <summary>Idle members in town that could reasonably take this quest, best fit first.</summary>
        List<Adventurer> PickCandidates(Quest q, Adventurer leader)
        {
            Services.TryGet<GuildParties>(out var parties);
            int slots = q.MaxParty - q.Assignees.Count;
            if (slots <= 0) return new List<Adventurer>();
            return _roster.Members
                .Where(a => a != leader && FreeInTown(a)
                            && !IsInvited(a)
                            // Someone else's party stays with its own leader (P4-50) — its leader too, as for hunts
                            // (P4-77: a leader who said yes pulled the inviter into its party, left its own members
                            // waiting in town for the whole quest, and reset an outing still being split).
                            && (parties == null || parties.PartyOf(a) == null)
                            && Relationships.Get(leader, a) > HostileThreshold)
                // Friends first: fitness scaled by how the leader feels about them.
                .Select(a => (a, score: RuleBasedDecisionMaker.Score(a, q) * (1f + Relationships.Get(leader, a) / 100f)))
                .Where(x => x.score > 0f)
                .OrderByDescending(x => x.score)
                .Take(slots)
                .Select(x => x.a)
                .ToList();
        }

        /// <summary>What a member too green for the quest says turning the invitation down (P4-88).</summary>
        public const string TooGreenLine = "아직 나한텐 벅찬 일이야.";

        /// <summary>
        /// The rules' answer: by values, traits and feeling towards the leader — and, for a quest (P4-88), not when the
        /// member is far under its level and it is 위험 for them (they had followed the party at 55% regardless).
        /// </summary>
        static (bool accept, string say) RuleDecide(Adventurer c, Adventurer leader, Quest quest = null)
        {
            if (c == null) return (false, null);
            if (quest != null && QuestRisk.HoldsBack(c, quest)) return (false, TooGreenLine);
            float chance = RuleAcceptChance(c.Data.values, c.Traits?.PartyAffinity ?? 1f, Relationships.Get(c, leader));
            bool accept = GameRandom.Shared.Value < chance;
            return (accept, accept ? "좋아, 같이 가자!" : "이번엔 사양할게.");
        }

        void Answer(Gathering g, Adventurer c, bool accept, string say)
        {
            // Timed out / finished already — or a newer gathering for the same quest took its place
            // (the leader left and someone else took it): a stale answer must not join that one.
            if (!_gatherings.TryGetValue(g.Quest, out var current) || current != g) return;
            g.Pending--;
            g.Answered.Add(c);
            if (c == null || !c.IsAlive) return;

            var q = g.Quest;
            // The member may have taken another quest while thinking; the slot may be gone.
            bool alreadyOnQuest = c.CurrentQuest == q && q.IsAssigned(c);
            // Still free (P4-69, as a hunt answer already checks): a member that went to rest, to the
            // guild counter or out to meet an intruder meanwhile is not pulled out of it by a late yes.
            if (accept && !alreadyOnQuest && (c.CurrentQuest != null || !FreeInTown(c) || !_board.TryAccept(q, c, invited: true))) accept = false;
            if (!string.IsNullOrEmpty(say)) c.Say(say);
            // Before the state change: the last answer ends the gathering (PartyFormedEvent).
            EventBus.Publish(new PartyInviteAnsweredEvent { Quest = q, Leader = g.Leader, Invitee = c, Accepted = accept });
            if (accept)
            {
                c.CurrentQuest = q;
                g.Joined.Add(c);
                c.ChangeState(c.TravelState); // redirected to PartyGatherState while gathering
            }
        }

        void Finish(Gathering g)
        {
            if (!_gatherings.Remove(g.Quest)) return;
            // Party work (P4-85) that too few said yes to: the group does not set out to be killed.
            if (QuestRisk.TooFewFor(g.Quest, AdventurerStats.Power(g.Leader.Data), g.Quest.Assignees.Count))
            {
                GiveUp(g.Quest, g.Leader, g.Joined);
                return;
            }
            if (g.Joined.Count == 0) return;
            EventBus.Publish(new PartyFormedEvent { Quest = g.Quest, Leader = g.Leader, Members = g.Joined.ToList() });
        }

        /// <summary>Said by a leader whose party work found too few to go along.</summary>
        public const string TooFewLine = "이 인원으로는 무리야. 다음에 다시 모이자.";

        /// <summary>The quest goes back on the board; the leader and whoever had joined stay in town.</summary>
        void GiveUp(Quest q, Adventurer leader, List<Adventurer> joined)
        {
            foreach (var a in joined.Append(leader).ToList())
            {
                if (a == null) continue;
                _board.Abandon(q, a);
                if (a.CurrentQuest == q) a.CurrentQuest = null;
                if (a.IsAlive) a.ChangeState(a.IdleState);
            }
            if (leader != null && leader.IsAlive) leader.Say(TooFewLine);
        }
    }
}
