using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.LLM;
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
        public bool FromLlm;
    }

    /// <summary>Invitations are over; everyone who joined leaves town together.</summary>
    public struct PartyFormedEvent
    {
        public Quest Quest;
        public Adventurer Leader;
        public IReadOnlyList<Adventurer> Members;
    }

    /// <summary>
    /// LLM party formation (P3-4). When the first adventurer takes a quest for 2+ people, the
    /// best-suited idle members in town are invited; each decides from their own point of view
    /// (LLM, or a trait/values roll as fallback). The leader and joiners wait in
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
            public CancellationTokenSource Cts;
        }

        readonly LlmGateway _gateway;
        readonly QuestBoard _board;
        readonly GuildRoster _roster;
        readonly Dictionary<Quest, Gathering> _gatherings = new();

        public PartyService(LlmGateway gateway, QuestBoard board, GuildRoster roster)
        {
            _gateway = gateway;
            _board = board;
            _roster = roster;
            EventBus.Subscribe<QuestAcceptedEvent>(OnAccepted);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<QuestAcceptedEvent>(OnAccepted);
            foreach (var g in _gatherings.Values) g.Cts.Cancel();
            _gatherings.Clear();
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
        public bool IsInvited(Adventurer a) => _gatherings.Values.Any(g => g.Invited.Contains(a) && !g.Answered.Contains(a));

        void OnAccepted(QuestAcceptedEvent e)
        {
            var q = e.Quest;
            if (q.MaxParty <= 1 || q.Assignees.Count != 1 || e.Taker is not Adventurer leader || _gatherings.ContainsKey(q)) return;
            var candidates = PickCandidates(q, leader);
            if (candidates.Count == 0) return;

            var g = new Gathering { Quest = q, Leader = leader, StartRealTime = Time.unscaledTime, Cts = new CancellationTokenSource() };
            g.Invited.AddRange(candidates);
            g.Pending = candidates.Count;
            _gatherings[q] = g;

            string invite = PartyPrompt.InviteLine(q);
            leader.Say(invite);
            foreach (var c in candidates)
            {
                // The invitation replaces a decision in progress (one LLM call per member, and no
                // race where the member takes the quest on their own while answering the invite).
                if (c.Fsm.Current == c.DecidingState) c.ChangeState(c.IdleState);
                _ = InviteAsync(g, c, invite);
            }
        }

        /// <summary>Idle members in town that could reasonably take this quest, best fit first.</summary>
        List<Adventurer> PickCandidates(Quest q, Adventurer leader)
        {
            return _roster.Members
                .Where(a => a != leader && a.IsAlive && !a.IsVisitor && a.CurrentQuest == null
                            && (a.Fsm.Current == a.IdleState || a.Fsm.Current == a.DecidingState)
                            && !IsInvited(a)
                            && Relationships.Get(leader, a) > HostileThreshold)
                // Friends first: fitness scaled by how the leader feels about them.
                .Select(a => (a, score: RuleBasedDecisionMaker.Score(a, q) * (1f + Relationships.Get(leader, a) / 100f)))
                .Where(x => x.score > 0f)
                .OrderByDescending(x => x.score)
                .Take(q.MaxParty - 1)
                .Select(x => x.a)
                .ToList();
        }

        async Task InviteAsync(Gathering g, Adventurer c, string invite)
        {
            bool accept;
            string say;
            bool fromLlm = false;
            try
            {
                (accept, say, fromLlm) = await Decide(g, c, invite);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                (accept, say) = RuleDecide(c, g.Leader);
            }
            Answer(g, c, accept, say, fromLlm);
        }

        async Task<(bool accept, string say, bool fromLlm)> Decide(Gathering g, Adventurer c, string invite)
        {
            if (_gateway != null && _gateway.Enabled && _gateway.IsReady)
            {
                var result = await _gateway.RequestAsync(PartyPrompt.BuildRequest(c, g.Leader, g.Quest, invite), g.Cts.Token);
                if (result.Ok && LlmJson.TryParse<PartyPrompt.ResponseDto>(result.Text, out var dto) && c != null
                    && PartyPrompt.TryInterpret(dto, c.HpRatio, DecisionPrompt.RiskLabel(c, g.Quest), c.Traits?.DangerAversion ?? 0f,
                        c.Data.values, out var accept, out var say))
                {
                    if (result.Exchange != null) result.Exchange.Note = $"채택: {(accept ? "합류" : "거절")}";
                    Debug.Log($"[LLM-PARTY] {c.Data.name} ← {g.Leader.Data.name} '{g.Quest.Title}': {dto.accept} | \"{say}\" ({result.Seconds:0.0}s)");
                    return (accept, say, true);
                }
                if (result.Exchange != null) result.Exchange.Note = "규칙 기반으로 대체: " + (result.Ok ? "parse" : result.Error);
            }
            var (ra, rs) = RuleDecide(c, g.Leader);
            return (ra, rs, false);
        }

        static (bool accept, string say) RuleDecide(Adventurer c, Adventurer leader)
        {
            if (c == null) return (false, null);
            float chance = PartyPrompt.RuleAcceptChance(c.Data.values, c.Traits?.PartyAffinity ?? 1f, Relationships.Get(c, leader));
            bool accept = GameRandom.Shared.Value < chance;
            return (accept, accept ? "좋아, 같이 가자!" : "이번엔 사양할게.");
        }

        void Answer(Gathering g, Adventurer c, bool accept, string say, bool fromLlm)
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
            if (accept && !alreadyOnQuest && (c.CurrentQuest != null || !_board.TryAccept(q, c))) accept = false;
            if (!string.IsNullOrEmpty(say)) c.Say(say);
            // Before the state change: the last answer ends the gathering (PartyFormedEvent).
            EventBus.Publish(new PartyInviteAnsweredEvent { Quest = q, Leader = g.Leader, Invitee = c, Accepted = accept, FromLlm = fromLlm });
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
            if (g.Pending > 0) g.Cts.Cancel();
            g.Cts.Dispose();
            if (g.Joined.Count == 0) return;
            EventBus.Publish(new PartyFormedEvent { Quest = g.Quest, Leader = g.Leader, Members = g.Joined.ToList() });
        }
    }
}
