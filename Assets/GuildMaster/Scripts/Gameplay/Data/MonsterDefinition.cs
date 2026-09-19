using System.Collections.Generic;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Data
{
    public enum DangerLevel { Low, Medium, High }

    public enum MonsterShape { Slime, Beast, Humanoid, Brute }

    [System.Serializable]
    public class LootEntry
    {
        public ItemDefinition item;
        [Range(0f, 1f)] public float chance = 1f;
        public int min = 1;
        public int max = 1;
    }

    /// <summary>What a species does with items lying on the ground (P4-21).</summary>
    public enum MonsterPickupMode { None, Equip, Absorb }

    [CreateAssetMenu(menuName = "GuildMaster/Monster", fileName = "Monster")]
    public class MonsterDefinition : ScriptableObject
    {
        public string id;
        public string displayName;

        [Header("Visual")]
        public MonsterShape shape;
        public Color bodyColor = Color.green;
        public Color accentColor = Color.white;
        public float scale = 1f;

        [Header("Combat")]
        public float maxHp = 20;
        public float attack = 4;
        public float defense;
        public float attackRange = 1.1f;
        public float attackCooldown = 1.2f;
        public float moveSpeed = 2.5f;
        public float sightRadius = 6f;
        public DangerLevel danger;
        [Tooltip("Fighting smarts (P4-26): Instinct for mindless beasts up to Veteran for bosses.")]
        public Units.CombatTier combatTier = Units.CombatTier.Novice;

        [Header("Behaviour")]
        [Tooltip("Attacks guild members on sight. Otherwise only retaliates.")]
        public bool aggressiveToHumans;
        [Tooltip("Runs away from predators instead of fighting.")]
        public bool fleesFromPredators;
        public List<MonsterDefinition> prey = new();
        public List<MonsterDefinition> rivals = new();
        public float wanderRadius = 5f;
        public float leashRadius = 14f;
        [Tooltip("Base territory radius around a nest; scales with how full the nest is (Nest.TerritoryRadius).")]
        public float territoryRadius = 5f;
        public bool canRaid;
        [Tooltip("Raid-capable nests: population from which raids become possible (danger level 1).")]
        public int raidThreshold = 99;
        [Tooltip("Each this many members above the threshold raises the danger level by one.")]
        public int raidStep = 3;
        [Tooltip("Members of a nest move as a flock following a pack leader.")]
        public bool packHunter;
        public List<string> skillIds = new();

        [Header("Ranged variant (e.g. goblin archers)")]
        [Range(0f, 1f)] public float rangedVariantChance;
        public float rangedVariantRange = 4.5f;
        public string rangedVariantName;

        [Header("Art (Resources/Art/Monsters)")]
        public string artKey;
        public string rangedArtKey;

        [Header("Ecosystem")]
        public List<Biome> biomes = new();
        public int nestCapacity = 6;
        [Tooltip("Logistic growth rate per game hour.")]
        public float growthPerHour = 0.05f;
        public int initialPopulation = 3;

        [Header("Rewards")]
        public List<LootEntry> loot = new();
        public int expValue = 5;

        [Header("Items (P4-21)")]
        [Tooltip("Equip: wears weapons/armour/trinkets it finds. Absorb: swallows items for extra HP.")]
        public MonsterPickupMode pickupMode;
        [Tooltip("Gear worn at spawn (chance each); worn gear always drops on death.")]
        public List<LootEntry> startingGear = new();

        /// <summary>True if this species can drop the item (loot table or gear it may spawn with).</summary>
        public bool CanDrop(ItemDefinition item)
            => item != null && (loot.Exists(l => l.item == item) || startingGear.Exists(l => l.item == item));

        public bool IsPredatorOf(MonsterDefinition other) => other != null && prey.Contains(other);
        public bool IsRivalOf(MonsterDefinition other) => other != null && rivals.Contains(other);
    }
}
