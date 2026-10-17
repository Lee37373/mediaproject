using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Data
{
    [Serializable]
    public struct ItemCost
    {
        public string itemId;
        public int count;
    }

    /// <summary>
    /// One building level (index 0 = Lv.1). Effect fields only matter for the buildings that use
    /// them (0 = not applicable). Costs are paid to reach this level from the previous one.
    /// </summary>
    [Serializable]
    public class BuildingLevel
    {
        public int goldCost;
        public List<ItemCost> itemCosts = new();

        [Header("Guild hall")]
        public int rosterCapacity;
        public int visitorLevelBonus;

        [Header("Town hall")]
        public float maxHp;
        public int dailyTax;

        [Header("Training ground")]
        public float idleExpPerHour;

        [Header("Watchtower (P4-13): what a built tower costs and sees")]
        /// <summary>Vision radius of the finished tower; it sees over walls.</summary>
        public float sightRadius;
        /// <summary>Footprint side in cells (square).</summary>
        public int footprint = 2;

        [Header("Blacksmith / apothecary (read by crafting, P4-2)")]
        public int craftTier;
        public float craftSpeed = 1f;

        /// <summary>Player-facing summary of what this level gives.</summary>
        public string effectText;
    }

    /// <summary>Upgrade path of a town building (P4-1). Built in code by DefaultContent.</summary>
    public class BuildingDefinition : ScriptableObject
    {
        public Town.BuildingType type;
        public List<BuildingLevel> levels = new();

        public int MaxLevel => levels.Count;
        public BuildingLevel Level(int level) => levels[Mathf.Clamp(level, 1, levels.Count) - 1];
    }
}
