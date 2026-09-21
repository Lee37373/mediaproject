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
    /// Prompt, response DTO and grammar for persuading an adventurer to take a quest (P4-24).
    /// Same shape as the recruitment dialogue: the decision comes first so the line matches it.
    /// </summary>
    public static class PersuadePrompt
    {
        public const int ReplyMaxChars = 100;
        /// <summary>Safety cap; a reply cut by it is trimmed to its last full sentence.</summary>
        public const int ReplyGrammarChars = 110;
        /// <summary>Exchanges (player line + adventurer line) kept in the prompt.</summary>
        public const int HistoryTurns = 6;

        static readonly string[] Decisions = { "undecided", "accept", "refuse" };
        static readonly string[] ProposalDecisions = { "accept", "refuse" };

        [Serializable]
        public class ResponseDto
        {
            public string decision;
            public int willingness_delta;
            public string reply;
        }

        public static string BuildSystem(Adventurer a, Quest q, int willingness, int threshold, int offeredGold = 0)
        {
            var d = a.Data;
            var sb = new StringBuilder();
            sb.Append($"너는 '{d.name}'이라는 {AdventurerData.ClassLabel(d.cls)} 모험가(레벨 {d.level})다. ");
            sb.Append($"MBTI는 {d.mbti}, 가장 중요하게 여기는 가치는 '{d.values}'다.");
            var hints = a.Traits != null ? a.Traits.PromptHints.ToList() : new List<string>();
            if (hints.Count > 0) sb.Append(" 특징: ").Append(string.Join(" ", hints));
            sb.Append("\n너는 모험가 길드 소속이다. 길드 마스터가 게시판에 올린 의뢰가 너무 위험해 보여 아무도 맡지 않았고, ");
            sb.Append("길드 마스터가 직접 찾아와 네가 그 의뢰를 맡도록 설득하고 있다.\n");
            sb.Append($"의뢰: {q.Title}. 보상 {q.RewardGold}G와 {q.RewardExp}EXP, 위험도 {Quest.DangerLabel(q.Danger)}, ");
            sb.Append($"너에게는 '{DecisionPrompt.RiskLabel(a, q)}' 수준이다. 최대 {q.MaxParty}명이 함께 갈 수 있다.\n");
            if (offeredGold > 0) sb.Append($"길드 마스터는 협상으로 보상을 {offeredGold}G 더 얹겠다고 약속했다(네가 의뢰를 맡으면 지급된다).\n");
            sb.Append($"네 상태: 체력 {(int)(a.HpRatio * 100f)}%, 피로 {(int)d.fatigue}, 스트레스 {(int)d.stress}, 소지금 {d.gold}G({DecisionPrompt.MoneyLabel(d.gold)}).\n");
            var memories = AdventurerMemory.SelectForPrompt(d.memories).Select(m => m.Format()).ToList();
            if (memories.Count > 0) sb.Append("네가 직접 겪은 일: ").Append(string.Join(" / ", memories)).Append('\n');
            sb.Append($"현재 설득도는 {willingness}/100이고, {threshold} 이상이 되어야 의뢰를 맡을 마음이 든다.\n");
            sb.Append("규칙:\n");
            sb.Append("- 성격과 가치관이 드러나게, 1~2문장 60자 안팎의 자연스러운 한국어 구어체로 대답한다(reply). 상투적인 말이나 앞에서 한 말은 반복하지 않는다.\n");
            sb.Append($"- 길드 마스터가 구체적이고 그럴듯한 이유(보상, 함께 갈 동료, 준비물이나 안전 대책, 네 가치관에 맞는 이야기)를 대면 willingness_delta를 올리고, 빈말이거나 무례하면 내린다(-{PersuasionRules.MaxDelta}~{PersuasionRules.MaxDelta}).\n");
            sb.Append("- 위험하다는 사실이 사라지는 것은 아니다. 겁이 많거나 안전을 중시하면 쉽게 넘어가지 않는다. 죽을 뻔했거나 동료가 쓰러진 기억이 있으면 더 신중하다.\n");
            sb.Append($"- 설득도가 {threshold} 미만이면 accept를 고르지 않는다. 아직 결정하지 않았으면 undecided, 맡기로 했으면 accept, 확실히 싫으면 refuse다.\n");
            sb.Append("- 길드 마스터가 정식으로 부탁하면 accept 또는 refuse로 분명히 답하고, reply도 그 결정과 일치해야 한다.\n");
            sb.Append("- 앞에서 한 말을 그대로 반복하지 않는다.\n");
            sb.Append("반드시 JSON 한 개로만 답한다.");
            return sb.ToString();
        }

        /// <summary>Transcript of the last exchanges ending with the new player line.</summary>
        public static string BuildUser(IReadOnlyList<PersuadeTurn> turns, string npcName, bool proposal)
        {
            var sb = new StringBuilder();
            int start = Math.Max(0, turns.Count - HistoryTurns * 2);
            for (int i = start; i < turns.Count; i++)
                sb.Append(turns[i].FromPlayer ? "길드 마스터: " : $"{npcName}: ").Append(turns[i].Text).Append('\n');
            if (proposal) sb.Append("(길드 마스터가 정식으로 이 의뢰를 맡아 달라고 부탁했다. accept 또는 refuse로 답한다.)\n");
            sb.Append($"{npcName}의 대답:");
            return sb.ToString();
        }

        public static string Grammar { get; } = BuildGrammar(Decisions);

        /// <summary>A formal request must be answered: only accept or refuse.</summary>
        public static string ProposalGrammar { get; } = BuildGrammar(ProposalDecisions);

        static string BuildGrammar(string[] decisions) => new GbnfObject()
            .Enum("decision", decisions)
            .Int("willingness_delta", 2)
            .String("reply", ReplyGrammarChars)
            .Build();

        public static LlmRequest BuildRequest(Adventurer a, Quest q, int willingness, int threshold, IReadOnlyList<PersuadeTurn> turns, bool proposal,
            int offeredGold = 0) => new()
        {
            System = BuildSystem(a, q, willingness, threshold, offeredGold),
            User = BuildUser(turns, a.Data.name, proposal),
            Grammar = proposal ? ProposalGrammar : Grammar,
            MaxTokens = 220,
            Temperature = 0.6f,
            Priority = LlmPriority.Dialogue,
            DedupeKey = "persuade:" + a.Data.id,
            TimeoutSeconds = 25f,
            Tag = "persuade:" + a.Data.id,
        };

        public static PersuadeDecision ParseDecision(string s) => s switch
        {
            "accept" => PersuadeDecision.Accept,
            "refuse" => PersuadeDecision.Refuse,
            _ => PersuadeDecision.Undecided,
        };
    }
}
