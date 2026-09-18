using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Quests;

namespace GuildMaster.Traits
{
    /// <summary>
    /// Aggregated effect of an adventurer's traits. Phase 2 provides neutral defaults;
    /// Phase 3 fills it from TraitDefinition assets.
    /// </summary>
    public class TraitSet
    {
        readonly Adventurer _owner;
        readonly List<IStatModifier> _modifiers = new();

        public TraitSet(Adventurer owner) { _owner = owner; }

        public IEnumerable<IStatModifier> Modifiers => _modifiers;
        public float RetreatThreshold { get; protected set; } = 0.35f;
        public float FatigueMultiplier { get; protected set; } = 1f;
        public float ExpMultiplier { get; protected set; } = 1f;

        public float AdjustQuestValue(Quest quest, float value) => value;
    }
}
