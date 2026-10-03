namespace GuildMaster.World
{
    /// <summary>What a natural disaster leaves on a cell (P4-72); one at a time per cell.</summary>
    public enum HazardKind : byte
    {
        None,
        /// <summary>A forest cell on fire: burns whoever stands in it, then turns to <see cref="Burnt"/>.</summary>
        Fire,
        /// <summary>Burnt forest: no trees, nothing hides there any more, until it grows back.</summary>
        Burnt,
        /// <summary>Swamp mist gone poisonous: hurts and shortens sight.</summary>
        Poison,
        /// <summary>An avalanche's snow: blocks the cell.</summary>
        Snow,
        /// <summary>Inside a sandstorm: hard to see and slow going.</summary>
        Sandstorm,
        /// <summary>An earthquake's crack: blocks the cell.</summary>
        Fissure,
    }

    /// <summary>
    /// How each hazard changes the ground (P4-72): walking, sight and the forest rules
    /// (<see cref="ForestSight"/>). <see cref="GridMap"/> applies these; the disasters that lay them
    /// down are in <c>Disasters/</c>.
    /// </summary>
    public static class Hazards
    {
        /// <summary>Nobody walks through it (snow piled up, the ground split open).</summary>
        public static bool Blocks(HazardKind k) => k is HazardKind.Snow or HazardKind.Fissure;

        /// <summary>Path cost factor: walkers go round fire and poison when they can.</summary>
        public static float CostFactor(HazardKind k) => k switch
        {
            HazardKind.Fire => 4f,
            HazardKind.Poison => 2f,
            HazardKind.Sandstorm => 1.8f,
            _ => 1f,
        };

        /// <summary>Sight factor for an eye standing in it.</summary>
        public static float SightFactor(HazardKind k) => k switch
        {
            HazardKind.Poison => 0.5f,
            HazardKind.Sandstorm => 0.35f,
            _ => 1f,
        };

        /// <summary>Burnt forest is forest no more: no trees, open to view.</summary>
        public static bool ClearsForest(HazardKind k) => k == HazardKind.Burnt;

        /// <summary>Harmful to stand in (damage over time).</summary>
        public static bool Hurts(HazardKind k) => k is HazardKind.Fire or HazardKind.Poison;
    }
}
