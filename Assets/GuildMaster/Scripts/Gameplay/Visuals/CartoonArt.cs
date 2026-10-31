using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Visuals
{
    /// <summary>
    /// Map props in the cartoon style of the LAYERLAB character art: flat fills, thick dark
    /// outlines, rounded shapes, drawn from signed-distance shapes at 32 px per cell (anti-aliased).
    /// Replaces the 16 px pixel art for trees, rocks, nests and buildings.
    /// </summary>
    public static partial class CartoonArt
    {
        public const int PixelsPerUnit = 32;

        static readonly Dictionary<string, Sprite> s_cache = new();
        static readonly Color Ink = new(0.14f, 0.17f, 0.20f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => s_cache.Clear();

        // ---------------- Canvas ----------------

        /// <summary>Small RGBA canvas with signed-distance shape fills (negative = inside).</summary>
        sealed class Canvas
        {
            public readonly int W, H;
            readonly Color[] _px;

            public Canvas(int w, int h)
            {
                W = w; H = h;
                _px = new Color[w * h];
            }

            /// <summary>Fills a shape with an outline band of <paramref name="outlinePx"/> just inside the edge.</summary>
            public void Shape(Func<float, float, float> sdf, Color fill, Color outline, float outlinePx)
            {
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float d = sdf(x + 0.5f, y + 0.5f);
                    if (d > 1f) continue;
                    float cover = Mathf.Clamp01(0.5f - d);             // anti-aliased edge
                    var c = d > -outlinePx ? outline : fill;
                    if (outlinePx > 0f && d > -outlinePx - 1f && d <= -outlinePx)  // soften the fill/outline seam
                        c = Color.Lerp(fill, outline, Mathf.Clamp01(d + outlinePx + 1f));
                    Blend(x, y, c, cover);
                }
            }

            /// <summary>Fills only where the canvas already has paint (e.g. highlights inside a canopy).</summary>
            public void ShapeClipped(Func<float, float, float> sdf, Func<float, float, float> clip, Color fill)
            {
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float d = Mathf.Max(sdf(x + 0.5f, y + 0.5f), clip(x + 0.5f, y + 0.5f));
                    if (d > 1f) continue;
                    Blend(x, y, fill, Mathf.Clamp01(0.5f - d));
                }
            }

            void Blend(int x, int y, Color c, float a)
            {
                if (a <= 0f) return;
                int i = y * W + x;
                var dst = _px[i];
                float outA = a * c.a + dst.a * (1f - a * c.a);
                if (outA <= 0f) return;
                var rgb = ((Vector4)c * (a * c.a) + (Vector4)dst * (dst.a * (1f - a * c.a))) / outA;
                _px[i] = new Color(rgb.x, rgb.y, rgb.z, outA);
            }

            public Sprite ToSprite(string key, Vector2 pivot)
            {
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    name = key,
                };
                tex.SetPixels(_px);
                tex.Apply(false, true);
                var s = Sprite.Create(tex, new Rect(0, 0, W, H), pivot, PixelsPerUnit, 0, SpriteMeshType.FullRect);
                s.name = key;
                s_cache[key] = s;
                return s;
            }
        }

        // ---------------- SDF helpers (pixels) ----------------

        static Func<float, float, float> Circle(float cx, float cy, float r) => (x, y) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;

        static Func<float, float, float> Ellipse(float cx, float cy, float rx, float ry)
            => (x, y) =>
            {
                float nx = (x - cx) / rx, ny = (y - cy) / ry;
                float k = Mathf.Sqrt(nx * nx + ny * ny);
                return (k - 1f) * Mathf.Min(rx, ry);
            };

        static Func<float, float, float> RoundRect(float x0, float y0, float x1, float y1, float r)
            => (x, y) =>
            {
                float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
                float hx = (x1 - x0) * 0.5f - r, hy = (y1 - y0) * 0.5f - r;
                float qx = Mathf.Abs(x - cx) - hx, qy = Mathf.Abs(y - cy) - hy;
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
            };

        /// <summary>A stroke from one point to another with round ends (emblems, P4-87).</summary>
        static Func<float, float, float> Segment(float x0, float y0, float x1, float y1, float r)
            => (x, y) =>
            {
                float dx = x1 - x0, dy = y1 - y0;
                float t = Mathf.Clamp01(((x - x0) * dx + (y - y0) * dy) / Mathf.Max(1e-4f, dx * dx + dy * dy));
                float px = x0 + dx * t - x, py = y0 + dy * t - y;
                return Mathf.Sqrt(px * px + py * py) - r;
            };

        static Func<float, float, float> Union(params Func<float, float, float>[] shapes)
            => (x, y) =>
            {
                float d = float.MaxValue;
                foreach (var s in shapes) d = Mathf.Min(d, s(x, y));
                return d;
            };

        static Color Hex(int rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        static Color Darker(Color c, float k) => new(c.r * k, c.g * k, c.b * k, c.a);
        static Color Lighter(Color c, float k) => Color.Lerp(c, Color.white, k);

        // ---------------- Props ----------------

        static readonly Color[] LeafColors = { Hex(0x4FAE3C), Hex(0x5DBB44), Hex(0x44A046), Hex(0x6BC04A) };

        public static Sprite Tree(int variant) => Tree(variant, World.Region.Grassland);

        /// <summary>
        /// A tree as it grows in a region (P4-55): green here, dry olive scrub in the desert, frosted
        /// white in the snow, dark in the swamp, charred in the wasteland.
        /// </summary>
        public static Sprite Tree(int variant, World.Region region)
        {
            string key = "c_tree:" + variant + ":" + region;
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var rng = new System.Random(variant * 7919 + 13);
            float J() => (float)rng.NextDouble() * 4f - 2f;
            var c = new Canvas(52, 64);
            var (leaf, outline) = region switch
            {
                World.Region.Desert => (Hex(0xA9A24C), Hex(0x4A4420)),
                World.Region.Snow => (Hex(0xE6F0F7), Hex(0x587186)),
                World.Region.Swamp => (Hex(0x3F5B2D), Hex(0x18261A)),
                World.Region.Wasteland => (Hex(0x5E4B42), Hex(0x271B18)),
                _ => (LeafColors[variant % LeafColors.Length], Hex(0x23402A)),
            };
            // Ground shadow, trunk, then a cloud-like canopy.
            c.Shape(Ellipse(26, 7, 15, 4.5f), new Color(0f, 0f, 0f, 0.22f), new Color(0f, 0f, 0f, 0.22f), 0f);
            c.Shape(RoundRect(21, 5, 31, 26, 3f), Hex(0x8A5A30), Hex(0x3E2716), 2.5f);
            var canopy = Union(Circle(26 + J(), 38, 16), Circle(14 + J(), 32 + J(), 11), Circle(38 + J(), 31 + J(), 11), Circle(26 + J(), 50, 11));
            c.Shape(canopy, leaf, outline, 3f);
            c.ShapeClipped(Union(Circle(19, 44, 7), Circle(29, 50, 5)), (x, y) => canopy(x, y) + 4f, Lighter(leaf, 0.28f));
            c.ShapeClipped(Circle(34, 28, 9), (x, y) => canopy(x, y) + 4f, Darker(leaf, 0.82f));
            return c.ToSprite(key, new Vector2(0.5f, 0.12f));
        }

        public static Sprite Rock(int variant) => Rock(variant, World.Region.Grassland);

        /// <summary>A rock in a region's stone (P4-55): sandstone, snow-dusted, mossy, scorched.</summary>
        public static Sprite Rock(int variant, World.Region region)
        {
            string key = "c_rock:" + variant + ":" + region;
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var c = new Canvas(40, 30);
            float g = 0.62f + variant * 0.04f;
            var stone = region switch
            {
                World.Region.Desert => new Color(g * 1.3f, g * 1.1f, g * 0.78f),
                World.Region.Snow => new Color(g * 1.42f, g * 1.47f, g * 1.53f),
                World.Region.Swamp => new Color(g * 0.72f, g * 0.78f, g * 0.64f),
                World.Region.Wasteland => new Color(g * 0.72f, g * 0.5f, g * 0.45f),
                _ => new Color(g, g * 0.98f, g * 1.04f),
            };
            var body = Union(Ellipse(18, 12, 14, 9), Ellipse(26 + variant % 2 * 2, 10, 11, 7));
            c.Shape(Ellipse(20, 5, 16, 3.5f), new Color(0f, 0f, 0f, 0.2f), new Color(0f, 0f, 0f, 0.2f), 0f);
            c.Shape(body, stone, Hex(0x3C3B45), 2.5f);
            c.ShapeClipped(Ellipse(14, 16, 6, 3.5f), (x, y) => body(x, y) + 3f, Lighter(stone, 0.35f));
            return c.ToSprite(key, new Vector2(0.5f, 0.15f));
        }

        /// <param name="def">The owners the guild has seen there; null for a den whose owners nobody has seen (plain earth, P4-76).</param>
        public static Sprite Nest(Data.MonsterDefinition def)
        {
            string key = "c_nest:" + (def != null ? def.id : "unknown");
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var c = new Canvas(60, 40);
            var mound = def != null ? Color.Lerp(def.bodyColor, Hex(0x7A5634), 0.65f) : Hex(0x7A5634);
            var body = Ellipse(30, 14, 27, 13);
            c.Shape(body, mound, Darker(mound, 0.45f), 3f);
            c.ShapeClipped(Ellipse(22, 20, 12, 5), (x, y) => body(x, y) + 3f, Lighter(mound, 0.2f));
            c.Shape(Ellipse(30, 9, 9, 7), Hex(0x1A1418), Hex(0x1A1418), 0f);   // burrow entrance
            return c.ToSprite(key, new Vector2(0.5f, 0.25f));
        }

        /// <summary>Travelling merchant's cart with a striped awning (P4-3).</summary>
        public static Sprite MerchantCart()
        {
            const string key = "c_merchant_cart";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var c = new Canvas(64, 60);
            var wood = Hex(0xA8703C);
            var woodInk = Hex(0x4A2E17);
            c.Shape(Ellipse(32, 6, 27, 4.5f), new Color(0f, 0f, 0f, 0.22f), new Color(0f, 0f, 0f, 0.22f), 0f);
            // Awning poles behind the goods.
            c.Shape(RoundRect(9, 16, 13, 50, 1.5f), Hex(0x7A5230), woodInk, 1.5f);
            c.Shape(RoundRect(51, 16, 55, 50, 1.5f), Hex(0x7A5230), woodInk, 1.5f);
            // Goods: sacks, a crate and potion bottles.
            c.Shape(Ellipse(20, 30, 8, 6), Hex(0xD9C08A), Hex(0x6B5530), 2f);
            c.Shape(RoundRect(28, 24, 42, 36, 2f), Hex(0xC98B4A), woodInk, 2f);
            c.Shape(Circle(47, 29, 4f), Hex(0xE0533F), Hex(0x5A1E18), 1.5f);
            c.Shape(Circle(40, 38, 3.5f), Hex(0x4FA3E0), Hex(0x1F3F5A), 1.5f);
            // Cart body and wheels.
            var body = RoundRect(5, 12, 59, 26, 3f);
            c.Shape(body, wood, woodInk, 2.5f);
            c.ShapeClipped(RoundRect(5, 18, 59, 20, 0f), (x, y) => body(x, y) + 3f, Darker(wood, 0.8f));
            c.Shape(Circle(16, 11, 7f), Hex(0x6B4526), woodInk, 2.5f);
            c.Shape(Circle(48, 11, 7f), Hex(0x6B4526), woodInk, 2.5f);
            c.Shape(Circle(16, 11, 2f), woodInk, woodInk, 0f);
            c.Shape(Circle(48, 11, 2f), woodInk, woodInk, 0f);
            // Striped awning.
            var awning = Union(RoundRect(4, 46, 60, 56, 3f), Circle(11, 46, 4.5f), Circle(25, 46, 4.5f), Circle(39, 46, 4.5f), Circle(53, 46, 4.5f));
            c.Shape(awning, Hex(0xE8574A), Hex(0x6E1F1A), 2.5f);
            for (int i = 0; i < 4; i++)
            {
                float x0 = 11 + i * 14;
                c.ShapeClipped(RoundRect(x0, 38, x0 + 7, 60, 0f), (x, y) => awning(x, y) + 2.5f, Hex(0xF6EEDC));
            }
            return c.ToSprite(key, new Vector2(0.5f, 0.1f));
        }

        // ---------------- Ruins (P4-85) ----------------

        /// <summary>A ruin's way down: a broken stone arch over dark steps (looted: the opening caved in).</summary>
        public static Sprite RuinEntrance(bool caved = false)
        {
            string key = "c_ruin_entrance:" + caved;
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var c = new Canvas(64, 60);
            var stone = Hex(0x9C958A);
            var ink = Hex(0x3A3632);
            c.Shape(Ellipse(32, 7, 28, 5f), new Color(0f, 0f, 0f, 0.25f), new Color(0f, 0f, 0f, 0.25f), 0f);
            // The dark way down, steps fading into it.
            c.Shape(RoundRect(17, 6, 47, 40, 4f), Hex(0x14110F), ink, 2f);
            if (!caved)
                for (int s = 0; s < 3; s++)
                    c.Shape(RoundRect(19 + s * 2, 8 + s * 6, 45 - s * 2, 12 + s * 6, 1f), Darker(stone, 0.55f - s * 0.12f), ink, 1f);
            else
            {
                c.Shape(Circle(26, 16, 7f), Darker(stone, 0.8f), ink, 1.5f);
                c.Shape(Circle(38, 13, 6f), stone, ink, 1.5f);
                c.Shape(Circle(32, 24, 5f), Darker(stone, 0.9f), ink, 1.5f);
            }
            // Pillars and the arch over them, one corner broken off.
            c.Shape(RoundRect(8, 6, 18, 46, 2f), stone, ink, 2f);
            c.Shape(RoundRect(46, 6, 56, 40, 2f), Darker(stone, 0.92f), ink, 2f);
            c.Shape(RoundRect(6, 42, 46, 52, 2f), Lighter(stone, 0.1f), ink, 2f);
            c.Shape(Circle(51, 43, 3f), Darker(stone, 0.85f), ink, 1.5f);
            // Moss.
            c.ShapeClipped(Ellipse(14, 48, 7, 3f), RoundRect(6, 42, 46, 52, 2f), Hex(0x6E8E4A));
            return c.ToSprite(key, new Vector2(0.5f, 0.1f));
        }

        /// <summary>The steps up inside a ruin, back to its way down.</summary>
        public static Sprite RuinStairsUp()
        {
            const string key = "c_ruin_stairs";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var c = new Canvas(40, 36);
            var stone = Hex(0x8E877D);
            var ink = Hex(0x2C2926);
            for (int s = 0; s < 4; s++)
                c.Shape(RoundRect(4 + s * 3, 4 + s * 7, 36 - s * 3, 12 + s * 7, 1.5f), Lighter(stone, s * 0.08f), ink, 1.5f);
            c.Shape(Ellipse(20, 33, 9, 2.5f), new Color(1f, 0.95f, 0.75f, 0.35f), new Color(1f, 0.95f, 0.75f, 0.35f), 0f);
            return c.ToSprite(key, new Vector2(0.5f, 0.1f));
        }

        /// <summary>A treasure chest; open, with the gold showing.</summary>
        public static Sprite TreasureChest(bool open)
        {
            string key = "c_chest:" + open;
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var c = new Canvas(40, 36);
            var wood = Hex(0x8B5A2B);
            var ink = Hex(0x3A2412);
            var band = Hex(0xE2B84A);
            c.Shape(Ellipse(20, 5, 17, 3.5f), new Color(0f, 0f, 0f, 0.25f), new Color(0f, 0f, 0f, 0.25f), 0f);
            c.Shape(RoundRect(5, 4, 35, 20, 2f), wood, ink, 2f);
            c.Shape(RoundRect(5, 11, 35, 14, 0f), band, Darker(band, 0.6f), 1f);
            if (open)
            {
                c.Shape(Ellipse(20, 20, 13, 4f), band, Darker(band, 0.6f), 1.5f);
                c.Shape(Circle(15, 22, 2.5f), Lighter(band, 0.4f), Darker(band, 0.6f), 1f);
                c.Shape(Circle(24, 23, 2f), Lighter(band, 0.5f), Darker(band, 0.6f), 1f);
                c.Shape(RoundRect(6, 22, 34, 33, 3f), Darker(wood, 0.85f), ink, 2f);
            }
            else
            {
                c.Shape(RoundRect(5, 18, 35, 28, 5f), Lighter(wood, 0.08f), ink, 2f);
                c.Shape(RoundRect(17, 13, 23, 21, 1f), band, Darker(band, 0.55f), 1.5f);
            }
            return c.ToSprite(key, new Vector2(0.5f, 0.1f));
        }

        /// <summary>A campfire's ring of stones and crossed logs (P4-66); the flame is drawn on top.</summary>
        public static Sprite CampfireLogs(bool burntOut = false)
        {
            string key = "c_campfire:" + burntOut;
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var c = new Canvas(40, 24);
            var woodInk = Hex(0x4A2E17);
            c.Shape(Ellipse(20, 7, 18, 5f), new Color(0f, 0f, 0f, 0.22f), new Color(0f, 0f, 0f, 0.22f), 0f);
            // Ashes in the middle, then the logs crossed over them.
            c.Shape(Ellipse(20, 9, 10, 4f), burntOut ? Hex(0x55504C) : Hex(0x3A2A22), Hex(0x2A2220), 1f);
            var wood = burntOut ? Hex(0x3E3632) : Hex(0x9A6436);
            c.Shape((x, y) => RoundRect(-11, -2.2f, 11, 2.2f, 2f)(0.9f * (x - 20) + 0.44f * (y - 11), -0.44f * (x - 20) + 0.9f * (y - 11)), wood, woodInk, 1.5f);
            c.Shape((x, y) => RoundRect(-11, -2.2f, 11, 2.2f, 2f)(0.9f * (x - 20) - 0.44f * (y - 11), 0.44f * (x - 20) + 0.9f * (y - 11)), Darker(wood, 0.9f), woodInk, 1.5f);
            // Stones around the pit.
            for (int i = 0; i < 7; i++)
            {
                float t = i / 7f * Mathf.PI * 2f + 0.3f;
                float sx = 20 + Mathf.Cos(t) * 14f, sy = 9 + Mathf.Sin(t) * 5.5f;
                c.Shape(Ellipse(sx, sy, 3.4f, 2.6f), Hex(0x8E8C94), Hex(0x3C3B45), 1.5f);
            }
            return c.ToSprite(key, new Vector2(0.5f, 0.3f));
        }

        /// <summary>One frame of a campfire flame (P4-66): a teardrop of orange with a yellow heart.</summary>
        public static Sprite Flame(int frame)
        {
            frame = Mathf.Abs(frame) % 3;
            string key = "c_flame:" + frame;
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var c = new Canvas(24, 32);
            float lean = (frame - 1) * 1.5f;
            Func<float, float, float> Tongue(float cx, float baseY, float w, float h, float tip)
                => (x, y) =>
                {
                    // A drop: round at the bottom, drawn to a point that leans with the frame.
                    float t = Mathf.Clamp01((y - baseY) / h);
                    float half = w * Mathf.Sqrt(Mathf.Max(0f, 1f - t)) * (1f - 0.35f * t);
                    float centre = cx + tip * t * t;
                    float dx = Mathf.Abs(x - centre) - half;
                    float dy = Mathf.Max(baseY - y - 2f, y - (baseY + h));
                    return Mathf.Max(dx, dy);
                };
            c.Shape(Tongue(12, 3, 9f, 26f - frame, lean), Hex(0xF2762E), Hex(0xB23A1C), 1.5f);
            c.Shape(Tongue(12, 3, 5.5f, 17f + frame, -lean * 0.6f), Hex(0xFFC93C), Hex(0xFFC93C), 0f);
            c.Shape(Tongue(12, 3, 2.5f, 9f, lean * 0.3f), Hex(0xFFF3B8), Hex(0xFFF3B8), 0f);
            return c.ToSprite(key, new Vector2(0.5f, 0.05f));
        }

        /// <summary>
        /// A town gate spanning <paramref name="cells"/> cells (P4-39): a wooden double door seen
        /// from the front when it runs along x (north/south gates), a plank strip seen from above
        /// when it runs along y (east/west). Broken, it is a few splintered stumps.
        /// </summary>
        public static Sprite GateDoor(bool alongX, int cells, bool broken)
        {
            string key = $"c_gate_{(alongX ? "x" : "y")}{cells}{(broken ? "_broken" : "")}";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var wood = Hex(0x9A6436);
            var woodDark = Hex(0x6B4224);
            var woodInk = Hex(0x3E2512);
            var iron = Hex(0x5A6068);
            int len = cells * PixelsPerUnit;
            int w = alongX ? len : 30, h = alongX ? 40 : len;
            var c = new Canvas(w, h);
            if (broken)
            {
                // Splintered stumps along the sill.
                int stumps = cells * 2;
                for (int i = 0; i < stumps; i++)
                {
                    float t = (i + 0.5f) / stumps * len;
                    float tall = 6f + (i * 7 % 5) * 2f;
                    if (alongX) c.Shape(RoundRect(t - 5f, 1f, t + 5f, 1f + tall, 1.5f), woodDark, woodInk, 2f);
                    else c.Shape(RoundRect(4f, t - 5f, 26f, t + 5f, 1.5f), woodDark, woodInk, 2f);
                }
                return c.ToSprite(key, new Vector2(0.5f, alongX ? 0f : 0.5f));
            }
            if (alongX)
            {
                // Two leaves, vertical planks, iron bands and a ring on each.
                float mid = len * 0.5f;
                foreach (var (x0, x1) in new[] { (2f, mid - 1f), (mid + 1f, len - 2f) })
                {
                    var leaf = RoundRect(x0, 1f, x1, h - 2f, 3f);
                    c.Shape(leaf, wood, woodInk, 2.5f);
                    for (float px = x0 + 8f; px < x1 - 3f; px += 8f)
                        c.ShapeClipped(RoundRect(px, 0f, px + 1.5f, h, 0f), (x, y) => leaf(x, y) + 2.5f, woodDark);
                    foreach (float by in new[] { 9f, h - 11f })
                        c.ShapeClipped(RoundRect(x0, by, x1, by + 4f, 0f), (x, y) => leaf(x, y) + 2.5f, iron);
                }
                c.Shape(Circle(mid - 5f, h * 0.5f, 2.2f), Hex(0xC9A24A), woodInk, 1f);
                c.Shape(Circle(mid + 5f, h * 0.5f, 2.2f), Hex(0xC9A24A), woodInk, 1f);
                return c.ToSprite(key, new Vector2(0.5f, 0f));
            }
            var strip = RoundRect(3f, 2f, w - 3f, h - 2f, 3f);
            c.Shape(strip, wood, woodInk, 2.5f);
            for (float py = 10f; py < h - 4f; py += 9f)
                c.ShapeClipped(RoundRect(0f, py, w, py + 1.5f, 0f), (x, y) => strip(x, y) + 2.5f, woodDark);
            foreach (float bx in new[] { 8f, w - 12f })
                c.ShapeClipped(RoundRect(bx, 0f, bx + 4f, h, 0f), (x, y) => strip(x, y) + 2.5f, iron);
            return c.ToSprite(key, new Vector2(0.5f, 0.5f));
        }

        // ---------------- Other villages (P4-84) ----------------

        /// <summary>
        /// Another village seen from afar (3×3 cells): two cottages behind a stake fence, roofs in the colours of
        /// its region. Ruined, it is burnt stumps of wall and rubble.
        /// </summary>
        public static Sprite Village(World.Region region, bool ruined)
        {
            string key = $"c_village:{region}:{ruined}";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            const int S = 96;
            var c = new Canvas(S, S);
            var woodInk = Hex(0x4A2E17);
            c.Shape(Ellipse(48, 22, 44, 16), new Color(0f, 0f, 0f, 0.18f), new Color(0f, 0f, 0f, 0.18f), 0f);
            if (ruined)
            {
                var soot = Hex(0x3A3532);
                c.Shape(Ellipse(48, 30, 36, 14), Hex(0x6E655E), Darker(Hex(0x6E655E), 0.6f), 2f);
                c.Shape(RoundRect(18, 26, 40, 46, 2f), soot, Hex(0x1E1A18), 2.5f);
                c.Shape(RoundRect(54, 24, 80, 40, 2f), soot, Hex(0x1E1A18), 2.5f);
                for (int i = 0; i < 9; i++)
                {
                    float x = 10 + i * 9.5f, tall = 5f + (i * 5 % 4) * 2.5f;
                    c.Shape(RoundRect(x - 2.5f, 12, x + 2.5f, 12 + tall, 1f), Hex(0x2E2622), Hex(0x161210), 1.5f);
                }
                for (int i = 0; i < 6; i++)
                    c.Shape(Ellipse(22 + i * 10, 20 + (i % 2) * 6, 4.5f, 3f), Hex(0x8E8C94), Hex(0x3C3B45), 1.5f);
                return c.ToSprite(key, new Vector2(0.5f, 0.5f));
            }
            var roof = region switch
            {
                World.Region.Desert => Hex(0xD98E4A),
                World.Region.Snow => Hex(0x6F8FB0),
                World.Region.Swamp => Hex(0x6E8B4A),
                World.Region.Wasteland => Hex(0x9A5A3C),
                _ => Hex(0xB9854A),
            };
            var wall = region == World.Region.Desert ? Hex(0xEBD3A0) : Hex(0xF3E6C8);
            // Back fence stakes, the two cottages, then the front stakes over their feet.
            for (int i = 0; i < 9; i++)
            {
                float x = 9 + i * 9.8f;
                c.Shape(RoundRect(x - 2.5f, 52, x + 2.5f, 70, 1.5f), Hex(0x9A6436), woodInk, 1.5f);
            }
            void Cottage(float x0, float y0, float w, float h)
            {
                var walls = RoundRect(x0, y0, x0 + w, y0 + h * 0.55f, 3f);
                c.Shape(walls, wall, Ink, 2.5f);
                c.Shape(RoundRect(x0 + w * 0.5f - 5, y0, x0 + w * 0.5f + 5, y0 + 13, 4f), Hex(0x9A6236), Ink, 2f);
                var top = RoundRect(x0 - 3, y0 + h * 0.5f, x0 + w + 3, y0 + h, 5f);
                c.Shape(top, roof, Ink, 2.5f);
                c.ShapeClipped(RoundRect(x0, y0 + h * 0.5f + 6, x0 + w, y0 + h * 0.5f + 8, 1f), (x, y) => top(x, y) + 2.5f, Darker(roof, 0.8f));
                if (region == World.Region.Snow)
                    c.ShapeClipped(RoundRect(x0 - 3, y0 + h - 7, x0 + w + 3, y0 + h, 3f), (x, y) => top(x, y), Hex(0xF4F8FC));
            }
            Cottage(14, 34, 34, 40);
            Cottage(50, 26, 32, 38);
            for (int i = 0; i < 9; i++)
            {
                float x = 9 + i * 9.8f;
                if (i is 4 or 5) continue; // the gateway
                c.Shape(RoundRect(x - 2.5f, 8, x + 2.5f, 24, 1.5f), Hex(0xA8703C), woodInk, 1.5f);
            }
            c.Shape(RoundRect(6, 14, 43, 17, 1f), Hex(0x7A5230), woodInk, 1f);
            c.Shape(RoundRect(57, 14, 90, 17, 1f), Hex(0x7A5230), woodInk, 1f);
            return c.ToSprite(key, new Vector2(0.5f, 0.5f));
        }

        // ---------------- Buildings ----------------

        public static Sprite Building(Town.BuildingType type, int tilesW, int tilesH)
        {
            string key = $"c_building:{type}:{tilesW}x{tilesH}";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            int w = tilesW * PixelsPerUnit, h = tilesH * PixelsPerUnit;
            var c = new Canvas(w, h);
            if (type == Town.BuildingType.TrainingGround) return TrainingYard(c, key);
            if (type == Town.BuildingType.Watchtower) return Watchtower(c, key);
            if (type == Town.BuildingType.ArrowTower) return ArrowTower(c, key);

            var roof = type switch
            {
                Town.BuildingType.TownHall => Hex(0xD9534F),
                Town.BuildingType.GuildHall => Hex(0x4A7BD0),
                Town.BuildingType.Blacksmith => Hex(0x6B6F7A),
                Town.BuildingType.Apothecary => Hex(0x4DB06B),
                // The guild's facilities (P4-87): a roof colour each and an emblem on the roof.
                Town.BuildingType.Armory => Hex(0x9B3B2E),
                Town.BuildingType.ArmorWorkshop => Hex(0x5B7A99),
                Town.BuildingType.DiningHall => Hex(0xE08A2E),
                Town.BuildingType.PoisonLab => Hex(0x6FA83A),
                Town.BuildingType.RuneForge => Hex(0x7B5BD6),
                Town.BuildingType.FrostAltar => Hex(0x6FBBE0),
                Town.BuildingType.Dormitory => Hex(0xA0643A),
                _ => Hex(0xB9854A),
            };
            var wall = Hex(0xF3E6C8);
            float ink = 3.5f;
            float roofBottom = h * 0.52f;

            // Walls (lower part), with a darker base strip.
            var walls = RoundRect(6, 2, w - 6, roofBottom + 8, 5f);
            c.Shape(walls, wall, Ink, ink);
            c.ShapeClipped(RoundRect(6, 2, w - 6, 12, 3f), (x, y) => walls(x, y) + ink, Hex(0xD8C7A2));

            // Windows and door.
            int bays = Mathf.Max(2, tilesW - 1);
            float bayW = (w - 12f) / bays;
            float doorX = w * 0.5f;
            for (int i = 0; i < bays; i++)
            {
                float cx = 6 + bayW * (i + 0.5f);
                if (Mathf.Abs(cx - doorX) < bayW * 0.6f) continue;
                c.Shape(RoundRect(cx - 8, roofBottom - 26, cx + 8, roofBottom - 8, 4f), Hex(0x9ED8F5), Ink, 2.5f);
                c.Shape(RoundRect(cx - 1, roofBottom - 25, cx + 1, roofBottom - 9, 0.5f), Ink, Ink, 0f);
            }
            c.Shape(RoundRect(doorX - 11, 2, doorX + 11, 34, 8f), Hex(0x9A6236), Ink, 3f);
            c.Shape(Circle(doorX + 5, 18, 2f), Hex(0xF2C94C), Hex(0xF2C94C), 0f);

            // Roof on top with shingle rows.
            var roofShape = RoundRect(1, roofBottom, w - 1, h - 2, 7f);
            c.Shape(roofShape, roof, Ink, ink);
            for (float y = roofBottom + 10; y < h - 8; y += 11)
            {
                float yy = y;
                c.ShapeClipped(RoundRect(4, yy, w - 4, yy + 3, 1.5f), (x, y2) => roofShape(x, y2) + ink, Darker(roof, 0.8f));
            }
            c.ShapeClipped(RoundRect(8, h - 14, w - 8, h - 9, 2f), (x, y) => roofShape(x, y) + ink, Lighter(roof, 0.3f));
            if (type == Town.BuildingType.Blacksmith)
                c.Shape(RoundRect(w - 26, h - 34, w - 14, h - 2, 2f), Hex(0x8A8E99), Ink, 2.5f);   // chimney
            if (type == Town.BuildingType.DiningHall)
                c.Shape(RoundRect(w - 24, h - 30, w - 13, h - 2, 2f), Hex(0xB98A55), Ink, 2.5f);   // kitchen chimney
            Emblem(c, type, w * 0.5f, (roofBottom + h) * 0.5f);
            return c.ToSprite(key, new Vector2(0.5f, 0.5f));
        }

        /// <summary>A round plaque on a facility's roof (P4-87) showing what it does; nothing for the town's own buildings.</summary>
        static void Emblem(Canvas c, Town.BuildingType type, float cx, float cy)
        {
            if (!Town.FacilityRules.IsFacility(type)) return;
            const float r = 13f;
            c.Shape(Circle(cx, cy, r), Hex(0xF6E7C1), Ink, 2.5f);
            var steel = Hex(0xC9CED8);
            switch (type)
            {
                case Town.BuildingType.Armory: // crossed swords
                    c.Shape(Segment(cx - 7, cy - 7, cx + 7, cy + 7, 1.8f), steel, Ink, 1.2f);
                    c.Shape(Segment(cx + 7, cy - 7, cx - 7, cy + 7, 1.8f), steel, Ink, 1.2f);
                    c.Shape(Segment(cx - 7, cy - 3, cx - 3, cy - 7, 1.4f), Hex(0x8A5A30), Ink, 1f);
                    c.Shape(Segment(cx + 7, cy - 3, cx + 3, cy - 7, 1.4f), Hex(0x8A5A30), Ink, 1f);
                    break;
                case Town.BuildingType.ArmorWorkshop: // shield
                    c.Shape(Union(RoundRect(cx - 7, cy - 2, cx + 7, cy + 8, 2f), Circle(cx, cy - 2, 7f)), Hex(0x5B7A99), Ink, 2f);
                    c.Shape(Segment(cx, cy - 7, cx, cy + 6, 1.2f), Hex(0xF2C94C), Hex(0xF2C94C), 0f);
                    break;
                case Town.BuildingType.DiningHall: // bowl with steam
                    c.Shape(Union(Ellipse(cx, cy - 2, 9, 5), RoundRect(cx - 9, cy - 2, cx + 9, cy, 1f)), Hex(0xE8C98A), Ink, 2f);
                    c.Shape(Segment(cx - 3, cy + 3, cx - 3, cy + 8, 1.1f), Hex(0xBFC6D0), Hex(0xBFC6D0), 0f);
                    c.Shape(Segment(cx + 3, cy + 3, cx + 3, cy + 8, 1.1f), Hex(0xBFC6D0), Hex(0xBFC6D0), 0f);
                    break;
                case Town.BuildingType.PoisonLab: // flask with green brew
                    c.Shape(Union(Circle(cx, cy - 3, 6.5f), RoundRect(cx - 2.5f, cy, cx + 2.5f, cy + 8, 1f)), Hex(0x8BE04A), Ink, 2f);
                    c.Shape(Circle(cx - 2, cy - 2, 1.4f), Hex(0xD8FFB0), Hex(0xD8FFB0), 0f);
                    break;
                case Town.BuildingType.RuneForge: // rune ring
                    c.Shape(Circle(cx, cy, 8f), Hex(0xB8A2F5), Ink, 2f);
                    c.Shape(Circle(cx, cy, 4f), Hex(0xF6E7C1), Ink, 1.5f);
                    c.Shape(Segment(cx, cy - 8, cx, cy + 8, 1f), Hex(0x4A2E9A), Hex(0x4A2E9A), 0f);
                    break;
                case Town.BuildingType.FrostAltar: // snowflake
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        float dx = Mathf.Cos(a) * 8f, dy = Mathf.Sin(a) * 8f;
                        c.Shape(Segment(cx - dx, cy - dy, cx + dx, cy + dy, 1.4f), Hex(0x9ED8F5), Hex(0x2B6E9A), 0.8f);
                    }
                    break;
                case Town.BuildingType.Dormitory: // bed
                    c.Shape(RoundRect(cx - 9, cy - 5, cx + 9, cy + 1, 2f), Hex(0xD9534F), Ink, 1.5f);
                    c.Shape(RoundRect(cx - 9, cy - 1, cx - 3, cy + 4, 1.5f), Hex(0xFFF6DA), Ink, 1.5f);
                    c.Shape(RoundRect(cx - 10, cy - 7, cx - 8, cy + 6, 0.8f), Hex(0x8A5A30), Ink, 1f);
                    break;
            }
        }

        /// <summary>
        /// What the building looks like inside (seen when the outside fades out): plank floor inside
        /// thick walls, plus a few pieces of furniture per building type. Null for open yards.
        /// </summary>
        public static Sprite BuildingInterior(Town.BuildingType type, int tilesW, int tilesH)
        {
            if (type is Town.BuildingType.TrainingGround or Town.BuildingType.Watchtower or Town.BuildingType.ArrowTower) return null;
            string key = $"c_interior:{type}:{tilesW}x{tilesH}";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            int w = tilesW * PixelsPerUnit, h = tilesH * PixelsPerUnit;
            var c = new Canvas(w, h);
            var wallBand = Hex(0xC9B48E);
            var floor = Hex(0xD9A866);
            // Outer walls, then the plank floor with board lines.
            var outer = RoundRect(2, 2, w - 2, h - 2, 6f);
            c.Shape(outer, wallBand, Ink, 3.5f);
            var room = RoundRect(12, 12, w - 12, h - 14, 3f);
            c.Shape(room, floor, Darker(wallBand, 0.7f), 2f);
            for (float y = 22; y < h - 16; y += 12)
            {
                float yy = y;
                c.ShapeClipped(RoundRect(14, yy, w - 14, yy + 1.5f, 0.5f), (x, y2) => room(x, y2) + 2f, Darker(floor, 0.85f));
            }
            // Doorway gap in the front wall.
            c.Shape(RoundRect(w * 0.5f - 11, 0, w * 0.5f + 11, 14, 2f), floor, floor, 0f);

            float cx = w * 0.5f;
            switch (type)
            {
                case Town.BuildingType.TownHall:
                    c.Shape(RoundRect(cx - 12, 14, cx + 12, h - 30, 3f), Hex(0xC0392B), Darker(Hex(0xC0392B), 0.6f), 2f);   // carpet
                    c.Shape(RoundRect(cx - 40, h - 44, cx + 40, h - 28, 4f), Hex(0x8A5A30), Ink, 2.5f);                    // long table
                    for (int i = -1; i <= 1; i++) c.Shape(Circle(cx + i * 26, h - 52, 5f), Hex(0xB07A44), Ink, 2f);       // chairs
                    break;
                case Town.BuildingType.GuildHall:
                    c.Shape(RoundRect(cx - 30, h - 30, cx + 30, h - 16, 2f), Hex(0xB98A55), Ink, 2.5f);                   // quest board
                    for (int i = 0; i < 4; i++) c.Shape(RoundRect(cx - 26 + i * 14, h - 27, cx - 16 + i * 14, h - 19, 1f), Hex(0xFFF6DA), Ink, 1f);
                    c.Shape(RoundRect(18, 30, 58, 42, 3f), Hex(0x8A5A30), Ink, 2.5f);                                      // counter
                    c.Shape(Circle(w - 40, 48, 11f), Hex(0xA56B3A), Ink, 2.5f);                                            // round table
                    c.Shape(Circle(w - 58, 48, 4.5f), Hex(0xB07A44), Ink, 2f);
                    c.Shape(Circle(w - 22, 48, 4.5f), Hex(0xB07A44), Ink, 2f);
                    break;
                case Town.BuildingType.Blacksmith:
                    c.Shape(RoundRect(16, h - 42, 44, h - 18, 4f), Hex(0x6B6F7A), Ink, 2.5f);                              // forge
                    c.Shape(Ellipse(30, h - 30, 8, 6), Hex(0xFF9F3A), Hex(0xFF6B2A), 2f);                                  // fire
                    c.Shape(Union(RoundRect(w - 52, 36, w - 20, 44, 2f), RoundRect(w - 42, 24, w - 30, 38, 1f)), Hex(0x4A4E57), Ink, 2f);  // anvil
                    break;
                case Town.BuildingType.Apothecary:
                    c.Shape(RoundRect(14, h - 32, w - 14, h - 20, 2f), Hex(0x8A5A30), Ink, 2f);                            // shelf
                    var bottles = new[] { Hex(0xE74C3C), Hex(0x3498DB), Hex(0x2ECC71), Hex(0xE74C3C), Hex(0x9B59B6) };
                    for (int i = 0; i < bottles.Length && 20 + i * 12 < w - 20; i++) c.Shape(Circle(22 + i * 12, h - 26, 4f), bottles[i], Ink, 1.5f);
                    c.Shape(RoundRect(cx - 18, 26, cx + 18, 36, 2f), Hex(0xA56B3A), Ink, 2f);                              // counter
                    break;
                // The guild's facilities (P4-87).
                case Town.BuildingType.Armory:
                    c.Shape(RoundRect(16, h - 30, w - 16, h - 22, 2f), Hex(0x8A5A30), Ink, 2f);                            // weapon rack
                    for (float x = 22; x < w - 18; x += 10) c.Shape(Segment(x, h - 44, x, h - 20, 1.6f), Hex(0xC9CED8), Ink, 1f);
                    break;
                case Town.BuildingType.ArmorWorkshop:
                    for (int i = -1; i <= 1; i += 2)
                        c.Shape(Union(Circle(cx + i * 18, h - 30, 6f), RoundRect(cx + i * 18 - 7, h - 50, cx + i * 18 + 7, h - 34, 3f)), Hex(0x8C9AAE), Ink, 2f);  // armour stands
                    break;
                case Town.BuildingType.DiningHall:
                    c.Shape(RoundRect(cx - 26, 30, cx + 26, 42, 3f), Hex(0xA56B3A), Ink, 2f);                              // long table
                    c.Shape(RoundRect(18, h - 34, 44, h - 20, 3f), Hex(0x6B6F7A), Ink, 2f);                                // stove
                    break;
                case Town.BuildingType.PoisonLab:
                    c.Shape(RoundRect(14, h - 32, w - 14, h - 20, 2f), Hex(0x8A5A30), Ink, 2f);                            // shelf
                    for (int i = 0; 22 + i * 12 < w - 20; i++) c.Shape(Circle(22 + i * 12, h - 26, 4f), Hex(0x8BE04A), Ink, 1.5f);
                    c.Shape(Circle(cx, 34, 8f), Hex(0x4A4E57), Ink, 2f);                                                    // cauldron
                    break;
                case Town.BuildingType.RuneForge:
                    c.Shape(Circle(cx, h * 0.5f, 16f), Hex(0xB8A2F5), Hex(0x4A2E9A), 2f);                                  // rune circle
                    c.Shape(Circle(cx, h * 0.5f, 9f), floor, Hex(0x4A2E9A), 1.5f);
                    break;
                case Town.BuildingType.FrostAltar:
                    c.Shape(RoundRect(cx - 14, h * 0.5f - 6, cx + 14, h * 0.5f + 8, 3f), Hex(0x9ED8F5), Hex(0x2B6E9A), 2f);  // altar
                    break;
                case Town.BuildingType.Dormitory:
                    for (float x = 20; x < w - 24; x += 22)
                    {
                        c.Shape(RoundRect(x, h - 46, x + 16, h - 18, 3f), Hex(0xD9534F), Ink, 2f);                         // beds
                        c.Shape(RoundRect(x + 2, h - 26, x + 14, h - 20, 2f), Hex(0xFFF6DA), Ink, 1f);
                    }
                    break;
            }
            return c.ToSprite(key, new Vector2(0.5f, 0.5f));
        }

        /// <summary>A stone shaft with battlements, a lookout window and a pennant (P4-13).</summary>
        static Sprite Watchtower(Canvas c, string key)
        {
            int w = c.W, h = c.H;
            var stone = Hex(0xB9BCC6);
            float shaftW = w * 0.34f;
            float cx = w * 0.5f;
            // Shaft, slightly tapered by drawing a wider base block underneath.
            c.Shape(RoundRect(cx - shaftW * 0.72f, 2, cx + shaftW * 0.72f, h * 0.22f, 4f), Darker(stone, 0.85f), Ink, 3.5f);
            var shaft = RoundRect(cx - shaftW, h * 0.16f, cx + shaftW, h * 0.74f, 4f);
            c.Shape(shaft, stone, Ink, 3.5f);
            // Stone courses.
            for (float y = h * 0.24f; y < h * 0.70f; y += 13)
            {
                float yy = y;
                c.ShapeClipped(RoundRect(cx - shaftW, yy, cx + shaftW, yy + 2f, 1f), (x, y2) => shaft(x, y2) + 3.5f, Darker(stone, 0.82f));
            }
            // Lookout window.
            c.Shape(RoundRect(cx - 9, h * 0.46f, cx + 9, h * 0.62f, 7f), Hex(0x2B3242), Ink, 2.5f);
            // Battlemented top: a wider band with three merlons.
            var top = RoundRect(cx - shaftW * 1.25f, h * 0.70f, cx + shaftW * 1.25f, h * 0.82f, 3f);
            c.Shape(top, Lighter(stone, 0.12f), Ink, 3.5f);
            float merlonW = shaftW * 0.5f;
            for (int i = -1; i <= 1; i++)
            {
                float mx = cx + i * shaftW * 0.8f;
                c.Shape(RoundRect(mx - merlonW * 0.5f, h * 0.81f, mx + merlonW * 0.5f, h * 0.92f, 2f), Lighter(stone, 0.12f), Ink, 3f);
            }
            // Pennant on the middle merlon.
            c.Shape(RoundRect(cx - 1.5f, h * 0.88f, cx + 1.5f, h - 3, 1f), Hex(0x8A5A30), Ink, 2f);
            c.Shape(RoundRect(cx + 1.5f, h - 14, cx + 18, h - 4, 2f), Hex(0xD9534F), Ink, 2.5f);
            return c.ToSprite(key, new Vector2(0.5f, 0.5f));
        }

        /// <summary>A timber tower on four legs with a railed platform, a peaked roof and an arrow slit (P4-113).</summary>
        static Sprite ArrowTower(Canvas c, string key)
        {
            int w = c.W, h = c.H;
            var timber = Hex(0xB07A44);
            float cx = w * 0.5f;
            float legW = w * 0.06f, spread = w * 0.3f;
            // Legs, splayed a little, with a cross brace.
            c.Shape(RoundRect(cx - spread - legW, 2, cx - spread + legW, h * 0.56f, 2f), Darker(timber, 0.85f), Ink, 3f);
            c.Shape(RoundRect(cx + spread - legW, 2, cx + spread + legW, h * 0.56f, 2f), Darker(timber, 0.85f), Ink, 3f);
            c.Shape(RoundRect(cx - spread, h * 0.22f, cx + spread, h * 0.27f, 1.5f), Darker(timber, 0.75f), Ink, 2f);
            // The platform: a plank box with a slit to shoot through.
            var box = RoundRect(cx - spread * 1.25f, h * 0.48f, cx + spread * 1.25f, h * 0.74f, 3f);
            c.Shape(box, timber, Ink, 3.5f);
            for (float x = cx - spread * 1.25f + 10; x < cx + spread * 1.25f - 4; x += 10)
            {
                float xx = x;
                c.ShapeClipped(RoundRect(xx, h * 0.48f, xx + 1.5f, h * 0.74f, 0.5f), (px, py) => box(px, py) + 3.5f, Darker(timber, 0.8f));
            }
            c.Shape(RoundRect(cx - 2.5f, h * 0.54f, cx + 2.5f, h * 0.68f, 1.5f), Hex(0x2B2420), Ink, 1.5f);
            // Peaked roof.
            c.Shape(RoundRect(cx - spread * 1.4f, h * 0.73f, cx + spread * 1.4f, h * 0.82f, 3f), Hex(0x9B3B2E), Ink, 3f);
            c.Shape(RoundRect(cx - spread * 0.9f, h * 0.81f, cx + spread * 0.9f, h * 0.9f, 3f), Hex(0x9B3B2E), Ink, 3f);
            c.Shape(RoundRect(cx - spread * 0.4f, h * 0.89f, cx + spread * 0.4f, h - 4, 3f), Hex(0x9B3B2E), Ink, 3f);
            return c.ToSprite(key, new Vector2(0.5f, 0.5f));
        }

        static Sprite TrainingYard(Canvas c, string key)
        {
            int w = c.W, h = c.H;
            var yard = RoundRect(3, 3, w - 3, h - 3, 6f);
            c.Shape(yard, Hex(0xDDBB82), Hex(0x8A5A30), 4f);
            // Fence posts along the border and a couple of training dummies.
            for (float x = 10; x < w - 6; x += 22)
            {
                c.Shape(RoundRect(x - 3, 2, x + 3, 12, 2f), Hex(0xB07A44), Ink, 2f);
                c.Shape(RoundRect(x - 3, h - 12, x + 3, h - 2, 2f), Hex(0xB07A44), Ink, 2f);
            }
            for (int i = 0; i < 2; i++)
            {
                float cx = w * (0.33f + 0.34f * i), cy = h * 0.45f;
                c.Shape(RoundRect(cx - 2, cy - 18, cx + 2, cy + 6, 1f), Hex(0x8A5A30), Ink, 1.5f);
                c.Shape(Ellipse(cx, cy + 4, 9, 11), Hex(0xE8C98A), Ink, 2.5f);
                c.Shape(Circle(cx, cy + 20, 6), Hex(0xE8C98A), Ink, 2.5f);
            }
            return c.ToSprite(key, new Vector2(0.5f, 0.5f));
        }
    }
}
