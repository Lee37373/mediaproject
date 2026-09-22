using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Monsters
{
    /// <summary>
    /// Pack bosses (P4-57, user decisions 2026-09-22): every species can have a boss — a bigger,
    /// tougher member with its own name and art that leads the pack. A pack gets one only by a low
    /// chance when it is founded (map start, newcomers from the map edge), and a boss that dies is
    /// never replaced. Pure numbers, tested.
    /// </summary>
    public static class MonsterBoss
    {
        /// <summary>Chance a newly founded pack has a boss.</summary>
        public const float Chance = 0.15f;
        public const float HpMultiplier = 3f;
        public const float AttackMultiplier = 1.5f;
        public const float DefenseBonus = 2f;
        /// <summary>Drawn this much bigger than the species.</summary>
        public const float SizeMultiplier = 1.3f;
        /// <summary>Born this many levels above the rest of the pack.</summary>
        public const int LevelBonus = 3;
        /// <summary>Fights one step smarter than its species.</summary>
        public const int TierBonus = 1;
        /// <summary>The loot table is rolled this many times when it dies.</summary>
        public const int LootRolls = 3;
        public const int ExpMultiplier = 3;

        public static CombatTier SpeciesTier(CombatTier species, bool boss)
            => boss ? (CombatTier)Mathf.Min((int)CombatTier.Heroic, (int)species + TierBonus) : species;

        public static string Name(string speciesName, string bossName)
            => string.IsNullOrEmpty(bossName) ? $"{speciesName} 우두머리" : bossName;
    }
}
