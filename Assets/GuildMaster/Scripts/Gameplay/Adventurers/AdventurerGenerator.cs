using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Traits;
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

        /// <summary>A name no current guild member has (falls back to any name when all are taken).</summary>
        static string PickUnusedName(GameRandom rng)
        {
            if (!Services.TryGet<GuildRoster>(out var roster)) return rng.Pick(Names);
            var free = new List<string>(Names);
            foreach (var m in roster.Members) free.Remove(m.Data.name);
            return free.Count > 0 ? rng.Pick(free) : rng.Pick(Names);
        }

        /// <summary>
        /// Classes of the founding members, in order (P4-38): the guild starts with one party that
        /// has a front line, a back line and a healer instead of whatever three the dice gave.
        /// </summary>
        public static readonly AdventurerClass[] StartingLineup =
            { AdventurerClass.Warrior, AdventurerClass.Ranger, AdventurerClass.Priest, AdventurerClass.Mage };

        /// <param name="traitPool">Traits to roll from; defaults to the registered GameDatabase.</param>
        public static AdventurerData Generate(GameRandom rng, int level = 1, IReadOnlyList<TraitDefinition> traitPool = null)
        {
            var d = new AdventurerData
            {
                id = NextId(),
                name = PickUnusedName(rng),
                cls = AdventurerData.RollClass(rng.Value),
                grade = AdventurerData.RollGrade(rng.Range(0f, 1f)),
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
            traitPool ??= Services.TryGet<GameDatabase>(out var db) ? db.traits : null;
            foreach (var t in TraitRoller.Roll(rng, traitPool)) d.traitIds.Add(t.id);
            return d;
        }
    }
}
