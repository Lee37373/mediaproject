using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Monsters
{
    /// <summary>
    /// What one monster wears and has swallowed (P4-21). Humanoids (Equip) wear one item per slot;
    /// slimes (Absorb) swallow items for extra max HP up to a cap; any species that picks things up
    /// wears a unique item (the crown). The rules are static and pure so they can be tested.
    /// </summary>
    public class MonsterEquipment
    {
        /// <summary>Absorbed HP is capped at this share of the species' base max HP.</summary>
        public const float AbsorbCapShare = 1f;
        public const string PackLeaderEffect = "pack_leader";

        readonly Dictionary<EquipSlot, ItemDefinition> _worn = new();

        public float AbsorbedHp { get; private set; }
        public IEnumerable<ItemDefinition> Worn => _worn.Values;
        public int WornCount => _worn.Count;

        public ItemDefinition InSlot(EquipSlot slot) => _worn.TryGetValue(slot, out var i) ? i : null;

        public bool Has(string uniqueEffect)
        {
            foreach (var i in _worn.Values) if (i.uniqueEffect == uniqueEffect) return true;
            return false;
        }

        public void Clear()
        {
            _worn.Clear();
            AbsorbedHp = 0f;
        }

        /// <summary>Wears the item; returns what it replaced in that slot (to drop), or null.</summary>
        public ItemDefinition Equip(ItemDefinition item)
        {
            if (item == null || item.equipSlot == EquipSlot.None) return null;
            _worn.TryGetValue(item.equipSlot, out var old);
            _worn[item.equipSlot] = item;
            return old;
        }

        /// <summary>Swallows the item; returns the HP actually gained (0 at the cap).</summary>
        public float Absorb(ItemDefinition item, float cap)
        {
            float gain = Mathf.Min(AbsorbHp(item), Mathf.Max(0f, cap - AbsorbedHp));
            AbsorbedHp += gain;
            return gain;
        }

        /// <summary>Adds worn bonuses and absorbed HP to the stats.</summary>
        public void Apply(ref CombatStats stats)
        {
            foreach (var i in _worn.Values)
            {
                stats.Attack += i.monsterAttackBonus;
                stats.Defense += i.monsterDefenseBonus;
                stats.MaxHp += i.monsterHpBonus;
            }
            stats.MaxHp += AbsorbedHp;
        }

        // ---------------- Rules (pure) ----------------

        /// <summary>How good an item is to wear (compares items in the same slot).</summary>
        public static float Score(ItemDefinition i)
            => i == null ? 0f : i.monsterAttackBonus + i.monsterDefenseBonus * 1.2f + i.monsterHpBonus * 0.15f + (i.IsUnique ? 100f : 0f);

        /// <summary>HP a slime gains from swallowing the item (half its value, at least 1).</summary>
        public static float AbsorbHp(ItemDefinition i) => i == null ? 0f : Units.Combat.ScaledHp(Mathf.Max(1f, i.baseValue * 0.5f));

        /// <summary>
        /// Would a monster with this pickup mode want the item, given what it wears in that slot and
        /// how much it has absorbed? Unique items are worn by any picker (never swallowed); Equip
        /// takes strict upgrades for its slot; Absorb swallows anything else until the cap.
        /// </summary>
        public static bool Wants(MonsterPickupMode mode, ItemDefinition item, ItemDefinition wornInSlot, float absorbed, float absorbCap)
        {
            if (item == null || mode == MonsterPickupMode.None) return false;
            if (item.IsUnique) return item.equipSlot != EquipSlot.None && (wornInSlot == null || !wornInSlot.IsUnique);
            return mode switch
            {
                MonsterPickupMode.Equip => item.equipSlot != EquipSlot.None && Score(item) > Score(wornInSlot),
                MonsterPickupMode.Absorb => absorbed < absorbCap,
                _ => false,
            };
        }

        /// <summary>
        /// What a dead monster leaves on the ground: everything it wore (always) plus its loot
        /// table rolled <paramref name="rolls"/> times (Q-C: all loot drops to the ground, whoever the killer).
        /// </summary>
        public static List<ItemStack> BuildDrops(MonsterDefinition def, IEnumerable<ItemDefinition> worn, GameRandom rng, List<ItemStack> into = null, int rolls = 1)
        {
            var list = into ?? new List<ItemStack>();
            list.Clear();
            if (worn != null)
                foreach (var i in worn) if (i != null) list.Add(new ItemStack(i.id, 1));
            if (def == null) return list;
            // Bosses roll the table several times (P4-57).
            for (int roll = 0; roll < rolls; roll++)
                foreach (var entry in def.loot)
                {
                    if (entry.item == null || !rng.Chance(entry.chance)) continue;
                    list.Add(new ItemStack(entry.item.id, rng.Range(entry.min, entry.max + 1)));
                }
            return list;
        }
    }
}
