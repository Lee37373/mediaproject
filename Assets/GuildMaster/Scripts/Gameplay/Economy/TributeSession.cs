using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.LLM;
using GuildMaster.Welfare;

namespace GuildMaster.Economy
{
    /// <summary>
    /// Prompt and grammar for the tribute talk (상납금, 2026-10-29). The member is described as a person
    /// (<see cref="NpcVoice"/>) with their purse; the model writes the answer, says what kind of line the guild
    /// master's was, and picks the offer from the steps the code allows (the grammar only lets it pick those).
    /// </summary>
    public static class TributePrompt
    {
        public const int ReplyMaxChars = 100;
        public const int ReplyGrammarChars = 120;
        public const int HistoryTurns = 6;

        [Serializable]
        public class ResponseDto
        {
            public string reply;
            public string tactic;
            public string offer;
        }

        public static string BuildSystem(Adventurer m, int grudge)
        {
            var d = m.Data;
            var register = NpcVoice.RecruitRegister(d.mbti, d.traitIds);
            var sb = new StringBuilder();
            sb.Append($"너는 판타지 모험가 길드의 길드원 '{d.name}'이다. {AdventurerData.ClassLabel(d.cls)}이고 레벨은 {d.level}이다.\n");
            sb.Append(NpcVoice.Sheet(m)).Append('\n');
            sb.Append("몸과 마음: ").Append(NpcVoice.Condition(m)).Append('\n');
            var memories = AdventurerMemory.SelectForPrompt(d.memories, 3).Select(x => x.Text).ToList();
            if (memories.Count > 0) sb.Append("최근 겪은 일: ").Append(string.Join(" / ", memories)).Append('\n');
            sb.Append($"지금 가진 돈: {d.gold}골드. 의뢰를 뛰어 힘들게 번 돈이다.\n");
            sb.Append("길드 마스터가 너를 따로 불러, 길드에 네 돈을 내라고(상납하라고) 하고 있다. 둘만 있는 자리다.\n");
            if (grudge > 0) sb.Append("전에도 길드 마스터에게 돈 때문에 험한 소리를 들어 아직 기분이 좋지 않다.\n");
            sb.Append("\n말하는 법:\n");
            sb.Append("- 길드 마스터에게 말한다. ").Append(NpcVoice.RegisterRule(register, d.mbti)).Append('\n');
            sb.Append("- 네 성격과 가치관대로 반응한다. 아까운 돈이라 쉽게 내놓지 않는다. 사정이나 명분이 그럴듯하거나 보답이 솔깃하면 마음이 움직이고, 협박을 들으면 겁을 먹거나 반발한다.\n");
            sb.Append("- 얼마를 낼지는 매번 괄호 속 지시에서 고른다. 낸다면 대사에 그 금액(골드)을 정확히 말한다. 안 낸다면 거절하거나 얼버무린다.\n");
            sb.Append("- 대사만 쓴다. 지문(괄호 속 행동), 영어는 쓰지 않는다. 한두 문장.\n\n");
            sb.Append("출력 필드:\n");
            sb.Append("- reply: 네 대사\n");
            sb.Append("- tactic: 길드 마스터가 방금 한 말이 어떤 종류인지 하나 고른다. 지금 기분이 아니라 말의 내용으로 판단한다.\n");
            sb.Append("  · plead: 사정하거나 부탁하는 말\n");
            sb.Append("  · reason: 식량, 장비, 운영비처럼 돈이 필요한 이유(명분)를 대는 말\n");
            sb.Append("  · praise: 너를 칭찬하거나 추켜세우는 말\n");
            sb.Append("  · bargain: 대가나 보답(좋은 의뢰, 휴가, 나중에 갚기)을 약속하는 말\n");
            sb.Append("  · pressure: 협박하거나 윽박지르며 내놓으라고 강요하는 말\n");
            sb.Append("  · rude: 모욕하거나 무례한 말\n");
            sb.Append("  · small: 그 밖의 평범한 말\n");
            sb.Append("  · low: 성의 없거나 너무 짧은 말(ㅇㅇ, 응 같은 한두 글자 포함)\n");
            sb.Append("- offer: 이번 대답에서 내겠다고 한 금액의 단계(괄호 속 지시에 있는 것 중 하나)\n");
            sb.Append("반드시 JSON 한 개로만 답한다.");
            return sb.ToString();
        }

