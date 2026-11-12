using System;
using System.Collections.Generic;

namespace GuildMaster.Social
{
    public enum ResidentJob { Blacksmith, Herbalist, Chief, Baker, Guard, Child }

    /// <summary>
    /// Who a town resident is (user request 2026-10-07: "마을에 있는 주민이나 방문자간의 대화나 영입이 아예 없는
    /// 느낌"): name, job, temperament, what they care about, how they talk — and two things that change in play:
    /// how they feel about each person they have talked to, and what they think of the guild. A resident who thinks
    /// well of the guild talks it up to visitors; one who does not puts them off. Pure data, covered by tests.
    /// </summary>
    public class ResidentProfile
    {
        public string Id;
        public string Name;
        public ResidentJob Job;
        public string Mbti;
        public string Values;
        /// <summary>How they talk and what they talk about, for the prompt.</summary>
        public string Persona;
        /// <summary>-100..100: what they think of the guild (rumors, how members treat them).</summary>
        public int GuildOpinion;
        readonly Dictionary<string, int> _feelings = new();

        public const int Min = -100, Max = 100;
        /// <summary>From here a resident speaks well of the guild to visitors; at or below the negative, badly.</summary>
        public const int GoodOpinion = 25;

        public int FeelingToward(string id) => id != null && _feelings.TryGetValue(id, out var v) ? v : 0;

        public void AddFeeling(string id, int delta)
        {
            if (string.IsNullOrEmpty(id) || delta == 0) return;
            _feelings[id] = Math.Clamp(FeelingToward(id) + delta, Min, Max);
        }

        public void AddOpinion(int delta) => GuildOpinion = Math.Clamp(GuildOpinion + delta, Min, Max);

        /// <summary>Everyone they have feelings about (for saving).</summary>
        public IReadOnlyDictionary<string, int> Feelings => _feelings;

        /// <summary>Puts saved feelings back (P4-119), replacing the ones there.</summary>
        public void RestoreFeelings(IEnumerable<KeyValuePair<string, int>> feelings)
        {
            _feelings.Clear();
            if (feelings == null) return;
            foreach (var kv in feelings)
                if (!string.IsNullOrEmpty(kv.Key)) _feelings[kv.Key] = Math.Clamp(kv.Value, Min, Max);
        }

        public string JobLabel => Label(Job);

        public static string Label(ResidentJob job) => job switch
        {
            ResidentJob.Blacksmith => "대장장이",
            ResidentJob.Herbalist => "약초상",
            ResidentJob.Chief => "촌장",
            ResidentJob.Baker => "빵집 일꾼",
            ResidentJob.Guard => "마을 경비병",
            _ => "마을 꼬마",
        };

        /// <summary>What they think of the guild, in words for the prompt.</summary>
        public string OpinionText => GuildOpinion >= 50 ? "모험가 길드를 아주 좋게 본다. 마을의 자랑이라고 생각한다."
            : GuildOpinion >= GoodOpinion ? "모험가 길드를 꽤 좋게 본다."
            : GuildOpinion > -GoodOpinion ? "모험가 길드에 대해서는 그저 그렇다. 좋은 점도 불만도 조금씩 있다."
            : "모험가 길드를 탐탁지 않게 본다. 시끄럽고 위험한 일만 몰고 온다고 생각한다.";

        /// <summary>The townsfolk every session starts with.</summary>
        public static List<ResidentProfile> Defaults() => new()
        {
            new() { Id = "RES_hans", Name = "한스", Job = ResidentJob.Blacksmith, Mbti = "ISTJ", Values = "명예", GuildOpinion = 10,
                Persona = "말수가 적고 무뚝뚝한 대장장이다. 상대의 무기와 갑옷 상태부터 훑어보고, 쇠와 날 얘기가 나오면 말이 길어진다. 짧고 투박한 반말을 쓴다." },
            new() { Id = "RES_mari", Name = "마리", Job = ResidentJob.Herbalist, Mbti = "INFJ", Values = "지식", GuildOpinion = 20,
                Persona = "조용하고 다정한 약초상이다. 다친 사람을 보면 걱정부터 하고, 약초와 치료 얘기를 곧잘 한다. 부드러운 해요체를 쓴다." },
            new() { Id = "RES_bartol", Name = "바르톨", Job = ResidentJob.Chief, Mbti = "ESTJ", Values = "안전", GuildOpinion = 0,
                Persona = "마을 안전과 살림을 늘 걱정하는 깐깐한 촌장이다. 길드가 마을에 도움이 되는지 따지고, 훈계조로 길게 말한다. 점잖은 하게체(\"~하게\", \"~이네\")를 쓴다." },
            new() { Id = "RES_elly", Name = "엘리", Job = ResidentJob.Baker, Mbti = "ESFP", Values = "동료", GuildOpinion = 15,
                Persona = "수다스럽고 밝은 빵집 일꾼이다. 마을 소문을 제일 먼저 알고 신나서 퍼뜨리며, 맞장구와 감탄사가 많다. 발랄한 해요체를 쓴다." },
            new() { Id = "RES_greta", Name = "그레타", Job = ResidentJob.Guard, Mbti = "ISTP", Values = "안전", GuildOpinion = 5,
                Persona = "과묵한 마을 경비병이다. 성 밖 몬스터 동향과 순찰 얘기만 짧게 한다. 딱딱한 다나까체(\"~입니다\", \"~합니까\")를 쓴다." },
            new() { Id = "RES_toby", Name = "토비", Job = ResidentJob.Child, Mbti = "ENFP", Values = "모험", GuildOpinion = 40,
                Persona = "모험가를 동경하는 장난꾸러기 꼬마다. 모험담을 졸라 듣고 싶어 하고 이것저것 캐묻는다. 어린아이 말투의 반말을 쓴다." },
        };
    }
}
