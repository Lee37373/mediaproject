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

        public static Sprite Tree(int variant)
        {
            string key = "c_tree:" + variant;
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var rng = new System.Random(variant * 7919 + 13);
            float J() => (float)rng.NextDouble() * 4f - 2f;
            var c = new Canvas(52, 64);
            var leaf = LeafColors[variant % LeafColors.Length];
            var outline = Hex(0x23402A);
            // Ground shadow, trunk, then a cloud-like canopy.
            c.Shape(Ellipse(26, 7, 15, 4.5f), new Color(0f, 0f, 0f, 0.22f), new Color(0f, 0f, 0f, 0.22f), 0f);
            c.Shape(RoundRect(21, 5, 31, 26, 3f), Hex(0x8A5A30), Hex(0x3E2716), 2.5f);
            var canopy = Union(Circle(26 + J(), 38, 16), Circle(14 + J(), 32 + J(), 11), Circle(38 + J(), 31 + J(), 11), Circle(26 + J(), 50, 11));
            c.Shape(canopy, leaf, outline, 3f);
            c.ShapeClipped(Union(Circle(19, 44, 7), Circle(29, 50, 5)), (x, y) => canopy(x, y) + 4f, Lighter(leaf, 0.28f));
            c.ShapeClipped(Circle(34, 28, 9), (x, y) => canopy(x, y) + 4f, Darker(leaf, 0.82f));
            return c.ToSprite(key, new Vector2(0.5f, 0.12f));
        }

        public static Sprite Rock(int variant)
        {
            string key = "c_rock:" + variant;
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var c = new Canvas(40, 30);
            float g = 0.62f + variant * 0.04f;
            var stone = new Color(g, g * 0.98f, g * 1.04f);
            var body = Union(Ellipse(18, 12, 14, 9), Ellipse(26 + variant % 2 * 2, 10, 11, 7));
            c.Shape(Ellipse(20, 5, 16, 3.5f), new Color(0f, 0f, 0f, 0.2f), new Color(0f, 0f, 0f, 0.2f), 0f);
            c.Shape(body, stone, Hex(0x3C3B45), 2.5f);
            c.ShapeClipped(Ellipse(14, 16, 6, 3.5f), (x, y) => body(x, y) + 3f, Lighter(stone, 0.35f));
            return c.ToSprite(key, new Vector2(0.5f, 0.15f));
        }

        public static Sprite Nest(Data.MonsterDefinition def)
        {
            string key = "c_nest:" + def.id;
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var c = new Canvas(60, 40);
            var mound = Color.Lerp(def.bodyColor, Hex(0x7A5634), 0.65f);
            var body = Ellipse(30, 14, 27, 13);
            c.Shape(body, mound, Darker(mound, 0.45f), 3f);
            c.ShapeClipped(Ellipse(22, 20, 12, 5), (x, y) => body(x, y) + 3f, Lighter(mound, 0.2f));
            c.Shape(Ellipse(30, 9, 9, 7), Hex(0x1A1418), Hex(0x1A1418), 0f);   // burrow entrance
            return c.ToSprite(key, new Vector2(0.5f, 0.25f));
        }

        // ---------------- Buildings ----------------

        public static Sprite Building(Town.BuildingType type, int tilesW, int tilesH)
        {
            string key = $"c_building:{type}:{tilesW}x{tilesH}";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            int w = tilesW * PixelsPerUnit, h = tilesH * PixelsPerUnit;
            var c = new Canvas(w, h);
            if (type == Town.BuildingType.TrainingGround) return TrainingYard(c, key);

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
