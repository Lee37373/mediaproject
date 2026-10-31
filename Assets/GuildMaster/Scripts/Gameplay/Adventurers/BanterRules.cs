using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// What small talk does between two (P4-104e, user decision 2026-10-21 "잡담 호감": by the rules, small changes — every talk
    /// +1~2, a quarrel down, their natures matter, a chat with a visitor warms it a little to the guild). Pure; the talk itself is
    /// <see cref="BanterService"/>.
    /// </summary>
    public static class BanterRules
    {
        /// <summary>A friendly talk: +1 each way, +2 for the like-minded (same values) or by a campfire.</summary>
        public const int Friendly = 1, KindredBonus = 1;
        /// <summary>A quarrel: −2 each way, −3 with an abrasive one in it (arrogant, a loner).</summary>
        public const int Quarrel = -2, AbrasiveExtra = -1;
        /// <summary>What a talk with a member does to a visitor's warmth to the guild (recruitment affinity, applied at the next talk).</summary>
        public const int VisitorWarmth = 2, VisitorChill = -2;

        /// <summary>
        /// Odds that a talk in town turns into a quarrel (pure): seldom between friends, often between those who dislike each other,
        /// more with an abrasive one, less between the like-minded. Talks by a campfire are never quarrels — out there they stick together.
        /// </summary>
        public static float QuarrelChance(int relation, bool sameValues, bool abrasive)
        {
            float p = 0.08f;
            if (relation < 0) p += 0.25f;
            if (relation <= -40) p += 0.2f;
            if (abrasive) p += 0.12f;
            if (sameValues) p -= 0.05f;
            if (relation >= Welfare.StressRules.FriendRelation) p -= 0.05f;
            return Mathf.Clamp01(p);
        }

        /// <summary>How much a friendly talk raises what each feels for the other (pure).</summary>
        public static int FriendlyChange(bool sameValues, bool byTheFire) => Friendly + (sameValues || byTheFire ? KindredBonus : 0);

        /// <summary>How much a quarrel lowers it (pure).</summary>
        public static int QuarrelChange(bool abrasive) => Quarrel + (abrasive ? AbrasiveExtra : 0);
    }
}
