using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.LLM;
using UnityEngine;

namespace GuildMaster.Welfare
{
    public enum CounselOutcome { Ongoing, Relieved, Walkout, Ended }

    /// <summary>Published once when a counselling talk is over (it had at least one line from the guild master).</summary>
    public struct CounselEndedEvent
    {
        public Adventurer Member;
        public CounselOutcome Outcome;
        /// <summary>Net stress taken away (negative: the talk made it worse).</summary>
        public int Relief;
        public CounselConcernKind Concern;
    }

    public class CounselTurn
    {
        public bool FromPlayer;
        public string Text;
    }

    /// <summary>
    /// One counselling talk (면담) between the guild master and a member (user request 2026-10-01: "스트레스 관리나
    /// 상담 요청 등 할 때 단순 버튼 클릭이 아닌 실제 대화로 위로하면서"). Replaces the one-click interview: the member
    /// has a worry of their own (<see cref="CounselConcern"/>), each line of the guild master's is read for what kind
    /// of line it is (model, checked by rules) and scored in <see cref="CounselScoring"/>; stress moves line by line.
    /// The interview cooldown and an asked-for talk are counted on the first line (<see cref="GuildWelfare.BeginCounsel"/>),
    /// so opening and closing the window costs nothing.
    /// </summary>
    public class CounselSession
    {
        readonly List<CounselTurn> _turns = new();
        readonly List<(int afterTurn, string text, int delta)> _reactions = new();
        readonly List<(int afterTurn, string text)> _notes = new();
        readonly GuildWelfare _welfare;
        readonly GameClock _clock;
        CounselReaction? _last;
        int _streak;
        bool _begun;
        bool _demandMet;
        /// <summary>At the start: the member had asked for a talk; their unanswered demands (grudges).</summary>
        readonly bool _asked;
        readonly int _grudges;

        public Adventurer Member { get; }
        public CounselConcern Concern { get; }
        /// <summary>The member talks about the worry now (a kind word or a question got them talking).</summary>
        public bool Open { get; private set; }
        /// <summary>The guild master has got to the worry (the window shows it from then on).</summary>
        public bool ConcernKnown { get; private set; }
        public int Relief { get; private set; }
        public int Harm { get; private set; }
        public int Hurtful { get; private set; }
        /// <summary>Caring lines that fell flat (<see cref="CounselOdds"/>); at the limit the member walks out.</summary>
        public int Fails { get; private set; }
        public int PlayerTurns { get; private set; }
        public float StartStress { get; }
        public bool Busy { get; private set; }
        public bool LastReplyFromLlm { get; private set; }
        public string LastFallbackReason { get; private set; }
        public CounselOutcome Outcome { get; private set; } = CounselOutcome.Ongoing;
        public IReadOnlyList<CounselTurn> Turns => _turns;
        public IReadOnlyList<(int afterTurn, string text, int delta)> Reactions => _reactions;
        public IReadOnlyList<(int afterTurn, string text)> Notes => _notes;
        public event Action Changed;

        readonly List<CounselChoice> _choices = new();
        readonly HashSet<string> _usedChoices = new();
        readonly NpcVoice.Register _register;
        /// <summary>
        /// The lines the guild master can pick next (user decision 2026-10-28: choices plus free typing; see
        /// <see cref="CounselBank"/>). Empty when the talk is over or no lines are shipped.
        /// </summary>
        public IReadOnlyList<CounselChoice> Choices => _choices;
        /// <summary>True when the last answer came from the written lines for a chosen line.</summary>
        public bool LastReplyFromChoice { get; private set; }

        public CounselSession(Adventurer member, GuildWelfare welfare, GameClock clock)
        {
            Member = member;
            _welfare = welfare;
            _clock = clock;
            StartStress = member.Data.stress;
            var care = welfare?.Care(member);
            Concern = CounselConcerns.Detect(ReadState(member, care));
            _asked = care != null && care.Demand == CareDemand.Talk;
            _grudges = care?.Grudges ?? 0;
            // Talkers and members who are not holding a grudge say what is wrong straight away.
            bool extrovert = !string.IsNullOrEmpty(member.Data.mbti) && char.ToUpperInvariant(member.Data.mbti[0]) == 'E';
            Open = extrovert && (care == null || care.Grudges == 0);
            _register = NpcVoice.RecruitRegister(member.Data.mbti, member.Data.traitIds);
            // In the member's own speech level when the written lines have it (2026-10-28).
            string opening = CounselBank.Shared.Opening(Concern, Open, _register, GameRandom.Shared) ?? CounselLines.Opening(Concern, Open, GameRandom.Shared);
            AddTurn(false, opening);
            member.Say(opening, toFeed: false);
            RefreshChoices();
        }

