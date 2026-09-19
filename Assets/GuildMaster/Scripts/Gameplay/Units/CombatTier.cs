using UnityEngine;

namespace GuildMaster.Units
{
    /// <summary>
    /// How smart a unit fights. Higher tiers retarget more often, give up hopeless chases sooner and
    /// weigh more factors when picking targets (see <see cref="TargetSelector"/>):
    /// Novice nearest/retaliate · Skilled + finish wounded · Veteran + spread out, guard the back line ·
    /// Heroic + focus fire on kills, cover retreating allies, hold area skills for groups.
    /// </summary>
    public enum CombatTier { Novice, Skilled, Veteran, Heroic }

    public static class CombatTiers
    {
        public static string Label(CombatTier t) => t switch
        {
            CombatTier.Novice => "초보",
            CombatTier.Skilled => "숙련",
            CombatTier.Veteran => "베테랑",
            _ => "영웅",
        };

        /// <summary>Level band (1–4, 5–9, 10–14, 15+) plus a bonus from the adventurer's birth grade.</summary>
        public static CombatTier FromLevel(int level, int gradeBonus)
        {
            int band = level >= 15 ? 3 : level >= 10 ? 2 : level >= 5 ? 1 : 0;
            return (CombatTier)Mathf.Clamp(band + gradeBonus, 0, 3);
        }

        /// <summary>Seconds between target re-evaluations during a fight.</summary>
        public static float RetargetInterval(CombatTier t) => t switch
        {
            CombatTier.Novice => 2.5f,
            CombatTier.Skilled => 1.5f,
            CombatTier.Veteran => 1.0f,
            _ => 0.7f,
        };

        /// <summary>Seconds of chasing without closing in before the target is dropped as unreachable.</summary>
        public static float ChaseGiveUp(CombatTier t) => t switch
        {
            CombatTier.Novice => 5f,
            CombatTier.Skilled => 3.5f,
            CombatTier.Veteran => 3f,
            _ => 2.5f,
        };
    }
}
