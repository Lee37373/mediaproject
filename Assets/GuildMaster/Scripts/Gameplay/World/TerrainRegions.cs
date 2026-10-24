using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>Large terrain regions (P4-55, user decision 2026-09-22).</summary>
    public enum Region : byte { Grassland, Desert, Snow, Swamp, Wasteland }

    /// <summary>
    /// Terrain regions (P4-55, user decision 2026-09-22): grassland around the town, then a desert, a snowfield, a swamp
    /// and a volcanic wasteland. Since P4-102 they are laid out zone by zone (<see cref="ZoneMap"/>) and kept per cell in
    /// <see cref="MapData.Regions"/>; before, each was a slice of the compass round the town.
    /// Each region has its own look (painter, trees, minimap, a tint on the monsters living there —
    /// real monster attributes come later) and its own rules: moving is slower and seeing shorter
    /// in the harsher ones (multiplied with the forest rule, see <see cref="ForestSight"/>).
    /// </summary>
    public static class TerrainRegions
    {
        /// <summary>Cells over which two regions' terrain mixes (lakes and woods thin out instead of being cut off).</summary>
        public const float BlendRadius = 7f;
        /// <summary>How far (cells) the painted border frays either way: the regions bite into each other.</summary>
        public const float Fringe = 2.5f;

        /// <summary>
        /// Regions read around a cell (itself and 8 around it at <see cref="BlendRadius"/>), for mixing terrain across
        /// borders (user report 2026-09-22: borders looked cut with a ruler).
        /// </summary>
        public static void SampleAround(MapData map, int x, int y, Region[] into)
        {
            into[0] = map.RegionOf(x, y);
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                int sx = Mathf.Clamp(Mathf.RoundToInt(x + Mathf.Cos(a) * BlendRadius), 0, map.Width - 1);
                int sy = Mathf.Clamp(Mathf.RoundToInt(y + Mathf.Sin(a) * BlendRadius), 0, map.SurfaceHeight - 1);
                into[k + 1] = map.RegionOf(sx, sy);
            }
        }

        /// <summary>The region as painted at a point: the cells' regions read a little off, by fine noise (the border frays).</summary>
        public static Region FrayedAt(MapData map, float x, float y)
        {
            float fx = (Noise(x * 0.45f + 13f, y * 0.45f) - 0.5f) * 2f * Fringe;
            float fy = (Noise(x * 0.45f, y * 0.45f + 29f) - 0.5f) * 2f * Fringe;
            int cx = Mathf.Clamp(Mathf.FloorToInt(x + fx), 0, map.Width - 1), cy = Mathf.Clamp(Mathf.FloorToInt(y + fy), 0, map.Height - 1);
            return map.Regions[cy * map.Width + cx];
        }

        /// <summary>Smooth value noise in 0..1, pure C# (the ground is painted on worker threads; the zones use it too).</summary>
        internal static float Noise(float x, float y)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            float a = Hash01(x0, y0), b = Hash01(x0 + 1, y0), c = Hash01(x0, y0 + 1), d = Hash01(x0 + 1, y0 + 1);
            return a + (b - a) * tx + (c - a) * ty + (a - b - c + d) * tx * ty;
        }

        static float Hash01(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        // ---------------- Rules (P4-55, user decision: movement and sight) ----------------

        /// <summary>Walking cost multiplier off the roads.</summary>
        public static float MoveMultiplier(Region r) => r switch
        {
            Region.Desert => 1.2f,     // loose sand
            Region.Snow => 1.4f,       // deep snow
            Region.Swamp => 1.6f,      // mud
            Region.Wasteland => 1.1f,  // ash and rubble
            _ => 1f,
        };

        /// <summary>Sight multiplier for an eye standing here (not for elevated eyes).</summary>
        public static float SightMultiplier(Region r) => r switch
        {
            Region.Snow => 0.7f,       // blowing snow
            Region.Swamp => 0.6f,      // mist
            Region.Wasteland => 0.8f,  // ash in the air
            _ => 1f,
        };

        /// <summary>Tint for monsters living in a region (until they get real attributes).</summary>
        public static Color MonsterTint(Region r) => r switch
        {
            Region.Desert => new Color(1f, 0.88f, 0.62f),
            Region.Snow => new Color(0.82f, 0.92f, 1f),
            Region.Swamp => new Color(0.66f, 0.78f, 0.55f),
            Region.Wasteland => new Color(1f, 0.66f, 0.55f),
            _ => Color.white,
        };

        public static string Label(Region r) => r switch
        {
            Region.Desert => "사막",
            Region.Snow => "설원",
            Region.Swamp => "늪지",
            Region.Wasteland => "황무지",
            _ => "초원",
        };
    }
}
