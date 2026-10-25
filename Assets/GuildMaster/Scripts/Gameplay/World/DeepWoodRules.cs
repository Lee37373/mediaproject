namespace GuildMaster.World
{
    /// <summary>
    /// The deep woods' rules (P4-103, user request 2026-10-24: "not rock between the lands, a very dangerous forest"; user
    /// decision: dens that ambush, short sight and slow going, miasma and traps, safe only on the trails). Deep woods are
    /// forest (<see cref="MapData.Deep"/>), so every forest rule holds there too — hiding, ambush, the woods blocking the
    /// view from outside, wildfire; these come on top. Pure numbers, one place.
    /// </summary>
    public static class DeepWoodRules
    {
        /// <summary>Walking cost on top of the forest's (1.6): wading through undergrowth.</summary>
        public const float MoveFactor = 1.5f;
        /// <summary>Sight on top of the forest's third: the canopy shuts the light out.</summary>
        public const float SightFactor = 0.6f;
        /// <summary>Depth added where monsters are born in the deep woods (as a pocket of danger, P4-102): the dens there are strong.</summary>
        public const float DepthShift = 0.3f;

        /// <summary>
        /// Miasma and traps lie this far from the town and beyond: the woods ringing the town's fields are only dark — the
        /// first hunts go that way, and newcomers keep within 40 cells (P4-99).
        /// </summary>
        public const float HazardMinDistance = 70f;
        /// <summary>Share of the deep woods (in patches) under miasma.</summary>
        public const float MiasmaCover = 0.3f;
        /// <summary>Miasma: a share of its maximum health a person loses per game hour standing in it. Monsters live with it.</summary>
        public const float MiasmaDamagePerHour = 0.1f;

        /// <summary>One hidden trap per this many deep-wood cells (beyond <see cref="HazardMinDistance"/>), none on a trail.</summary>
        public const int CellsPerTrap = 220;
        /// <summary>A sprung trap: a share of the victim's maximum health, seconds held fast, game hours until it is set again.</summary>
        public const float TrapDamage = 0.12f, TrapHoldSeconds = 1.5f, TrapRearmHours = 24f;
        /// <summary>Seconds a sprung trap stays drawn where it caught someone.</summary>
        public const float TrapShownSeconds = 40f;

        /// <summary>Dens in the deep woods: one per this many cells of belt, at most this many on a map, this far from the town at least.</summary>
        public const int BeltCellsPerDen = 900, MaxDens = 8;
        public const float DenMinDistance = 80f;

        // ---- How the guild weighs them, where it has seen them (P4-104b: "only when strong or in a hurry, on the trails") ----

        /// <summary>
        /// Risk a known deep-wood cell adds to a walk, weighted by the walker's caution (<see cref="RouteRisk.Multiplier"/>): an
        /// ordinary party pays about twice a forest's detour cost for it and keeps to the trails, a brave one less, a hurried
        /// one nothing.
        /// </summary>
        public const float RouteRisk = 2f;
        /// <summary>A hunt in known deep woods asks this much more strength (the danger steps rate only its young's level).</summary>
        public const float QuestFactor = 1.4f;
        /// <summary>A free hunt picks a den in known deep woods only for a fight expected to cost this share of what it would take on elsewhere.</summary>
        public const float HuntLossShare = 0.5f;
    }
}
