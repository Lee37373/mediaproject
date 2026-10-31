using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>
    /// The town's walls on the tile map: laying them out when the map is made, and pushing them out a
    /// step (P4-86) — the old ring becomes floor, the new ring and its four gates go up, the ground round
    /// it is cleared, the plaza roads run on to the new gates and each road outside is led from its new
    /// gate back onto the old one. Pure tile work, so the generator, the game and the tests share it, and
    /// a saved town is rebuilt exactly by growing the freshly made map again step by step.
    /// </summary>
    public static class TownWalls
    {
        /// <summary>Open ground round the walls: a circle this far past them...</summary>
        public const int ClearRound = 7;
        /// <summary>...and a square this far past them.</summary>
        public const int ClearSquare = 2;
        /// <summary>How far to each side of its gate a road may have wandered (where the new road looks for it).</summary>
        public const int RoadSearch = 8;

        /// <summary>The gates in the map's order (<see cref="MapData.Gates"/>): north, south, west, east.</summary>
        public static readonly Vector2Int[] GateDirections = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        /// <summary>The walls and everything inside them.</summary>
        public static RectInt RectFor(Vector2Int centre, int half) => new(centre.x - half, centre.y - half, half * 2 + 1, half * 2 + 1);

        /// <summary>A cell of the open ground round walls of this half size (the town itself included).</summary>
        public static bool InClearing(int x, int y, Vector2Int centre, int half)
        {
            int clear = half + ClearRound, dx = x - centre.x, dy = y - centre.y;
            return dx * dx + dy * dy <= clear * clear || Mathf.Abs(dx) <= half + ClearSquare && Mathf.Abs(dy) <= half + ClearSquare;
        }

        /// <summary>Everything <see cref="Grow"/> to this half size may touch.</summary>
        public static RectInt GrowthArea(MapData map, int half)
        {
            var r = RectFor(map.TownCenter, half + ClearRound + 1);
            int xMin = Mathf.Max(0, r.xMin), yMin = Mathf.Max(0, r.yMin);
            int xMax = Mathf.Min(map.Width, r.xMax), yMax = Mathf.Min(map.SurfaceHeight, r.yMax);
            return new RectInt(xMin, yMin, Mathf.Max(0, xMax - xMin), Mathf.Max(0, yMax - yMin));
        }

        /// <summary>
        /// Map generation: open ground round the town, floor, walls and the four gates. The roads out are the
        /// generator's (it carves them from the gates with its own random stream), then <see cref="LayPlazaRoads"/>.
        /// </summary>
        public static void Lay(MapData map, Vector2Int centre, int half)
        {
            map.TownCenter = centre;
            map.TownHalfSize = map.BaseTownHalfSize = half;
            int clear = half + ClearRound;
            for (int y = centre.y - clear; y <= centre.y + clear; y++)
            for (int x = centre.x - clear; x <= centre.x + clear; x++)
                if (map.InBounds(x, y) && InClearing(x, y, centre, half)) map[x, y] = TileType.Grass;
            map.Gates.Clear();
            Raise(map, half);
        }

        /// <summary>The roads crossing the town from gate to gate.</summary>
        public static void LayPlazaRoads(MapData map)
        {
            var c = map.TownCenter;
            int half = map.TownHalfSize;
            for (int k = -half + 1; k <= half - 1; k++)
            {
                map[c.x + k, c.y] = TileType.Road;
                map[c.x, c.y + k] = TileType.Road;
            }
        }

        /// <summary>
        /// Pushes the walls out to <paramref name="half"/> (P4-86) and returns the area that may have changed
        /// (to repaint). Natural ground round the new walls is cleared to grass and loses its trees; roads stay.
        /// </summary>
        public static RectInt Grow(MapData map, int half)
        {
            if (half <= map.TownHalfSize) return default;
            var c = map.TownCenter;
            var area = GrowthArea(map, half);
            var town = RectFor(c, half);
            for (int y = area.yMin; y < area.yMax; y++)
            for (int x = area.xMin; x < area.xMax; x++)
            {
                if (town.Contains(new Vector2Int(x, y)) || !InClearing(x, y, c, half)) continue;
                int i = map.Index(x, y);
                if (map.Tiles[i] is TileType.Grass or TileType.Forest or TileType.Sand or TileType.Water or TileType.Mountain)
                {
                    map.Tiles[i] = TileType.Grass;
                    map.Trees[i] = false;
                }
            }
            for (int y = town.yMin; y < town.yMax; y++)
            for (int x = town.xMin; x < town.xMax; x++)
                map.Trees[map.Index(x, y)] = false;
            Raise(map, half);
            LayPlazaRoads(map);
            for (int g = 0; g < GateDirections.Length; g++) LeadRoadOut(map, GateDirections[g], half);
            return area;
        }

        /// <summary>Floor inside, the ring of wall, three-cell gates in the middle of each side; sets the town's size.</summary>
        static void Raise(MapData map, int half)
        {
            var c = map.TownCenter;
            var r = RectFor(c, half);
            map.TownHalfSize = half;
            map.TownRect = r;
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                bool border = x == r.xMin || y == r.yMin || x == r.xMax - 1 || y == r.yMax - 1;
                map[x, y] = border ? TileType.Wall : TileType.TownFloor;
            }
            for (int g = 0; g < GateDirections.Length; g++)
            {
                var dir = GateDirections[g];
                var gate = c + dir * half;
                var side = new Vector2Int(dir.y, dir.x);
                for (int k = -1; k <= 1; k++) map[gate + side * k] = TileType.Gate;
                while (map.Gates.Count <= g) map.Gates.Add(gate);
                map.Gates[g] = gate;
            }
        }

        /// <summary>
        /// The road out of a moved gate: the old road's stretch across the cleared ground goes, and a new one
        /// runs from the gate, drifting a cell at a time like the old one did, to where the old road leaves the
        /// clearing. Without an old road it just runs straight across the clearing.
        /// </summary>
        static void LeadRoadOut(MapData map, Vector2Int dir, int half)
        {
            var c = map.TownCenter;
            var side = new Vector2Int(dir.y, dir.x);
            Vector2Int At(int t, int s) => c + dir * t + side * s;
            int join = half + ClearRound + 1;

            int target = 0;
            bool found = false;
            for (int t = join; t <= join + 10 && !found; t++)
                for (int k = 0; k <= RoadSearch && !found; k++)
                    foreach (int s in k == 0 ? new[] { 0 } : new[] { -k, k })
                    {
                        var p = At(t, s);
                        if (!map.InBounds(p) || map[p] != TileType.Road) continue;
                        join = t;
                        target = s;
                        found = true;
                        break;
                    }

            for (int t = half + 1; t < join; t++)
            for (int s = -RoadSearch; s <= RoadSearch; s++)
            {
                var p = At(t, s);
                if (map.InBounds(p) && map[p] == TileType.Road) map[p] = TileType.Grass;
            }

            int lateral = 0;
            for (int t = half + 1; t <= join; t++)
            {
                if (!map.InBounds(At(t, lateral))) return;
                map[At(t, lateral)] = TileType.Road;
                // Drift towards the old road: every third cell, or every cell once it is running out of room.
                while (lateral != target && (Mathf.Abs(target - lateral) >= join - t || (t - half) % 3 == 0))
                {
                    lateral += target > lateral ? 1 : -1;
                    if (!map.InBounds(At(t, lateral))) return;
                    map[At(t, lateral)] = TileType.Road;
                    if ((t - half) % 3 == 0 && Mathf.Abs(target - lateral) < join - t) break;
                }
            }
        }
    }
}