        public static string Mood(int will, int sour)
        {
            string m = TributeRules.Cap(will) switch
            {
                TributeOffer.None => "한 푼도 내고 싶지 않다.",
                TributeOffer.Little => "아깝지만 조금은 낼 수도 있다.",
                TributeOffer.Some => "어느 정도는 내도 되겠다는 쪽으로 기울었다.",
                _ => "달라는 만큼 내도 되겠다는 마음이다.",
            };
            if (sour > 0) m += " 길드 마스터의 말에 기분이 상했다.";
            return m;
        }

        public static string OfferWords(TributeOffer o, int gold) => o switch
        {
            TributeOffer.None => "none=안 낸다",
            TributeOffer.Little => $"little={gold}골드(조금)",
            TributeOffer.Some => $"some={gold}골드(꽤)",
            _ => $"much={gold}골드(많이)",
        };

        public static string BuildAside(int will, int sour, int pot, bool last)
        {
            var allowed = TributeRules.Allowed(will).Select(o => OfferWords(o, TributeRules.Amount(pot, o)));
            return "(네 속마음: " + Mood(will, sour) + " 이번에 고를 수 있는 offer: " + string.Join(", ", allowed)
                   + ". 이 중 하나만 고르고, 대사의 금액을 그것과 맞춘다." + (last ? " 이번이 마지막 대답이다." : "") + ")";
        }

        public static string Grammar(int will) => new GbnfObject()
            .String("reply", ReplyGrammarChars)
            .Enum("tactic", TributeRules.TacticCodes)
            .Enum("offer", TributeRules.Allowed(will).Select(TributeRules.Code))
            .Build();

        public static LlmRequest BuildRequest(Adventurer m, int grudge, int will, int sour, int pot, bool last, IReadOnlyList<CounselTurn> turns)
        {
            var req = new LlmRequest
            {
                System = BuildSystem(m, grudge),
                Grammar = Grammar(will),
                MaxTokens = 220,
                Temperature = 0.7f,
                Priority = LlmPriority.Dialogue,
                DedupeKey = "tribute:" + m.Data.id,
                TimeoutSeconds = 25f,
                Tag = "tribute:" + m.Data.id,
            };
            NpcVoice.SetChat(req, turns.Select(t => (t.FromPlayer, t.Text)), HistoryTurns * 2, BuildAside(will, sour, pot, last));
            return req;
        }
    }

    /// <summary>Published once when a tribute talk is over (it had at least one line from the guild master).</summary>
    public struct TributeEndedEvent
    {
        public Adventurer Member;
        public TributeOutcome Outcome;
        public int Amount;
        public bool Pressured;
    }

    /// <summary>
    /// One tribute talk (상납금, user decision 2026-10-29): the guild master talks a member out of some of their gold.
    /// Picked lines and typed lines both go to the model (LLM feature <see cref="LlmFeature.Tribute"/>), which answers
    /// in the member's words and picks the offer within what <see cref="TributeTalk"/> allows; without the model the
    /// written lines answer. The guild master takes the offer on the table whenever there is one.
    /// </summary>
    public class TributeSession
    {
        readonly List<CounselTurn> _turns = new();
        readonly List<(int afterTurn, string text, int delta)> _reactions = new();
        readonly List<TributeLines.Choice> _choices = new();
        readonly HashSet<string> _used = new();
        readonly TributeService _service;
        readonly GuildTreasury _treasury;
        readonly GuildWelfare _welfare;
        readonly GameClock _clock;
        readonly NpcVoice.Register _register;
        readonly int _grudge;
        bool _begun;

        public Adventurer Member { get; }
        public TributeTalk Talk { get; }
        public bool Busy { get; private set; }
        public bool LastReplyFromLlm { get; private set; }
        public string LastFallbackReason { get; private set; }
        public int Taken { get; private set; }
        public IReadOnlyList<CounselTurn> Turns => _turns;
        public IReadOnlyList<(int afterTurn, string text, int delta)> Reactions => _reactions;
        public IReadOnlyList<TributeLines.Choice> Choices => _choices;
        public TributeOutcome Outcome => Talk.Outcome;

