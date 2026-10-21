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
    /// The first taker of party work found too few free to go and waits in town gathering the rest as they come home
    /// (P4-97). It ends in <see cref="PartyFormedEvent"/>, or — time up and still too few — the quest given up.
    /// </summary>
    public struct PartyRecruitingEvent
    {
        public Quest Quest;
        public Adventurer Leader;
        public int Count;
    }

    /// <summary>
    /// Party formation (P3-4). When the first adventurer takes a quest for 2+ people, the best-suited idle members in
    /// town are invited; each answers from its own point of view — by values, traits and feeling towards the leader
    /// (P4-91, user decision 2026-10-19: the rules decide, the model no longer does). The leader and joiners wait in
    /// <see cref="PartyGatherState"/> until every answer is in, then depart together.
    /// Party work (<see cref="QuestRisk.IsPartyWork"/>) is gathered for instead (P4-97, user decision 2026-10-21): the
    /// leader waits in town up to <see cref="RecruitHours"/>, asking whoever comes home — a standing party through its
    /// leader, as a whole — and sets out once the party is up to it (<see cref="QuestRisk.ReadyToSetOut"/>).
    /// </summary>
    public class PartyService : IDisposable, ISaveable, ISaveLink
    {
        /// <summary>Real seconds before stragglers are dropped and the party leaves anyway.</summary>
        public const float GatherTimeoutRealSeconds = 60f;
        /// <summary>The leader does not invite members they feel this badly about (적대).</summary>
        public const int HostileThreshold = -40;
        /// <summary>
        /// Test hook: every invitation is answered yes with this chance instead of by values and feelings (a no from the
        /// dice sends the member out hunting for hours, which a test of the gathering cannot wait for). Null in play.
        /// </summary>
        internal static float? AcceptChanceOverride;

        // ---------------- Gathering for party work (P4-97) ----------------

        /// <summary>
        /// Game hours the first taker of party work waits in town for the rest; then it goes with what it has if that is
        /// not "무모", or gives the quest up. A day: a free hunt is up to 8 hours out there (<see cref="FreeHunt.MaxHours"/>),
        /// but with the walks and a night camped out a band was gone 20 to 26 hours in natural runs — half a day missed
        /// half the guild, and the gathering gave up as the hunters walked back in.
        /// </summary>
        public const float RecruitHours = 24f;
        /// <summary>Game hours between looks round town for members come home (an idle member decides after 0.6 hours at the earliest).</summary>
        public const float CallEveryHours = 0.25f;
        /// <summary>Game hours before one who said no — for a party, its leader — is asked again.</summary>
        public const float AskAgainHours = 4f;
        /// <summary>Game hours a quest whose gathering came to nothing is left alone before anyone takes it up again.</summary>
        public const float RestAfterFailedHours = 12f;
        /// <summary>
        /// Added to the odds of a yes to a lord's or the dragon's quest (P4-97): the guild's biggest fight needs nearly all
        /// of it. At the plain odds (0.55) half said no in a natural run, went hunting, and were back a day later (a night
        /// camped out) — after the gathering had given up. Loners still mostly decline (0.2 → 0.5).
        /// </summary>
        public const float BossWorkEagerness = 0.3f;

        class Gathering
        {
            public Quest Quest;
            public Adventurer Leader;
            public readonly List<Adventurer> Invited = new();
            public readonly List<Adventurer> Joined = new();
            public readonly HashSet<Adventurer> Answered = new();
            public int Pending;
            public float StartRealTime;
            // Party work (P4-97): gathered for over game hours.
            public bool Recruiting;
            public float StartHour;
            public float NextCallHour;
            /// <summary>Game hour each one (for a standing party, its leader) last said no.</summary>
            public readonly Dictionary<Adventurer, float> Declined = new();
        }

        readonly QuestBoard _board;
        readonly GuildRoster _roster;
        readonly GameClock _clock;
        readonly Dictionary<Quest, Gathering> _gatherings = new();
        /// <summary>Game hour each quest's gathering came to nothing (P4-97).</summary>
        readonly Dictionary<Quest, float> _failedAt = new();
        /// <summary>Game hour a taker put a big hunt down for want of company (P4-100): not taken up by it again at once.</summary>
        readonly Dictionary<(Quest, Adventurer), float> _putDownAt = new();

        public PartyService(QuestBoard board, GuildRoster roster, GameClock clock = null)
        {
            _board = board;
            _roster = roster;
            _clock = clock;
            EventBus.Subscribe<QuestAcceptedEvent>(OnAccepted);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<QuestAcceptedEvent>(OnAccepted);
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            _hunts.Clear();
            _gatherings.Clear();
            _failedAt.Clear();
            _putDownAt.Clear();
        }

        float Hour => _clock != null ? _clock.TotalHours : Time.time / 10f;

        /// <summary>What the leader says asking someone along on a quest.</summary>
        public static string InviteLine(Quest q) => $"'{q.Title}' 같이 가지 않을래?";

        /// <summary>What the leader says asking someone along on a free hunt (P4-51).</summary>
        public static string HuntInviteLine() => "사냥 나가는데, 같이 갈래?";

        /// <summary>What the first taker of party work says when it starts gathering (P4-97).</summary>
        public static string RecruitLine(Quest q) => $"'{q.Title}'에 함께 갈 사람을 모은다! 돌아오면 말 걸어 줘.";

        /// <summary>
        /// Odds an invitee says yes (spec P3-4): values 동료 0.8, loner-type traits 0.2, else 0.55; plus feeling towards
        /// the leader / 200 (P3-5), kept within 0.05..0.95.
        /// </summary>
        public static float RuleAcceptChance(string values, float partyAffinity, int feelingToLeader = 0)
        {
            float c = values == "동료" ? 0.8f : partyAffinity < 1f ? 0.2f : 0.55f;
            return Math.Max(0.05f, Math.Min(0.95f, c + feelingToLeader / 200f));
        }

        /// <summary>True while invitations for this quest are still being answered — for party work, while it is gathered for (P4-97).</summary>
        public bool IsGathering(Quest q)
        {
            if (q == null || !_gatherings.TryGetValue(q, out var g)) return false;
            if (g.Recruiting) return StillRecruiting(g);
            if (g.Pending > 0 && Time.unscaledTime - g.StartRealTime < GatherTimeoutRealSeconds && q.IsActive) return true;
            Finish(g);
            return false;
        }

        /// <summary>Party work being gathered for in town (P4-97).</summary>
        public bool IsRecruiting(Quest q) => q != null && _gatherings.TryGetValue(q, out var g) && g.Recruiting;

        /// <summary>Game hours left of a gathering for party work (0 if none).</summary>
        public float RecruitHoursLeft(Quest q)
            => q != null && _gatherings.TryGetValue(q, out var g) && g.Recruiting ? Mathf.Max(0f, RecruitHours - (Hour - g.StartHour)) : 0f;

        /// <summary>The quest's gathering came to nothing lately: nobody takes it up again for a while (P4-97).</summary>
        public bool RestingAfterFailure(Quest q)
        {
            if (q == null || !_failedAt.TryGetValue(q, out var at)) return false;
            if (Hour - at < RestAfterFailedHours) return true;
            _failedAt.Remove(q);
            return false;
        }

        /// <summary>
        /// This member put the big hunt down lately, everyone it asked having said no (P4-100): it does not take it up again
        /// until <see cref="AskAgainHours"/> have passed — or it would take it, ask the same people, and put it down again.
        /// </summary>
        public bool PutDownLately(Adventurer a, Quest q)
        {
            if (a == null || q == null || !_putDownAt.TryGetValue((q, a), out var at)) return false;
            if (Hour - at < AskAgainHours) return true;
            _putDownAt.Remove((q, a));
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
                            && Relationships.Get(leader, a) > HostileThreshold
                            && HuntBandService.SameFooting(a, leader)) // newcomers hunt with newcomers (P4-99)
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
            if (QuestRisk.IsPartyWork(q))
            {
                GatherFor(q, leader);
                return;
            }
            // The leader's standing party comes along without being asked (P4-50): those in town
            // and free take the quest with it; invitations only fill the slots left.
            var comrades = PartyComrades(q, leader);
            var candidates = PickCandidates(q, leader);
            if (candidates.Count == 0 && comrades.Count == 0) return;

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
                // Far too green for it, it stays home rather than follow its party out (P4-99, as an invitee holds back, P4-88),
                // and so may a newcomer when the quest is out where the young are stronger than it (P4-100: a dare).
                if (QuestRisk.TooGreen(m.Data, q, QuestRisk.RiskLabel(m, q)) || QuestRisk.NewcomerHoldsBack(m, q)) continue;
                if (_board.TryAccept(q, m, invited: true)) list.Add(m);
            }
            return list;
        }

        /// <summary>Idle members in town that could reasonably take this quest, best fit first (<paramref name="skip"/>: not asked now).</summary>
        List<Adventurer> PickCandidates(Quest q, Adventurer leader, Func<Adventurer, bool> skip = null)
        {
            Services.TryGet<GuildParties>(out var parties);
            int slots = q.MaxParty - q.Assignees.Count;
            if (slots <= 0) return new List<Adventurer>();
            return _roster.Members
                .Where(a => a != leader && FreeInTown(a)
                            && !IsInvited(a)
                            && (skip == null || !skip(a))
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

        /// <summary>What a party's leader says turning party work down for a member of it far too green (P4-97).</summary>
        public const string PartyTooGreenLine = "우리 파티엔 아직 벅찬 일이야.";

        /// <summary>
        /// The rules' answer: by values, traits and feeling towards the leader — and, for a quest (P4-88), not when the
        /// member is far under its level and it is 위험 for them (they had followed the party at 55% regardless).
        /// </summary>
        static (bool accept, string say) RuleDecide(Adventurer c, Adventurer leader, Quest quest = null)
        {
            if (c == null) return (false, null);
            if (quest != null && (QuestRisk.NewcomerHoldsBack(c, quest) || QuestRisk.HoldsBack(c, quest))) return (false, TooGreenLine);
            float chance = AcceptChanceOverride ?? Math.Min(0.95f, RuleAcceptChance(c.Data.values, c.Traits?.PartyAffinity ?? 1f, Relationships.Get(c, leader))
                                                               + (QuestRisk.IsBossWork(quest) ? BossWorkEagerness : 0f));
            bool accept = GameRandom.Shared.Value < chance;
            return (accept, accept ? "좋아, 같이 가자!" : "이번엔 사양할게.");
        }

        void Answer(Gathering g, Adventurer c, bool accept, string say)
        {
            // Timed out / finished already — or a newer gathering for the same quest took its place
            // (the leader left and someone else took it): a stale answer must not join that one.
            if (!IsCurrent(g)) return;
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
            if (!accept && g.Recruiting) g.Declined[c] = Hour;
            // Before the state change: the last answer ends the gathering (PartyFormedEvent).
            EventBus.Publish(new PartyInviteAnsweredEvent { Quest = q, Leader = g.Leader, Invitee = c, Accepted = accept });
            if (accept) Join(g, c);
        }

        /// <summary>On the quest now: waits with the rest (TravelState sends it to PartyGatherState while gathering).</summary>
        static void Join(Gathering g, Adventurer c)
        {
            c.CurrentQuest = g.Quest;
            g.Joined.Add(c);
            c.ChangeState(c.TravelState); // redirected to PartyGatherState while gathering
        }

        bool IsCurrent(Gathering g) => _gatherings.TryGetValue(g.Quest, out var current) && current == g;

        void Finish(Gathering g)
        {
            if (!_gatherings.Remove(g.Quest)) return;
            var q = g.Quest;
            // Everyone on it but the leader: those who joined, and a whole standing party come along (P4-97).
            var goers = q.Assignees.OfType<Adventurer>().Where(a => a != g.Leader && a.IsAlive && a.CurrentQuest == q).ToList();
            // Party work (P4-85) that too few said yes to: the group does not set out to be killed. (A big hunt alone is its
            // taker's own call, P4-100: PutDownIfAlone.)
            if (QuestRisk.IsPartyWork(q) && QuestRisk.TooFewFor(q, AdventurerStats.Power(g.Leader.Data), q.Assignees.Count))
            {
                GiveUp(q, g.Leader, goers);
                // Gathered for and still too few (P4-97): left alone a while, or the next one to take it would wait again at once.
                if (g.Recruiting) _failedAt[q] = Hour;
                return;
            }
            if (goers.Count == 0) return;
            EventBus.Publish(new PartyFormedEvent { Quest = q, Leader = g.Leader, Members = goers });
        }

        /// <summary>Said by a leader whose party work found too few to go along.</summary>
        public const string TooFewLine = "이 인원으로는 무리야. 다음에 다시 모이자.";

        /// <summary>Said by the taker of a big hunt that nobody would come along on (P4-100).</summary>
        public const string AloneLine = "혼자서는 무리야. 같이 갈 사람이 있을 때 다시 맡자.";

        /// <summary>The quest goes back on the board; the leader and whoever had joined stay in town.</summary>
        void GiveUp(Quest q, Adventurer leader, List<Adventurer> joined, string line = TooFewLine)
        {
            foreach (var a in joined.Append(leader).ToList())
            {
                if (a == null) continue;
                _board.Abandon(q, a);
                if (a.CurrentQuest == q) a.CurrentQuest = null;
                if (a.IsAlive) a.ChangeState(a.IdleState);
            }
            if (leader != null && leader.IsAlive) leader.Say(line);
        }

        /// <summary>
        /// A big hunt a member took on its own judgement, counting on company it could ask (P4-100, <see cref="QuestRisk.ExpectedParty"/>):
        /// the answers are in the moment it takes it, and if nobody said yes and it is not up to it alone, it puts the hunt back
        /// on the board, and leaves it a while (<see cref="PutDownLately"/>). True when it did. Not for one persuaded by the
        /// guild master or sent by a debug command: the guild master's word stands.
        /// </summary>
        public bool PutDownIfAlone(Quest q, Adventurer taker)
        {
            if (q == null || taker == null || !q.IsAssigned(taker) || q.Assignees.Count > 1) return false;
            if (_gatherings.TryGetValue(q, out var g) && (g.Recruiting || g.Pending > 0)) return false;
            if (!QuestRisk.TooFewFor(q, AdventurerStats.Power(taker.Data), 1)) return false;
            _gatherings.Remove(q);
            _putDownAt[(q, taker)] = Hour;
            GiveUp(q, taker, new List<Adventurer>(), AloneLine);
            return true;
        }

        // ---------------- Gathering for party work (P4-97) ----------------

        /// <summary>
        /// The first taker of party work asks everyone free now; if that is not enough it stays in town and gathers the
        /// rest as they come home (user decision 2026-10-21: "리더가 잡고 모집"). If nobody the guild could ever send would
        /// make it more than "무모", it gives the quest up at once as before (P4-85).
        /// </summary>
        void GatherFor(Quest q, Adventurer leader)
        {
            // On it from the moment it took it: the decision sets this only after the accept event, and the first one
            // asked, entering its travel state, found the leader off the quest and took the gathering over (seen in play).
            leader.CurrentQuest = q;
            var g = new Gathering
            {
                Quest = q, Leader = leader, StartRealTime = Time.unscaledTime,
                Recruiting = true, StartHour = Hour, NextCallHour = Hour + CallEveryHours,
            };
            _gatherings[q] = g;
            Call(g);
            if (!IsCurrent(g)) return; // enough said yes: they are on their way
            float power = AdventurerStats.Power(leader.Data);
            if (QuestRisk.ReadyToSetOut(q, power, q.Assignees.Count)) { Finish(g); return; }
            if (QuestRisk.TooFewFor(q, power, QuestRisk.GatherableCount(q)))
            {
                _gatherings.Remove(q);
                GiveUp(q, leader, q.Assignees.OfType<Adventurer>().Where(a => a != leader).ToList());
                return;
            }
            leader.Say(RecruitLine(q));
            EventBus.Publish(new PartyRecruitingEvent { Quest = q, Leader = leader, Count = q.Assignees.Count });
        }

        /// <summary>
        /// One look while gathering: a new leader if the old one is off it, everyone come home asked, and the party sets
        /// out once it is up to it (<see cref="QuestRisk.ReadyToSetOut"/>) — or, time up, on what is allowed then.
        /// </summary>
        bool StillRecruiting(Gathering g)
        {
            var q = g.Quest;
            if (!q.IsActive) { _gatherings.Remove(q); return false; }
            if (!OnIt(g.Leader, q))
            {
                // The leader went off it (it broke under the stress, F-2): the next on it takes over.
                g.Leader = q.Assignees.OfType<Adventurer>().FirstOrDefault(a => OnIt(a, q));
                if (g.Leader == null) { _gatherings.Remove(q); return false; }
            }
            if (Hour >= g.NextCallHour)
            {
                g.NextCallHour = Hour + CallEveryHours;
                Call(g);
                if (!IsCurrent(g)) return false; // the last one asked made it, and it set out
            }
            if (QuestRisk.ReadyToSetOut(q, AdventurerStats.Power(g.Leader.Data), q.Assignees.Count) || Hour - g.StartHour >= RecruitHours)
            {
                Finish(g);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Every hour each gathering looks round and keeps time even with nobody waiting in the plaza to look for it — the
        /// leader resting off a wound, everyone out meeting a raid (seen in play: a leader gone to rest gathered nobody for
        /// its whole wait). Those waiting look every frame as well (<see cref="IsGathering(Quest)"/>).
        /// </summary>
        void OnHour(HourPassedEvent e)
        {
            foreach (var g in _gatherings.Values.Where(g => g.Recruiting).ToList())
                if (IsCurrent(g)) StillRecruiting(g);
        }

        static bool OnIt(Adventurer a, Quest q) => a != null && a.IsAlive && a.CurrentQuest == q && q.IsAssigned(a);

        bool AskedLately(Gathering g, Adventurer a) => g.Declined.TryGetValue(a, out var at) && Hour - at < AskAgainHours;

        /// <summary>
        /// Asks everyone free in town now: the parties of those on it come along unasked (P4-50), members of no party are
        /// invited one by one (<see cref="PickCandidates"/>), and another standing party is asked through its leader and
        /// comes as a whole or not at all — a member left behind would wait in town for its leader the whole quest (P4-77).
        /// Each answer may complete the party, which then sets out at once (the gathering is gone).
        /// </summary>
        void Call(Gathering g)
        {
            var q = g.Quest;
            foreach (var lead in q.Assignees.OfType<Adventurer>().ToList())
            {
                var comrades = PartyComrades(q, lead);
                foreach (var m in comrades) m.CurrentQuest = q;
                foreach (var m in comrades)
                {
                    m.Say("가자, 리더!");
                    Join(g, m);
                }
                if (!IsCurrent(g)) return;
            }

            var leader = g.Leader;
            var candidates = PickCandidates(q, leader, a => AskedLately(g, a));
            if (candidates.Count > 0) leader.Say(InviteLine(q));
            foreach (var c in candidates)
            {
                if (!IsCurrent(g) || !q.HasFreeSlot) return;
                if (c.Fsm.Current == c.DecidingState) c.ChangeState(c.IdleState);
                g.Invited.Add(c);
                g.Pending++;
                var (accept, say) = RuleDecide(c, leader, q);
                Answer(g, c, accept, say);
            }

            if (!IsCurrent(g) || !Services.TryGet<GuildParties>(out var parties)) return;
            var own = parties.PartyOf(leader);
            foreach (var p in parties.Parties.ToList())
            {
                if (!IsCurrent(g) || !q.HasFreeSlot) return;
                var head = p.Leader;
                if (p == own || head == null || q.IsAssigned(head) || !FreeInTown(head) || IsInvited(head) || AskedLately(g, head)) continue;
                // Still settling the last outing, or one of them still resting: it does not go out yet (P4-79).
                if (parties.OutingUnfinished(head) || parties.MembersGettingReady(head)) continue;
                if (Relationships.Get(leader, head) <= HostileThreshold) continue;
                // Its newcomers out of their depth may stay home (P4-100: a dare): they do not wait for their leader (P4-99).
                var block = p.Members.Where(m => FreeInTown(m) && !IsInvited(m) && !QuestRisk.NewcomerHoldsBack(m, q)).ToList();
                if (!block.Contains(head) || block.Count > q.MaxParty - q.Assignees.Count) continue;
                AskParty(g, head, block);
            }
        }

        /// <summary>A standing party's leader answers for its party (P4-97): all of it free in town comes, or none.</summary>
        void AskParty(Gathering g, Adventurer head, List<Adventurer> block)
        {
            var q = g.Quest;
            if (head.Fsm.Current == head.DecidingState) head.ChangeState(head.IdleState);
            // One far too green for it would stay home waiting for its leader: the party does not go without it.
            bool green = block.Any(m => QuestRisk.TooGreen(m.Data, q, QuestRisk.RiskLabel(m, q)));
            var (accept, say) = green ? (false, PartyTooGreenLine) : RuleDecide(head, g.Leader, q);
            if (accept)
            {
                var took = new List<Adventurer>();
                foreach (var m in block)
                {
                    if (_board.TryAccept(q, m, invited: true)) took.Add(m);
                    else { accept = false; break; }
                }
                // The slots went meanwhile: nobody of it goes.
                if (!accept) foreach (var m in took) _board.Abandon(q, m);
                else foreach (var m in took) m.CurrentQuest = q;
            }
            if (!string.IsNullOrEmpty(say)) head.Say(say);
            if (!accept) g.Declined[head] = Hour;
            EventBus.Publish(new PartyInviteAnsweredEvent { Quest = q, Leader = g.Leader, Invitee = head, Accepted = accept });
            if (!accept) return;
            // The quest is set on all of them first: whichever state change completes the party finds them all on it.
            foreach (var m in block) Join(g, m);
        }

        // ---------------- Save (P5-4) ----------------

        [Serializable]
        class SavedGathering
        {
            public string quest;
            public string leader;
            public float start_hour;
            public float next_call_hour;
            public List<string> declined = new();
            public List<float> declined_at = new();
        }

        [Serializable]
        class SaveData
        {
            public List<SavedGathering> gatherings = new();
            public List<string> failed = new();
            public List<float> failed_at = new();
        }

        SaveData _restoring;

        public string SaveKey => "party_gathering";

        /// <summary>
        /// Gatherings for party work (they last hours) and the quests resting after one failed. An ordinary gathering is
        /// over within the frame it began (the rules answer at once), so there is none to save.
        /// </summary>
        public string Capture()
        {
            var s = new SaveData();
            foreach (var g in _gatherings.Values.Where(g => g.Recruiting && g.Leader != null).OrderBy(g => g.Quest.Id, StringComparer.Ordinal))
            {
                var sg = new SavedGathering { quest = g.Quest.Id, leader = g.Leader.Data.id, start_hour = g.StartHour, next_call_hour = g.NextCallHour };
                foreach (var kv in g.Declined.Where(kv => kv.Key != null).OrderBy(kv => kv.Key.Data.id, StringComparer.Ordinal))
                {
                    sg.declined.Add(kv.Key.Data.id);
                    sg.declined_at.Add(kv.Value);
                }
                s.gatherings.Add(sg);
            }
            foreach (var kv in _failedAt.Where(kv => kv.Key.IsActive && Hour - kv.Value < RestAfterFailedHours).OrderBy(kv => kv.Key.Id, StringComparer.Ordinal))
            {
                s.failed.Add(kv.Key.Id);
                s.failed_at.Add(kv.Value);
            }
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx) => _restoring = JsonUtility.FromJson<SaveData>(json);

        /// <summary>Gatherings are their quest and members: put back once both are (before the members resume waiting).</summary>
        public void Link(SaveContext ctx)
        {
            _gatherings.Clear();
            _failedAt.Clear();
            foreach (var sg in _restoring.gatherings)
            {
                if (ctx.Find<Quest>(sg.quest) is not { } q || ctx.Find<Adventurer>(sg.leader) is not { } leader) continue;
                var g = new Gathering
                {
                    Quest = q, Leader = leader, StartRealTime = Time.unscaledTime,
                    Recruiting = true, StartHour = sg.start_hour, NextCallHour = sg.next_call_hour,
                };
                foreach (var a in q.Assignees.OfType<Adventurer>()) if (a != leader) g.Joined.Add(a);
                for (int i = 0; i < sg.declined.Count && i < sg.declined_at.Count; i++)
                    if (ctx.Find<Adventurer>(sg.declined[i]) is { } a) g.Declined[a] = sg.declined_at[i];
                _gatherings[q] = g;
            }
            for (int i = 0; i < _restoring.failed.Count && i < _restoring.failed_at.Count; i++)
                if (ctx.Find<Quest>(_restoring.failed[i]) is { } q) _failedAt[q] = _restoring.failed_at[i];
            _restoring = null;
        }
    }
}