        /// <summary>What the concern check needs, read from the member and the guild.</summary>
        public static CounselInput ReadState(Adventurer a, MemberCare care)
        {
            var d = a.Data;
            float now = Services.TryGet<GameClock>(out var clock) ? clock.TotalHours : 0f;
            var recent = d.memories.Where(m => now - m.hour <= 72f).Select(m => m.Text).ToList();
            string rival = null;
            int worst = 0;
            if (Services.TryGet<GuildRoster>(out var roster))
                foreach (var o in roster.Members)
                {
                    if (o == null || o == a || !o.IsAlive) continue;
                    int f = Relationships.Get(a, o);
                    if (f < worst) { worst = f; rival = o.Data.name; }
                }
            return new CounselInput
            {
                Stress = d.stress,
                Fatigue = d.fatigue,
                HpRatio = a.HpRatio,
                Wounded = Legacy.GuildLegacy.WoundedRefusal(a) != null,
                Gold = d.gold,
                Debt = d.debt,
                Grudges = care?.Grudges ?? 0,
                Values = d.values,
                RecentMemories = recent,
                RivalName = rival,
                RivalFeeling = worst,
            };
        }

        public Task SendAsync(string playerText) => RunTurnAsync(playerText, null);

        /// <summary>
        /// A line picked from <see cref="Choices"/>: answered from the written lines, no model (user decision
        /// 2026-10-28). Scored and judged exactly like a typed line.
        /// </summary>
        public Task SendChoiceAsync(CounselChoice choice)
        {
            if (choice == null || !_choices.Contains(choice)) return Task.CompletedTask;
            return RunTurnAsync(choice.Text, choice);
        }

        async Task RunTurnAsync(string playerText, CounselChoice choice)
        {
            if (Busy || Outcome != CounselOutcome.Ongoing || Member == null || !Member.IsAlive) return;
            playerText = string.IsNullOrWhiteSpace(playerText) ? "..." : playerText.Trim();
            if (!_begun)
            {
                _begun = true;
                _demandMet = _welfare != null && _welfare.BeginCounsel(Member);
            }
            Busy = true;
            AddTurn(true, playerText);
            PlayerTurns++;
            Changed?.Invoke();

            (string text, CounselReaction reaction, bool touched) reply;
            if (choice != null) _usedChoices.Add(choice.Id);
            try { reply = choice != null ? ChoiceReply(choice) : await GetReplyAsync(playerText); }
            finally { Busy = false; }
            if (Member == null || !Member.IsAlive) { Finish(CounselOutcome.Ended); Changed?.Invoke(); return; }

            // A caring line has to land (2026-10-31): rolled like the recruit talk; a failure adds stress instead.
            bool offTopic = choice != null && IsOffTopic(choice);
            bool repeat = _last == reply.reaction;
            var roll = CounselRoll.Success;
            if (CounselOdds.Rolls(reply.reaction))
            {
                int chance = CounselOdds.Chance(reply.reaction, reply.touched, offTopic, Member.Data.mbti, _grudges, Member.Data.stress,
                                                _asked || _demandMet, Open, repeat);
                roll = CounselOdds.Roll(chance, GameRandom.Shared.Value, GameRandom.Shared.Value);
            }

            int streak = repeat ? _streak + 1 : 0;
            _last = reply.reaction;
            _streak = streak;
            int delta;
            string feedback;
            if (roll == CounselRoll.Success)
            {
                delta = CounselScoring.Cap(CounselScoring.Score(reply.reaction, streak, Member.Data.mbti, reply.touched, _demandMet), Relief, Harm);
                feedback = CounselScoring.Feedback(reply.reaction, delta, reply.touched);
                if (CounselScoring.IsHurtful(reply.reaction)) Hurtful++;
            }
            else
            {
                delta = CounselScoring.Cap(-CounselOdds.Harm(roll), Relief, Harm);
                feedback = CounselOdds.Feedback(roll);
                Fails++;
                if (roll == CounselRoll.CritFail) Hurtful++;
                reply = (FailLine(choice, reply.reaction, offTopic, roll), reply.reaction, false);
            }
            if (delta > 0) Relief += delta; else Harm -= delta;
            _welfare?.ApplyCounsel(Member, delta);

            AddTurn(false, reply.text);
            Member.Say(reply.text, toFeed: false);
            _reactions.Add((_turns.Count, feedback, delta));
            bool landed = roll == CounselRoll.Success;
            if (landed && !Open && CounselScoring.OpensUp(reply.reaction)) Open = true;
            if (landed && !ConcernKnown && (reply.touched || Open && CounselScoring.OpensUp(reply.reaction)))
            {
                ConcernKnown = true;
                _notes.Add((_turns.Count, "고민: " + Concern.Label));
            }

            var outcome = Verdict();
            if (outcome != CounselOutcome.Ongoing)
            {
                string closing = CounselBank.Shared.Closing(outcome, _register, GameRandom.Shared) ?? CounselLines.Closing(outcome, GameRandom.Shared);
                AddTurn(false, closing);
                Member.Say(closing, toFeed: false);
                Finish(outcome);
            }
            RefreshChoices();
            Changed?.Invoke();
        }

