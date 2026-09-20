using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.LLM;
using GuildMaster.Session;
using GuildMaster.Town;

namespace GuildMaster.Recruitment
{
    /// <summary>Prompt, response DTO and grammar for the recruitment dialogue (P3-3).</summary>
    public static class RecruitPrompt
    {
        public const int ReplyMaxChars = 200;
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

        public static string BuildSystem(Adventurer v, int affinity)
        {
            var d = v.Data;
            var sb = new StringBuilder();
            sb.Append($"너는 '{d.name}'이라는 {AdventurerData.ClassLabel(d.cls)} 모험가(레벨 {d.level})다. ");
            sb.Append($"MBTI는 {d.mbti}, 가장 중요하게 여기는 가치는 '{d.values}'다.");
            var hints = v.Traits != null ? v.Traits.PromptHints.ToList() : new List<string>();
            if (hints.Count > 0) sb.Append(" 특징: ").Append(string.Join(" ", hints));
            sb.Append("\n지금 이 마을 광장에 잠시 들렀고, 모험가 길드의 길드 마스터가 너를 길드에 영입하려고 말을 걸고 있다.\n");
            sb.Append(GuildInfo()).Append('\n');
            sb.Append($"현재 길드 마스터에 대한 호감도는 {affinity}/100이다.\n");
            sb.Append("규칙:\n");
            sb.Append("- 성격과 가치관대로, 대화 상대에게 하듯 자연스러운 한국어로 짧게 대답한다(reply).\n");
            // The 4B model's two habits: echoing the question back at itself ("나는 내 의뢰금을
            // 얼마나 원할까...") and forgetting it is the outsider (user report 2026-09-20).
            sb.Append("- 길드 마스터가 바로 앞에서 한 말에 대답한다. 그 말을 그대로 따라 쓰거나 질문을 되묻지 않는다.\n");
            sb.Append("- 질문을 받으면 네 생각·조건·사정을 직접 말한다. 혼잣말로 되뇌지 않는다.\n");
            sb.Append("- 너는 아직 이 길드의 길드원이 아니라 영입 제안을 받는 쪽이다. 네가 길드 마스터인 것처럼 말하지 않는다.\n");
            sb.Append($"- 길드 마스터의 말이 네 가치관이나 성격에 맞으면 affinity_delta를 올리고, 무례하거나 안 맞으면 내린다(-{MaxDelta}~{MaxDelta}).\n");
            sb.Append("- 아직 결정하지 않았으면 decision은 undecided, 길드에 들어가고 싶으면 join, 확실히 싫으면 refuse다.\n");
            sb.Append("- 길드 마스터가 정식으로 영입을 제안하면 호감도에 따라 join 또는 refuse로 분명히 답하고, reply도 그 결정과 일치해야 한다.\n");
            sb.Append("- 앞에서 한 말을 그대로 반복하지 않는다.\n");
            sb.Append("반드시 JSON 한 개로만 답한다.");
            return sb.ToString();
        }

        static string GuildInfo()
        {
            var parts = new List<string>();
            if (Services.TryGet<GuildRoster>(out var roster))
                parts.Add($"길드원 {roster.Members.Count}명" + (roster.Members.Count > 0
                    ? $"({string.Join(", ", roster.Members.Select(m => $"{m.Data.name} {AdventurerData.ClassLabel(m.Data.cls)} Lv.{m.Data.level}"))})"
                    : ""));
            if (Services.TryGet<GuildTreasury>(out var t)) parts.Add($"길드 자금 {t.Gold}G");
            if (Services.TryGet<TownService>(out var town))
                parts.Add("시설 " + string.Join(", ", town.Buildings.Select(b => $"{b.DisplayName} Lv.{b.Level}")));
            if (Services.TryGet<GameLog>(out var log))
            {
                var recent = log.Recent(3).ToList();
                if (recent.Count > 0) parts.Add("최근 소식: " + string.Join(" / ", recent));
            }
            return "길드 정보: " + string.Join(". ", parts) + ".";
        }

        /// <summary>Transcript of the last exchanges ending with the new player line.</summary>
        public static string BuildUser(IReadOnlyList<RecruitTurn> turns, string visitorName, bool proposal)
        {
            var sb = new StringBuilder();
            int start = Math.Max(0, turns.Count - HistoryTurns * 2);
            for (int i = start; i < turns.Count; i++)
                sb.Append(turns[i].FromPlayer ? "길드 마스터: " : $"{visitorName}: ").Append(turns[i].Text).Append('\n');
            if (proposal) sb.Append("(길드 마스터가 정식으로 영입을 제안했다. join 또는 refuse로 답한다.)\n");
            // Pointing at the line to answer: with the bare transcript the model often replied to an
            // older turn, or repeated the question to itself (user report 2026-09-20).
            for (int i = turns.Count - 1; i >= 0; i--)
            {
                if (!turns[i].FromPlayer) continue;
                sb.Append($"({visitorName}이(가) 대답할 말은 바로 위 \"{turns[i].Text}\"이다.)\n");
                break;
            }
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
        public const int ReplyGrammarChars = 200;

        public static LlmRequest BuildRequest(Adventurer v, int affinity, IReadOnlyList<RecruitTurn> turns, bool proposal) => new()
        {
            System = BuildSystem(v, affinity),
            User = BuildUser(turns, v.Data.name, proposal),
            Grammar = proposal ? ProposalGrammar : Grammar,
            MaxTokens = 400,
            // Lowered from 0.8 (2026-09-20): at 0.8 the 4B model drifted off the player's line.
            Temperature = 0.65f,
            Priority = LlmPriority.Dialogue,
            DedupeKey = "recruit:" + v.Data.id,
            TimeoutSeconds = 40f,
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