        public TributeSession(Adventurer member, TributeService service, GuildTreasury treasury, GuildWelfare welfare, GameClock clock)
        {
            Member = member;
            _service = service;
            _treasury = treasury;
            _welfare = welfare;
            _clock = clock;
            float now = Now;
            _grudge = service != null ? service.Grudge(member, now) : 0;
            var d = member.Data;
            Talk = new TributeTalk(d.gold, d.values, d.traitIds, d.stress, _grudge);
            _register = NpcVoice.RecruitRegister(d.mbti, d.traitIds);
            string opening = TributeLines.Line("open", _register, 0, GameRandom.Shared);
            AddTurn(false, opening);
            member.Say(opening, toFeed: false);
            RefreshChoices();
        }

        float Now => _clock != null ? _clock.TotalHours : 0f;

        public Task SendAsync(string text) => RunTurnAsync(text);

        public Task SendChoiceAsync(TributeLines.Choice choice)
        {
            if (choice == null || !_choices.Contains(choice)) return Task.CompletedTask;
            _used.Add(choice.Text);
            return RunTurnAsync(choice.Text);
        }

        async Task RunTurnAsync(string text)
        {
            if (Busy || !Talk.CanTalk || Member == null || !Member.IsAlive) return;
            text = string.IsNullOrWhiteSpace(text) ? "..." : text.Trim();
            if (!_begun)
            {
                _begun = true;
                _service?.Begin(Member, Now);
            }
            Busy = true;
            AddTurn(true, text);
            _choices.Clear();
            (string reply, TributeTactic tactic, TributeOffer? offer) answer;
            try { answer = await GetReplyAsync(text); }
            finally { Busy = false; }
            if (Member == null || !Member.IsAlive) { Talk.End(); return; }

            var step = Talk.Hear(answer.tactic, answer.offer);
            if (step.Stress > 0f)
            {
                if (_welfare != null) _welfare.AdjustStress(Member, step.Stress);
                else Member.Data.stress = UnityEngine.Mathf.Clamp(Member.Data.stress + step.Stress, 0f, 100f);
            }
            // The written answer follows the code's offer; the model's own line is kept unless the talk is over.
            string line = answer.reply;
            if (Talk.Outcome == TributeOutcome.Walkout) line = TributeLines.Line("walkout", _register, 0, GameRandom.Shared);
            else if (line == null) line = FallbackLine(step);
            AddTurn(false, line);
            Member.Say(line, toFeed: false);
            _reactions.Add((_turns.Count, TributeRules.Feedback(step.Tactic, step.WillDelta, step.Stress), step.WillDelta));
            if (Talk.Outcome == TributeOutcome.Walkout) Finish();
            RefreshChoices();
        }

        string FallbackLine(TributeStep step)
        {
            if ((step.Tactic == TributeTactic.Pressure && step.WillDelta <= 0) || step.Tactic == TributeTactic.Rude)
                return TributeLines.Line("angry", _register, 0, GameRandom.Shared);
            return TributeLines.Line(TributeLines.OfferKey(step.Offer), _register, Talk.OfferGold, GameRandom.Shared);
        }

        async Task<(string, TributeTactic, TributeOffer?)> GetReplyAsync(string text)
        {
            LastReplyFromLlm = false;
            LastFallbackReason = null;
            var rules = TributeRules.Classify(text);
            var gw = LlmUse.Ready(LlmFeature.Tribute);
            if (gw == null)
            {
                LastFallbackReason = !LlmUse.Allows(LlmFeature.Tribute) ? "LLM 꺼짐" : "모델 준비 중";
                return (null, rules, null);
            }
            int will = Talk.WillAfter(rules);
            bool last = Talk.PlayerTurns + 1 >= TributeRules.MaxTurns;
            var result = await gw.RequestAsync(TributePrompt.BuildRequest(Member, _grudge, will, Talk.Sour + TributeRules.SourPoints(rules), Talk.Pot, last, _turns));
            TributePrompt.ResponseDto dto = null;
            bool parsed = result.Ok && LlmJson.TryParse<TributePrompt.ResponseDto>(result.Text, out dto) && !string.IsNullOrWhiteSpace(dto.reply);
            string reply = parsed ? LlmJson.CleanSentence(dto.reply, TributePrompt.ReplyMaxChars, TributePrompt.ReplyGrammarChars, false) : null;
            if (parsed && !string.IsNullOrWhiteSpace(reply))
            {
                var tactic = TributeRules.Reconcile(TributeRules.ParseTactic(dto.tactic), rules);
                var offer = TributeRules.ParseOffer(dto.offer);
                // A different reading of the line moves the cap: a model reply that names an amount no longer allowed
                // would contradict the window, so the written line answers instead.
                if (tactic != rules && offer.HasValue && TributeRules.Clamp(offer.Value, Talk.WillAfter(tactic)) != offer.Value) reply = null;
                if (result.Exchange != null) result.Exchange.Note = $"상납: {dto.tactic}→{TributeRules.Code(tactic)}, {dto.offer}";
                LastReplyFromLlm = reply != null;
                return (reply, tactic, offer);
            }
            LastFallbackReason = !result.Ok ? "응답 실패" : "응답 형식 오류";
            if (result.Exchange != null) result.Exchange.Note = "규칙 기반으로 대체: " + (!result.Ok ? result.Error : "parse");
            return (null, rules, null);
        }

