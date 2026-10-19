using GuildMaster.Data;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>
    /// How the town grows (P4-86, user decision 2026-10-19): the walls move out four cells a side at a time, three
    /// times — 23 → 31 → 39 → 47 cells across — for gold and materials, at once, never while a raid is being fought.
    /// It buys room (for the P4-87 buildings), nothing else: taxes and visitors stay with the town hall. Pure rules
    /// for <see cref="TownExpansion"/>, the build window and the tests.
    /// </summary>
    public static class TownExpansionRules
    {
        /// <summary>Cells the walls move out on each side per step.</summary>
        public const int Step = 4;
        public const int MaxStage = 3;
        /// <summary>A den the guild knows of this close past the new walls has to be cleared first; an empty site there is retired.</summary>
        public const int NestClearance = 4;

        public readonly struct Cost
        {
            public readonly int Gold;
            public readonly ItemCost[] Items;
            public Cost(int gold, params ItemCost[] items) { Gold = gold; Items = items; }
        }

        static readonly Cost[] Costs =
        {
            new(600, I("stone_shard", 15)),
            new(1500, I("stone_shard", 25), I("iron_ore", 15)),
            new(3000, I("iron_ore", 30), I("crown_shard", 2)),
        };

        static ItemCost I(string id, int n) => new() { itemId = id, count = n };

        /// <summary>What growing from <paramref name="stage"/> to the next costs; null at the last stage.</summary>
        public static Cost? CostToGrow(int stage) => stage >= 0 && stage < MaxStage ? Costs[stage] : null;

        /// <summary>The walls' half size at a stage, from the map's starting one.</summary>
        public static int HalfSizeAt(int baseHalf, int stage) => baseHalf + Step * Mathf.Clamp(stage, 0, MaxStage);

        /// <summary>The town's width at that half size, walls included.</summary>
        public static int Width(int half) => half * 2 + 1;

        /// <summary>
        /// The town's own sight when the walls have moved out <paramref name="growth"/> cells: as far past the walls'
        /// corners as at the start (P4-41: the watch on the walls sees raiders coming) — 18, 23.7, 29.3, 35 cells.
        /// </summary>
        public static float SightRadius(float baseRadius, int growth) => baseRadius + Mathf.Max(0, growth) * Mathf.Sqrt(2f);

        /// <summary>Ground a den must not hold for walls of this half size: inside them or <see cref="NestClearance"/> past them.</summary>
        public static bool Crowds(Vector2Int cell, Vector2Int centre, int half)
            => Mathf.Max(Mathf.Abs(cell.x - centre.x), Mathf.Abs(cell.y - centre.y)) <= half + NestClearance;
    }
}
