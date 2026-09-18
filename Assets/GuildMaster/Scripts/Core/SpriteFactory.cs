using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Core
{
    /// <summary>
    /// Generates and caches placeholder pixel-art sprites in code so the project runs
    /// without art assets. Replace call sites with real sprites later.
    /// </summary>
    public static class SpriteFactory
    {
        public const int PixelsPerUnit = 16;

        static readonly Dictionary<string, Sprite> s_cache = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => s_cache.Clear();

        static Texture2D NewTexture(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            return tex;
        }

        static Sprite Finish(string key, Texture2D tex, Color32[] px, Vector2 pivot, float ppu = PixelsPerUnit)
        {
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, ppu, 0, SpriteMeshType.FullRect);
            sprite.name = key;
            s_cache[key] = sprite;
            return sprite;
        }

        /// <summary>1x1 white sprite (scaled freely).</summary>
        public static Sprite White
        {
            get
            {
                const string key = "white";
                if (s_cache.TryGetValue(key, out var s)) return s;
                var tex = NewTexture(1, 1);
                return Finish(key, tex, new[] { (Color32)Color.white }, new Vector2(0.5f, 0.5f), 1);
            }
        }

        /// <summary>Solid ground tile with subtle per-pixel noise.</summary>
        public static Sprite Tile(string id, Color baseColor, int seed, float noise = 0.06f, Color? speckle = null, float speckleChance = 0f)
        {
            string key = $"tile:{id}:{seed}";
            if (s_cache.TryGetValue(key, out var s)) return s;
            const int n = PixelsPerUnit;
            var tex = NewTexture(n, n);
            var px = new Color32[n * n];
            var rng = new System.Random(seed);
            for (int i = 0; i < px.Length; i++)
            {
                float d = ((float)rng.NextDouble() - 0.5f) * 2f * noise;
                var c = new Color(baseColor.r + d, baseColor.g + d, baseColor.b + d, 1f);
                if (speckle.HasValue && rng.NextDouble() < speckleChance) c = speckle.Value;
                px[i] = c;
            }
            return Finish(key, tex, px, new Vector2(0.5f, 0.5f));
        }

        public static Sprite Circle(int diameterPx, Color fill, Color outline)
        {
            string key = $"circle:{diameterPx}:{ColorUtility.ToHtmlStringRGBA(fill)}:{ColorUtility.ToHtmlStringRGBA(outline)}";
            if (s_cache.TryGetValue(key, out var s)) return s;
            var tex = NewTexture(diameterPx, diameterPx);
            var px = new Color32[diameterPx * diameterPx];
            float r = diameterPx / 2f;
            for (int y = 0; y < diameterPx; y++)
            for (int x = 0; x < diameterPx; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                px[y * diameterPx + x] = d > r ? new Color32(0, 0, 0, 0) : d > r - 1.2f ? (Color32)outline : (Color32)fill;
            }
            return Finish(key, tex, px, new Vector2(0.5f, 0.5f));
        }

        /// <summary>Soft radial gradient (for glows, shadows, vision circles).</summary>
        public static Sprite SoftCircle(int diameterPx = 64)
        {
            string key = $"soft:{diameterPx}";
            if (s_cache.TryGetValue(key, out var s)) return s;
            var tex = NewTexture(diameterPx, diameterPx);
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[diameterPx * diameterPx];
            float r = diameterPx / 2f;
            for (int y = 0; y < diameterPx; y++)
            for (int x = 0; x < diameterPx; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / r);
                px[y * diameterPx + x] = new Color(1, 1, 1, t * t);
            }
            return Finish(key, tex, px, new Vector2(0.5f, 0.5f), diameterPx);
        }

        /// <summary>
        /// Builds a sprite from ASCII rows (top row first). Each char maps to a palette color;
        /// '.' or ' ' is transparent. Pivot defaults to bottom-center (feet).
        /// </summary>
        public static Sprite FromTemplate(string key, string[] rows, IReadOnlyDictionary<char, Color> palette, Vector2? pivot = null)
        {
            if (s_cache.TryGetValue(key, out var s)) return s;
            int h = rows.Length;
            int w = 0;
            foreach (var row in rows) w = Mathf.Max(w, row.Length);
            var tex = NewTexture(w, h);
            var px = new Color32[w * h];
            for (int ry = 0; ry < h; ry++)
            {
                string row = rows[ry];
                int y = h - 1 - ry;
                for (int x = 0; x < w; x++)
                {
                    char ch = x < row.Length ? row[x] : '.';
                    px[y * w + x] = ch == '.' || ch == ' ' || !palette.TryGetValue(ch, out var c)
                        ? new Color32(0, 0, 0, 0)
                        : (Color32)c;
                }
            }
            return Finish(key, tex, px, pivot ?? new Vector2(0.5f, 0f));
        }

        /// <summary>Simple bordered rectangle, used for UI panels and building placeholders.</summary>
        public static Sprite Box(int w, int h, Color fill, Color border, int borderPx = 1)
        {
            string key = $"box:{w}x{h}:{ColorUtility.ToHtmlStringRGBA(fill)}:{ColorUtility.ToHtmlStringRGBA(border)}:{borderPx}";
            if (s_cache.TryGetValue(key, out var s)) return s;
            var tex = NewTexture(w, h);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool edge = x < borderPx || y < borderPx || x >= w - borderPx || y >= h - borderPx;
                px[y * w + x] = edge ? border : fill;
            }
            return Finish(key, tex, px, new Vector2(0.5f, 0.5f));
        }
    }
}
