using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.LLM;
using GuildMaster.Quests;

namespace GuildMaster.Persuasion
{
    public struct PersuadeTurn
    {
        public bool FromPlayer;
        public string Text;
    }

    /// <summary>
    /// Prompt, response DTO and grammar for persuading an adventurer to take a quest (P4-24). Same shape as
    /// the recruitment dialogue: the member is described as a person, earlier turns are real chat turns, the
    /// line comes first and the decision has to agree with it. Game numbers (willingness, the bar for a yes,
    /// stress, fatigue) reach the model only as feelings.
    /// </summary>
    public static class PersuadePrompt
    {
        public const int ReplyMaxChars = 110;
        /// <summary>Safety cap; a reply cut by it is trimmed to its last full sentence.</summary>
        public const int ReplyGrammarChars = 130;
        /// <summary>Exchanges (player line + adventurer line) kept in the prompt.</summary>
        public const int HistoryTurns = 6;

        static readonly string[] Decisions = { "undecided", "accept", "refuse" };
        static readonly string[] ProposalDecisions = { "accept", "refuse" };

        [Serializable]
        public class ResponseDto
        {
            public string reply;
            public string decision;
            public int willingness_delta;
        }

        /// <summary>The situation and the member's own circumstances. Willingness is not in here (see <see cref="BuildAside"/>).</summary>
        public static string BuildSystem(Adventurer a, Quest q, int offeredGold = 0)
        {
            var d = a.Data;
            var sb = new StringBuilder();
            sb.Append($"너는 판타지 세계의 모험가 '{d.name}'이다. {AdventurerData.ClassLabel(d.cls)}이고 레벨은 {d.level}이다.\n");
            sb.Append(NpcVoice.Sheet(a)).Append('\n');
            sb.Append("너는 모험가 길드 소속이다. 게시판에 올라온 의뢰 하나가 너무 위험해 보여 아무도 맡지 않았고, ");
            sb.Append("길드 마스터가 직접 찾아와 네가 그 의뢰를 맡아 주도록 설득하고 있다.\n");
            sb.Append($"의뢰: {q.Title}. 보상은 {q.RewardGold}G와 경험치 {q.RewardExp}, 위험도는 {Quest.DangerLabel(q.Danger)}이고 ");
            sb.Append($"네게는 '{DecisionPrompt.RiskLabel(a, q)}' 수준이다. 최대 {q.MaxParty}명이 함께 갈 수 있다.\n");
            if (offeredGold > 0) sb.Append($"길드 마스터는 보상을 {offeredGold}G 더 얹어 주겠다고 약속했다(네가 의뢰를 맡으면 지급된다).\n");
            sb.Append("지금 네 상태: ").Append(NpcVoice.Condition(a)).Append('\n');
            var memories = AdventurerMemory.SelectForPrompt(d.memories).Select(m => m.Format()).ToList();
            if (memories.Count > 0) sb.Append("네가 직접 겪은 일: ").Append(string.Join(" / ", memories)).Append('\n');
            sb.Append('\n');
            sb.Append("말하는 법:\n");
            sb.Append("- 길드 마스터에게 존댓말(해요체)로, 실제 사람이 말하듯 한두 문장으로 짧게 답한다.\n");
            sb.Append("- 먼저 상대가 방금 한 말에 반응한다. 이유가 구체적이고 그럴듯하면(보상, 함께 갈 동료, 준비물이나 안전 대책, 네 가치관에 맞는 이야기) 마음이 움직이고, 빈말이거나 무례하면 굳어진다.\n");
            sb.Append("- 위험하다는 사실이 사라지는 것은 아니다. 겁이 많거나 안전을 중시하면 쉽게 넘어가지 않는다. 죽을 뻔했거나 동료가 쓰러진 기억이 있으면 더 신중하다.\n");
            sb.Append("- 성격과 가치관은 이야기가 그쪽으로 흐를 때만 드러난다. 이미 한 말은 되풀이하지 않는다. 숫자나 게임 용어(설득도 등)는 말하지 않는다.\n");
            sb.Append("- 길드 마스터가 정식으로 부탁하면, 네 속마음에 따라 맡겠다(accept) 또는 정중히 거절(refuse)로 분명히 답한다.\n\n");
            sb.Append("대화 예시(다른 인물):\n");
            sb.Append("길드 마스터: 이번 의뢰, 부탁드려도 될까요?\n");
            sb.Append("세린: 솔직히 좀 무서운 의뢰라서요. 같이 갈 분이 있다면 생각해 볼게요.\n\n");
            sb.Append("JSON 항목:\n");
            sb.Append("- reply: 네가 하는 말. 대사만 쓴다(따옴표, 지문, 이름 없이).\n");
            sb.Append("- decision: undecided(아직 정하지 않음) / accept(맡겠다) / refuse(맡지 않겠다). reply에서 한 말과 같은 결론이어야 한다.\n");
            sb.Append("- willingness_delta: 길드 마스터의 방금 말이 설득력 있으면 +1~+10, 평범하면 0, 빈말이거나 무례하면 -1~-10.\n");
            sb.Append("반드시 JSON 한 개로만 답한다.");
            return sb.ToString();
        }

