using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>Access point for map data, navigation grid and pathfinding.</summary>
    public class WorldService
    {
        public MapData Map { get; }
        public GridMap Grid { get; }
        public Pathfinder Pathfinder { get; }
        /// <summary>Risk of the ground, shared with the pathfinder (P4-63).</summary>
        public RouteRisk Risk { get; }

        public WorldService(MapData map)
        {
            Map = map;
            Grid = new GridMap(map);
            Risk = new RouteRisk(map);
            Pathfinder = new Pathfinder(Grid, Risk);
        }

        public Vector2 TownCenterWorld => GridMap.CellToWorld(Map.TownCenter);

        public Bounds WorldBounds => new(new Vector3(Map.Width / 2f, Map.Height / 2f), new Vector3(Map.Width, Map.Height));

        /// <summary><paramref name="caution"/> weighs risky ground (P4-63, <see cref="RouteRisk"/>); 0 is the shortest way.</summary>
        public bool FindPath(Vector2 from, Vector2 to, List<Vector2Int> result, bool passesGates = true,
            System.Func<int, int, bool> believedShut = null, float caution = 0f)
            => Pathfinder.FindPathWorld(from, to, result, passesGates, believedShut, caution);

        /// <summary>
        /// Grid ray cast (Amanatides–Woo DDA) from a to b. Stops at the first hard-cover cell
        /// (mountain, wall, building). Trees are counted but not blocking. The start and end
        /// cells are ignored so units standing next to cover can still shoot past it.
        /// With <paramref name="forestBlocks"/> forest cells stop it too (sight from outside a forest,
        /// see <see cref="ForestSight"/>).
        /// </summary>
        public bool Raycast(Vector2 a, Vector2 b, out Vector2 hitPoint, out int treesCrossed, bool forestBlocks = false)
        {
            treesCrossed = 0;
            hitPoint = b;
            var start = GridMap.WorldToCell(a);
            var end = GridMap.WorldToCell(b);
            if (start == end) return false;

            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return false;
            d /= len;
            int x = start.x, y = start.y;
            int stepX = d.x > 0 ? 1 : -1, stepY = d.y > 0 ? 1 : -1;
            float tDeltaX = d.x != 0 ? Mathf.Abs(1f / d.x) : float.PositiveInfinity;
            float tDeltaY = d.y != 0 ? Mathf.Abs(1f / d.y) : float.PositiveInfinity;
            float tMaxX = d.x != 0 ? ((d.x > 0 ? x + 1 - a.x : a.x - x) * tDeltaX) : float.PositiveInfinity;
            float tMaxY = d.y != 0 ? ((d.y > 0 ? y + 1 - a.y : a.y - y) * tDeltaY) : float.PositiveInfinity;

            for (int guard = 0; guard < 512; guard++)
            {
                float t;
                if (tMaxX < tMaxY) { t = tMaxX; tMaxX += tDeltaX; x += stepX; }
                else { t = tMaxY; tMaxY += tDeltaY; y += stepY; }
                if (t > len || (x == end.x && y == end.y)) return false;
                if (Grid.IsHardCover(x, y) || (forestBlocks && Grid.IsForest(x, y)))
                {
                    hitPoint = a + d * t;
                    return true;
                }
                if (Grid.HasTree(x, y)) treesCrossed++;
            }
            return false;
        }

        /// <summary>Clear line of sight (no hard cover; trees allowed up to maxTrees).</summary>
        public bool HasLineOfSight(Vector2 a, Vector2 b, int maxTrees = 2, bool forestBlocks = false)
            => !Raycast(a, b, out _, out int trees, forestBlocks) && trees <= maxTrees;

        /// <summary>
        /// What an eye at <paramref name="eye"/> with this sight radius can see at
        /// <paramref name="target"/>: distance (shortened inside a forest), hard cover, and forests
        /// seen from outside (<see cref="ForestSight"/>); up to <paramref name="maxTrees"/> trees.
        /// </summary>
        public bool CanSee(Vector2 eye, Vector2 target, float sightRadius, int maxTrees = 2)
            => (target - eye).sqrMagnitude <= Sq(ForestSight.Radius(this, eye, sightRadius))
               && HasLineOfSight(eye, target, maxTrees, ForestSight.ForestBlocks(this, eye));

        static float Sq(float x) => x * x;

        /// <summary>True if a straight segment crosses only walkable cells (used for path smoothing).</summary>
        public bool HasClearLine(Vector2 a, Vector2 b)
        {
            float dist = Vector2.Distance(a, b);
            int steps = Mathf.CeilToInt(dist * 3f);
            for (int i = 1; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)steps);
                var c = GridMap.WorldToCell(p);
                if (!Grid.IsWalkable(c)) return false;
                // Reject lines that clip wall corners.
                var off = new Vector2(p.x - c.x - 0.5f, p.y - c.y - 0.5f);
                var sx = new Vector2Int(c.x + (off.x > 0.3f ? 1 : off.x < -0.3f ? -1 : 0), c.y);
                var sy = new Vector2Int(c.x, c.y + (off.y > 0.3f ? 1 : off.y < -0.3f ? -1 : 0));
                if (!Grid.IsWalkable(sx) || !Grid.IsWalkable(sy)) return false;
            }
            return true;
        }
    }
}
