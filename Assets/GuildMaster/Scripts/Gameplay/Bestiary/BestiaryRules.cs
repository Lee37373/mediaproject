using System.Collections.Generic;
using GuildMaster.Adventurers;

namespace GuildMaster.Bestiary
{
    /// <summary>
    /// The guild's bestiary and research (P4-107, H4 — Monster Hunter's research levels, a Pokédex; user decisions 2026-10-27).
    /// A species seen is level 1; research points — hours it was in sight, members' kills, monster skills seen, its loot
    /// bought — take it to level 2 (its skills, ground, prey, breeding, and what it is weak and strong against) and level 3
    /// (mastery: members hit it 10% harder and take 5% less from it). From level 2 its quests look less dangerous to the
    /// members (decision ②). Every species is weak to one class and strong against another (decision ③): a fact of the world
    /// whether the guild knows it or not, shown from level 2. Research only comes from watching and fighting (decision ④).
    /// Pure numbers, tuned in play.
    /// </summary>
    public static class BestiaryRules
    {
        // ---- Research points ----
        /// <summary>Per game hour that any of the kind is in the guild's sight.</summary>
        public const int SeenHour = 1;
        /// <summary>Per one of it killed by a member; a boss, region lord or the dragon counts for more.</summary>
        public const int Kill = 3, BossKill = 30;
        /// <summary>Per skill of it seen used (at most once an hour per kind).</summary>
        public const int SkillSeen = 2;
        /// <summary>Per piece of its loot the guild buys.</summary>
        public const int LootBought = 1;

        public const int Level2At = 20, Level3At = 60;
        public const int MaxLevel = 3;

        /// <summary>The research level: 0 never seen, 1 seen, then by the points.</summary>
        public static int Level(int points, bool seen) => !seen ? 0 : points >= Level3At ? 3 : points >= Level2At ? 2 : 1;

        /// <summary>Points to the next level from <paramref name="points"/> (0 at the top).</summary>
        public static int NextAt(int level) => level <= 1 ? Level2At : level == 2 ? Level3At : 0;

        // ---- What it does (decisions ①②) ----
        public const float MasteryDamage = 1.1f, MasteryTaken = 0.95f;
        public const float RiskAtLevel2 = 0.9f, RiskAtLevel3 = 0.85f;

        /// <summary>How much of a quest's risk requirement is left for a kind researched this far.</summary>
        public static float RiskFactor(int level) => level >= 3 ? RiskAtLevel3 : level >= 2 ? RiskAtLevel2 : 1f;

        // ---- Weak and strong (decision ③) ----
        /// <summary>A hit from the class it is weak to does this much; from the class it is strong against, this much.</summary>
        public const float Weak = 1.1f, Strong = 0.9f;

        /// <summary>Each kind's weakness and strength by class: blades chip stone, magic burns jelly, the holy undo the dead...</summary>
        static readonly Dictionary<string, (AdventurerClass weak, AdventurerClass strong)> Matchups = new()
        {
            ["slime"] = (AdventurerClass.Mage, AdventurerClass.Warrior),
            ["stone_slime"] = (AdventurerClass.Warrior, AdventurerClass.Ranger),
            ["skull"] = (AdventurerClass.Priest, AdventurerClass.Ranger),
            ["goblin"] = (AdventurerClass.Ranger, AdventurerClass.Priest),
            ["goblin_warrior"] = (AdventurerClass.Mage, AdventurerClass.Warrior),
            ["slime_king"] = (AdventurerClass.Mage, AdventurerClass.Warrior),
            ["fire_slime"] = (AdventurerClass.Ranger, AdventurerClass.Mage),
            ["bog_slime"] = (AdventurerClass.Mage, AdventurerClass.Ranger),
            ["frost_skull"] = (AdventurerClass.Priest, AdventurerClass.Mage),
            ["sand_goblin"] = (AdventurerClass.Warrior, AdventurerClass.Ranger),
            ["ash_skull"] = (AdventurerClass.Priest, AdventurerClass.Warrior),
            ["lord_desert"] = (AdventurerClass.Mage, AdventurerClass.Ranger),
            ["lord_snow"] = (AdventurerClass.Warrior, AdventurerClass.Mage),
            ["lord_swamp"] = (AdventurerClass.Priest, AdventurerClass.Ranger),
            ["lord_wasteland"] = (AdventurerClass.Ranger, AdventurerClass.Warrior),
            ["dragon"] = (AdventurerClass.Ranger, AdventurerClass.Mage),
        };

        public static IEnumerable<string> SpeciesWithMatchups => Matchups.Keys;

        public static bool TryMatchup(string speciesId, out AdventurerClass weak, out AdventurerClass strong)
        {
            if (speciesId != null && Matchups.TryGetValue(speciesId, out var m))
            {
                (weak, strong) = m;
                return true;
            }
            weak = strong = default;
            return false;
        }

        /// <summary>What a member of this class does to this kind: more if it is weak to it, less if it is strong against it.</summary>
        public static float ClassFactor(string speciesId, AdventurerClass attacker)
        {
            if (!TryMatchup(speciesId, out var weak, out var strong)) return 1f;
            return attacker == weak ? Weak : attacker == strong ? Strong : 1f;
        }

        // ---- Words for the bestiary ----
        /// <summary>How fast its dens fill up again (the species' growth runs from about 0.01 to 0.3 an hour).</summary>
        public static string Breeding(float growthPerHour) => growthPerHour >= 0.18f ? "빠름" : growthPerHour >= 0.05f ? "보통" : "느림";

        /// <summary>Its health at a glance, by the listed number (24–38 for the common kinds); lords and the dragon are beyond.</summary>
        public static string Toughness(float listedHp, bool overlord) => overlord ? "압도적" : listedHp >= 34f ? "튼튼함" : listedHp >= 28f ? "보통" : "약함";
    }
}
