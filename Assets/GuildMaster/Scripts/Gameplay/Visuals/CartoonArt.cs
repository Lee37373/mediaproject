using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Visuals
{
    /// <summary>
    /// Map props in the cartoon style of the LAYERLAB character art: flat fills, thick dark
    /// outlines, rounded shapes, drawn from signed-distance shapes at 32 px per cell (anti-aliased).
    /// Replaces the 16 px pixel art for trees, rocks, nests and buildings (the buildings: CartoonBuildings.cs).
    /// </summary>
    public static partial class CartoonArt
    {
        public const int PixelsPerUnit = 32;

        static readonly Dictionary<string, Sprite> s_cache = new();
        static readonly Color Ink = new(0.14f, 0.17f, 0.20f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => s_cache.Clear();

        // ---------------- Canvas ----------------

        /// <summary>
        /// Small RGBA canvas with signed-distance shape fills (negative = inside). Shapes are given in logical pixels (32 a
        /// cell); <c>scale</c> paints the picture that many times finer (the buildings, P4-114, to stand as crisp as the
        /// characters beside them). On such a canvas whole 32×32 tiles, then 8×8 blocks, well outside a shape are skipped and
        /// ones well inside filled without a look, so its shapes' distances must not grow faster than the true distance (the
        /// helpers here are exact or under it); the small pictures at scale 1 look at every pixel (the flame's tongue grows
        /// faster at its tip).
        /// </summary>
        sealed class Canvas
        {
            public readonly int W, H;
            readonly int _s, _pw, _ph;
            readonly bool _cull;
            readonly Color[] _px;
            const int Tile = 32, Block = 8;

            public Canvas(int w, int h, int scale = 1)
            {
                W = w; H = h;
                _s = Mathf.Max(1, scale);
                _pw = w * _s; _ph = h * _s;
                _px = new Color[_pw * _ph];
                _cull = _s > 1;
            }

            /// <summary>Fills a shape with an outline band of <paramref name="outlinePx"/> just inside the edge.</summary>
            public void Shape(Func<float, float, float> sdf, Color fill, Color outline, float outlinePx)
            {
                float inv = 1f / _s, o = outlinePx * _s;
                Blocks(sdf, o, (bx, by, ex, ey, solid) =>
                {
                    for (int y = by; y < ey; y++)
                    for (int x = bx; x < ex; x++)
                    {
                        if (solid) { Blend(x, y, fill, 1f); continue; }
                        float d = sdf((x + 0.5f) * inv, (y + 0.5f) * inv) * _s;
                        if (d > 1f) continue;
                        float cover = Mathf.Clamp01(0.5f - d);             // anti-aliased edge
                        var c = d > -o ? outline : fill;
                        if (o > 0f && d > -o - 1f && d <= -o)              // soften the fill/outline seam
                            c = Color.Lerp(fill, outline, Mathf.Clamp01(d + o + 1f));
                        Blend(x, y, c, cover);
                    }
                });
            }

            /// <summary>Fills only where the canvas already has paint (e.g. highlights inside a canopy).</summary>
            public void ShapeClipped(Func<float, float, float> sdf, Func<float, float, float> clip, Color fill)
            {
                float inv = 1f / _s;
                // The clip is looked at only near the shape (a shape's own distance is under the pair's).
                Func<float, float, float> both = (x, y) =>
                {
                    float d = sdf(x, y);
                    return d > 16f ? d : Mathf.Max(d, clip(x, y));
                };
                Blocks(both, 0f, (bx, by, ex, ey, solid) =>
                {
                    for (int y = by; y < ey; y++)
                    for (int x = bx; x < ex; x++)
                    {
                        if (solid) { Blend(x, y, fill, 1f); continue; }
                        float px = (x + 0.5f) * inv, py = (y + 0.5f) * inv;
                        float d = sdf(px, py) * _s;
                        if (d > 1f) continue;
                        d = Mathf.Max(d, clip(px, py) * _s);
                        if (d > 1f) continue;
                        Blend(x, y, fill, Mathf.Clamp01(0.5f - d));
                    }
                });
            }

            /// <summary>
            /// The parts of the canvas a shape may touch, as (x0, y0, x1, y1, solid) in its pixels: solid when wholly inside,
            /// more than <paramref name="inner"/> pixels from the edge. Without culling, the whole canvas at once.
            /// </summary>
            void Blocks(Func<float, float, float> dist, float inner, Action<int, int, int, int, bool> paint)
            {
                if (!_cull) { paint(0, 0, _pw, _ph, false); return; }
                float inv = 1f / _s;
                for (int ty = 0; ty < _ph; ty += Tile)
                for (int tx = 0; tx < _pw; tx += Tile)
                {
                    int tex = Mathf.Min(tx + Tile, _pw), tey = Mathf.Min(ty + Tile, _ph);
                    float tReach = (tex - tx + tey - ty) * 0.36f + 1.5f;   // half the diagonal, and the soft edge
                    float dt = dist((tx + tex) * 0.5f * inv, (ty + tey) * 0.5f * inv) * _s;
                    if (dt > tReach) continue;
                    if (dt < -tReach - inner - 1f) { paint(tx, ty, tex, tey, true); continue; }
                    for (int by = ty; by < tey; by += Block)
                    for (int bx = tx; bx < tex; bx += Block)
                    {
                        int ex = Mathf.Min(bx + Block, tex), ey = Mathf.Min(by + Block, tey);
                        float reach = (ex - bx + ey - by) * 0.36f + 1.5f;
                        float dc = dist((bx + ex) * 0.5f * inv, (by + ey) * 0.5f * inv) * _s;
                        if (dc > reach) continue;
                        paint(bx, by, ex, ey, dc < -reach - inner - 1f);
                    }
                }
            }

            void Blend(int x, int y, Color c, float a)
            {
                if (a <= 0f) return;
                int i = y * _pw + x;
                if (a >= 1f && c.a >= 1f) { _px[i] = c; return; }   // opaque over anything: just the colour
                var dst = _px[i];
                float outA = a * c.a + dst.a * (1f - a * c.a);
                if (outA <= 0f) return;
                var rgb = ((Vector4)c * (a * c.a) + (Vector4)dst * (dst.a * (1f - a * c.a))) / outA;
                _px[i] = new Color(rgb.x, rgb.y, rgb.z, outA);
            }

            public Sprite ToSprite(string key, Vector2 pivot)
            {
                var tex = new Texture2D(_pw, _ph, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    name = key,
                };
                tex.SetPixels(_px);
                tex.Apply(false, true);
                var s = Sprite.Create(tex, new Rect(0, 0, _pw, _ph), pivot, PixelsPerUnit * _s, 0, SpriteMeshType.FullRect);
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

        /// <summary>A polygon (any simple one, corners in order), rounded out by <paramref name="r"/> (P4-114: roofs, gables).</summary>
        static Func<float, float, float> Poly(float r, params Vector2[] v)
        {
            int n = v.Length;
            var px = new float[n];
            var py = new float[n];
            var inv = new float[n];   // 1 / the squared length of the edge ending at each corner
            for (int i = 0, j = n - 1; i < n; j = i, i++)
            {
                px[i] = v[i].x; py[i] = v[i].y;
                inv[i] = 1f / Mathf.Max(1e-6f, (v[j] - v[i]).sqrMagnitude);
            }
            return (x, y) =>
            {
                float d = (x - px[0]) * (x - px[0]) + (y - py[0]) * (y - py[0]), s = 1f;
                for (int i = 0, j = n - 1; i < n; j = i, i++)
                {
                    float ex = px[j] - px[i], ey = py[j] - py[i], wx = x - px[i], wy = y - py[i];
                    float t = Mathf.Clamp01((wx * ex + wy * ey) * inv[i]);
                    float bx = wx - ex * t, by = wy - ey * t;
                    d = Mathf.Min(d, bx * bx + by * by);
                    bool c1 = y >= py[i], c2 = y < py[j], c3 = ex * wy > ey * wx;
                    if (c1 && c2 && c3 || !c1 && !c2 && !c3) s = -s;
                }
                return s * Mathf.Sqrt(d) - r;
            };
        }

        /// <summary>An endless row of circles every <paramref name="step"/> along y = <paramref name="cy"/> (scalloped shingles, P4-114).</summary>
        static Func<float, float, float> CircleRow(float x0, float cy, float step, float r)
            => (x, y) =>
            {
                float lx = x - x0;
                lx -= step * Mathf.Round(lx / step);
                return Mathf.Sqrt(lx * lx + (y - cy) * (y - cy)) - r;
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
    }
}
