using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>Creates random adventurer data (name, class, personality, starting gear).</summary>
    public static class AdventurerGenerator
    {
        static readonly string[] Names =
        {
            "카엘", "리아", "브란", "세라", "도윤", "마르코", "엘라", "토르", "하린", "베일",
            "로웬", "시아", "그렌", "미라", "레오", "아리스", "펠릭스", "노아", "이솔", "카이",
            "루나", "다리안", "벨라", "오스틴", "테오", "제나", "라일", "에이든", "니나", "바론",
        };

        static readonly string[] Mbtis =
        {
            "ISTJ", "ISFJ", "INFJ", "INTJ", "ISTP", "ISFP", "INFP", "INTP",
            "ESTP", "ESFP", "ENFP", "ENTP", "ESTJ", "ESFJ", "ENFJ", "ENTJ",
        };

        public static readonly string[] Values = { "돈", "명예", "동료", "모험", "안전", "지식" };

        static int s_counter;

        public static string NextId() => $"NPC_{++s_counter:000}";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCounter() => s_counter = 0;

        public static AdventurerData Generate(GameRandom rng, int level = 1)
        {
            var d = new AdventurerData
            {
                id = NextId(),
                name = rng.Pick(Names),
                cls = (AdventurerClass)rng.Range(0, 3),
                level = Mathf.Max(1, level),
                gold = rng.Range(20, 70),
                mbti = rng.Pick(Mbtis),
                values = rng.Pick(Values),
                potions = rng.Range(0, 2),
                gearTier = 0,
                fatigue = rng.Range(0f, 20f),
                hairColor = Color.HSVToRGB(rng.Range(0f, 1f), rng.Range(0.3f, 0.8f), rng.Range(0.25f, 0.9f)),
                skinColor = Color.Lerp(new Color(0.98f, 0.84f, 0.70f), new Color(0.55f, 0.38f, 0.26f), rng.Range(0f, 0.8f)),
            };
            return d;
        }
    }
}
