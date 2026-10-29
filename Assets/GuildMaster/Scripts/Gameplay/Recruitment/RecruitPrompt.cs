using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.LLM;
using GuildMaster.Session;
using GuildMaster.Town;

namespace GuildMaster.Recruitment
{
    /// <summary>
    /// Prompt, response DTO and grammar for the recruitment dialogue (P3-3). The visitor is described as
    /// a person (see <see cref="NpcVoice"/>), earlier turns go in as real chat turns, and in chat the model writes its
    /// line first and then gives a decision that agrees with it; a formal offer is answered decision first.
    /// </summary>
    public static class RecruitPrompt
    {
        public const int ReplyMaxChars = 110;
        public const int MaxDelta = 15;
        /// <summary>Exchanges (player line + visitor line) kept in the prompt.</summary>
        public const int HistoryTurns = 6;

        static readonly string[] Decisions = { "undecided", "join", "refuse" };

        [Serializable]
        public class ResponseDto
        {
            public string reply;
            public string decision;
            /// <summary>What kind of line the guild master's was (a <see cref="RecruitScoring.Codes"/> code); scored in code.</summary>
            public string reaction;
        }

        /// <summary>
        /// Stable for a whole talk (the visitor's current feelings travel with the newest message, see
        /// <see cref="BuildAside"/>), so the model can reuse what it already read.
        /// </summary>
        public static string BuildSystem(Adventurer v)
        {
            var d = v.Data;
            var register = NpcVoice.RecruitRegister(d.mbti, d.traitIds);
            var sb = new StringBuilder();
            sb.Append($"너는 판타지 세계의 모험가 '{d.name}'이다. {AdventurerData.ClassLabel(d.cls)}이고 레벨은 {d.level}이다.\n");
            sb.Append(NpcVoice.Sheet(v)).Append('\n');
            sb.Append("지금 너는 마을 광장에 잠시 들렀는데, 모험가 길드의 길드 마스터가 다가와 길드에 들어오라며 말을 걸고 있다.\n");
            sb.Append(GuildInfo()).Append("\n\n");
            // Rewritten (user report 2026-09-29: "대화가 성격이나 가치관에 상관없이 너무 유사"). Every visitor
            // used to get the same speech level ("존댓말(해요체), 한두 문장"), the same shared example line and
            // an instruction to keep their values out of ordinary talk — so the model answered everyone in
            // one polite, neutral voice. Speech level, length, what they push for and how they react to
            // topics outside their interest now all come from the character.
            sb.Append("말하는 법:\n");
            sb.Append("- ").Append(NpcVoice.RegisterRule(register, d.mbti)).Append(" 대사는 실제 사람이 하는 말처럼 자연스럽게.\n");
            sb.Append("- 먼저 상대가 방금 한 말에 반응하되, 반응하는 방식은 위 성격을 그대로 따른다. 네 관심사에 닿는 말엔 적극적으로, 관심 없는 이야기엔 시큰둥하게 반응하고 네가 궁금한 쪽으로 화제를 끌고 온다.\n");
            sb.Append("- 네가 먼저 꺼내거나 캐묻는 이야기: ").Append(NpcVoice.ValueAgenda(d.values)).Append(". 매번은 아니어도 대화가 이쪽으로 자주 흐르게 한다.\n");
            // Added after user report 2026-09-23 #1: real play showed the same vague filler
            // ("아직 특별히 정해둔 건 없어요") answering completely different questions (직업, 성격 x2).
            sb.Append("- 직업, 어디서 왔는지, 하는 일 같은 가벼운 질문에는 구체적으로 답한다. \"아직 특별히 정해둔 건 없어요\" 같은 회피성 대답으로 얼버무리지 않는다.\n");
            // Matches VisitorReveal's rules (user report 2026-10-07): the visitor names their values, personality or a
            // trait only when the aside says so, so what the window shows and what is said always agree.
            sb.Append("- 무엇을 가장 중요하게 여기는지, 어떤 성격인지, 네 특징이 무엇인지를 말로 직접 밝히는 것은 괄호 속에서 '털어놓는다'고 할 때만 한다. 그 전에는 말투와 반응으로만 은근히 드러낸다.\n");
            sb.Append("- 직전에 자신이 한 대답을 그대로, 또는 거의 그대로 반복하지 않는다. 질문이 다르면 다른 표현으로 답한다.\n");
            sb.Append("- 네 '특징'은 대답마다 말투나 내용에 조금씩 묻어나게 한다. 특징의 이름을 대지 않아도, 듣는 사람이 알아챌 수 있게.\n");
            sb.Append("- 길드 마스터가 같은 권유를 조르듯 되풀이하면 부담스러워하고 그 티를 낸다.\n");
            sb.Append("- 숫자나 게임 용어(호감도 등)는 말하지 않는다.\n");
            sb.Append("- 길드 마스터가 정식으로 영입을 제안하면, 네 속마음에 따라 들어가겠다(join) 또는 거절(refuse)로 분명히 답한다.\n\n");
            sb.Append("대화 예시(너와 성향이 비슷한 다른 인물):\n");
            sb.Append("길드 마스터: 여기서 뭐 하세요?\n");
            sb.Append("모험가: ").Append(NpcVoice.ExampleReply(d.values, register)).Append("\n\n");
            sb.Append("JSON 항목:\n");
            sb.Append("- reply: 네가 하는 말. 대사만 쓴다(따옴표, 지문, 이름 없이).\n");
            // No formal proposal any more (user request 2026-09-30): joining happens in the flow of the talk.
            sb.Append("- decision: undecided(아직 정하지 않음) / join / refuse. reply에서 한 말과 같은 결론이어야 한다. ");
            sb.Append("join은 길드 마스터가 대화 중에 들어오라고 권했고 네 마음이 그쪽으로 기울었을 때, 또는 이야기하다 네가 먼저 들어가고 싶어졌을 때 쓴다(속마음이 완전히 확신하지 않아도, 대화가 마음에 들었다면 괜찮다). ");
            sb.Append("아직 망설여지면 undecided로 두고 무엇이 걸리는지 말한다. refuse는 대화를 끝내는 거절이라, 마음이 완전히 떠났거나 상대가 무례해서 더는 말하고 싶지 않을 때만 쓴다. 평범한 대화 중에는 undecided다.\n");
            // What a visitor gives away is decided by fixed rules now (VisitorReveal, user report 2026-10-07:
            // "정보가 드러나는 포인트가 애매모호함"); the model is told in the aside when to say it.
            // The model no longer picks a number (user request 2026-09-30): it says what KIND of line it
            // heard, and RecruitScoring turns that into points. Judged on content, not on the visitor's mood,
            // so a wary visitor can still be won over by the right words.
            var wants = new List<string>();
            var dislikes = new List<string>();
            if (d.traitIds != null)
                foreach (var id in d.traitIds)
                {
                    string w = RecruitScoring.TraitWants(id), x = RecruitScoring.TraitDislikes(id);
                    if (w.Length > 0) wants.Add(w);
                    if (x.Length > 0) dislikes.Add(x);
                }
            sb.Append("- reaction: 방금 길드 마스터가 한 말이 어떤 종류인지 하나 고른다. 지금 기분이 아니라 말의 내용으로 판단한다.\n");
            sb.Append("  · value: 네가 중요하게 여기는 것(").Append(NpcVoice.ValueAgenda(d.values)).Append(")을 채워 주는 말\n");
            sb.Append("  · trait: ").Append(wants.Count > 0 ? string.Join(", ", wants) + "처럼 네 특징에 맞는 말" : "네 특징을 알아봐 주는 말").Append("\n");
            sb.Append("  · answer: 네가 물어본 것에 성의 있고 구체적으로 답한 말\n");
            sb.Append("  · friendly: 그 밖의 성의 있는 보통 대화(인사, 길드 소개, 권유 등)\n");
            sb.Append("  · low: 성의 없거나 너무 짧은 말(ㅇㅇ, ㅋㅋ, 응 같은 한두 글자 포함)\n");
            sb.Append("  · clash: ").Append(RecruitScoring.ValueClash(d.values));
            if (dislikes.Count > 0) sb.Append(", ").Append(string.Join(", ", dislikes));
            sb.Append("처럼 네 가치관이나 특징과 정면으로 안 맞는 말\n");
            sb.Append("  · rude: 무례하거나 기분을 상하게 하는 말\n");
            sb.Append("  단순히 다른 화제라는 이유만으로 clash를 고르지 않는다.\n");
            sb.Append("반드시 JSON 한 개로만 답한다.");
            return sb.ToString();
        }

