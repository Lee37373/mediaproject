using System;
using System.Collections.Generic;
using GuildMaster.LLM;

namespace GuildMaster.Adventurers.Prompts
{
    /// <summary>
    /// The leader splits the party's pool (P4-50). Members are keyed m1..mN (m1 is the leader);
    /// the answer gives each one's gold and one line to the party. DTO field names are the JSON keys.
    /// </summary>
    public static class SplitPrompt
    {
        public const int SayMaxChars = 100;
        public const int MaxMembers = 6;

        public const string SystemPrompt =
            "너는 판타지 모험가 길드 파티의 리더다. 파티가 방금 길드로 돌아왔고, 함께 번 골드(pool_gold)를 파티원에게 나눠야 한다. " +
            "주어진 JSON의 네 성격(mbti, values, traits)과 각 파티원의 활약(damage: 가한 피해, damage_taken: 앞에서 받아낸 피해, healing, kills), 네가 그 파티원에게 느끼는 감정(my_feeling)을 보고 " +
            "각자의 몫(m1, m2, ...)을 정한다. m1은 너 자신이다.\n" +
            "- 몫의 합은 pool_gold와 같아야 한다.\n" +
            "- 공정한 성격이면 활약에 비례하거나 고르게 나눈다. 욕심 많은 성격이면 자기 몫을 더 챙길 수 있다.\n" +
            "- 적의 공격을 받아낸 것(damage_taken)과 치유(healing)도 활약이다. 친한 동료에게 후할 수 있지만, 너무 불공평하면 파티원이 떠날 수 있다.\n" +
            "say는 분배하며 파티에게 하는 짧은 한국어 한 마디다. 반드시 JSON 한 개로만 답한다.";

        [Serializable]
        public class MemberDto
        {
            public string key;
            public string name;
            public string @class;
            public int level;
            public int damage;
            public int damage_taken;
            public int healing;
            public int kills;
            /// <summary>"친함(+42)" — the leader's feeling towards this member ("나" for the leader).</summary>
            public string my_feeling;
        }

        [Serializable]
        public class ContextDto
        {
            public string name;
            public string mbti;
            public string values;
            public string[] traits;
            public int pool_gold;
            public MemberDto[] members;
        }

        /// <summary>m1..m6 are read by name; unused ones stay 0.</summary>
        [Serializable]
        public class ResponseDto
        {
            public int m1, m2, m3, m4, m5, m6;
            public string say;

            public int Get(int i) => i switch { 0 => m1, 1 => m2, 2 => m3, 3 => m4, 4 => m5, 5 => m6, _ => 0 };
        }

        public static string Key(int i) => "m" + (i + 1);

        public static string Grammar(int members)
        {
            var g = new GbnfObject();
            for (int i = 0; i < Math.Min(members, MaxMembers); i++) g.Int(Key(i), 5, allowNegative: false);
            return g.String("say", SayMaxChars).Build();
        }

        public static LlmRequest BuildRequest(ContextDto ctx, string leaderId) => new()
        {
            System = SystemPrompt,
            User = LlmJson.ToJson(ctx),
            Grammar = Grammar(ctx.members.Length),
            MaxTokens = 220,
            Temperature = 0.6f,
            Priority = LlmPriority.Party,
            DedupeKey = "split:" + leaderId,
            TimeoutSeconds = 40f,
            Tag = "split:" + leaderId,
        };

        /// <summary>
        /// Game-rule check of the answer: shares must be non-negative and not all zero; they are then
        /// scaled to the pool exactly (the model's arithmetic is not trusted). False → rule split.
        /// </summary>
        public static bool TryInterpret(ResponseDto dto, int members, int pool, out int[] shares, out string say)
        {
            shares = null;
            say = null;
            if (dto == null || members <= 0 || members > MaxMembers) return false;
            var raw = new int[members];
            long sum = 0;
            for (int i = 0; i < members; i++)
            {
                raw[i] = dto.Get(i);
                if (raw[i] < 0) return false;
                sum += raw[i];
            }
            if (sum <= 0) return false;
            var weights = new float[members];
            for (int i = 0; i < members; i++) weights[i] = raw[i];
            shares = PartySettlement.Distribute(pool, weights);
            say = LlmJson.CleanSentence(dto.say, SayMaxChars, SayMaxChars, true);
            return true;
        }
    }
}
