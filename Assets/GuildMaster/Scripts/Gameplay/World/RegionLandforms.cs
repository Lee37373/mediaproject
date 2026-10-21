using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>
    /// Each outer region's own lay of the land (P4-101, user decision 2026-10-21): the desert's canyon walls, the
    /// snowfield's ice cracks, the swamp's channels and the wasteland's lava streams. Long winding lines broken by gaps,
    /// so a walk through a region bends round them. Most run across the way out from the town (bands round it, bent by
    /// noise): the guild walks outward, and lines along that way (plain noise contours) hardly turned it (measured: the
    /// walk to a den 3% longer than straight).
    /// </summary>
    public static class RegionLandforms
    {
        /// <summary>Cells from a region border kept clear: the barriers' passes open onto land, not into a wall.</summary>
        public const float BorderMargin = 12f;

        /// <summary>
        /// One family of lines, <see cref="Half"/> cells either side of their middle with a sand bank out to
        /// <see cref="Bank"/>, none where the gap noise at <see cref="GapFreq"/> is below <see cref="Gap"/>. With
        /// <see cref="Spacing"/>: bands round the town that far apart, pushed in and out by up to <see cref="Warp"/>
        /// cells. Without: the middle contour of noise at <see cref="Freq"/>, warped by up to <see cref="Warp"/> cells.
        /// </summary>
        struct Lines
        {
            public float Spacing, Freq, Half, Bank, GapFreq, Gap, Warp;
            public TileType Tile;
        }

        static readonly Lines[] None = System.Array.Empty<Lines>();
        static readonly Lines[] Canyons = { new() { Spacing = 64f, Half = 1.4f, GapFreq = 0.022f, Gap = 0.40f, Warp = 26f, Tile = TileType.Mountain } };
        static readonly Lines[] Cracks = { new() { Spacing = 56f, Half = 0.9f, GapFreq = 0.025f, Gap = 0.40f, Warp = 22f, Tile = TileType.Water } };
        static readonly Lines[] Channels =
        {
            new() { Spacing = 48f, Half = 1.0f, Bank = 2.0f, GapFreq = 0.028f, Gap = 0.42f, Warp = 20f, Tile = TileType.Water },
            new() { Freq = 0.042f, Half = 0.9f, Bank = 1.8f, GapFreq = 0.05f, Gap = 0.50f, Warp = 6f, Tile = TileType.Water },
        };
        static readonly Lines[] LavaStreams = { new() { Spacing = 60f, Half = 1.5f, Bank = 2.6f, GapFreq = 0.022f, Gap = 0.38f, Warp = 24f, Tile = TileType.Water } };

        static Lines[] Of(Region r) => r switch
        {
            Region.Desert => Canyons,
            Region.Snow => Cracks,
            Region.Swamp => Channels,
            Region.Wasteland => LavaStreams, // the wasteland's water is lava
            _ => None,
        };

        public static void Lay(MapData map, Region[] raw, RegionBarriers.Layout borders, Vector2Int town, int townHalf, GameRandom rng)
        {
            int w = map.Width, h = map.Height;
            float ox = rng.Range(0f, 5000f), oy = rng.Range(0f, 5000f);
            float gx = rng.Range(0f, 5000f), gy = rng.Range(0f, 5000f);
            float wx = rng.Range(0f, 5000f), wy = rng.Range(0f, 5000f);
            var u = new float[w * h];
            foreach (Region region in System.Enum.GetValues(typeof(Region)))
            {
                var families = Of(region);
                for (int k = 0; k < families.Length; k++)
                {
                    var l = families[k];
                    float o = k * 300f;
                    // The noise less its middle, on the region's cells (and a ring round them, for the slope).
                    for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (raw[i] != region && !Beside(raw, w, h, x, y, region)) { u[i] = float.NaN; continue; }
                        if (l.Spacing > 0f)
                        {
                            float r = Vector2.Distance(new Vector2(x, y), town)
                                      + (Mathf.PerlinNoise(x * 0.012f + wx + o, y * 0.012f + wy) - 0.5f) * 2f * l.Warp;
                            u[i] = Mathf.Sin(r * Mathf.PI * 2f / l.Spacing);
                            continue;
                        }
                        float px = x + (Mathf.PerlinNoise(x * 0.02f + wx + o, y * 0.02f + wy) - 0.5f) * 2f * l.Warp;
                        float py = y + (Mathf.PerlinNoise(x * 0.02f + wx, y * 0.02f + wy + o) - 0.5f) * 2f * l.Warp;
                        u[i] = Mathf.PerlinNoise(px * l.Freq + ox + o, py * l.Freq + oy) - 0.5f;
                    }
                    for (int y = 1; y < h - 1; y++)
                    for (int x = 1; x < w - 1; x++)
                    {
                        int i = y * w + x;
                        if (raw[i] != region || map.Regions[i] != region || borders.Distance[i] < BorderMargin) continue;
                        if (RegionBarriers.NearTown(x, y, town, townHalf)) continue;
                        if (map.Tiles[i] is TileType.Road or TileType.TownFloor or TileType.Wall or TileType.Gate) continue;
                        float sx = (u[i + 1] - u[i - 1]) * 0.5f, sy = (u[i + w] - u[i - w]) * 0.5f;
                        float slope = Mathf.Sqrt(sx * sx + sy * sy);
                        if (float.IsNaN(slope) || slope < 1e-5f) continue;
                        float d = Mathf.Abs(u[i]) / slope; // cells to the contour
                        if (d > Mathf.Max(l.Half, l.Bank)) continue;
                        if (Mathf.PerlinNoise(x * l.GapFreq + gx + o, y * l.GapFreq + gy) < l.Gap) continue; // a gap
                        if (d <= l.Half)
                        {
                            if (l.Tile == TileType.Water && OtherWaterBeside(map, x, y, region)) continue;
                            map.Tiles[i] = l.Tile;
                            map.Trees[i] = false;
                        }
                        else if (map.Tiles[i] is TileType.Grass or TileType.Forest)
                        {
                            map.Tiles[i] = TileType.Sand;
                            map.Trees[i] = false;
                        }
                    }
                }
            }
        }

        static bool Beside(Region[] raw, int w, int h, int x, int y, Region region)
        {
            for (int k = 0; k < 4; k++)
            {
                int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                if (nx >= 0 && ny >= 0 && nx < w && ny < h && raw[ny * w + nx] == region) return true;
            }
            return false;
        }

        /// <summary>Water of another region's colours beside the cell (a lava stream must not run into a lake).</summary>
        static bool OtherWaterBeside(MapData map, int x, int y, Region region)
        {
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (!map.InBounds(nx, ny)) continue;
                int j = ny * map.Width + nx;
                if (map.Tiles[j] == TileType.Water && map.Regions[j] != region) return true;
            }
            return false;
        }
    }
}
