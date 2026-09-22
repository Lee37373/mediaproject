using GuildMaster.Core;
using GuildMaster.World;
using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Fog
{
    public interface IVisionSource
    {
        Vector2 VisionPosition { get; }
        float SightRadius { get; }
        /// <summary>Elevated watch (town): walls, mountains and buildings do not block its view.</summary>
        bool SeesOverWalls { get; }
    }

    public interface IFogHideable
    {
        Vector2 FogPosition { get; }
        void SetFogVisible(bool visible);
    }

    public struct FogUpdatedEvent { public int NewlyExploredCells; }

    /// <summary>
    /// Grid fog of war with three states (unexplored / explored / visible).
    /// Vision is recomputed per cell on a fixed tick and each cell's alpha fades smoothly. Rendering
    /// (P4-7) upsamples the cell alphas to <see cref="RenderScale"/> texels per cell after a small
    /// 1-2-1 blur, so edges are round instead of stair-stepped. Explored-but-unseen ground is covered
    /// with a cool grey-blue instead of black, which reads as faded (desaturated) memory.
    /// </summary>
    public class FogOfWar : MonoBehaviour
    {
        public const int SortingOrder = 3000;
        /// <summary>Render texels per map cell (the simulation stays one value per cell).</summary>
        public const int RenderScale = 2;

        /// <summary>Tint over explored ground (faded look); unexplored ground is near black.</summary>
        static readonly Color ExploredTint = new(0.16f, 0.18f, 0.27f);
        static readonly Color UnexploredTint = new(0.02f, 0.02f, 0.04f);

        static readonly List<IVisionSource> s_sources = new();
        static readonly List<IFogHideable> s_hideables = new();

        int _w, _h;
        bool[] _explored;
        int[] _visibleStamp;
        int _stamp;
        float[] _alpha;
        float[] _blurred;
        Color32[] _pixels;
        int _tw, _th;
        Texture2D _texture;
        Sprite _sprite;
        float _exploredAlpha = 0.55f;
        float _tickInterval = 0.1f;
        float _timer;
        bool _dirty;

        public int ExploredCount { get; private set; }
        public int Width => _w;
        public int Height => _h;

        /// <summary>Debug: disables fog rendering and hiding.</summary>
        public bool Disabled { get; set; }

        public static void Register(IVisionSource s) { if (!s_sources.Contains(s)) s_sources.Add(s); }
        public static void Unregister(IVisionSource s) => s_sources.Remove(s);
        public static void Register(IFogHideable h) { if (!s_hideables.Contains(h)) s_hideables.Add(h); }
        public static void Unregister(IFogHideable h) => s_hideables.Remove(h);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_sources.Clear(); s_hideables.Clear(); }

        /// <summary>Runtime assets outlive the scene unless destroyed (PlayMode tests boot many times per session).</summary>
        void OnDestroy()
        {
            if (_sprite != null) Destroy(_sprite);
            if (_texture != null) Destroy(_texture);
        }

        public void Init(int width, int height, float exploredAlpha, float tickInterval)
        {
            _w = width;
            _h = height;
            _exploredAlpha = exploredAlpha;
            _tickInterval = tickInterval;
            int n = width * height;
            _explored = new bool[n];
            _visibleStamp = new int[n];
            _alpha = new float[n];
            _blurred = new float[n];
            for (int i = 0; i < n; i++) _alpha[i] = 1f;
            _tw = width * RenderScale;
            _th = height * RenderScale;
            _pixels = new Color32[_tw * _th];

            _texture = new Texture2D(_tw, _th, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "FogTexture"
            };
            Render();

            // RenderScale pixels per unit: the sprite still covers exactly the map.
            var sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = _sprite = Sprite.Create(_texture, new Rect(0, 0, _tw, _th), Vector2.zero, RenderScale, 0, SpriteMeshType.FullRect);
            sr.sortingOrder = SortingOrder;
            transform.position = Vector3.zero;
            Tick(0f);
        }

        public bool IsVisible(Vector2Int c) => Disabled || (InBounds(c) && _visibleStamp[c.y * _w + c.x] == _stamp);
        public bool IsVisible(Vector2 world) => IsVisible(new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y)));
        public bool IsExplored(Vector2Int c) => Disabled || (InBounds(c) && _explored[c.y * _w + c.x]);

        /// <summary>Real vision regardless of the debug fog toggle (F3). Knowledge systems use these.</summary>
        public bool IsVisibleRaw(Vector2Int c) => InBounds(c) && _visibleStamp[c.y * _w + c.x] == _stamp;
        public bool IsVisibleRaw(Vector2 world) => IsVisibleRaw(new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y)));
        public bool IsExploredRaw(Vector2Int c) => InBounds(c) && _explored[c.y * _w + c.x];
        bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < _w && c.y < _h;

        /// <summary>Fraction (0..1) of explored cells inside a circle.</summary>
        public float ExploredFraction(Vector2 center, float radius)
        {
            int total = 0, explored = 0;
            ForEachCellInCircle(center, radius, i => { total++; if (_explored[i]) explored++; });
            return total == 0 ? 1f : explored / (float)total;
        }

        /// <summary>Permanently reveals an area (e.g. the town at start).</summary>
        public void Reveal(Vector2 center, float radius)
        {
            ForEachCellInCircle(center, radius, i =>
            {
                if (!_explored[i]) { _explored[i] = true; ExploredCount++; }
            });
            _dirty = true;
        }

        void Update()
        {
            if (_explored == null) return;
            // Unscaled so fog keeps fading while paused/sped up consistently.
            float dt = Time.unscaledDeltaTime;
            _timer += dt;
            if (_timer >= _tickInterval)
            {
                Tick(_timer);
                _timer = 0f;
            }
        }

        void Tick(float elapsed)
        {
            _stamp++;
            int newly = 0;
            Services.TryGet<WorldService>(out var world);
            foreach (var src in s_sources)
            {
                var eye = src.VisionPosition;
                bool occlude = world != null && !src.SeesOverWalls;
                // Forests (P4-48): a third of the sight inside one, and seen from outside one they
                // block like a wall. Elevated eyes (town watch, watchtowers) ignore both.
                float radius = occlude ? ForestSight.Radius(world, eye, src.SightRadius) : src.SightRadius;
                bool forestBlocks = occlude && ForestSight.ForestBlocks(world, eye);
                ForEachCellInCircle(eye, radius, i =>
                {
                    if (_visibleStamp[i] == _stamp) return; // already seen this tick
                    // Hard cover (mountain, wall, building) hides what lies behind it; the
                    // blocking cell itself stays visible. Trees do not block the fog.
                    if (occlude && world.Raycast(eye, new Vector2(i % _w + 0.5f, i / _w + 0.5f), out _, out _, forestBlocks)) return;
                    _visibleStamp[i] = _stamp;
                    if (!_explored[i]) { _explored[i] = true; newly++; }
                });
            }
            ExploredCount += newly;

            float fade = elapsed <= 0f ? 1f : Mathf.Clamp01(elapsed * 5f);
            bool changed = _dirty;
            for (int i = 0; i < _alpha.Length; i++)
            {
                float target = Disabled || _visibleStamp[i] == _stamp ? 0f : _explored[i] ? _exploredAlpha : 1f;
                float a = _alpha[i];
                if (Mathf.Abs(a - target) < 0.004f) continue;
                a = Mathf.Lerp(a, target, fade);
                if (Mathf.Abs(a - target) < 0.01f) a = target;
                _alpha[i] = a;
                changed = true;
            }
            if (changed)
            {
                Render();
                _dirty = false;
            }

            foreach (var h in s_hideables) h.SetFogVisible(IsVisible(h.FogPosition));

            if (newly > 0) Core.EventBus.Publish(new FogUpdatedEvent { NewlyExploredCells = newly });
        }

        /// <summary>
        /// Cell alphas → render texture: 1-2-1 blur per cell, then bilinear upsampling to
        /// RenderScale texels per cell; colour goes from the explored tint to near black as alpha
        /// rises from the explored level to fully unexplored.
        /// </summary>
        void Render()
        {
            BlurCells();
            if (_palette == null) BuildPalette();
            if (_sx0 == null) BuildSampleTables();
            var blurred = _blurred;
            var pixels = _pixels;
            var palette = _palette;
            var line = _line;
            int w = _w, tw = _tw;
            for (int ty = 0; ty < _th; ty++)
            {
                // Vertical blend of the two cell rows for this texel row, then horizontal per texel.
                int r0 = _sy0[ty] * w, r1 = _sy1[ty] * w;
                float fy = _sfy[ty];
                for (int x = 0; x < w; x++) line[x] = blurred[r0 + x] + (blurred[r1 + x] - blurred[r0 + x]) * fy;
                int row = ty * tw;
                for (int tx = 0; tx < tw; tx++)
                {
                    float l = line[_sx0[tx]];
                    float a = l + (line[_sx1[tx]] - l) * _sfx[tx];
                    int idx = (int)(a * 255f + 0.5f);
                    pixels[row + tx] = palette[idx < 0 ? 0 : idx > 255 ? 255 : idx];
                }
            }
            _texture.SetPixels32(pixels);
            _texture.Apply(false);
        }

        /// <summary>
        /// Colour per alpha byte: the explored tint up to the explored level, darkening to near
        /// black as it approaches fully unexplored.
        /// </summary>
        void BuildPalette()
        {
            _palette = new Color32[256];
            float span = Mathf.Max(0.001f, 1f - _exploredAlpha);
            for (int i = 0; i < 256; i++)
            {
                float a = i / 255f;
                var c = Color.Lerp(ExploredTint, UnexploredTint, Mathf.Clamp01((a - _exploredAlpha) / span));
                _palette[i] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)i);
            }
        }

        Color32[] _palette;
        // Bilinear sample positions per texel column/row (cell indices and blend factor), fixed per map.
        int[] _sx0, _sx1, _sy0, _sy1;
        float[] _sfx, _sfy, _line;

        void BuildSampleTables()
        {
            Axis(_tw, _w, out _sx0, out _sx1, out _sfx);
            Axis(_th, _h, out _sy0, out _sy1, out _sfy);
            _line = new float[_w];

            static void Axis(int texels, int cells, out int[] i0, out int[] i1, out float[] f)
            {
                i0 = new int[texels];
                i1 = new int[texels];
                f = new float[texels];
                for (int t = 0; t < texels; t++)
                {
                    // Texel centre in cell space; cell centres sit at integer + 0.5.
                    float p = (t + 0.5f) / RenderScale - 0.5f;
                    int a = Mathf.Clamp(Mathf.FloorToInt(p), 0, cells - 1);
                    i0[t] = a;
                    i1[t] = Mathf.Min(a + 1, cells - 1);
                    f[t] = Mathf.Clamp01(p - a);
                }
            }
        }

        /// <summary>Separable 1-2-1 blur of the cell alphas into <see cref="_blurred"/> (edges clamped).</summary>
        void BlurCells()
        {
            // Horizontal pass into _blurred, then vertical pass back through a row buffer.
            var src = _alpha;
            var dst = _blurred;
            int w = _w, h = _h;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    float l = src[row + (x > 0 ? x - 1 : 0)], c = src[row + x], r = src[row + (x < w - 1 ? x + 1 : x)];
                    dst[row + x] = (l + 2f * c + r) * 0.25f;
                }
            }
            if (_column == null || _column.Length != h) _column = new float[h];
            var col = _column;
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++) col[y] = dst[y * w + x];
                for (int y = 0; y < h; y++)
                {
                    float d = col[y > 0 ? y - 1 : 0], c = col[y], u = col[y < h - 1 ? y + 1 : y];
                    dst[y * w + x] = (d + 2f * c + u) * 0.25f;
                }
            }
        }

        float[] _column;

        void ForEachCellInCircle(Vector2 center, float radius, System.Action<int> action)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
            int maxX = Mathf.Min(_w - 1, Mathf.FloorToInt(center.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
            int maxY = Mathf.Min(_h - 1, Mathf.FloorToInt(center.y + radius));
            float r2 = radius * radius;
            for (int y = minY; y <= maxY; y++)
            {
                float dy = y + 0.5f - center.y;
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x + 0.5f - center.x;
                    if (dx * dx + dy * dy <= r2) action(y * _w + x);
                }
            }
        }
    }
}