        /// <summary>A guess at, or help for, a worry the member does not have.</summary>
        bool IsOffTopic(CounselChoice c)
            => (c.Kind == CounselChoiceKind.Touch || c.Kind == CounselChoiceKind.Solve) && c.Topic != null && Concern != null && c.Topic != Concern.Kind;

        /// <summary>
        /// The chance (%) that a choice lands, for the window; 0 for a harsh line (never rolled, always hurts).
        /// See <see cref="ChanceHidden"/>: before the worry is known a guess's odds would give the answer away.
        /// </summary>
        public int ChanceOf(CounselChoice c)
        {
            if (c == null || Member == null || Concern == null) return 0;
            var (reaction, touched) = CounselBank.ReactionFor(c, Concern);
            return CounselOdds.Chance(reaction, touched, IsOffTopic(c), Member.Data.mbti, _grudges, Member.Data.stress,
                                      _asked || _demandMet, Open, _last == reaction);
        }

        public bool ChanceHidden(CounselChoice c)
            => c != null && !ConcernKnown && (c.Kind == CounselChoiceKind.Touch || c.Kind == CounselChoiceKind.Solve);

        /// <summary>The member's answer to a line that fell flat (written lines; a short fallback without them).</summary>
        string FailLine(CounselChoice choice, CounselReaction reaction, bool offTopic, CounselRoll roll)
        {
            var bank = CounselBank.Shared;
            string key;
            if (roll == CounselRoll.CritFail) key = "fail.crit";
            else if (offTopic) key = choice.Kind == CounselChoiceKind.Solve ? "solution.miss" : "wrong.guess";
            else if (choice != null) key = "fail." + choice.Kind;
            else key = "fail." + reaction switch
            {
                CounselReaction.Solution => nameof(CounselChoiceKind.Solve),
                CounselReaction.Listen => nameof(CounselChoiceKind.Listen),
                CounselReaction.Praise => nameof(CounselChoiceKind.Praise),
                CounselReaction.Small => nameof(CounselChoiceKind.Small),
                _ => nameof(CounselChoiceKind.Empathy),
            };
            string text = bank.Line(key, _register, GameRandom.Shared) ?? bank.Line("fail.Empathy", _register, GameRandom.Shared);
            return string.IsNullOrEmpty(text) ? "…그래요." : text.Replace("{subject}", string.IsNullOrEmpty(Concern?.Subject) ? "그 친구" : Concern.Subject);
        }

        (string, CounselReaction, bool) ChoiceReply(CounselChoice choice)
        {
            LastReplyFromLlm = false;
            LastFallbackReason = null;
            LastReplyFromChoice = true;
            return CounselBank.Shared.Respond(choice, Concern, ConcernKnown, _register, GameRandom.Shared);
        }

        /// <summary>New lines to pick from after each turn, from what the guild master knows so far.</summary>
        void RefreshChoices()
        {
            _choices.Clear();
            if (Outcome != CounselOutcome.Ongoing || Member == null || Concern == null) return;
            _choices.AddRange(CounselBank.Shared.Offer(Concern, ConcernKnown, _usedChoices, GameRandom.Shared));
        }

        /// <summary>Whether the talk is over after this line (pure rules on the session's totals).</summary>
        CounselOutcome Verdict() => Judge(Relief, Harm, Hurtful, PlayerTurns, Member.Data.stress, Fails);

        public static CounselOutcome Judge(int relief, int harm, int hurtful, int playerTurns, float stressNow, int fails = 0)
        {
            if (hurtful >= CounselScoring.WalkoutNegatives || harm >= CounselScoring.MaxHarm || fails >= CounselOdds.FailLimit)
                return CounselOutcome.Walkout;
            if (relief >= CounselScoring.ReliefGoal || (relief > 0 && stressNow < CounselScoring.CalmStress)) return CounselOutcome.Relieved;
            if (playerTurns >= CounselScoring.MaxTurns) return CounselOutcome.Ended;
            return CounselOutcome.Ongoing;
        }

