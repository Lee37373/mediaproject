using UnityEngine;

namespace GuildMaster.Units
{
    /// <summary>
    /// How smart a unit fights (P4-17, reworked in P4-26). Monsters and adventurers share the rules:
    /// Instinct nearest target, switches only to whoever hits it · Novice + retarget now and then, mild
    /// spreading · Skilled + kiting, spreading, finish the wounded · Veteran + firing positions and cover,
    /// strong spreading, guard the back line · Heroic + focus fire on kills, cover retreating allies,
    /// hold area skills for groups. Adventurers start at Novice; monsters set theirs per species.
    /// </summary>
    public enum CombatTier { Instinct, Novice, Skilled, Veteran, Heroic }

    public static class CombatTiers
    {
        public static string Label(CombatTier t) => t switch
        {
            CombatTier.Instinct => "본능",
            CombatTier.Novice => "초보",
            CombatTier.Skilled => "숙련",
            CombatTier.Veteran => "베테랑",
            _ => "영웅",
        };

        /// <summary>Adventurers: level band (1–4, 5–9, 10–14, 15+) plus the birth grade bonus, Novice to Heroic.</summary>
        public static CombatTier FromLevel(int level, int gradeBonus)
        {
            int band = level >= 15 ? 3 : level >= 10 ? 2 : level >= 5 ? 1 : 0;
            return (CombatTier)Mathf.Clamp((int)CombatTier.Novice + band + gradeBonus, (int)CombatTier.Novice, (int)CombatTier.Heroic);
        }

        /// <summary>
        /// Seconds between target re-evaluations during a fight. Instinct never re-evaluates on a timer
        /// (infinity): it only turns on whoever just hit it (see <see cref="RetaliatesOnly"/>).
        /// </summary>
        public static float RetargetInterval(CombatTier t) => t switch
        {
            CombatTier.Instinct => float.PositiveInfinity,
            CombatTier.Novice => 4f,
            CombatTier.Skilled => 1.5f,
            CombatTier.Veteran => 1.0f,
            _ => 0.7f,
        };

        /// <summary>Keeps its target until something else hits it (no weighing of options).</summary>
        public static bool RetaliatesOnly(CombatTier t) => t == CombatTier.Instinct;

        /// <summary>Seconds of chasing without closing in before the target is dropped as unreachable.</summary>
        public static float ChaseGiveUp(CombatTier t) => t switch
        {
            CombatTier.Instinct => 8f,
            CombatTier.Novice => 5f,
            CombatTier.Skilled => 3.5f,
            CombatTier.Veteran => 3f,
            _ => 2.5f,
        };

        /// <summary>Ranged units step away from melee threats while reloading.</summary>
        public static bool CanKite(CombatTier t) => t >= CombatTier.Skilled;

        /// <summary>
        /// Ranged units search firing spots (line of sight, cover from shooters, away from melee and
        /// telegraphs). Below this they just walk towards the target until they have a shot.
        /// </summary>
        public static bool SeeksFiringPosition(CombatTier t) => t >= CombatTier.Veteran;

        /// <summary>
        /// Target-score penalty for a candidate that <paramref name="crowd"/> allies already attack:
        /// a mild cost per attacker plus a heavy one past the free slots, so a pack splits up instead of
        /// queueing behind one back. Instinct does not spread at all; Novice only a little.
        /// </summary>
        public static float CrowdPenalty(CombatTier t, int crowd)
        {
            if (crowd <= 0) return 0f;
            var (perAttacker, freeSlots) = t switch
            {
                CombatTier.Instinct => (0f, int.MaxValue),
                CombatTier.Novice => (0.4f, 3),
                CombatTier.Skilled => (0.8f, 2),
                _ => (1.5f, 1),
            };
            return crowd * perAttacker + Mathf.Max(0, crowd - freeSlots) * 3f;
        }
    }
}
