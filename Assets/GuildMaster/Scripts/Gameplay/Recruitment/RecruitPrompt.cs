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
            public int affinity_delta;
        }

        /// <summary>
        /// Stable for a whole talk (the visitor's current feelings travel with the newest message, see
        /// <see cref="BuildAside"/>), so the model can reuse what it already read.
        /// </summary>
        public static string BuildSystem(Adventurer v)
        {
            var d = v.Data;
            var sb = new StringBuilder();
            sb.Append($"너는 판타지 세계의 모험가 '{d.name}'이다. {AdventurerData.ClassLabel(d.cls)}이고 레벨은 {d.level}이다.\n");
            sb.Append(NpcVoice.Sheet(v)).Append('\n');
            sb.Append("지금 너는 마을 광장에 잠시 들렀는데, 모험가 길드의 길드 마스터가 다가와 길드에 들어오라며 말을 걸고 있다.\n");
            sb.Append(GuildInfo()).Append("\n\n");
            sb.Append("말하는 법:\n");
            sb.Append("- 길드 마스터에게 존댓말(해요체)로, 실제 사람이 말하듯 한두 문장으로 짧게 답한다.\n");
            sb.Append("- 먼저 상대가 방금 한 말에 반응한다. 인사에는 인사로, 질문에는 네 사정을 섞어 답하고, 궁금한 게 있으면 짧게 되묻는다.\n");
            // Added after user report 2026-09-23 #1: real play showed the same vague filler
            // ("아직 특별히 정해둔 건 없어요") answering completely different questions (직업, 성격 x2)
            // in a row, even though 성격/가치관 for this exact visitor are right above in the sheet.
            sb.Append("- 성격, 직업, 좋아하는 것 등 너 자신에 대해 직접 물으면 위 '성격'/'특징'에 적힌 내용을 바탕으로 구체적으로 답한다. \"아직 특별히 정해둔 건 없어요\" 같은 회피성 대답으로 얼버무리지 않는다.\n");
            sb.Append("- 성격과 가치관은 이야기가 그쪽으로 흐를 때만 자연스럽게 드러난다. 모든 말에 넣지는 않되, 직접 질문받으면 위 규칙대로 답한다.\n");
            sb.Append("- 직전에 자신이 한 대답을 그대로, 또는 거의 그대로 반복하지 않는다. 질문이 다르면 다른 표현으로 답한다.\n");
            sb.Append("- 숫자나 게임 용어(호감도 등)는 말하지 않는다.\n");
            sb.Append("- 길드 마스터가 정식으로 영입을 제안하면, 네 속마음에 따라 들어가겠다(join) 또는 정중히 거절(refuse)로 분명히 답한다.\n\n");
            sb.Append("대화 예시(다른 인물):\n");
            sb.Append("길드 마스터: 여기서 뭐 하세요?\n");
            sb.Append("세린: 다음에 어디로 갈지 고민 중이었어요. 그쪽은 무슨 일이세요?\n\n");
            sb.Append("JSON 항목:\n");
            sb.Append("- reply: 네가 하는 말. 대사만 쓴다(따옴표, 지문, 이름 없이).\n");
            sb.Append("- decision: undecided(아직 정하지 않음) / join / refuse. reply에서 한 말과 같은 결론이어야 한다. refuse는 대화를 끝내는 거절이라, 정식 제안을 거절할 때나 상대가 무례해서 더는 말하고 싶지 않을 때만 쓴다. 평범한 대화 중에는 undecided다.\n");
            // Widened once already (2026-09-23 #3: "변동폭이 너무 작음"), but that pass lumped "다른 화제"
            // in with genuine mismatch/보람없음, so ordinary conversation about anything but the visitor's
            // exact value got punished — same-day follow-up report: "호감도 올리는 것도 생각보다 쉽지가
            // 않네". Split "성의 있게 답했지만 무관한 말" (mildly positive, like real conversation) out
            // from "가치관과 정면으로 안 맞는 말" (the only kind of mismatch that should cost much).
            sb.Append("- affinity_delta: 방금 들은 말이 네 마음에 드는 정도를 정직하게 매긴다. 매번 0을 주지 말고 실제로 오르내리게 하되, 성의 있는 보통 대화까지 깎지는 않는다.\n");
            sb.Append("  · 네 가치관/성격에 맞고 끌리는 말: +5~+15\n");
            sb.Append("  · 성의 있게 답했지만 가치관과 특별히 관련은 없는 말: +2~+5\n");
            sb.Append("  · 성의 없이 대충 대답하거나 짧게 얼버무리는 말: -1~-3\n");
            sb.Append("  · 네 가치관과 정면으로 안 맞거나 그 가치를 무시하는 말: -2~-5\n");
            sb.Append("  · 무례하거나 기분을 상하게 하는 말: -8~-15\n");
            sb.Append("  단순히 다른 화제라는 이유만으로 깎지 않는다. 진지하게 답했다면 가치관과 달라도 두 번째 항목으로 취급한다.\n");
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
        public static string BuildAside(int affinity, bool proposal)
        {
            var parts = new List<string>();
            if (affinity >= 0) parts.Add("네 속마음: " + NpcVoice.RecruitMood(affinity) + " 방금 들은 말에 따라 조금 달라질 수 있다.");
            if (proposal)
                parts.Add("길드 마스터가 정식으로 영입을 제안했다. 들어가고 싶은 마음이면 join, 아직 아니면 정중히 refuse로 답하고 무엇이 아쉬운지 말한다.");
            return parts.Count == 0 ? null : "(" + string.Join(" ", parts) + ")";
        }

        static readonly string[] ProposalDecisions = { "join", "refuse" };

        /// <summary>Chat: the line comes first (it answers what was said), then the decision that follows from it.</summary>
        public static string Grammar { get; } = BuildGrammar(Decisions, decisionFirst: false);

        /// <summary>
        /// A formal offer must be answered (only join or refuse), and here the answer IS the decision: it comes
        /// first and the line is written to match it (a line written first drifted into "let me think about
        /// it" while the decision said join).
        /// </summary>
        public static string ProposalGrammar { get; } = BuildGrammar(ProposalDecisions, decisionFirst: true);

        static string BuildGrammar(string[] decisions, bool decisionFirst)
        {
            var g = new GbnfObject();
            if (decisionFirst) g.Enum("decision", decisions).String("reply", ReplyGrammarChars);
            else g.String("reply", ReplyGrammarChars).Enum("decision", decisions);
            return g.Int("affinity_delta", 2).Build();
        }

        /// <summary>Safety cap; a reply cut by it is trimmed to its last full sentence.</summary>
        public const int ReplyGrammarChars = 130;

        public static LlmRequest BuildRequest(Adventurer v, int affinity, IReadOnlyList<RecruitTurn> turns, bool proposal)
        {
            var req = new LlmRequest
            {
                System = BuildSystem(v),
                Grammar = proposal ? ProposalGrammar : Grammar,
                MaxTokens = 240,
                Temperature = 0.7f,
                Priority = LlmPriority.Dialogue,
                DedupeKey = "recruit:" + v.Data.id,
                TimeoutSeconds = 25f,
                Tag = "recruit:" + v.Data.id,
            };
            NpcVoice.SetChat(req, turns.Select(t => (t.FromPlayer, t.Text)), HistoryTurns * 2, BuildAside(affinity, proposal));
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
