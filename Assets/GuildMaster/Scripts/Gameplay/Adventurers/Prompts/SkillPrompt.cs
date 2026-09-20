using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.LLM;

namespace GuildMaster.Adventurers.Prompts
{
    /// <summary>
    /// Choosing what to learn on level-up (P4-34, 2026-09-20 user decision). The adventurer picks
    /// from what their class can still learn, and when they are already carrying the maximum they
    /// also say which one they let go of. DTO field names are the JSON keys (snake_case).
    /// </summary>
    public static class SkillPrompt
    {
        public const int ReasonGrammarChars = 40;
        public const int SayGrammarChars = 100;

        public const string SystemPrompt =
            "너는 판타지 모험가 길드에 소속된 모험가 NPC다. 방금 레벨이 올라 새 기술을 하나 익힐 수 있다.\n" +
            "- learn: candidates 중 배울 것 하나를 고른다. id는 목록에 있는 값만 쓴다.\n" +
            "- forget: must_forget이 true면 known 중 버릴 것 하나를 고른다(false면 빈 문자열).\n" +
            "성격(mbti, values), 특성(traits), 직업(class), 그리고 memories에 남은 실제 경험을 보고 고른다. " +
            "혼자 앞서다 다친 기억이 많으면 살아남는 기술을, 동료를 지키지 못한 기억이 있으면 지키는 기술을 고르는 식이다.\n" +
            "버릴 기술은 지금 방식에 가장 안 맞는 것을 고른다. 쓸 만한 것을 버리고 비슷한 것을 배우지는 않는다.\n" +
            "reason은 20자 이내의 짧은 구절, say는 동료에게 하는 짧은 한국어 대사 한 마디다. " +
            "반드시 JSON 한 개로만 답한다.";

        [Serializable]
        public class SkillDto
        {
            public string id;
            public string name;
            public string what_it_does;
        }

        [Serializable]
        public class ContextDto
        {
            public string name;
            public string @class;
            public int level;
            public string mbti;
            public string values;
            public string[] traits;
            public SkillDto[] known;
            public SkillDto[] candidates;
            public bool must_forget;
            public string[] memories;
        }

        [Serializable]
        public class ResponseDto
        {
            public string learn;
            public string forget;
            public string reason;
            public string say;
        }

        public static SkillDto Describe(string id) => new()
        {
            id = id,
            name = SkillCatalog.NameOf(id),
            what_it_does = SkillCatalog.DescriptionOf(id),
        };

        public static ContextDto BuildContext(Adventurer a, IReadOnlyList<string> candidates)
        {
            var d = a.Data;
            return new ContextDto
            {
                name = d.name,
                @class = AdventurerData.ClassLabel(d.cls),
                level = d.level,
                mbti = d.mbti,
                values = d.values,
                traits = a.Traits != null ? a.Traits.PromptHints.ToArray() : Array.Empty<string>(),
                known = d.skillIds.Select(Describe).ToArray(),
                candidates = candidates.Select(Describe).ToArray(),
                must_forget = SkillCatalog.MustForget(d.skillIds),
                memories = AdventurerMemory.SelectForPrompt(d.memories).Select(m => m.Format()).ToArray(),
            };
        }

        public static string BuildGrammar(IEnumerable<string> candidates, IEnumerable<string> known)
            => new GbnfObject()
                .Enum("learn", candidates)
                .Enum("forget", known.Append(""))
                .String("reason", ReasonGrammarChars)
                .String("say", SayGrammarChars)
                .Build();

        public static LlmRequest BuildRequest(Adventurer a, IReadOnlyList<string> candidates) => new()
        {
            System = SystemPrompt,
            User = LlmJson.ToJson(BuildContext(a, candidates)),
            Grammar = BuildGrammar(candidates, a.Data.skillIds),
            MaxTokens = 300,
            Temperature = 0.7f,
            Priority = LlmPriority.Decision,
            DedupeKey = "skill:" + a.Data.id,
            TimeoutSeconds = 40f,
            Tag = "skill:" + a.Data.id,
        };

        /// <summary>
        /// Turns a model answer into a (learn, forget) pair, or (null, null) when it is unusable.
        /// The model may name a skill outside the offer, or forget something it does not have.
        /// </summary>
        public static (string learn, string forget) Interpret(ResponseDto r, IReadOnlyList<string> candidates, IReadOnlyList<string> known)
        {
            if (r == null || string.IsNullOrWhiteSpace(r.learn)) return (null, null);
            string learn = r.learn.Trim();
            if (SkillCatalog.IndexOf(candidates, learn) < 0) return (null, null);
            if (!SkillCatalog.MustForget(known)) return (learn, null);
            string forget = r.forget?.Trim();
            if (string.IsNullOrEmpty(forget) || SkillCatalog.IndexOf(known, forget) < 0) return (null, null);
            return (learn, forget);
        }
    }
}