        void RefreshChoices()
        {
            _choices.Clear();
            if (Talk.CanTalk) _choices.AddRange(TributeLines.Offer(_used, GameRandom.Shared));
        }

        /// <summary>Takes the offer on the table: the gold moves from the member to the guild.</summary>
        public int Accept()
        {
            if (Busy || Member == null || Member.Data == null) return 0;
            var d = Member.Data;
            int goldBefore = d.gold;
            int amount = UnityEngine.Mathf.Min(Talk.Accept(), d.gold);
            if (amount <= 0) return 0;
            d.gold -= amount;
            _treasury?.AddGold(amount, GoldReason.Tribute);
            Taken = amount;
            float stress = TributeRules.PaidStress(amount, goldBefore, d.values, Talk.Pressured);
            if (_welfare != null) _welfare.AdjustStress(Member, stress);
            else d.stress = UnityEngine.Mathf.Clamp(d.stress + stress, 0f, 100f);
            string line = TributeLines.Line("paid", _register, amount, GameRandom.Shared);
            AddTurn(false, line);
            Member.Say(line, toFeed: false);
            if (stress > 0f) _reactions.Add((_turns.Count, $"[{amount}골드를 받았다 · 스트레스 +{stress:0}]", 0));
            Finish();
            return amount;
        }

        /// <summary>Leaves without taking anything (the window's button or closing it).</summary>
        public void Abort()
        {
            if (Talk.Outcome != TributeOutcome.Ongoing) return;
            if (_begun && Member != null && Member.IsAlive && Talk.PlayerTurns > 0)
            {
                string line = TributeLines.Line("pass", _register, 0, GameRandom.Shared);
                AddTurn(false, line);
            }
            Talk.End();
            if (_begun) Finish();
        }

        void AddTurn(bool fromPlayer, string text) => _turns.Add(new CounselTurn { FromPlayer = fromPlayer, Text = text });

        void Finish()
        {
            _choices.Clear();
            if (Member == null || Member.Data == null) return;
            _service?.Finish(Member, Taken, Talk.Sour, Now);
            string text = Talk.Outcome switch
            {
                TributeOutcome.Paid => Talk.Pressured ? $"길드 마스터에게 {Taken}골드를 뜯겼다" : $"길드 마스터의 부탁으로 길드에 {Taken}골드를 보탰다",
                TributeOutcome.Walkout => "길드 마스터가 돈을 내놓으라고 윽박질러 자리를 박차고 나왔다",
                _ => Talk.Sour > 0 ? "길드 마스터가 돈 이야기로 험한 말을 했다" : null,
            };
            if (text != null)
                AdventurerMemory.Add(Member.Data.memories, Now, Talk.Outcome == TributeOutcome.Ended ? 1 : 2, text, "tribute");
            if (Taken > 0 && Services.TryGet<Session.GameLog>(out var log))
                log.Add($"{Member.Data.name}{MemoryRecorder.Josa(Member.Data.name, "이", "가")} 길드에 상납금 {Taken}G를 냈습니다{(Talk.Pressured ? " (협박)" : "")}.");
            EventBus.Publish(new TributeEndedEvent { Member = Member, Outcome = Talk.Outcome, Amount = Taken, Pressured = Talk.Pressured });
        }
    }
}
