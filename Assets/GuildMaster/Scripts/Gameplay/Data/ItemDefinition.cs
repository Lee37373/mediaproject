using UnityEngine;

namespace GuildMaster.Data
{
    public enum ItemCategory { Material, Potion, Gear }

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
