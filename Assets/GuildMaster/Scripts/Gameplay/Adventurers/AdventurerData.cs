using System;
using System.Collections.Generic;
using GuildMaster.Data;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    public enum AdventurerClass { Warrior, Ranger, Mage }

    /// <summary>Birth grade (fixed): adds to the level band when deciding combat smarts (CombatTier).</summary>
    public enum AdventurerGrade { Common, Rare, Heroic }

    /// <summary>Serializable state of an adventurer (separate from the MonoBehaviour for saving/LLM context).</summary>
    [Serializable]
    public class AdventurerData
    {
        public string id;
        public string name;
        public AdventurerClass cls;
        public AdventurerGrade grade;
        public int level = 1;
        public int exp;
        public int gold;
        [Range(0, 100)] public float fatigue;
        [Range(0, 100)] public float stress;
        public string mbti;
        public string values;
        public List<string> traitIds = new();
        public List<MemoryEntry> memories = new();
        /// <summary>How this adventurer feels about other members (see Relationships).</summary>
        public List<RelationEntry> relations = new();
        /// <summary>Conversation history (recruit talks, small talk), see DialogueLog.</summary>
        public List<DialogueLine> dialogue = new();
        /// <summary>Recruitment talk state kept between talks (-1 = never talked).</summary>
        public int recruitAffinity = -1;
        public int recruitTurns;
        public int potions;
        public int gearTier;
        public List<ItemStack> loot = new();
        public int kills;
        public int questsCompleted;
        /// <summary>F-6: game hour until which the member is badly wounded and takes no quest (0 = not wounded).</summary>
        public float woundedUntilHour;
        /// <summary>F-6: game hour of the last lethal blow survived (the cooldown before the next one counts from here).</summary>
        public float lastStandHour = -999f;
        public Color hairColor;
        public Color skinColor;

        public int ExpToNextLevel => 60 + level * 40;

        public int LootCount
        {
            get
            {
                int n = 0;
                foreach (var s in loot) n += s.count;
                return n;
            }
        }

        public void AddLoot(string itemId, int count)
        {
            for (int i = 0; i < loot.Count; i++)
            {
                if (loot[i].itemId != itemId) continue;
                var s = loot[i];
                s.count += count;
                loot[i] = s;
                return;
            }
            loot.Add(new ItemStack(itemId, count));
        }

        public static string ClassLabel(AdventurerClass c) => c switch
        {
            AdventurerClass.Warrior => "전사",
            AdventurerClass.Ranger => "궁수",
            AdventurerClass.Mage => "마법사",
            _ => c.ToString(),
        };

        public static string GradeLabel(AdventurerGrade g) => g switch
        {
            AdventurerGrade.Rare => "희귀",
            AdventurerGrade.Heroic => "영웅",
            _ => "일반",
        };

        /// <summary>Common 70%, Rare 25%, Heroic 5%.</summary>
        public static AdventurerGrade RollGrade(float roll01)
            => roll01 < 0.05f ? AdventurerGrade.Heroic : roll01 < 0.30f ? AdventurerGrade.Rare : AdventurerGrade.Common;
    }

    /// <summary>Anything that tweaks an adventurer's combat stats (traits, buffs).</summary>
    public interface IStatModifier
    {
        void Modify(ref CombatStats stats);
    }

    public static class AdventurerStats
    {
        public static CombatStats Base(AdventurerClass cls) => cls switch
        {
            AdventurerClass.Warrior => new CombatStats { MaxHp = 60, Attack = 9, Defense = 4, AttackRange = 1.2f, AttackCooldown = 1.0f, MoveSpeed = 3.2f, SightRadius = 7f },
            AdventurerClass.Ranger => new CombatStats { MaxHp = 42, Attack = 8, Defense = 2, AttackRange = 5f, AttackCooldown = 1.2f, MoveSpeed = 3.4f, SightRadius = 9f },
            _ => new CombatStats { MaxHp = 36, Attack = 13, Defense = 1, AttackRange = 4.5f, AttackCooldown = 1.6f, MoveSpeed = 3.0f, SightRadius = 8f },
        };

        public static CombatStats Compute(AdventurerData d, IEnumerable<IStatModifier> modifiers)
        {
            var s = Base(d.cls);
            float lv = d.level - 1;
            s.MaxHp *= 1f + 0.12f * lv;
            s.Attack *= 1f + 0.10f * lv;
            s.Defense += 0.6f * lv;
            s.Attack += 3f * d.gearTier;
            s.Defense += 2f * d.gearTier;
            if (modifiers != null) foreach (var m in modifiers) m.Modify(ref s);
            s.MaxHp = Mathf.Max(1f, Mathf.Round(s.MaxHp));
            s.Attack = Mathf.Max(1f, s.Attack);
            s.Defense = Mathf.Max(0f, s.Defense);
            s.MoveSpeed = Mathf.Max(0.5f, s.MoveSpeed);
            s.SightRadius = Mathf.Max(2f, s.SightRadius);
            return s;
        }

        /// <summary>Rough combat power used by decision heuristics.</summary>
        public static float Power(AdventurerData d) => d.level + d.gearTier * 1.5f;
    }
}