        static string GuildInfo()
        {
            var parts = new List<string>();
            if (Services.TryGet<GuildRoster>(out var roster))
            {
                int n = roster.Members.Count;
                parts.Add($"길드원 {n}명" + (n > 0 ? $"({string.Join(", ", roster.Members.Take(4).Select(m => m.Data.name))}{(n > 4 ? " 등" : "")})" : ""));
            }
            if (Services.TryGet<GuildTreasury>(out var t)) parts.Add($"길드 자금 {t.Gold}G");
            // Facility levels and recent guild news give the model something concrete to answer with
            // instead of vague generalities.
            if (Services.TryGet<TownService>(out var town))
                parts.Add("시설 " + string.Join(", ", town.Buildings.Select(b => $"{b.DisplayName} Lv.{b.Level}")));
            if (Services.TryGet<GameLog>(out var log))
            {
                var recent = log.Recent(3).ToList();
                if (recent.Count > 0) parts.Add("최근 소식: " + string.Join(" / ", recent));
            }
            return "길드 정보: " + string.Join(", ", parts) + ".";
        }

        /// <summary>
        /// The visitor's inner state, attached to the newest message: how much they like the guild master now
        /// (in words) and, for a formal offer, that it has to be answered. Kept out of the system prompt so that
        /// stays the same for the whole talk.
        /// </summary>
        public static string BuildAside(int affinity, bool proposal) => BuildAside(affinity, proposal, false);

