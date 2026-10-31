using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Monsters
{
    /// <summary>
    /// Rare variants (P4-85, user decision 2026-10-18): a sudden incident brings out one shining member of a kind
    /// that lives where it appears — a name of its own, born well above the land's level, much tougher — that
    /// lingers alone for a few hours and then is gone. It never joins a pack and never breeds. Pure numbers, tested.
    /// </summary>
    public static class RareMonster
    {
        /// <summary>Born this many levels above what the land there breeds (capped at the ordinary maximum).</summary>
        public const int LevelBonus = 5;
        public const float HpMultiplier = 6f;
        public const float AttackMultiplier = 2f;
        public const float DefenseBonus = 2f;
        /// <summary>Drawn this much bigger than its kind.</summary>
        public const float SizeMultiplier = 1.25f;
        /// <summary>Fights one step smarter than its kind (as a pack boss).</summary>
        public const int TierBonus = 1;
        public const int ExpMultiplier = 6;

        /// <summary>The names a variant goes by, with the shine it is drawn in (index = <see cref="Monster.RareVariant"/>, saved).</summary>
        static readonly (string prefix, Color tint)[] Variants =
        {
            ("황금", new Color(1f, 0.84f, 0.3f)),
            ("백은", new Color(0.86f, 0.92f, 1f)),
            ("흑요석", new Color(0.55f, 0.45f, 0.75f)),
            ("진홍", new Color(1f, 0.45f, 0.42f)),
            ("벽옥", new Color(0.45f, 1f, 0.7f)),
        };

        public static int VariantCount => Variants.Length;

        static int Clamp(int variant) => Mathf.Clamp(variant, 0, Variants.Length - 1);

        /// <summary>"황금 고블린 전사".</summary>
        public static string Name(int variant, string speciesName) => $"{Variants[Clamp(variant)].prefix} {speciesName}";

        public static Color Tint(int variant) => Variants[Clamp(variant)].tint;

        public static int Level(int landLevel) => Mathf.Min(MonsterLevel.MaxLevel, Mathf.Max(1, landLevel) + LevelBonus);

        public static CombatTier SpeciesTier(CombatTier species) => (CombatTier)Mathf.Min((int)CombatTier.Heroic, (int)species + TierBonus);

        // ---------------- Spoils ----------------

        /// <summary>The trophy every rare variant drops when killed (a material the guild sells).</summary>
        public const string TrophyId = "rare_essence";
        /// <summary>Level from which it carries tier-3 gear instead of tier-2.</summary>
        public const int TopGearLevel = 15;

        public static int GearTier(int level) => level >= TopGearLevel ? 3 : 2;

        /// <summary>What a killed rare variant certainly drops, on top of its kind's loot table: its trophy and a piece of gear.</summary>
        public static void AddSpoils(int level, System.Collections.Generic.List<Data.ItemStack> loot)
        {
            loot.Add(new Data.ItemStack(TrophyId, 1));
            loot.Add(new Data.ItemStack($"gear_t{GearTier(level)}", 1));
        }
    }
}
