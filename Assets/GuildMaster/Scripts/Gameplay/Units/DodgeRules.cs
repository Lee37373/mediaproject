using UnityEngine;

namespace GuildMaster.Units
{
    /// <summary>
    /// How soon a unit standing in a hostile telegraph starts to get out (P4-95, user decision 2026-10-20: "높은 등급일수록 더
    /// 빠르게 반응할 확률이 올라가게"). Every unit used to step out the very frame a marker appeared, so even the dragon's
    /// fire was walked out of nearly every time. Now each unit takes it in once per marker: quickly with a chance that grows
    /// with its combat tier, else only after a moment. Rolled by <see cref="TelegraphService.TryGetEscape"/>, for every
    /// telegraph (the dragon, the region lords, the disaster monsters, a slam).
    /// </summary>
    public static class DodgeRules
    {
        /// <summary>A quick read of the danger.</summary>
        public const float QuickReaction = 0.1f;
        /// <summary>A slow read: somewhere between these.</summary>
        public const float SlowReactionMin = 0.4f, SlowReactionMax = 0.75f;

        /// <summary>The chance of the quick read by tier (adventurers are Heroic from Lv15, see <see cref="CombatTiers.FromLevel"/>).</summary>
        public static float QuickChance(CombatTier tier) => tier switch
        {
            CombatTier.Instinct => 0.1f,
            CombatTier.Novice => 0.25f,
            CombatTier.Skilled => 0.45f,
            CombatTier.Veteran => 0.65f,
            _ => 0.8f,
        };

        /// <summary>Seconds before it moves: <paramref name="roll"/> (0..1) picks quick or slow, <paramref name="spread"/> (0..1) where in the slow band.</summary>
        public static float ReactionDelay(CombatTier tier, float roll, float spread)
            => roll < QuickChance(tier) ? QuickReaction : Mathf.Lerp(SlowReactionMin, SlowReactionMax, Mathf.Clamp01(spread));
    }
}
