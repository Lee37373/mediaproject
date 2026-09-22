using System.Collections.Generic;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>
    /// Where the player may put a building (P4-13). Pure rules so they can be tested and so the
    /// placement preview and the actual build always agree.
    /// </summary>
    public static class ConstructionRules
    {
        /// <summary>A tower must keep this many cells clear of a town gate so it cannot plug it.</summary>
        public const int GateClearance = 2;

        /// <summary>The footprint a building of this size would occupy if dropped at a point.</summary>
        public static RectInt FootprintAt(Vector2 point, int size)
        {
            var c = GridMap.WorldToCell(point);
            int half = Mathf.Max(0, size - 1) / 2;
            return new RectInt(c.x - half, c.y - half, Mathf.Max(1, size), Mathf.Max(1, size));
        }

        /// <summary>
        /// Why the building cannot stand here, or null when it can. Anywhere walkable is allowed
        /// (user decision, 2026-09-20) — inside town, outside it, next to a nest — as long as the
        /// ground carries a building and the gates stay open.
        /// </summary>
        public static string CannotPlace(GridMap grid, IReadOnlyList<Building> buildings, IReadOnlyList<Vector2Int> gates, RectInt foot)
        {
            for (int y = foot.yMin; y < foot.yMax; y++)
            for (int x = foot.xMin; x < foot.xMax; x++)
            {
                var cell = new Vector2Int(x, y);
                if (!grid.InBounds(cell)) return "지도 밖입니다.";
                if (!grid.IsWalkable(cell)) return "여기에는 지을 수 없습니다 (물·산·성벽이거나 이미 무언가 있습니다).";
            }
            if (buildings != null)
                foreach (var b in buildings)
                    if (b != null && b.Footprint.Overlaps(foot)) return $"{b.DisplayName}과(와) 겹칩니다.";
            if (gates != null)
                foreach (var gate in gates)
                    if (Near(foot, gate, GateClearance)) return "성문을 막을 수 없습니다.";
            return null;
        }

        /// <summary>Is the cell within `distance` cells of the rectangle (Chebyshev)?</summary>
        static bool Near(RectInt foot, Vector2Int cell, int distance)
        {
            int dx = Mathf.Max(foot.xMin - cell.x, cell.x - (foot.xMax - 1), 0);
            int dy = Mathf.Max(foot.yMin - cell.y, cell.y - (foot.yMax - 1), 0);
            return Mathf.Max(dx, dy) <= distance;
        }

        /// <summary>Gold and materials returned when a building is taken down (half, rounded down).</summary>
        public static int Refund(int paid) => paid / 2;
    }
}
