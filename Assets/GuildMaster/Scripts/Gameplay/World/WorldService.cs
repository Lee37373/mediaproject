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

        /// <summary>
        /// Cells the walls have moved out since the start (P4-86: 0, 4, 8, 12). Distances "from the town" that mean
        /// "out past the walls" add it, so they keep their gap from the walls as the town grows.
        /// </summary>
        public int TownGrowth => Map.TownHalfSize - Map.BaseTownHalfSize;

        /// <summary>The surface (P4-85: the underground band above it is not the land the camera roams).</summary>
        public Bounds WorldBounds => new(new Vector3(Map.Width / 2f, Map.SurfaceHeight / 2f), new Vector3(Map.Width, Map.SurfaceHeight));

        /// <summary>The dungeon the camera looks into at this point (with a little dark round it), or null on the surface.</summary>
        public Bounds? UndergroundBoundsAt(Vector2 p)
        {
            var ruin = Map.RuinAt(GridMap.WorldToCell(p));
            if (ruin == null) return null;
            var a = ruin.Area;
            return new Bounds(new Vector3(a.center.x, a.center.y), new Vector3(a.width + 8f, a.height + 8f));
        }

        // ---------------- Portals (P4-85 ruins) ----------------

        /// <summary>
        /// Where a point is as far as the surface goes: down in a ruin, its way down (a dungeon's coordinates are hundreds of
        /// cells from the town; the walk home is from the ruin). Anywhere else, the point itself.
        /// </summary>
        public Vector2 OnTheSurface(Vector2 p)
        {
            if (p.y < Map.SurfaceHeight) return p;
            var ruin = Map.RuinAt(GridMap.WorldToCell(p));
            return ruin != null ? GridMap.CellToWorld(ruin.Entrance) : p;
        }

        /// <summary>The portal on the other side of this cell (a ruin's way down or up), if it is one.</summary>
        public bool TryGetPortalExit(Vector2Int c, out Vector2Int exit)
        {
            foreach (var r in Map.Ruins)
            {
                if (r.Entrance == c) { exit = r.Inside; return true; }
                if (r.Inside == c) { exit = r.Entrance; return true; }
            }
            exit = default;
            return false;
        }

        /// <summary>Which side of the portals a cell is on: 0 the surface, 1 + a ruin's index its dungeon, -1 the dark between.</summary>
        public int LayerOf(Vector2Int c)
        {
            if (c.y < Map.SurfaceHeight) return 0;
            var r = Map.RuinAt(c);
            return r != null ? r.Index + 1 : -1;
        }

        static readonly List<Vector2Int> s_leg = new();

        /// <summary>
        /// <paramref name="caution"/> weighs risky ground (P4-63, <see cref="RouteRisk"/>); 0 is the shortest way. Between the
        /// surface and a dungeon (P4-85) the route goes through the ruin's way down or up: one leg to the portal, a step to
        /// its other side (two cells far apart in the result — <see cref="PathFollower"/> steps through), and the rest.
        /// </summary>
        public bool FindPath(Vector2 from, Vector2 to, List<Vector2Int> result, bool passesGates = true,
            System.Func<int, int, bool> believedShut = null, float caution = 0f,
            System.Func<int, int, bool> unknown = null)
        {
            if (Map.Ruins.Count == 0) return Pathfinder.FindPathWorld(from, to, result, passesGates, believedShut, caution, unknown);
            var a = GridMap.WorldToCell(from);
            var b = GridMap.WorldToCell(to);
            int la = LayerOf(a), lb = LayerOf(b);
            if (la == lb || la < 0 || lb < 0) return Pathfinder.FindPath(a, b, result, passesGates, believedShut, caution, unknown);
            result.Clear();
            var at = a;
            if (la > 0)
            {
                var up = Map.Ruins[la - 1];
                if (!Pathfinder.FindPath(at, up.Inside, s_leg, passesGates, believedShut, caution, unknown)) return false;
                Append(result, s_leg);
                result.Add(up.Entrance);
                at = up.Entrance;
            }
            if (lb > 0)
            {
                var down = Map.Ruins[lb - 1];
                if (!Pathfinder.FindPath(at, down.Entrance, s_leg, passesGates, believedShut, caution, unknown)) return false;
                Append(result, s_leg);
                result.Add(down.Inside);
                at = down.Inside;
            }
            if (!Pathfinder.FindPath(at, b, s_leg, passesGates, believedShut, caution, unknown)) return false;
            Append(result, s_leg);
            return true;
        }

        static void Append(List<Vector2Int> into, List<Vector2Int> leg)
        {
            foreach (var c in leg) if (into.Count == 0 || into[^1] != c) into.Add(c);
        }

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
