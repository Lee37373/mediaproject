using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.LLM;
using GuildMaster.Quests;

namespace GuildMaster.Proposals
{
    /// <summary>
    /// Prompt, response DTO, grammar and validation for the LLM quest proposals (F-8). The model is only given
    /// plans the rules already made valid: it picks up to <see cref="ProposalRules.MaxCandidates"/> of them by id and
    /// writes a title and a one-line pitch for each, so it cannot invent a target or a reward.
    /// </summary>
    public static class ProposalPrompt
    {
        public const int TitleMaxChars = 24;
        public const int TitleGrammarChars = 40;
        public const int PitchMaxChars = 80;
        public const int PitchGrammarChars = 120;

        [Serializable]
        public class PoolEntryDto
        {
            public string id;
            public string kind;
            public string target;
            public string area;
            public string style;
            public string danger;
            public string risk_for_guild;
            public int recommended_party;
            public int reward_gold;
            public int count;
        }

        [Serializable]
        public class ContextDto
        {
            public int day;
            public int guild_gold;
            public int members;
            public int strongest_level;
            public string situation;
            public List<PoolEntryDto> pool = new();
        }

        [Serializable]
        public class ResponseDto
        {
            public string pick1;
            public string title1;
            public string pitch1;
            public string pick2;
            public string title2;
            public string pitch2;
            public string pick3;
            public string title3;
            public string pitch3;
            public string pick4;
            public string title4;
            public string pitch4;
        }

        public const string SystemPrompt =
            "너는 모험가 길드의 의뢰 게시판을 맡은 서기다. 길드에 들어온 의뢰 요청(의뢰인이 보상을 내고 길드는 수수료를 받는다) 중에서 길드 마스터에게 보여 줄 것을 아래 pool에서 골라 이름과 한 줄 소개를 쓴다.\n" +
            "규칙:\n" +
            "- pool에 있는 id만 고른다. 같은 id를 두 번 고르지 않는다. 최대 4개를 고른다.\n" +
            "- 서로 다른 성격(안전 / 균형 / 모험)과 서로 다른 대상이 섞이게 고른다. 길드 상태(situation, 골드, 길드원 수와 실력)에 맞는 것을 우선한다.\n" +
            "- title은 짧은 의뢰 이름(20자 이내), pitch는 이 의뢰를 지금 왜 할 만한지 한 문장(60자 이내)이다. 자연스러운 한국어로 쓴다.\n" +
            "- pool의 대상, 지역, 위험도, 보상을 바꾸거나 새로 지어내지 않는다. 숫자는 pitch에 쓰지 않는다.\n" +
            "반드시 JSON 한 개로만 답한다.";

        public static ContextDto BuildContext(int day, int gold, int members, int strongestLevel, string situation, IReadOnlyList<ProposalPlan> pool)
        {
            var ctx = new ContextDto
            {
                day = day,
                guild_gold = gold,
                members = members,
                strongest_level = strongestLevel,
                situation = situation ?? "",
            };
            foreach (var p in pool)
                ctx.pool.Add(new PoolEntryDto
                {
                    id = p.Id,
                    kind = ProposalTexts.KindLabel(p.Target.Type),
                    target = p.Target.Label,
                    area = p.Target.Area,
                    style = ProposalTexts.StyleLabel(p.Style),
                    danger = Quest.DangerLabel(p.Danger),
                    risk_for_guild = p.Risk,
                    recommended_party = p.Party,
                    reward_gold = p.RewardGold,
                    count = p.Count,
                });
            return ctx;
        }

        /// <summary>Grammar allowing only the pool's ids in the pick fields.</summary>
        public static string BuildGrammar(IEnumerable<string> ids)
        {
            var list = ids.ToList();
            var g = new GbnfObject();
            for (int i = 1; i <= ProposalRules.MaxCandidates; i++)
                g = g.Enum("pick" + i, list).String("title" + i, TitleGrammarChars).String("pitch" + i, PitchGrammarChars);
            return g.Build();
        }

        public static LlmRequest BuildRequest(ContextDto ctx) => new()
        {
            System = SystemPrompt,
            User = LlmJson.ToJson(ctx),
            Grammar = BuildGrammar(ctx.pool.Select(p => p.id)),
            MaxTokens = 700,
            Temperature = 0.8f,
            Priority = LlmPriority.Decision,
            DedupeKey = "quest-proposals",
            TimeoutSeconds = 90f,
            Tag = "proposals",
        };

        static (string pick, string title, string pitch) Slot(ResponseDto d, int i) => i switch
        {
            1 => (d.pick1, d.title1, d.pitch1),
            2 => (d.pick2, d.title2, d.pitch2),
            3 => (d.pick3, d.title3, d.pitch3),
            _ => (d.pick4, d.title4, d.pitch4),
        };

        /// <summary>
        /// Semantic validation of a model response. Unknown or repeated ids are dropped; a plan whose text is
        /// unusable keeps the rule-based wording. With fewer than two valid picks the response is rejected. When
        /// the model chose fewer than <see cref="ProposalRules.MaxCandidates"/>, the rest come from the rules' own pick.
        /// </summary>
        public static bool TryInterpret(ResponseDto dto, IReadOnlyList<ProposalPlan> pool, out List<ProposalPlan> picked, out string reject, GameRandom rng = null)
        {
            picked = null;
            reject = null;
            if (dto == null) { reject = "parse"; return false; }

            var result = new List<ProposalPlan>();
            for (int i = 1; i <= ProposalRules.MaxCandidates; i++)
            {
                var (id, title, pitch) = Slot(dto, i);
                if (string.IsNullOrEmpty(id)) continue;
                var plan = pool.FirstOrDefault(p => p.Id == id);
                if (plan == null || result.Contains(plan)) continue;

                string cleanTitle = LlmJson.CleanSentence(title, TitleMaxChars, TitleGrammarChars, false);
                string cleanPitch = LlmJson.CleanSentence(pitch, PitchMaxChars, PitchGrammarChars, false);
                bool worded = !string.IsNullOrWhiteSpace(cleanTitle) && !string.IsNullOrWhiteSpace(cleanPitch);
                plan.Title = string.IsNullOrWhiteSpace(cleanTitle) ? ProposalTexts.FallbackTitle(plan) : cleanTitle;
                plan.Pitch = string.IsNullOrWhiteSpace(cleanPitch) ? ProposalTexts.FallbackPitch(plan, rng) : cleanPitch;
                plan.FromLlm = worded;
                result.Add(plan);
            }
            if (result.Count < 2) { reject = "picks"; return false; }

            foreach (var plan in ProposalRules.Recommend(pool, pool.Count))
            {
                if (result.Count >= ProposalRules.MaxCandidates) break;
                if (result.Contains(plan)) continue;
                ProposalTexts.ApplyFallback(plan, rng);
                result.Add(plan);
            }
            picked = result;
            return true;
        }
    }
}
