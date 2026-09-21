using UnityEngine;

namespace GuildMaster.Legacy
{
    /// <summary>
    /// What a guild member's death leaves behind and the narrow escapes before it (F-6): relics, memorials,
    /// the last stand and the wound that follows. Pure numbers and rules with no Unity objects, so they are
    /// covered by EditMode tests. Every value here is meant to be tuned in play (balance = P5-1).
    /// </summary>
    public static class LegacyRules
    {
        // ---------------- Relics ----------------

        /// <summary>How long what a dead member dropped lies on the ground (same as the ground items' lifetime).</summary>
        public const float RelicHours = Items.GroundItemService.LifetimeHours;
        /// <summary>A member sent to fetch relics must have at least this share of health.</summary>
        public const float RetrieverMinHp = 0.7f;
        /// <summary>Game hours before a retrieval trip is given up.</summary>
        public const float RetrievalHours = 8f;
        /// <summary>Stress relief for the friends of the dead when one of their relics is brought back.</summary>
        public const float RelicRecoveredFriendRelief = 12f;
        /// <summary>Records of the fallen kept (oldest dropped beyond this).</summary>
        public const int MaxRecords = 30;

        // ---------------- Memorial ----------------

        public const int MemorialBaseCost = 40;
        public const int MemorialCostPerLevel = 10;
        /// <summary>Stress relief for every member at the memorial...</summary>
        public const float MemorialRelief = 8f;
        /// <summary>...and for those who counted the dead as a friend (instead of, not on top of, the above).</summary>
        public const float MemorialFriendRelief = 15f;

        public static int MemorialCost(int level) => MemorialBaseCost + MemorialCostPerLevel * Mathf.Max(1, level);

        // ---------------- Last stand ----------------

        /// <summary>Chance to survive a lethal blow at level 1.</summary>
        public const float LastStandBase = 0.25f;
        public const float LastStandPerLevel = 0.02f;
        public const float LastStandMaxLevelBonus = 0.15f;
        /// <summary>Members who fight for honour or for their comrades hold on a little more often.</summary>
        public const float LastStandSpiritBonus = 0.05f;
        public const float LastStandMaxChance = 0.5f;
        /// <summary>Game hours before the same member can be saved by a last stand again.</summary>
        public const float LastStandCooldownHours = 48f;
        /// <summary>Real seconds after the last stand in which the member cannot be hurt any more, so it can get away.</summary>
        public const float GraceSeconds = 3f;
        /// <summary>Stress the brush with death adds.</summary>
        public const float LastStandStress = 20f;

        /// <summary>Chance of surviving a lethal blow (0..<see cref="LastStandMaxChance"/>).</summary>
        public static float LastStandChance(int level, string values)
        {
            float chance = LastStandBase + Mathf.Min(LastStandMaxLevelBonus, LastStandPerLevel * Mathf.Max(0, level - 1));
            if (values == "명예" || values == "동료") chance += LastStandSpiritBonus;
            return Mathf.Clamp(chance, 0f, LastStandMaxChance);
        }

        public static bool CanLastStand(float now, float lastStandHour) => now - lastStandHour >= LastStandCooldownHours;

        // ---------------- The wound after it ----------------

        /// <summary>Game hours the member is badly wounded and takes no quest.</summary>
        public const float WoundedHours = 12f;
        public const int TreatBaseCost = 30;
        public const int TreatCostPerLevel = 5;
        /// <summary>Treatment brings the member back to at least this share of health.</summary>
        public const float TreatHpRatio = 0.7f;

        public static int TreatCost(int level) => TreatBaseCost + TreatCostPerLevel * Mathf.Max(1, level);
    }
}
