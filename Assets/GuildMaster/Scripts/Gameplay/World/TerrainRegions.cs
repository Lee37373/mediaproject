using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>Large terrain regions (P4-55, user decision 2026-09-22).</summary>
    public enum Region : byte { Grassland, Desert, Snow, Swamp, Wasteland }

    /// <summary>Where the regions lie on one map (drawn from the seed by <see cref="MapGenerator"/>).</summary>
    public struct RegionLayout
    {
        public Vector2 Center;
        /// <summary>Grassland out to about here around the town (the early rings stay familiar).</summary>
        public float GrassRadius;
        /// <summary>Turns the four outer regions round the town, so each seed puts them elsewhere.</summary>
        public float Rotation;
        public float NoiseX, NoiseY;
        /// <summary>False on hand-made maps (tests): everything is grassland.</summary>
        public bool Enabled;
    }

    /// <summary>
    /// Terrain regions (P4-55, user decision 2026-09-22): grassland around the town, then by
    /// direction a desert, a snowfield, a swamp and a volcanic wasteland, with wobbling borders.
    /// Each region has its own look (painter, trees, minimap, a tint on the monsters living there —
    /// real monster attributes come later) and its own rules: moving is slower and seeing shorter
    /// in the harsher ones (multiplied with the forest rule, see <see cref="ForestSight"/>).
    /// </summary>
    public static class TerrainRegions
    {
        const float BorderWobble = 12f;
        /// <summary>How far (cells) a point is pushed around before its region is read: winding borders.</summary>
        const float DomainWarp = 14f;
        const float AngleWobble = 0.45f; // radians: bends the borders without letting one region swallow a neighbour

        static readonly Region[] Outer = { Region.Desert, Region.Snow, Region.Swamp, Region.Wasteland };

        public static RegionLayout Layout(Vector2 center, float mapSize, float rotation, float noiseX, float noiseY) => new()
        {
            Center = center,
            GrassRadius = mapSize * 0.21f, // 256 → ~54 cells: the slime and goblin rings
            Rotation = rotation,
            NoiseX = noiseX,
            NoiseY = noiseY,
            Enabled = true,
        };

        /// <summary>The region at a point (continuous, so borders can be painted smooth).</summary>
        public static Region At(in RegionLayout l, float x, float y)
        {
            if (!l.Enabled) return Region.Grassland;
            // Domain warp: the point is nudged by noise first, so borders wind instead of running
            // as straight spokes from the town (measured in play: they looked ruled).
            float wx = x + (Noise(x * 0.035f + l.NoiseX + 91f, y * 0.035f + l.NoiseY) - 0.5f) * 2f * DomainWarp;
            float wy = y + (Noise(x * 0.035f + l.NoiseX, y * 0.035f + l.NoiseY + 57f) - 0.5f) * 2f * DomainWarp;
            x = wx;
            y = wy;
            var d = new Vector2(x, y) - l.Center;
            float wobble = (Noise(x * 0.025f + l.NoiseX, y * 0.025f + l.NoiseY) - 0.5f) * 2f * BorderWobble;
            if (d.magnitude < l.GrassRadius + wobble) return Region.Grassland;
            float angle = Mathf.Atan2(d.y, d.x) + l.Rotation
                          + (Noise(x * 0.012f + l.NoiseY, y * 0.012f + l.NoiseX) - 0.5f) * 2f * AngleWobble;
            float t = Mathf.Repeat(angle / (Mathf.PI * 2f), 1f);
            return Outer[Mathf.Min(Outer.Length - 1, (int)(t * Outer.Length))];
        }

        /// <summary>Cells over which two regions' terrain mixes (lakes and woods thin out instead of being cut off).</summary>
        public const float BlendRadius = 7f;
        /// <summary>How far (cells) the painted border frays either way: the regions bite into each other.</summary>
        public const float Fringe = 2.5f;

        /// <summary>
        /// Regions sampled around a point (itself and 8 around it at <see cref="BlendRadius"/>), for
        /// mixing terrain across borders (user report 2026-09-22: borders looked cut with a ruler).
        /// </summary>
        public static void SampleAround(in RegionLayout l, float x, float y, Region[] into)
        {
            into[0] = At(l, x, y);
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                into[k + 1] = At(l, x + Mathf.Cos(a) * BlendRadius, y + Mathf.Sin(a) * BlendRadius);
            }
        }

        /// <summary>The region as painted: the border frayed by fine noise (the cell rule stays <see cref="At"/>).</summary>
        public static Region AtFrayed(in RegionLayout l, float x, float y)
        {
            float fx = (Noise(x * 0.45f + l.NoiseY + 13f, y * 0.45f + l.NoiseX) - 0.5f) * 2f * Fringe;
            float fy = (Noise(x * 0.45f + l.NoiseY, y * 0.45f + l.NoiseX + 29f) - 0.5f) * 2f * Fringe;
            return At(l, x + fx, y + fy);
        }

        /// <summary>Smooth value noise in 0..1, pure C# (the ground is painted on worker threads).</summary>
        static float Noise(float x, float y)
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
