using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.LLM;

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
            sb.Append("- 성격과 가치관은 이야기가 그쪽으로 흐를 때만 자연스럽게 드러난다. 모든 말에 넣지 않는다.\n");
            sb.Append("- 이미 한 말은 되풀이하지 않는다. 숫자나 게임 용어(호감도 등)는 말하지 않는다.\n");
            sb.Append("- 길드 마스터가 정식으로 영입을 제안하면, 네 속마음에 따라 들어가겠다(join) 또는 정중히 거절(refuse)로 분명히 답한다.\n\n");
            sb.Append("대화 예시(다른 인물):\n");
            sb.Append("길드 마스터: 여기서 뭐 하세요?\n");
            sb.Append("세린: 다음에 어디로 갈지 고민 중이었어요. 그쪽은 무슨 일이세요?\n\n");
            sb.Append("JSON 항목:\n");
            sb.Append("- reply: 네가 하는 말. 대사만 쓴다(따옴표, 지문, 이름 없이).\n");
            sb.Append("- decision: undecided(아직 정하지 않음) / join / refuse. reply에서 한 말과 같은 결론이어야 한다. refuse는 대화를 끝내는 거절이라, 정식 제안을 거절할 때나 상대가 무례해서 더는 말하고 싶지 않을 때만 쓴다. 평범한 대화 중에는 undecided다.\n");
            sb.Append("- affinity_delta: 길드 마스터의 방금 말이 마음에 들면 +1~+10, 평범하면 0, 무례하거나 안 맞으면 -1~-10.\n");
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
