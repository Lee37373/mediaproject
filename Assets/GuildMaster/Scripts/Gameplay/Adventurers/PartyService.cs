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
            foreach (var h in _hunts.Values) h.Cts.Cancel();
            _hunts.Clear();
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
            public CancellationTokenSource Cts;
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
        /// likes most first. Each answers from its own point of view (LLM, rule fallback).
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

            var g = new HuntGathering { Band = band, Leader = leader, StartRealTime = Time.unscaledTime, Cts = new CancellationTokenSource() };
            g.Invited.AddRange(candidates);
            g.Pending = candidates.Count;
            _hunts[band] = g;

            string invite = PartyPrompt.HuntInviteLine();
            leader.Say(invite);
            int size = band.Members.Count + candidates.Count;
            foreach (var c in candidates)
            {
                if (c.Fsm.Current == c.DecidingState) c.ChangeState(c.IdleState);
                _ = InviteToHuntAsync(g, c, invite, size);
            }
        }

        async Task InviteToHuntAsync(HuntGathering g, Adventurer c, string invite, int size)
        {
            bool accept;
            string say;
            bool fromLlm = false;
            try
            {
                (accept, say, fromLlm) = await DecideHunt(g, c, invite, size);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                (accept, say) = RuleDecide(c, g.Leader);
            }
            AnswerHunt(g, c, accept, say, fromLlm);
        }

        async Task<(bool accept, string say, bool fromLlm)> DecideHunt(HuntGathering g, Adventurer c, string invite, int size)
        {
            if (_gateway != null && _gateway.Enabled && _gateway.IsReady)
            {
                var result = await _gateway.RequestAsync(PartyPrompt.BuildHuntRequest(c, g.Leader, invite, size), g.Cts.Token);
                if (result.Ok && LlmJson.TryParse<PartyPrompt.ResponseDto>(result.Text, out var dto) && c != null
                    && PartyPrompt.TryInterpret(dto, c.HpRatio, PartyPrompt.HuntRisk, c.Traits?.DangerAversion ?? 0f,
                        c.Data.values, out var accept, out var say))
                {
                    if (result.Exchange != null) result.Exchange.Note = $"채택: {(accept ? "합류" : "거절")}";
                    Debug.Log($"[LLM-PARTY] {c.Data.name} ← {g.Leader.Data.name} 자율 사냥: {dto.accept} | \"{say}\" ({result.Seconds:0.0}s)");
                    return (accept, say, true);
                }
                if (result.Exchange != null) result.Exchange.Note = "규칙 기반으로 대체: " + (result.Ok ? "parse" : result.Error);
            }
            var (ra, rs) = RuleDecide(c, g.Leader);
            return (ra, rs, false);
        }

        void AnswerHunt(HuntGathering g, Adventurer c, bool accept, string say, bool fromLlm)
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
            EventBus.Publish(new PartyInviteAnsweredEvent { Quest = null, Leader = g.Leader, Invitee = c, Accepted = accept, FromLlm = fromLlm });
            if (accept)
            {
                g.Band.Members.Add(c); // before the state change, so its Enter finds this band
                c.ChangeState(c.FreeHuntState);
            }
        }

        void FinishHunt(HuntGathering g)
        {
            if (!_hunts.Remove(g.Band)) return;
            if (g.Pending > 0) g.Cts.Cancel();
            g.Cts.Dispose();
        }

        void OnAccepted(QuestAcceptedEvent e)
        {
            var q = e.Quest;
            if (q.MaxParty <= 1 || q.Assignees.Count != 1 || e.Taker is not Adventurer leader || _gatherings.ContainsKey(q)) return;
            // The leader's standing party comes along without being asked (P4-50): those in town
            // and free take the quest with it; invitations only fill the slots left.
            var comrades = PartyComrades(q, leader);
            var candidates = PickCandidates(q, leader);
            if (candidates.Count == 0 && comrades.Count == 0) return;

            var g = new Gathering { Quest = q, Leader = leader, StartRealTime = Time.unscaledTime, Cts = new CancellationTokenSource() };
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

        static bool FreeInTown(Adventurer a)
            => a.IsAlive && !a.IsVisitor && a.CurrentQuest == null && (a.Fsm.Current == a.IdleState || a.Fsm.Current == a.DecidingState);

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
                            // Someone else's party member waits for its own leader (P4-50).
                            && (parties == null || !parties.FollowsALeader(a))
                            && Relationships.Get(leader, a) > HostileThreshold)
                // Friends first: fitness scaled by how the leader feels about them.
                .Select(a => (a, score: RuleBasedDecisionMaker.Score(a, q) * (1f + Relationships.Get(leader, a) / 100f)))
                .Where(x => x.score > 0f)
                .OrderByDescending(x => x.score)
                .Take(slots)
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
            if (accept && !alreadyOnQuest && (c.CurrentQuest != null || !_board.TryAccept(q, c, invited: true))) accept = false;
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
