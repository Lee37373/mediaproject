using System.Collections.Generic;
using GuildMaster.Adventurers;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>What a guild facility does (P4-87).</summary>
    public enum FacilityEffect { None, Attack, Defense, MaxHp, Poison, ArmorBreak, Slow, StressRelief }

    /// <summary>
    /// The guild's own facilities (P4-87, user decisions 2026-10-16·18·19): seven buildings put up inside the walls. Three
    /// raise every member's attack, defence or health all the time, wherever they are; three mark whatever a member hits
    /// (poison, broken armour, chill) for a few seconds; the dormitory lets members in town shed stress faster (P4-92 —
    /// there is no roster limit any more, user decision 2026-10-19). Members start weaker than
    /// before (<see cref="BaseStatFactor"/>) and the three stat buildings at level 3 bring them back to the old strength.
    /// More of the same kind add up, each less than the one before. Pure numbers and rules, tested.
    /// </summary>
    public static class FacilityRules
    {
        public static readonly BuildingType[] All =
        {
            BuildingType.Armory, BuildingType.ArmorWorkshop, BuildingType.DiningHall,
            BuildingType.PoisonLab, BuildingType.RuneForge, BuildingType.FrostAltar, BuildingType.Dormitory,
        };

        public static bool IsFacility(BuildingType t) => EffectOf(t) != FacilityEffect.None;

        public static FacilityEffect EffectOf(BuildingType t) => t switch
        {
            BuildingType.Armory => FacilityEffect.Attack,
            BuildingType.ArmorWorkshop => FacilityEffect.Defense,
            BuildingType.DiningHall => FacilityEffect.MaxHp,
            BuildingType.PoisonLab => FacilityEffect.Poison,
            BuildingType.RuneForge => FacilityEffect.ArmorBreak,
            BuildingType.FrostAltar => FacilityEffect.Slow,
            BuildingType.Dormitory => FacilityEffect.StressRelief,
            _ => FacilityEffect.None,
        };

        public static bool IsStat(FacilityEffect e) => e is FacilityEffect.Attack or FacilityEffect.Defense or FacilityEffect.MaxHp;
        public static bool IsAffliction(FacilityEffect e) => e is FacilityEffect.Poison or FacilityEffect.ArmorBreak or FacilityEffect.Slow;

        // ---------------- Stats ----------------

        /// <summary>
        /// What an adventurer's attack, defence and health are worth with no stat building (user decision 2026-10-18: without
        /// facilities a party of four beats a pack of five of its level but is badly hurt, one alone cannot). Tuned by trial
        /// fights (<c>DebugCommands.Trial</c>).
        /// </summary>
        public const float BaseStatFactor = 0.75f;

        /// <summary>
        /// One stat building at this level adds this share of the weakened stat; level 3 makes up the whole cut, so the three
        /// at level 3 are exactly the old strength (user decision 2026-10-19).
        /// </summary>
        public static float StatBonusAt(int level) => (1f / BaseStatFactor - 1f) * Mathf.Clamp(level, 0, MaxLevel) / MaxLevel;

        public const int MaxLevel = 3;

        // ---------------- Afflictions ----------------

        /// <summary>How long a mark lasts; another hit only starts the time again (user decision 2026-10-19).</summary>
        public const float AfflictionSeconds = 4f;

        /// <summary>Poison: this share of the hit over <see cref="AfflictionSeconds"/>; armour: this share off; chill: this share off move and attack speed.</summary>
        public static float AfflictionAt(int level) => 0.1f * Mathf.Clamp(level, 0, MaxLevel);

        /// <summary>The strongest a mark gets (three of a kind at level 3 come to 52.5%, so this is only a guard).</summary>
        public const float MaxAffliction = 0.6f;

        // ---------------- Dormitory ----------------

        /// <summary>
        /// How much faster members in town shed stress, resting or not, with the dormitory at this level (P4-92, user decision
        /// 2026-10-19): ×1.5, ×2, ×2.5; 1 with none standing. Out of town and by a campfire it does nothing.
        /// </summary>
        public static float StressReliefAt(int level) => 1f + 0.5f * Mathf.Clamp(level, 0, MaxLevel);

        // ---------------- More than one of a kind ----------------

        /// <summary>Most of one kind (the dormitory: one only, user decision 2026-10-19).</summary>
        public static int MaxCopies(BuildingType t) => t == BuildingType.Dormitory ? 1 : CopyWeights.Length;

        /// <summary>
        /// The strongest of a kind counts whole, the next half, the third a quarter (user decision 2026-10-19: they add up but
        /// each less).
        /// </summary>
        public static readonly float[] CopyWeights = { 1f, 0.5f, 0.25f };

        /// <summary>Each further one of a kind costs this much more (all its levels), so the second costs ×1.5 and the third ×2.25.</summary>
        public const float CopyCostFactor = 1.5f;

        public static float CopyCost(int earlierCopies) => Mathf.Pow(CopyCostFactor, Mathf.Max(0, earlierCopies));

        /// <summary>A cost scaled for the n-th of a kind (rounded up to 5 gold / a whole item).</summary>
        public static int ScaledGold(int gold, int earlierCopies) => Mathf.CeilToInt(gold * CopyCost(earlierCopies) / 5f) * 5;
        public static int ScaledCount(int count, int earlierCopies) => Mathf.CeilToInt(count * CopyCost(earlierCopies));

        /// <summary>
        /// The sum of a kind's standing buildings, strongest first: <paramref name="perLevel"/> of each level times its
        /// weight. Levels of fallen ones are left out by the caller.
        /// </summary>
        public static float Combined(List<int> levels, System.Func<int, float> perLevel)
        {
            if (levels == null || levels.Count == 0) return 0f;
            levels.Sort((a, b) => b.CompareTo(a));
            float sum = 0f;
            for (int i = 0; i < levels.Count && i < CopyWeights.Length; i++) sum += perLevel(levels[i]) * CopyWeights[i];
            return sum;
        }

        /// <summary>What a member's stats are worth with these stat buildings standing (levels per kind).</summary>
        public static StatScale MemberScale(List<int> armory, List<int> armor, List<int> dining)
            => new(BaseStatFactor * (1f + Combined(armory, StatBonusAt)),
                   BaseStatFactor * (1f + Combined(armor, StatBonusAt)),
                   BaseStatFactor * (1f + Combined(dining, StatBonusAt)));

        /// <summary>Anyone not of the guild (visitors, candidates, refugees): weakened, no buildings.</summary>
        public static StatScale Unbuilt => new(BaseStatFactor, BaseStatFactor, BaseStatFactor);

        /// <summary>A mark's strength with these buildings of its kind standing.</summary>
        public static float AfflictionOf(List<int> levels) => Mathf.Min(MaxAffliction, Combined(levels, AfflictionAt));

        /// <summary>One level's effect as the build and building windows show it ("공격력 +11%", "맞은 적 중독: 피해의 10%를 4초에 걸쳐").</summary>
        public static string EffectText(BuildingType t, int level)
        {
            int pct = Mathf.RoundToInt(StatBonusAt(level) * 100f);
            int share = Mathf.RoundToInt(AfflictionAt(level) * 100f);
            string secs = $"{AfflictionSeconds:0}초";
            return EffectOf(t) switch
            {
                FacilityEffect.Attack => $"길드원 공격력 +{pct}%",
                FacilityEffect.Defense => $"길드원 방어 +{pct}%",
                FacilityEffect.MaxHp => $"길드원 최대 체력 +{pct}%",
                FacilityEffect.Poison => $"길드원이 때린 적 중독: 준 피해의 {share}%를 {secs}에 걸쳐",
                FacilityEffect.ArmorBreak => $"길드원이 때린 적 방어 -{share}% ({secs})",
                FacilityEffect.Slow => $"길드원이 때린 적 이동·공격 속도 -{share}% ({secs})",
                FacilityEffect.StressRelief => $"마을에 있는 길드원 스트레스 회복 ×{StressReliefAt(level):0.#}",
                _ => "",
            };
        }
    }
}
