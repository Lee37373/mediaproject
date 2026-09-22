using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Monsters
{
    /// <summary>
    /// Monster levels and EXP (user decision 2026-09-22). Every monster has its own level: it is
    /// born higher the farther out its nest lies, and it gains EXP by killing — other monsters or
    /// guild members — and levels up like an adventurer. Higher levels mean more HP and attack,
    /// more EXP for whoever kills it, and every few levels a step up in fighting smarts. Pure, tested.
    /// </summary>
    public static class MonsterLevel
    {
        public const int MaxLevel = 20;
        /// <summary>Levels added at the outermost nest site (the innermost one breeds level 1).</summary>
        public const int EdgeBirthBonus = 4;
        /// <summary>Monsters of the same pack this close share a kill's EXP (as adventurers do).</summary>
        public const float ExpShareRadius = 8f;
        /// <summary>Levels per step of combat tier above the species' own.</summary>
        public const int LevelsPerTier = 5;

        /// <summary>Birth level at a site, 0 = innermost .. 1 = outermost nest distance.</summary>
        public static int BirthLevel(float distance01) => 1 + Mathf.RoundToInt(Mathf.Clamp01(distance01) * EdgeBirthBonus);

        /// <summary>EXP to go from this level to the next (adventurers: 30 + 25 per level).</summary>
        public static int ExpToNext(int level) => 20 + 20 * level;

        public static float HpScale(int level) => 1f + 0.12f * (level - 1);
        public static float AttackScale(int level) => 1f + 0.08f * (level - 1);

        /// <summary>EXP a kill of this monster is worth (to adventurers and to monsters alike).</summary>
        public static int ExpWorth(int speciesExp, int level) => Mathf.Max(1, Mathf.RoundToInt(speciesExp * (1f + 0.25f * (level - 1))));

        /// <summary>EXP a monster gets for killing a guild member of this level.</summary>
        public static int ExpForAdventurer(int adventurerLevel) => 8 + 4 * Mathf.Max(1, adventurerLevel);

        /// <summary>The species' own smarts, one step up per <see cref="LevelsPerTier"/> levels.</summary>
        public static CombatTier TierAt(CombatTier species, int level)
            => (CombatTier)Mathf.Min((int)CombatTier.Heroic, (int)species + (level - 1) / LevelsPerTier);
    }
}
