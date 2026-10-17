using GuildMaster.Data;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Monsters
{
    /// <summary>
    /// Monster levels and EXP (user decision 2026-09-22). Every monster has its own level: it is
    /// born higher the farther out its nest lies, and it gains EXP by killing — other monsters or
    /// guild members — and levels up like an adventurer. P4-58 (user decision: "strength is the
    /// level"): the species differ only a little (±30%), so the level carries the strength — it grows
    /// like an adventurer's (HP +12%, attack +10%, defense +0.6 per level), sets the danger shown to
    /// the guild, and every few levels steps up the fighting smarts. Pure, tested.
    /// </summary>
    public static class MonsterLevel
    {
        public const int MaxLevel = 30;
        /// <summary>Levels added at the inner land's edge (the innermost nest site breeds level 1).</summary>
        public const int EdgeBirthBonus = 14;
        /// <summary>
        /// P4-81 (user decision 2026-10-16): levels added again across the new outer land, from the inner
        /// edge (15) to the outermost nest site (25).
        /// </summary>
        public const int OuterBirthBonus = 10;
        /// <summary>Monsters of the same pack this close share a kill's EXP (as adventurers do).</summary>
        public const float ExpShareRadius = 8f;
        /// <summary>Levels per step of combat tier above the species' own.</summary>
        public const int LevelsPerTier = 7;
        /// <summary>Danger shown to the guild: 보통 from this level, 높음 from <see cref="HighDangerLevel"/>.</summary>
        public const int MediumDangerLevel = 5;
        public const int HighDangerLevel = 10;

        /// <summary>
        /// Birth level at a site by its depth (<see cref="Ecosystem.EcosystemManager.Depth"/>): 0 = innermost
        /// nest site .. 1 = the inner land's edge (as the whole 256 map was) .. 2 = outermost nest site.
        /// </summary>
        public static int BirthLevel(float depth)
            => 1 + Mathf.RoundToInt(Mathf.Clamp01(depth) * EdgeBirthBonus) + Mathf.RoundToInt(Mathf.Clamp01(depth - 1f) * OuterBirthBonus);

        /// <summary>EXP to go from this level to the next (adventurers: 30 + 25 per level).</summary>
        public static int ExpToNext(int level) => 20 + 20 * level;

        /// <summary>Per-level growth, the same as an adventurer's (AdventurerStats.Compute).</summary>
        public static float HpScale(int level) => 1f + 0.12f * (level - 1);
        public static float AttackScale(int level) => 1f + 0.10f * (level - 1);
        public static float DefenseBonus(int level) => 0.6f * (level - 1);

        /// <summary>Species stats grown to this level (the monster itself and the guild's estimates share it).</summary>
        public static CombatStats Grow(CombatStats s, int level)
        {
            s.MaxHp *= HpScale(level);
            s.Attack *= AttackScale(level);
            s.Defense += DefenseBonus(level);
            return s;
        }

        /// <summary>EXP a kill of this monster is worth (to adventurers and to monsters alike).</summary>
        public static int ExpWorth(int speciesExp, int level) => Mathf.Max(1, Mathf.RoundToInt(speciesExp * (1f + 0.35f * (level - 1))));

        /// <summary>EXP a monster gets for killing a guild member of this level.</summary>
        public static int ExpForAdventurer(int adventurerLevel) => 8 + 4 * Mathf.Max(1, adventurerLevel);

        /// <summary>
        /// Tier EXP — picked over battlefields — for one step of smarts beyond the level's own.
        /// Measured at 40 first: over 55 game hours with six dens at war, not one monster reached a
        /// step. A fight leaves a handful of materials and the survivors share them, so one cleared
        /// field is worth about ten to whoever picks it over. At 15 that field is most of a step and
        /// two of them are one, which is the pace the rule was meant to have.
        /// </summary>
        public const int TierExpPerStep = 15;
        /// <summary>Scavenging teaches at most this much: it sharpens a pack, it does not remake it.</summary>
        public const int MaxScavengedTierSteps = 2;

        /// <summary>Steps of combat tier a monster has eaten its way to (user decision 2026-09-23).</summary>
        public static int ScavengedTierSteps(int tierExp)
            => Mathf.Clamp(tierExp / TierExpPerStep, 0, MaxScavengedTierSteps);

        /// <summary>The species' own smarts, one step up per <see cref="LevelsPerTier"/> levels.</summary>
        public static CombatTier TierAt(CombatTier species, int level) => TierAt(species, level, 0);

        /// <summary>
        /// As <see cref="TierAt(CombatTier,int)"/>, plus what it learned picking over the leavings of
        /// other fights. A pack left to scavenge an untouched battlefield fights smarter for it, which
        /// is what the guild pays for by not clearing the ground.
        /// </summary>
        public static CombatTier TierAt(CombatTier species, int level, int tierExp)
            => (CombatTier)Mathf.Min((int)CombatTier.Heroic,
                                     (int)species + (level - 1) / LevelsPerTier + ScavengedTierSteps(tierExp));

        /// <summary>How dangerous a monster (or a site's young) of this level is (P4-58: the level, not the species).</summary>
        public static DangerLevel DangerAt(int level)
            => level >= HighDangerLevel ? DangerLevel.High : level >= MediumDangerLevel ? DangerLevel.Medium : DangerLevel.Low;
    }
}