        /// <param name="nagging">The guild master just repeated an invitation (see RecruitmentSession.NagPenalty).</param>
        public static string BuildAside(int affinity, bool proposal, bool nagging) => BuildAside(affinity, proposal, nagging, null);

        /// <param name="tellNow">What the visitor lets out in this reply (VisitorReveal.ForModel), decided by the rules.</param>
        public static string BuildAside(int affinity, bool proposal, bool nagging, IEnumerable<string> tellNow)
        {
            var parts = new List<string>();
            if (affinity >= 0) parts.Add("네 속마음: " + NpcVoice.RecruitMood(affinity) + " " + Openness(affinity) + " 방금 들은 말에 따라 조금 달라질 수 있다.");
            if (nagging) parts.Add("길드 마스터가 방금 한 권유를 또 조르고 있다. 부담스럽다.");
            var tell = tellNow?.Where(t => !string.IsNullOrEmpty(t)).ToList();
            if (tell != null && tell.Count > 0)
                parts.Add("이번 대답에서는 " + string.Join(", ", tell) + "을(를) 네 말투로 분명하게 털어놓는다.");
            if (proposal)
                parts.Add("길드 마스터가 정식으로 영입을 제안했다. 들어가고 싶은 마음이면 join, 아직 아니면 refuse로 답하고 네 말투대로 무엇이 아쉬운지 말한다.");
            return parts.Count == 0 ? null : "(" + string.Join(" ", parts) + ")";
        }

        /// <summary>
        /// How much the visitor is willing to say about themselves: guarded strangers only hint, warmer ones
        /// answer when asked, the ones who like the guild master open up by themselves. Makes finding out a
        /// visitor's values and traits part of winning them over (user request 2026-09-29).
        /// </summary>
        public static string Openness(int affinity)
            => affinity < VisitorReveal.ValuesAskAffinity ? "아직 경계하고 있다. 속내를 물어도 얼버무린다."
             : affinity < 65 ? "조금 마음이 열렸다."
             : "마음이 많이 열렸다.";

        static readonly string[] ProposalDecisions = { "join", "refuse" };

        /// <summary>Chat: the line comes first (it answers what was said), then the decision that follows from it.</summary>
        public static string Grammar { get; } = BuildGrammar(Decisions, decisionFirst: false);

        /// <summary>
        /// A formal offer must be answered (only join or refuse), and here the answer IS the decision: it comes
        /// first and the line is written to match it (a line written first drifted into "let me think about
        /// it" while the decision said join).
        /// </summary>
        public static string ProposalGrammar { get; } = BuildGrammar(ProposalDecisions, decisionFirst: true);

        /// <summary>Grammar for one request: the line, the decision and what kind of line the guild master's was.</summary>
        public static string BuildGrammar(string[] decisions, bool decisionFirst)
        {
            var g = new GbnfObject();
            if (decisionFirst) g.Enum("decision", decisions).String("reply", ReplyGrammarChars);
            else g.String("reply", ReplyGrammarChars).Enum("decision", decisions);
            return g.Enum("reaction", RecruitScoring.Codes).Build();
        }

        /// <summary>Safety cap; a reply cut by it is trimmed to its last full sentence.</summary>
        public const int ReplyGrammarChars = 130;

        /// <summary>A talk the visitor may not say yes in yet (not asked, too early): see RecruitmentSession.MayJoin.</summary>
        static readonly string[] NotYetDecisions = { "undecided", "refuse" };

        /// <param name="mayJoin">False keeps "join" out of the answer altogether (user report 2026-10-18: the model
        /// said join to a bare "안녕" from a visitor the town had warmed up).</param>
        public static LlmRequest BuildRequest(Adventurer v, int affinity, IReadOnlyList<RecruitTurn> turns, bool proposal, bool nagging = false,
                                              IEnumerable<string> tellNow = null, bool mayJoin = true)
        {
            var req = new LlmRequest
            {
                System = BuildSystem(v),
                Grammar = BuildGrammar(proposal ? ProposalDecisions : mayJoin ? Decisions : NotYetDecisions, proposal),
                MaxTokens = 240,
                Temperature = 0.7f,
                Priority = LlmPriority.Dialogue,
                DedupeKey = "recruit:" + v.Data.id,
                TimeoutSeconds = 25f,
                Tag = "recruit:" + v.Data.id,
            };
            NpcVoice.SetChat(req, turns.Select(t => (t.FromPlayer, t.Text)), HistoryTurns * 2, BuildAside(affinity, proposal, nagging, tellNow));
            return req;
        }

        public static RecruitDecision ParseDecision(string s) => s switch
        {
            "join" => RecruitDecision.Join,
            "refuse" => RecruitDecision.Refuse,
            _ => RecruitDecision.Undecided,
        };
    }
}
