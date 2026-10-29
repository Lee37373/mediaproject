using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.LLM;

namespace GuildMaster.Welfare
{
    /// <summary>
    /// Prompt, response DTO and grammar for a counselling talk (면담, user request 2026-10-01). The member is
    /// described as a person (<see cref="NpcVoice"/>) with one worry of their own (<see cref="CounselConcern"/>);
    /// earlier turns go in as real chat turns; the model writes the line and says what kind of line the guild
    /// master's was (<see cref="CounselScoring.Codes"/>) and whether it went to the worry. Stress is scored in code.
    /// </summary>
    public static class CounselPrompt
    {
        public const int ReplyMaxChars = 110;
        public const int ReplyGrammarChars = 130;
        public const int HistoryTurns = 6;
        static readonly string[] YesNo = { "yes", "no" };

        [Serializable]
        public class ResponseDto
        {
            public string reply;
            public string reaction;
            public string touched;
        }

        public static string BuildSystem(Adventurer m, CounselConcern concern)
        {
            var d = m.Data;
            var register = NpcVoice.RecruitRegister(d.mbti, d.traitIds);
            var sb = new StringBuilder();
            sb.Append($"너는 판타지 모험가 길드의 길드원 '{d.name}'이다. {AdventurerData.ClassLabel(d.cls)}이고 레벨은 {d.level}이다.\n");
            sb.Append(NpcVoice.Sheet(m)).Append('\n');
            sb.Append("몸과 마음: ").Append(NpcVoice.Condition(m)).Append('\n');
            var memories = AdventurerMemory.SelectForPrompt(d.memories, 4).Select(x => x.Text).ToList();
            if (memories.Count > 0) sb.Append("최근 겪은 일: ").Append(string.Join(" / ", memories)).Append('\n');
            sb.Append("길드 마스터가 면담을 하자며 너를 따로 불렀다. 둘만 있는 자리다.\n");
            sb.Append("네가 요즘 가장 힘든 것(속마음): ").Append(concern.Detail).Append("\n\n");
            sb.Append("말하는 법:\n");
            sb.Append("- 길드 마스터에게 말한다. ").Append(NpcVoice.RegisterRule(register, d.mbti)).Append('\n');
            sb.Append("- 방금 들은 말에 먼저 반응한다. 마음을 알아주거나 들어 주려 하면 조금씩 마음을 열고, 다그치거나 무시하면 서운해하거나 날이 선다.\n");
            sb.Append("- 속마음을 얼마나 털어놓을지는 매번 괄호 속 지시를 따른다. 없는 일을 지어내지 않는다.\n");
            sb.Append("- 대사만 쓴다. 지문(괄호 속 행동), 영어, 숫자 점수는 쓰지 않는다. 한두 문장.\n\n");
            sb.Append("출력 필드:\n");
            sb.Append("- reply: 네 대사\n");
            sb.Append("- reaction: 길드 마스터가 방금 한 말이 어떤 종류인지 하나 고른다. 지금 기분이 아니라 말의 내용으로 판단한다.\n");
            sb.Append("  · empathy: 네 마음을 알아주고 위로하는 말\n");
            sb.Append("  · listen: 무엇이 힘든지 묻거나 들어 주겠다는 말\n");
            sb.Append("  · solution: 휴가, 보상, 일 조정, 치료처럼 구체적인 도움을 제안하거나 약속하는 말\n");
            sb.Append("  · praise: 네 노력이나 실력을 인정하는 말\n");
            sb.Append("  · small: 그 밖의 평범한 말\n");
            sb.Append("  · low: 성의 없거나 너무 짧은 말(ㅇㅇ, 응 같은 한두 글자 포함)\n");
            sb.Append("  · dismiss: 참으라거나 별일 아니라며 고민을 가볍게 여기는 말, 다그치는 말\n");
            sb.Append("  · rude: 모욕하거나 무례한 말\n");
            sb.Append("- touched: 그 말이 네 진짜 고민(").Append(concern.Label).Append(")을 짚었으면 yes, 아니면 no\n");
            sb.Append("반드시 JSON 한 개로만 답한다.");
            return sb.ToString();
        }

        /// <summary>How the member feels right now, in words.</summary>
        public static string StressMood(float stress)
            => stress >= StressRules.BreakingAt ? "한계다. 모든 게 버겁고 예민하다."
             : stress >= StressRules.StrainedAt ? "많이 지쳐 있고 마음이 무겁다."
             : stress >= StressRules.TenseAt ? "조금 답답하다."
             : "한결 편안해졌다.";

        public static string BuildAside(float stress, bool open)
            => "(네 속마음: " + StressMood(stress) + " "
               + (open ? "이제 고민을 솔직하게 털어놓을 수 있다." : "아직 고민을 바로 말하지 않는다. 돌려 말하거나 얼버무린다.")
               + " 방금 들은 말에 따라 마음이 조금 달라질 수 있다.)";

        public static string Grammar { get; } = new GbnfObject()
            .String("reply", ReplyGrammarChars)
            .Enum("reaction", CounselScoring.Codes)
            .Enum("touched", YesNo)
            .Build();

        public static LlmRequest BuildRequest(Adventurer m, CounselConcern concern, float stress, bool open, IReadOnlyList<CounselTurn> turns)
        {
            var req = new LlmRequest
            {
                System = BuildSystem(m, concern),
                Grammar = Grammar,
                MaxTokens = 240,
                Temperature = 0.7f,
                Priority = LlmPriority.Dialogue,
                DedupeKey = "counsel:" + m.Data.id,
                TimeoutSeconds = 25f,
                Tag = "counsel:" + m.Data.id,
            };
            NpcVoice.SetChat(req, turns.Select(t => (t.FromPlayer, t.Text)), HistoryTurns * 2, BuildAside(stress, open));
            return req;
        }
    }
}
