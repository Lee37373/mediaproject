using System.Collections.Generic;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Quirks a member picks up from what it lives through (P4-104e, G2 — Darkest Dungeon's quirks, Wildermyth's scars): a fear of
    /// the kind that nearly killed it, nerves of steel after enough close calls, the calm of one who lived through the dragon's
    /// fire. Stored as ids on the member (<see cref="AdventurerData.quirks"/>); pure rules, tuned in play.
    /// </summary>
    public static class QuirkRules
    {
        /// <summary>A fear of one kind: "fear:" + its species id.</summary>
        public const string FearPrefix = "fear:";
        public const string SteelNerves = "steel_nerves";
        public const string DragonSurvivor = "dragon_survivor";

        /// <summary>Chance that a close call against a kind leaves a fear of it (once per kind).</summary>
        public const float FearChance = 0.35f;
        /// <summary>Close calls survived after which fear has worn thin (and the fears it had go).</summary>
        public const int SteelNervesAfter = 4;
        /// <summary>Chance that a hard hit from the dragon, lived through, leaves its calm.</summary>
        public const float DragonSurvivorChance = 0.5f;
        /// <summary>A hit from the dragon this hard (its share of the maximum health left after) counts.</summary>
        public const float DragonHardHitBelow = 0.5f;

        /// <summary>Retreat line added while fighting a kind it fears.</summary>
        public const float FearRetreatShift = 0.1f;
        /// <summary>A feared kind this close is felt (a situational thought).</summary>
        public const float FearSightRadius = 9f;

        public static string Fear(string speciesId) => FearPrefix + speciesId;

        public static bool Fears(IReadOnlyList<string> quirks, string speciesId)
        {
            if (quirks == null || string.IsNullOrEmpty(speciesId)) return false;
            string id = Fear(speciesId);
            foreach (var q in quirks) if (q == id) return true;
            return false;
        }

        public static bool Has(IReadOnlyList<string> quirks, string id)
        {
            if (quirks == null) return false;
            foreach (var q in quirks) if (q == id) return true;
            return false;
        }

        /// <summary>
        /// After a close call against a kind (pure): a fear of it, unless it already has one or its nerves are steel — and the
        /// close call that makes the count turns its nerves to steel instead, wiping the fears. Returns the quirk gained, or null.
        /// </summary>
        public static string AfterCloseCall(List<string> quirks, string speciesId, int closeCalls, float roll)
        {
            if (quirks == null) return null;
            if (Has(quirks, SteelNerves)) return null;
            if (closeCalls >= SteelNervesAfter)
            {
                quirks.RemoveAll(q => q.StartsWith(FearPrefix));
                quirks.Add(SteelNerves);
                return SteelNerves;
            }
            if (string.IsNullOrEmpty(speciesId) || Fears(quirks, speciesId) || roll >= FearChance) return null;
            var fear = Fear(speciesId);
            quirks.Add(fear);
            return fear;
        }

        /// <summary>Its name as the windows show it ("해골 공포", "강철 신경", "불의 생존자"); <paramref name="speciesName"/> names a fear.</summary>
        public static string Label(string id, System.Func<string, string> speciesName)
        {
            if (id == SteelNerves) return "강철 신경";
            if (id == DragonSurvivor) return "불의 생존자";
            if (id != null && id.StartsWith(FearPrefix)) return $"{speciesName?.Invoke(id.Substring(FearPrefix.Length)) ?? id.Substring(FearPrefix.Length)} 공포";
            return id;
        }

        public static string Describe(string id, System.Func<string, string> speciesName)
        {
            if (id == SteelNerves) return "죽을 고비를 여러 번 넘겨 두려움이 무뎌졌습니다. 두려운 생각을 절반만 느끼고, 새 공포가 생기지 않습니다.";
            if (id == DragonSurvivor) return "드래곤의 불길을 견뎌 냈습니다. 드래곤을 봐도 겁먹지 않습니다.";
            if (id != null && id.StartsWith(FearPrefix))
                return $"{speciesName?.Invoke(id.Substring(FearPrefix.Length))}에게 죽을 뻔한 뒤로 그 종이 근처에 있으면 기분이 나빠지고, 그 종과 싸울 때 일찍 후퇴합니다.";
            return "";
        }
    }
}
