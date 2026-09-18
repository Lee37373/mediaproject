using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Data;
using GuildMaster.Quests;
using UnityEngine;

namespace GuildMaster.Traits
{
    /// <summary>Aggregated effect of an adventurer's traits (stats, behaviour, quest preference).</summary>
    public class TraitSet
    {
        public const float BaseRetreatThreshold = 0.35f;

        readonly List<TraitDefinition> _traits;

        public TraitSet(IEnumerable<TraitDefinition> traits)
        {
            _traits = traits?.Where(t => t != null).ToList() ?? new List<TraitDefinition>();
            float retreat = BaseRetreatThreshold;
            foreach (var t in _traits)
            {
                retreat += t.retreatShift;
                FatigueMultiplier *= t.fatigueMul;
                ExpMultiplier *= t.expMul;
                GoldGreed *= t.goldGreed;
                DangerAversion += t.dangerAversion;
                PartyAffinity *= t.partyAffinity;
                RecruitDifficulty += t.recruitDifficulty;
            }
            RetreatThreshold = Mathf.Clamp(retreat, 0.1f, 0.7f);
        }

        /// <summary>Resolves trait ids through the database; unknown ids are ignored.</summary>
        public static TraitSet FromIds(IEnumerable<string> ids, GameDatabase db)
            => new(ids == null || db == null ? null : ids.Select(db.Trait));

        public IReadOnlyList<TraitDefinition> Traits => _traits;
        public IEnumerable<IStatModifier> Modifiers => _traits;
        public float RetreatThreshold { get; }
        public float FatigueMultiplier { get; } = 1f;
        public float ExpMultiplier { get; } = 1f;
        public float GoldGreed { get; } = 1f;
        public float DangerAversion { get; }
        public float PartyAffinity { get; } = 1f;
        public float RecruitDifficulty { get; }

        public IEnumerable<string> PromptHints => _traits.Where(t => !string.IsNullOrEmpty(t.promptHint)).Select(t => t.promptHint);

        /// <summary>
        /// Personality adjustment of a quest's utility: greedy traits weigh gold more, brave/cowardly
        /// traits shift value per danger level, social/loner traits scale joining an existing party.
        /// </summary>
        public float AdjustQuestValue(Quest quest, float value)
        {
            if (quest == null || _traits.Count == 0) return value;
            int partySize = quest.Assignees.Count + 1;
            value += (GoldGreed - 1f) * quest.RewardGold / partySize;
            value *= Mathf.Max(0.1f, 1f - DangerAversion * 0.25f * (int)quest.Danger);
            if (quest.Assignees.Count > 0) value *= PartyAffinity;
            return Mathf.Max(0f, value);
        }

        /// <summary>Rich-text list for UI: positive traits green, negative red.</summary>
        public static string Format(IEnumerable<TraitDefinition> traits, Color positive, Color negative)
        {
            var parts = traits.Where(t => t != null)
                .Select(t => $"<color=#{ColorUtility.ToHtmlStringRGB(t.isPositive ? positive : negative)}>{t.displayName}</color>")
                .ToList();
            return parts.Count == 0 ? "-" : string.Join(", ", parts);
        }
    }
}