        async Task<(string, CounselReaction, bool)> GetReplyAsync(string playerText)
        {
            LastReplyFromLlm = false;
            LastFallbackReason = null;
            LastReplyFromChoice = false;
            var rules = CounselScoring.Classify(playerText);
            bool rulesTouched = Concern.Touches(playerText);
            if (!Services.TryGet<LlmGateway>(out var gw)) LastFallbackReason = "LLM 없음";
            else if (!gw.Enabled) LastFallbackReason = "LLM 꺼짐";
            else if (!gw.IsReady) LastFallbackReason = "모델 준비 중";
            else
            {
                var result = await gw.RequestAsync(CounselPrompt.BuildRequest(Member, Concern, Member.Data.stress, Open, _turns));
                CounselPrompt.ResponseDto dto = null;
                bool parsed = result.Ok && LlmJson.TryParse<CounselPrompt.ResponseDto>(result.Text, out dto) && !string.IsNullOrWhiteSpace(dto.reply);
                string text = parsed ? LlmJson.CleanSentence(dto.reply, CounselPrompt.ReplyMaxChars, CounselPrompt.ReplyGrammarChars, false) : null;
                string previous = LastMemberLine();
                bool repeated = parsed && previous != null && (string.IsNullOrWhiteSpace(text) || text == previous || previous.Contains(text));
                if (parsed && !repeated)
                {
                    LastReplyFromLlm = true;
                    var reaction = CounselScoring.Reconcile(CounselScoring.Parse(dto.reaction), rules);
                    // The model's "yes" only counts for a line that says something about the worry: it said yes to a
                    // general "요즘 좀 어때?" in the live test (2026-10-01).
                    bool touched = rulesTouched || (dto.touched == "yes" && CounselScoring.IsPositive(reaction)
                                                    && reaction != CounselReaction.Small && reaction != CounselReaction.Listen);
                    if (result.Exchange != null) result.Exchange.Note = $"면담: {dto.reaction}→{CounselScoring.Code(reaction)}, 고민 {(touched ? "짚음" : "-")}";
                    return (text, reaction, touched && CounselScoring.IsPositive(reaction));
                }
                LastFallbackReason = !result.Ok ? "응답 실패" : repeated ? "같은 대답 반복" : "응답 형식 오류";
                if (result.Exchange != null) result.Exchange.Note = "규칙 기반으로 대체: " + (!result.Ok ? result.Error : repeated ? "repeat" : "parse");
            }
            bool touchedRule = rulesTouched && CounselScoring.IsPositive(rules);
            bool openAfter = Open || CounselScoring.OpensUp(rules);
            return (CounselLines.Reply(rules, Concern, openAfter, touchedRule, GameRandom.Shared), rules, touchedRule);
        }

        string LastMemberLine()
        {
            for (int i = _turns.Count - 1; i >= 0; i--)
                if (!_turns[i].FromPlayer) return _turns[i].Text;
            return null;
        }

        /// <summary>The window was closed: a talk that had begun ends here.</summary>
        public void Abort()
        {
            if (Outcome == CounselOutcome.Ongoing && _begun) Finish(CounselOutcome.Ended);
        }

        void AddTurn(bool fromPlayer, string text) => _turns.Add(new CounselTurn { FromPlayer = fromPlayer, Text = text });

        void Finish(CounselOutcome outcome)
        {
            if (Outcome != CounselOutcome.Ongoing) return;
            Outcome = outcome;
            if (Member == null || Member.Data == null) return;
            float hour = _clock != null ? _clock.TotalHours : 0f;
            string text = outcome switch
            {
                CounselOutcome.Relieved => $"길드 마스터와 면담하며 {Concern.Label}{MemoryRecorder.Josa(Concern.Label, "을", "를")} 털어놓고 마음이 한결 가벼워졌다",
                CounselOutcome.Walkout => "길드 마스터와 면담하다 오히려 마음이 상했다",
                _ => Relief > Harm ? "길드 마스터와 면담을 하며 이야기를 나눴다" : "길드 마스터와 면담을 했지만 별 도움이 되지 않았다",
            };
            AdventurerMemory.Add(Member.Data.memories, hour, outcome == CounselOutcome.Ended ? 1 : 2, text, "care-talk");
            EventBus.Publish(new CounselEndedEvent { Member = Member, Outcome = outcome, Relief = Relief - Harm, Concern = Concern.Kind });
        }
    }
}
