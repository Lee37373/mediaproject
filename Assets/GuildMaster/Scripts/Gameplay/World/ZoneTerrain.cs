using System.Collections.Generic;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>
    /// The land inside the regions (P4-102): each zone's lie of the land (open, lakes, woods, rocky), the softer lines
    /// between zones of one region (a belt of scrub or wood, an ice brook, a bog channel, a lava brook — with
    /// gaps), and each outer region's landmark (a volcano, mesas, a frozen lake, a great mire). Replaces the rings of
    /// P4-101 (<c>RegionLandforms</c>), which read as combed stripes round the town.
    /// </summary>
    public static class ZoneTerrain
    {
        /// <summary>Share of the borders between zones of one region that get a line, and the share of such a line left open.</summary>
        const float DividerChance = 0.55f, DividerGap = 0.42f;
        /// <summary>Half widths: a brook, a belt of wood or scrub.</summary>
        const float BrookHalfMin = 0.8f, BrookHalfMax = 1.3f, WoodHalfMin = 2.5f, WoodHalfMax = 4.5f;
        /// <summary>A landmark's zone lies at least this far from the town (cells).</summary>
        const float LandmarkMinDistance = 140f;

        // ---------------- Lie of the land ----------------

        /// <summary>Draws each outer zone's lie of the land by its region, trying not to repeat a neighbour's. The fields stay mixed.</summary>
        public static void AssignCharacters(MapData map, GameRandom rng)
        {
            foreach (var z in map.Zones)
            {
                if (z.Index == ZoneMap.Home) { z.Character = ZoneCharacter.Mixed; continue; }
                var pick = Draw(z.Region, rng);
                for (int retry = 0; retry < 2 && z.Neighbours.Exists(n => n < z.Index && map.Zones[n].Region == z.Region && map.Zones[n].Character == pick); retry++)
                    pick = Draw(z.Region, rng);
                z.Character = pick;
            }
        }

        static ZoneCharacter Draw(Region r, GameRandom rng)
        {
            float roll = rng.Value;
            ZoneCharacter Pick(params (ZoneCharacter c, float p)[] table)
            {
                foreach (var (c, p) in table)
                {
                    if (roll < p) return c;
                    roll -= p;
                }
                return ZoneCharacter.Mixed;
            }
            return r switch
            {
                Region.Desert => Pick((ZoneCharacter.Open, 0.4f), (ZoneCharacter.Rocky, 0.35f), (ZoneCharacter.Lakes, 0.1f)),
                Region.Snow => Pick((ZoneCharacter.Woods, 0.35f), (ZoneCharacter.Lakes, 0.25f), (ZoneCharacter.Rocky, 0.25f), (ZoneCharacter.Open, 0.15f)),
                Region.Swamp => Pick((ZoneCharacter.Lakes, 0.4f), (ZoneCharacter.Woods, 0.35f)),
                Region.Wasteland => Pick((ZoneCharacter.Rocky, 0.4f), (ZoneCharacter.Lakes, 0.25f), (ZoneCharacter.Open, 0.2f)),
                _ => ZoneCharacter.Mixed,
            };
        }

        /// <summary>The zones' lie of the land read at the same points as <see cref="TerrainRegions.SampleAround"/>.</summary>
        public static void SampleLie(MapData map, int x, int y, ZoneCharacter[] into)
        {
            into[0] = Lie(map, x, y);
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                int sx = Mathf.Clamp(Mathf.RoundToInt(x + Mathf.Cos(a) * TerrainRegions.BlendRadius), 0, map.Width - 1);
                int sy = Mathf.Clamp(Mathf.RoundToInt(y + Mathf.Sin(a) * TerrainRegions.BlendRadius), 0, map.SurfaceHeight - 1);
                into[k + 1] = Lie(map, sx, sy);
            }
        }

        static ZoneCharacter Lie(MapData map, int x, int y)
        {
            var z = map.ZoneAt(x, y);
            return z?.Character ?? ZoneCharacter.Mixed;
        }

        // ---------------- Lines between zones of one region ----------------

        /// <summary>
        /// About half of the borders between zones of one outer region get a line with gaps: the desert a belt of scrub,
        /// the snowfield an ice brook or a wood, the swamp a bog channel or a wood, the wasteland a lava brook or dead
        /// wood (P4-103, user decision: no rock lines between lands). Not near the town, not within reach of a region border (its barrier is there).
        /// </summary>
        public static void Dividers(MapData map, RegionBarriers.Layout regionBorders, Vector2Int town, int townHalf, GameRandom rng)
        {
            int w = map.Width, h = map.SurfaceHeight;
            var labels = new int[w * h];
            for (int i = 0; i < labels.Length; i++) labels[i] = map.ZoneIds[i];
            var field = RegionBarriers.Field(labels, w, h);
            var kinds = new Dictionary<int, int>(); // 0 none, 2 brook, 3 wood (P4-103: no rock lines)
            var keys = new SortedSet<int>();
            foreach (int p in field.Pair) if (p >= 0) keys.Add(p);
            foreach (int key in keys)
            {
                int a = key / RegionBarriers.KeyBase, b = key % RegionBarriers.KeyBase;
                if (a == ZoneMap.Home || b == ZoneMap.Home || map.Zones[a].Region != map.Zones[b].Region || !rng.Chance(DividerChance))
                {
                    kinds[key] = 0;
                    continue;
                }
                kinds[key] = map.Zones[a].Region switch
                {
                    Region.Desert => 3,
                    Region.Snow => rng.Chance(0.6f) ? 2 : 3,
                    Region.Swamp => rng.Chance(0.6f) ? 2 : 3,
                    Region.Wasteland => rng.Chance(0.5f) ? 2 : 3,
                    _ => 3,
                };
            }
            float nx = rng.Range(0f, 5000f), ny = rng.Range(0f, 5000f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x, key = field.Pair[i];
                if (key < 0 || !kinds.TryGetValue(key, out int kind) || kind == 0) continue;
                float d = field.Distance[i];
                if (d > WoodHalfMax + 1f || RegionBarriers.NearTown(x, y, town, townHalf)) continue;
                if (regionBorders != null && regionBorders.Distance[i] < 10f) continue;
                // Gaps where the slow noise along the line is low; the width swells and narrows.
                if (Mathf.PerlinNoise(x * 0.045f + nx, y * 0.045f + ny) < DividerGap) continue;
                float n = Mathf.PerlinNoise(x * 0.08f + ny, y * 0.08f + nx);
                var t = map.Tiles[i];
                if (t is TileType.Road or TileType.TownFloor or TileType.Wall or TileType.Gate) continue;
                switch (kind)
                {
                    case 2 when d <= Mathf.Lerp(BrookHalfMin, BrookHalfMax, n):
                        Set(map, i, TileType.Water);
                        break;
                    case 2 when d <= Mathf.Lerp(BrookHalfMin, BrookHalfMax, n) + 1f && t != TileType.Water:
                        Set(map, i, TileType.Sand);
                        break;
                    case 3 when d <= Mathf.Lerp(WoodHalfMin, WoodHalfMax, n) && t is TileType.Grass or TileType.Sand:
                        Set(map, i, TileType.Forest);
                        break;
                }
            }
        }

        static void Set(MapData map, int i, TileType t)
        {
            map.Tiles[i] = t;
            map.Trees[i] = false;
        }

        // ---------------- Landmarks ----------------

        /// <summary>
        /// One landmark in each outer region, in its biggest zone far enough out: the wasteland's volcano (a cone of rock,
        /// a lava crater, lava running down its flanks, ash round it), the desert's mesas (rock tables with canyons
        /// between and an oasis by them), the snowfield's frozen lake under rock peaks, the swamp's great mire.
        /// </summary>
        public static void Landmarks(MapData map, Vector2Int town, GameRandom rng)
        {
            foreach (Region r in new[] { Region.Wasteland, Region.Desert, Region.Snow, Region.Swamp })
            {
                ZoneSite best = null;
                foreach (var z in map.Zones)
                {
                    if (z.Region != r || z.Index == ZoneMap.Home || Vector2.Distance(z.Centre, town) < LandmarkMinDistance) continue;
                    if (map.RegionOf(Mathf.RoundToInt(z.Centre.x), Mathf.RoundToInt(z.Centre.y)) != r) continue;
                    if (best == null || z.Cells > best.Cells) best = z;
                }
                if (best == null) continue;
                best.Landmark = true;
                var at = new Vector2Int(Mathf.RoundToInt(best.Centre.x), Mathf.RoundToInt(best.Centre.y));
                switch (r)
                {
                    case Region.Wasteland: Volcano(map, at, best.Index, rng); break;
                    case Region.Desert: Mesas(map, at, best.Index, rng); break;
                    case Region.Snow: FrozenLake(map, at, best.Index, rng); break;
                    case Region.Swamp: Mire(map, at, best.Index, rng); break;
                }
            }
        }

        /// <summary>Paints a cell of the zone (and the region it belongs to) if it lies on the map.</summary>
        static void Paint(MapData map, int x, int y, int zone, TileType t)
        {
            if (x < 2 || y < 2 || x >= map.Width - 2 || y >= map.SurfaceHeight - 2) return;
            int i = y * map.Width + x;
            if (map.ZoneIds[i] != zone) return;
            Set(map, i, t);
        }

        static void Volcano(MapData map, Vector2Int at, int zone, GameRandom rng)
        {
            float radius = rng.Range(11f, 15f), crater = rng.Range(3f, 4.2f);
            float nx = rng.Range(0f, 999f);
            int r = Mathf.CeilToInt(radius * 1.25f + 7f);
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Atan2(dy, dx);
                float edge = radius * (0.85f + 0.3f * TerrainRegions.Noise(Mathf.Cos(a) * 2f + nx, Mathf.Sin(a) * 2f + nx));
                int x = at.x + dx, y = at.y + dy;
                if (d <= crater) Paint(map, x, y, zone, TileType.Water);
                else if (d <= edge) Paint(map, x, y, zone, TileType.Mountain);
                else if (d <= edge + 5f + 2f * TerrainRegions.Noise(x * 0.3f, y * 0.3f + nx)) Paint(map, x, y, zone, TileType.Sand); // ash
            }
            // Lava running from the crater down the flanks and out over the ash.
            int flows = rng.Range(3, 6);
            float turn = rng.Range(0f, Mathf.PI * 2f);
            for (int f = 0; f < flows; f++)
            {
                float heading = turn + f * Mathf.PI * 2f / flows + rng.Range(-0.4f, 0.4f);
                var p = (Vector2)at;
                float length = radius + rng.Range(14f, 30f), half = rng.Range(0.8f, 1.3f);
                for (float s = 0f; s < length; s += 0.7f)
                {
                    heading += rng.Range(-0.12f, 0.12f);
                    p += new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * 0.7f;
                    float wide = half * (s > length - 6f ? Mathf.Max(0.4f, (length - s) / 6f) : 1f);
                    int hr = Mathf.CeilToInt(wide);
                    for (int dy = -hr; dy <= hr; dy++)
                    for (int dx = -hr; dx <= hr; dx++)
                        if (dx * dx + dy * dy <= wide * wide + 0.3f) Paint(map, Mathf.RoundToInt(p.x) + dx, Mathf.RoundToInt(p.y) + dy, zone, TileType.Water);
                }
            }
        }

        static void Mesas(MapData map, Vector2Int at, int zone, GameRandom rng)
        {
            int count = rng.Range(5, 9);
            var tables = new List<(Vector2 c, float r)>();
            for (int attempt = 0; attempt < 80 && tables.Count < count; attempt++)
            {
                var c = (Vector2)at + rng.InsideUnitCircle() * 30f;
                float r = rng.Range(4f, 9f);
                bool tooClose = false;
                // Canyons 3-6 cells wide between the tables.
                foreach (var t in tables) if (Vector2.Distance(t.c, c) < t.r + r + 3f) { tooClose = true; break; }
                if (tooClose) continue;
                tables.Add((c, r));
            }
            float nx = rng.Range(0f, 999f);
            foreach (var (c, rad) in tables)
            {
                int r = Mathf.CeilToInt(rad * 1.3f);
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    float a = Mathf.Atan2(dy, dx);
                    float edge = rad * (0.8f + 0.4f * TerrainRegions.Noise(Mathf.Cos(a) * 1.7f + c.x + nx, Mathf.Sin(a) * 1.7f + c.y));
                    if (dx * dx + dy * dy <= edge * edge) Paint(map, Mathf.RoundToInt(c.x) + dx, Mathf.RoundToInt(c.y) + dy, zone, TileType.Mountain);
                }
            }
            // The oasis: a pool ringed with palms, off to one side.
            var o = (Vector2)at + new Vector2(Mathf.Cos(rng.Range(0f, 6.28f)), Mathf.Sin(rng.Range(0f, 6.28f))) * 38f;
            float pool = rng.Range(4f, 6f);
            int pr = Mathf.CeilToInt(pool + 4f);
            for (int dy = -pr; dy <= pr; dy++)
            for (int dx = -pr; dx <= pr; dx++)
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy) * (0.85f + 0.3f * TerrainRegions.Noise(dx * 0.4f + nx, dy * 0.4f));
                int x = Mathf.RoundToInt(o.x) + dx, y = Mathf.RoundToInt(o.y) + dy;
                if (d <= pool) Paint(map, x, y, zone, TileType.Water);
                else if (d <= pool + 1.2f) Paint(map, x, y, zone, TileType.Sand);
                else if (d <= pool + 3.5f) Paint(map, x, y, zone, TileType.Forest);
            }
        }

        static void FrozenLake(MapData map, Vector2Int at, int zone, GameRandom rng)
        {
            float a0 = rng.Range(0f, Mathf.PI), rx = rng.Range(13f, 19f), ry = rng.Range(8f, 12f);
            float cos = Mathf.Cos(a0), sin = Mathf.Sin(a0), nx = rng.Range(0f, 999f);
            int r = Mathf.CeilToInt(rx * 1.3f + 3f);
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                float u = (dx * cos + dy * sin) / rx, v = (-dx * sin + dy * cos) / ry;
                float d = Mathf.Sqrt(u * u + v * v) * (0.85f + 0.3f * TerrainRegions.Noise(dx * 0.15f + nx, dy * 0.15f));
                int x = at.x + dx, y = at.y + dy;
                if (d <= 1f) Paint(map, x, y, zone, TileType.Water);
                else if (d <= 1.12f) Paint(map, x, y, zone, TileType.Sand);
            }
            // Peaks along one shore.
            var side = new Vector2(-sin, cos) * (ry + 6f);
            int peaks = rng.Range(2, 5);
            for (int k = 0; k < peaks; k++)
            {
                var c = (Vector2)at + side + new Vector2(cos, sin) * rng.Range(-rx, rx);
                float pr = rng.Range(3f, 6f);
                int rr = Mathf.CeilToInt(pr * 1.3f);
                for (int dy = -rr; dy <= rr; dy++)
                for (int dx = -rr; dx <= rr; dx++)
                {
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * (0.8f + 0.4f * TerrainRegions.Noise(dx * 0.35f + c.x, dy * 0.35f + nx));
                    if (d <= pr) Paint(map, Mathf.RoundToInt(c.x) + dx, Mathf.RoundToInt(c.y) + dy, zone, TileType.Mountain);
                }
            }
        }

        static void Mire(MapData map, Vector2Int at, int zone, GameRandom rng)
        {
            const float radius = 26f;
            float nx = rng.Range(0f, 999f);
            int r = Mathf.CeilToInt(radius);
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                if (d > 1f) continue;
                int x = at.x + dx, y = at.y + dy;
                float pools = TerrainRegions.Noise(x * 0.13f + nx, y * 0.13f) - d * 0.25f;
                if (pools > 0.5f) Paint(map, x, y, zone, TileType.Water);
                else if (pools > 0.44f) Paint(map, x, y, zone, TileType.Sand);
                else if (TerrainRegions.Noise(x * 0.2f, y * 0.2f + nx) > 0.3f + d * 0.3f) Paint(map, x, y, zone, TileType.Forest);
            }
        }

        // ---------------- Hearts ----------------

        /// <summary>Each zone's heart: the open walkable cell nearest its middle with open ground round it (roads and dens go by it).</summary>
        public static void Hearts(MapData map, int[] dist)
        {
            foreach (var z in map.Zones)
            {
                var c = new Vector2Int(Mathf.RoundToInt(z.Centre.x), Mathf.RoundToInt(z.Centre.y));
                z.Heart = c;
                bool found = false;
                for (int ring = 0; ring < 60 && !found; ring++)
                for (int dy = -ring; dy <= ring && !found; dy++)
                for (int dx = -ring; dx <= ring && !found; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != ring) continue;
                    int x = c.x + dx, y = c.y + dy;
                    if (x < 3 || y < 3 || x >= map.Width - 3 || y >= map.SurfaceHeight - 3) continue;
                    if (map.ZoneIds[y * map.Width + x] != z.Index || dist[y * map.Width + x] < 0 || !Open(map, x, y)) continue;
                    z.Heart = new Vector2Int(x, y);
                    found = true;
                }
            }
        }

        static bool Open(MapData map, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (map[x + dx, y + dy] is not (TileType.Grass or TileType.Sand)) return false;
            return true;
        }
    }
}
