using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.LLM;

namespace GuildMaster.Recruitment
{
    /// <summary>Prompt, response DTO and grammar for the recruitment dialogue (P3-3).</summary>
    public static class RecruitPrompt
    {
        public const int ReplyMaxChars = 100;
        public const int MaxDelta = 15;
        /// <summary>Exchanges (player line + visitor line) kept in the prompt.</summary>
        public const int HistoryTurns = 6;

        static readonly string[] Decisions = { "undecided", "join", "refuse" };

        [Serializable]
        public class ResponseDto
        {
            public string reply;
            public int affinity_delta;
            public string decision;
        }

        /// <summary>
        /// Kept short and stable for a whole talk (no affinity, no news feed): the model only re-reads
        /// what changed, and a small model answers faster and more on-topic with a short prompt.
        /// </summary>
        public static string BuildSystem(Adventurer v)
        {
            var d = v.Data;
            var sb = new StringBuilder();
            sb.Append($"너는 '{d.name}'이라는 {AdventurerData.ClassLabel(d.cls)} 모험가(레벨 {d.level})다. ");
            sb.Append($"MBTI는 {d.mbti}, 가장 중요하게 여기는 가치는 '{d.values}'다.");
            var hints = v.Traits != null ? v.Traits.PromptHints.ToList() : new List<string>();
            if (hints.Count > 0) sb.Append(" 특징: ").Append(string.Join(" ", hints));
            sb.Append("\n지금 이 마을 광장에 잠시 들렀고, 모험가 길드의 길드 마스터가 너를 길드에 영입하려고 말을 걸고 있다.\n");
            sb.Append(GuildInfo()).Append('\n');
            sb.Append("규칙:\n");
            sb.Append("- 성격과 가치관이 드러나게, 1~2문장 60자 안팎의 자연스러운 한국어 구어체로 대답한다(reply).\n");
            sb.Append("- 상대 말에 맞게 대답한다. 인사에는 인사로, 질문에는 네 사정(하는 일, 처지, 바라는 것)을 들어 대답하고, 가끔 짧게 되묻는다.\n");
            sb.Append("- \"그렇군요\", \"다른 건요?\" 같은 상투적인 말이나 앞에서 한 말을 반복하지 않는다.\n");
            sb.Append($"- 길드 마스터의 말이 네 가치관이나 성격에 맞으면 affinity_delta를 올리고, 무례하거나 안 맞으면 내린다(-{MaxDelta}~{MaxDelta}).\n");
            sb.Append("- decision: 아직 정하지 않았으면 undecided, 들어가고 싶으면 join, 확실히 싫으면 refuse. 정식 영입 제안에는 호감도에 따라 join 또는 refuse로 분명히 답하고 reply도 결정과 일치해야 한다.\n");
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

        /// <summary>Transcript of the last exchanges ending with the new player line.</summary>
        public static string BuildUser(IReadOnlyList<RecruitTurn> turns, string visitorName, bool proposal, int affinity = -1)
        {
            var sb = new StringBuilder();
            int start = Math.Max(0, turns.Count - HistoryTurns * 2);
            for (int i = start; i < turns.Count; i++)
                sb.Append(turns[i].FromPlayer ? "길드 마스터: " : $"{visitorName}: ").Append(turns[i].Text).Append('\n');
            if (affinity >= 0) sb.Append($"(지금 길드 마스터에 대한 네 호감도: {affinity}/100)\n");
            if (proposal) sb.Append("(길드 마스터가 정식으로 영입을 제안했다. join 또는 refuse로 답한다.)\n");
            sb.Append($"{visitorName}의 대답:");
            return sb.ToString();
        }

        static readonly string[] ProposalDecisions = { "join", "refuse" };

        /// <summary>Decision first so the reply is written to match it (the model otherwise
        /// says "yes" in the reply but answers "undecided").</summary>
        public static string Grammar { get; } = BuildGrammar(Decisions);

        /// <summary>A formal offer must be answered: only join or refuse.</summary>
        public static string ProposalGrammar { get; } = BuildGrammar(ProposalDecisions);

        static string BuildGrammar(string[] decisions) => new GbnfObject()
            .Enum("decision", decisions)
            .Int("affinity_delta", 2)
            .String("reply", ReplyGrammarChars)
            .Build();

        /// <summary>Safety cap; a reply cut by it is trimmed to its last full sentence.</summary>
        public const int ReplyGrammarChars = 110;

        public static LlmRequest BuildRequest(Adventurer v, int affinity, IReadOnlyList<RecruitTurn> turns, bool proposal) => new()
        {
            System = BuildSystem(v),
            User = BuildUser(turns, v.Data.name, proposal, affinity),
            Grammar = proposal ? ProposalGrammar : Grammar,
            MaxTokens = 220,
            Temperature = 0.6f,
            Priority = LlmPriority.Dialogue,
            DedupeKey = "recruit:" + v.Data.id,
            TimeoutSeconds = 25f,
            Tag = "recruit:" + v.Data.id,
        };

        public static RecruitDecision ParseDecision(string s) => s switch
        {
            "join" => RecruitDecision.Join,
            "refuse" => RecruitDecision.Refuse,
            _ => RecruitDecision.Undecided,
        };
    }
}
