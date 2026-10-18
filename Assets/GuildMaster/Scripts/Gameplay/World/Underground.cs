using System.Collections.Generic;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>
    /// The ruins' dungeons (P4-85, user decision 2026-10-18: "a separate space you go down into"). The generated
    /// surface is copied into a taller map whose extra rows hold an underground band, far enough above the surface
    /// that no eye or reach crosses the dark (<see cref="TileType.Void"/>) between; each ruin has a way down on the
    /// surface and a dungeon of rooms in its slot of the band, linked as a portal pair. The surface itself is exactly
    /// what the generator made (same random draws): ruins come from their own stream of the map seed.
    /// </summary>
    public static class Underground
    {
        /// <summary>Rows of dark between the surface's top and the band.</summary>
        public const int Gap = 32;
        /// <summary>Rows of the band (each dungeon is at most this tall).</summary>
        public const int BandHeight = 40;
        /// <summary>Rows of dark above the band.</summary>
        public const int TopMargin = 4;
        /// <summary>Ruins on a map, and the band's slot for each.</summary>
        public const int Count = 8;
        public const int SlotWidth = 60;
        /// <summary>Ways down keep this far from the town, the dens, the map edge and each other.</summary>
        public const float MinTownDistance = 50f, NestMargin = 15f, EdgeMargin = 12f, Spacing = 40f;
        /// <summary>Distance bands of the inner and the outer half of the ruins (P4-81 depth).</summary>
        public const float InnerMin = 50f, InnerMax = 170f, OuterMin = 190f, OuterMax = 290f;

        public static GameRandom StreamFor(int mapSeed) => new(mapSeed ^ 0x2B1A9C3);

        /// <summary>Height of the whole map for a surface this tall.</summary>
        public static int TotalHeight(int surfaceHeight) => surfaceHeight + Gap + BandHeight + TopMargin;

        /// <summary>
        /// The surface with an underground: a copy of <paramref name="surface"/> with the band above it, its ways down
        /// and dungeons. A surface too small to hold the slots comes back as it is.
        /// </summary>
        public static MapData Extend(MapData surface, int seed)
        {
            if (surface.Width < Count * SlotWidth + 16) return surface;
            int w = surface.Width, h = surface.Height;
            var map = new MapData(w, TotalHeight(h), h);
            System.Array.Copy(surface.Tiles, map.Tiles, w * h);
            System.Array.Copy(surface.Trees, map.Trees, w * h);
            System.Array.Copy(surface.Regions, map.Regions, w * h);
            map.TownRect = surface.TownRect;
            map.TownCenter = surface.TownCenter;
            map.RegionLayout = surface.RegionLayout;
            map.Gates.AddRange(surface.Gates);
            map.Nests.AddRange(surface.Nests);
            map.EdgeSpawnCells.AddRange(surface.EdgeSpawnCells);
            for (int i = w * h; i < map.Tiles.Length; i++) map.Tiles[i] = TileType.Void;

            var rng = StreamFor(seed);
            var entrances = PickEntrances(map, rng);
            for (int k = 0; k < entrances.Count; k++)
            {
                var region = map.RegionOf(entrances[k].x, entrances[k].y);
                int ordinal = 0;
                foreach (var other in map.Ruins) if (other.Region == region) ordinal++;
                var site = new RuinSite
                {
                    Index = k, Entrance = entrances[k], Region = region, RegionOrdinal = ordinal,
                    Area = new RectInt(16 + k * SlotWidth + 2, h + Gap + 2, SlotWidth - 4, BandHeight - 4),
                };
                Carve(map, site, rng);
                map.Ruins.Add(site);
            }
            return map;
        }

        // ---------------- Ways down ----------------

        /// <summary>
        /// Half in the inner land and half beyond it, one in each quarter of the compass per half; on open walkable ground
        /// with walkable cells all round, clear of the town, the dens, the edge and each other.
        /// </summary>
        static List<Vector2Int> PickEntrances(MapData map, GameRandom rng)
        {
            var result = new List<Vector2Int>();
            var town = map.TownCenter;
            for (int k = 0; k < Count; k++)
            {
                bool inner = k < Count / 2;
                float baseAngle = (k % 4) * 90f;
                bool found = false;
                for (int attempt = 0; attempt < 120 && !found; attempt++)
                {
                    // The quarter first; any angle once that is hopeless.
                    float angle = (attempt < 60 ? baseAngle + rng.Range(5f, 85f) : rng.Range(0f, 360f)) * Mathf.Deg2Rad;
                    float d = inner ? rng.Range(InnerMin, InnerMax) : rng.Range(OuterMin, OuterMax);
                    var c = town + new Vector2Int(Mathf.RoundToInt(Mathf.Cos(angle) * d), Mathf.RoundToInt(Mathf.Sin(angle) * d));
                    if (!Fits(map, c, result)) continue;
                    result.Add(c);
                    found = true;
                }
            }
            return result;
        }

        static bool Fits(MapData map, Vector2Int c, List<Vector2Int> taken)
        {
            if (c.x < EdgeMargin || c.y < EdgeMargin || c.x >= map.Width - EdgeMargin || c.y >= map.SurfaceHeight - EdgeMargin) return false;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var t = map[c.x + dx, c.y + dy];
                if (t is not (TileType.Grass or TileType.Sand)) return false;
            }
            if (map.TownRect.Contains(c) || Vector2Int.Distance(c, map.TownCenter) < MinTownDistance) return false;
            foreach (var n in map.Nests) if (Vector2Int.Distance(n.Cell, c) < NestMargin) return false;
            foreach (var o in taken) if (Vector2Int.Distance(o, c) < Spacing) return false;
            return true;
        }

        // ---------------- Dungeons ----------------

        /// <summary>Rooms in a 3 × 2 grid of the slot, visited as a snake from the bottom left.</summary>
        static readonly Vector2Int[] Snake = { new(0, 0), new(1, 0), new(2, 0), new(2, 1), new(1, 1), new(0, 1) };
        /// <summary>Rooms per dungeon (the first is the way up, the last holds the treasure).</summary>
        public const int Rooms = 5;

        /// <summary>Walls everywhere in the slot, then the rooms and the corridors between them, two cells wide.</summary>
        static void Carve(MapData map, RuinSite site, GameRandom rng)
        {
            var area = site.Area;
            for (int y = area.yMin; y < area.yMax; y++)
            for (int x = area.xMin; x < area.xMax; x++)
            {
                map[x, y] = TileType.RuinWall;
                map.Regions[map.Index(x, y)] = site.Region;
            }
            int cellW = area.width / 3, cellH = area.height / 2;
            for (int r = 0; r < Rooms; r++)
            {
                var g = Snake[r];
                int rw = rng.Range(8, Mathf.Min(14, cellW - 2)), rh = rng.Range(7, Mathf.Min(13, cellH - 2));
                int x0 = area.xMin + g.x * cellW + 1 + rng.Range(0, cellW - rw - 1);
                int y0 = area.yMin + g.y * cellH + 1 + rng.Range(0, cellH - rh - 1);
                var room = new RectInt(x0, y0, rw, rh);
                site.Rooms.Add(room);
                Fill(map, room);
            }
            for (int r = 1; r < site.Rooms.Count; r++) Corridor(map, Centre(site.Rooms[r - 1]), Centre(site.Rooms[r]), rng.Chance(0.5f));
            site.Inside = Centre(site.Rooms[0]);
            site.Chest = Centre(site.Rooms[^1]);
        }

        public static Vector2Int Centre(RectInt r) => new(r.xMin + r.width / 2, r.yMin + r.height / 2);

        static void Fill(MapData map, RectInt r)
        {
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
                map[x, y] = TileType.RuinFloor;
        }

        /// <summary>An L of two-wide floor from a to b (horizontal first, or vertical first).</summary>
        static void Corridor(MapData map, Vector2Int a, Vector2Int b, bool horizontalFirst)
        {
            var corner = horizontalFirst ? new Vector2Int(b.x, a.y) : new Vector2Int(a.x, b.y);
            Line(map, a, corner);
            Line(map, corner, b);
        }

        static void Line(MapData map, Vector2Int a, Vector2Int b)
        {
            int x0 = Mathf.Min(a.x, b.x), x1 = Mathf.Max(a.x, b.x), y0 = Mathf.Min(a.y, b.y), y1 = Mathf.Max(a.y, b.y);
            for (int y = y0; y <= y1 + 1; y++)
            for (int x = x0; x <= x1 + 1; x++)
                if (a.y == b.y && y > y0 + 1 || a.x == b.x && x > x0 + 1) continue;
                else map[x, y] = TileType.RuinFloor;
        }
    }
}
