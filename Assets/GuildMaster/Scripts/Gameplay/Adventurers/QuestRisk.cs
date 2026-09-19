using GuildMaster.Data;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// How risky a quest is for a given strength (pure). One formula shared by the adventurers'
    /// decisions (risk_for_me) and the quest board warning (P4-11), so what the player is told matches
    /// what the adventurers will think.
    /// </summary>
    public static class QuestRisk
    {
        public const string Easy = "여유";
        public const string Fair = "적당";
        public const string Risky = "위험";
        public const string Reckless = "무모";

        /// <summary>Party strength (leader power × 1.6 per extra member) over the danger's requirement.</summary>
        public static float Margin(float leaderPower, int partySize, DangerLevel danger)
            => leaderPower * (1f + 0.6f * (Mathf.Max(1, partySize) - 1)) / RuleBasedDecisionMaker.DangerRequirement(danger);

        public static string Label(float margin)
            => margin >= 2f ? Easy : margin >= 1f ? Fair : margin >= 0.8f ? Risky : Reckless;

        /// <summary>Lowest level at which one adventurer with this gear tier reaches "적당" alone.</summary>
        public static int SoloLevel(DangerLevel danger, int gearTier)
            => Mathf.Max(1, Mathf.CeilToInt(RuleBasedDecisionMaker.DangerRequirement(danger) - gearTier * 1.5f));

        /// <summary>Smallest party (up to <paramref name="maxParty"/>) led by this power that reaches "적당"; 0 if none.</summary>
        public static int PartyNeeded(float leaderPower, DangerLevel danger, int maxParty = 4)
        {
            for (int size = 1; size <= maxParty; size++)
                if (Margin(leaderPower, size, danger) >= 1f) return size;
            return 0;
        }
    }
}
