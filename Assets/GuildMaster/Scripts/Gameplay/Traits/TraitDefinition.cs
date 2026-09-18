using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Traits
{
    /// <summary>
    /// A personality/physical trait. Neutral values are 1 for multipliers and 0 for additive
    /// fields. Effects are aggregated by <see cref="TraitSet"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "GuildMaster/Trait", fileName = "Trait")]
    public class TraitDefinition : ScriptableObject, IStatModifier
    {
        [Header("기본")]
        public string id;
        public string displayName;
        [TextArea] public string description;
        public bool isPositive = true;

        [Header("스탯")]
        public float hpMul = 1f;
        public float atkMul = 1f;
        public float defAdd;
        public float speedMul = 1f;
        public float sightAdd;

        [Header("행동")]
        [Tooltip("Added to the HP ratio at which the adventurer retreats (+ = retreats earlier).")]
        public float retreatShift;
        public float fatigueMul = 1f;
        public float expMul = 1f;

        [Header("의뢰 선호")]
        [Tooltip("Multiplier on the gold share of a quest's value.")]
        public float goldGreed = 1f;
        [Tooltip("+ avoids dangerous quests, - seeks them (per danger level).")]
        public float dangerAversion;
        [Tooltip("Multiplier on the value of joining a quest others already took.")]
        public float partyAffinity = 1f;

        [Header("영입")]
        [Tooltip("+ harder to recruit, - easier (used by recruitment dialogue).")]
        public float recruitDifficulty;

        [Header("LLM")]
        [Tooltip("One Korean line describing the trait for prompts.")]
        public string promptHint;

        public List<TraitDefinition> conflicts = new();

        public void Modify(ref CombatStats stats)
        {
            stats.MaxHp *= hpMul;
            stats.Attack *= atkMul;
            stats.Defense += defAdd;
            stats.MoveSpeed *= speedMul;
            stats.SightRadius += sightAdd;
        }

        /// <summary>True if either trait lists the other as a conflict.</summary>
        public bool ConflictsWith(TraitDefinition other)
            => other != null && other != this && (conflicts.Contains(other) || other.conflicts.Contains(this));
    }
}
