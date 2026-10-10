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
    public static class CartoonArt
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

        // ---------------- Buildings ----------------

        public static Sprite Building(Town.BuildingType type, int tilesW, int tilesH)
        {
            string key = $"c_building:{type}:{tilesW}x{tilesH}";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            int w = tilesW * PixelsPerUnit, h = tilesH * PixelsPerUnit;
            var c = new Canvas(w, h);
            if (type == Town.BuildingType.TrainingGround) return TrainingYard(c, key);
            if (type == Town.BuildingType.Watchtower) return Watchtower(c, key);

            var roof = type switch
            {
                Town.BuildingType.TownHall => Hex(0xD9534F),
                Town.BuildingType.GuildHall => Hex(0x4A7BD0),
                Town.BuildingType.Blacksmith => Hex(0x6B6F7A),
                Town.BuildingType.Apothecary => Hex(0x4DB06B),
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
            return c.ToSprite(key, new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// What the building looks like inside (seen when the outside fades out): plank floor inside
        /// thick walls, plus a few pieces of furniture per building type. Null for open yards.
        /// </summary>
        public static Sprite BuildingInterior(Town.BuildingType type, int tilesW, int tilesH)
        {
            if (type is Town.BuildingType.TrainingGround or Town.BuildingType.Watchtower) return null;
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
