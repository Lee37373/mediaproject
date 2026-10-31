using UnityEngine;

namespace GuildMaster.Data
{
    /// <summary>Tool added in P4-94 (flares; appended so existing values keep their meaning).</summary>
    public enum ItemCategory { Material, Potion, Gear, Tool }

    /// <summary>Where a monster wears an item (P4-21). None = not wearable (absorbable only).</summary>
    public enum EquipSlot { None, Weapon, Armor, Trinket }

    [CreateAssetMenu(menuName = "GuildMaster/Item", fileName = "Item")]
    public class ItemDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        public ItemCategory category;
        [Min(0)] public int baseValue = 10;
        public Color color = Color.white;

        [Header("Potion")]
        public float healAmount;

        [Header("Gear")]
        public int gearTier;
        public float attackBonus;
        public float defenseBonus;

        [Header("Monster use (P4-21)")]
        public EquipSlot equipSlot;
        public float monsterAttackBonus;
        public float monsterDefenseBonus;
        public float monsterHpBonus;
        [Tooltip("Species-unique effect id (e.g. pack_leader). Unique items are worn, never absorbed.")]
        public string uniqueEffect;

        public bool IsUnique => !string.IsNullOrEmpty(uniqueEffect);
    }

    [System.Serializable]
    public struct ItemStack
    {
        public string itemId;
        public int count;

        public ItemStack(string itemId, int count)
        {
            this.itemId = itemId;
            this.count = count;
        }
    }
}