        /// <summary>
        /// The member's inner state, attached to the newest message: how willing they are now (in words) and,
        /// for a formal request, that it has to be answered. Null when there is nothing to add.
        /// </summary>
        public static string BuildAside(int willingness, int threshold, bool proposal)
        {
            var parts = new List<string> { "네 속마음: " + NpcVoice.PersuadeMood(willingness, threshold) + " 방금 들은 말에 따라 조금 달라질 수 있다." };
            if (proposal)
                parts.Add("길드 마스터가 정식으로 이 의뢰를 맡아 달라고 부탁했다. 맡을 마음이 있으면 accept, 아니면 정중히 refuse로 답한다.");
            return "(" + string.Join(" ", parts) + ")";
        }

        /// <summary>Chat: the line comes first (it answers what was said), then the decision that follows from it.</summary>
        public static string Grammar { get; } = BuildGrammar(Decisions, decisionFirst: false);

        /// <summary>A formal request must be answered (only accept or refuse), and the answer IS the decision: it comes first and the line is written to match it.</summary>
        public static string ProposalGrammar { get; } = BuildGrammar(ProposalDecisions, decisionFirst: true);

        static string BuildGrammar(string[] decisions, bool decisionFirst)
        {
            var g = new GbnfObject();
            if (decisionFirst) g.Enum("decision", decisions).String("reply", ReplyGrammarChars);
            else g.String("reply", ReplyGrammarChars).Enum("decision", decisions);
            return g.Int("willingness_delta", 2).Build();
        }

        public static LlmRequest BuildRequest(Adventurer a, Quest q, int willingness, int threshold, IReadOnlyList<PersuadeTurn> turns, bool proposal,
            int offeredGold = 0)
        {
            var req = new LlmRequest
            {
                System = BuildSystem(a, q, offeredGold),
                Grammar = proposal ? ProposalGrammar : Grammar,
                MaxTokens = 240,
                Temperature = 0.7f,
                Priority = LlmPriority.Dialogue,
                DedupeKey = "persuade:" + a.Data.id,
                TimeoutSeconds = 25f,
                Tag = "persuade:" + a.Data.id,
            };
            NpcVoice.SetChat(req, turns.Select(t => (t.FromPlayer, t.Text)), HistoryTurns * 2, BuildAside(willingness, threshold, proposal));
            return req;
        }

        public static PersuadeDecision ParseDecision(string s) => s switch
        {
            "accept" => PersuadeDecision.Accept,
            "refuse" => PersuadeDecision.Refuse,
            _ => PersuadeDecision.Undecided,
        };
    }
}
