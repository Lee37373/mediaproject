using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>
    /// Forests, regions and sight (P4-48, P4-55; user decisions 2026-09-22), in one place for the fog and for every
    /// unit's <c>CanSee</c>:
    /// - an eye outside a forest cannot look into it: forest cells block like a wall, so only the
    ///   first forest cell on a line is seen, not what lies inside or beyond it;
    /// - an eye inside a forest is not blocked by it, but sees only <see cref="InForestFactor"/> of
    ///   its usual distance;
    /// - the region's weather shortens sight too (<see cref="TerrainRegions.SightMultiplier"/>);
    /// - elevated eyes (town watch, watchtowers: <c>SeesOverWalls</c>) ignore all of it.
    /// </summary>
    public static class ForestSight
    {
        public const float InForestFactor = 1f / 3f;

        public static bool InForest(WorldService world, Vector2 p) => world.Grid.IsForest(GridMap.WorldToCell(p));

        /// <summary>
        /// How far an eye at <paramref name="eye"/> sees with this nominal radius: a third inside a
        /// forest, times the region's weather (snow, swamp mist, ash: P4-55). Elevated eyes see it all.
        /// </summary>
        public static float Radius(WorldService world, Vector2 eye, float radius, bool elevated = false)
        {
            if (elevated) return radius;
            var cell = GridMap.WorldToCell(eye);
            float r = radius * TerrainRegions.SightMultiplier(world.Map.RegionOf(cell.x, cell.y));
            return InForest(world, eye) ? r * InForestFactor : r;
        }

        /// <summary>Forests block this eye's view (it stands outside one and is not elevated).</summary>
        public static bool ForestBlocks(WorldService world, Vector2 eye, bool elevated = false)
            => !elevated && !InForest(world, eye);
    }
}
