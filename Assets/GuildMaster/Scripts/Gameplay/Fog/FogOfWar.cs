using GuildMaster.Core;
using GuildMaster.World;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Profiling;
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
    ///
    /// Only what changes is worked on (P5-2): a tick fades just the cells that came into or went
    /// out of sight (or are still fading), and redraws just the <see cref="Block"/>-cell blocks
    /// around them. It used to fade, blur and redraw the whole map ten times a second.
    /// </summary>
    public class FogOfWar : MonoBehaviour, ISaveable
    {
        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SaveData
        {
            public int width;
            public int height;
            /// <summary>Explored cells, one bit each (row-major), base64.</summary>
            public string explored;
        }

        public string SaveKey => "fog";

        public string Capture()
        {
            var bits = new byte[(_explored.Length + 7) / 8];
            for (int i = 0; i < _explored.Length; i++)
                if (_explored[i]) bits[i >> 3] |= (byte)(1 << (i & 7));
            return JsonUtility.ToJson(new SaveData { width = _w, height = _h, explored = System.Convert.ToBase64String(bits) });
        }

        /// <summary>What was explored comes back explored; what is in sight is worked out on the next tick.</summary>
        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            if (s.width != _w || s.height != _h) { ctx.Warnings.Add($"fog: saved {s.width}x{s.height}, map {_w}x{_h}"); return; }
            var bits = System.Convert.FromBase64String(s.explored);
            ExploredCount = 0;
            for (int i = 0; i < _explored.Length; i++)
            {
                bool e = (bits[i >> 3] & (1 << (i & 7))) != 0;
                _explored[i] = e;
                if (e) ExploredCount++;
                // Cells the boot's first tick already sees stay clear: the next tick does not count
                // them as newly in sight, so they would not fade back.
                _alpha[i] = _visibleStamp[i] == _stamp ? 0f : e ? _exploredAlpha : 1f;
            }
            for (int b = 0; b < _blockDirty.Length; b++) MarkBlock(b);
            Render();
        }

        public const int SortingOrder = 3000;
        /// <summary>Render texels per map cell (the simulation stays one value per cell).</summary>
        public const int RenderScale = 2;
        /// <summary>Redraw granularity in cells.</summary>
        const int Block = 16;
        /// <summary>A cell's alpha reaches the texels of cells this far away (blur ±1, bilinear ±1).</summary>
        const int Reach = 2;

        /// <summary>Tint over explored ground (faded look); unexplored ground is near black.</summary>
        static readonly Color ExploredTint = new(0.16f, 0.18f, 0.27f);
        static readonly Color UnexploredTint = new(0.02f, 0.02f, 0.04f);

        static readonly List<IVisionSource> s_sources = new();
        static readonly List<IFogHideable> s_hideables = new();

        static readonly ProfilerMarker s_visionMarker = new("Fog.Vision"), s_fadeMarker = new("Fog.Fade"), s_renderMarker = new("Fog.Render");

        int _w, _h;
        bool[] _explored;
        int[] _visibleStamp;
        int _stamp;
        float[] _alpha;
        float[] _blurred;
        int _tw, _th;
        Texture2D _texture;
        Sprite _sprite;
        float _exploredAlpha = 0.55f;
        float _tickInterval = 0.1f;
        float _timer;

        /// <summary>Cells whose alpha may not be at its target yet.</summary>
        readonly List<int> _active = new();
        bool[] _isActive;
        /// <summary>Cells seen last tick: those not seen this tick start fading out.</summary>
        List<int> _seen = new(), _seenBefore = new();
        int _bw, _bh;
        bool[] _blockDirty;
        readonly List<int> _dirtyBlocks = new();
        bool _refreshAll;

        public int ExploredCount { get; private set; }
        public int Width => _w;
        public int Height => _h;

        bool _disabled;

        /// <summary>Debug: disables fog rendering and hiding.</summary>
        public bool Disabled
        {
            get => _disabled;
            set
            {
                if (_disabled == value) return;
                _disabled = value;
                _refreshAll = true;
            }
        }

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
            _isActive = new bool[n];
            // Stamps start above the arrays' zeros: nothing counts as seen before the first tick.
            _stamp = 1;
            for (int i = 0; i < n; i++) _alpha[i] = 1f;
            _tw = width * RenderScale;
            _th = height * RenderScale;
            _bw = (width + Block - 1) / Block;
            _bh = (height + Block - 1) / Block;
            _blockDirty = new bool[_bw * _bh];

            _texture = new Texture2D(_tw, _th, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "FogTexture"
            };
            for (int b = 0; b < _blockDirty.Length; b++) MarkBlock(b);
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
                if (_explored[i]) return;
                _explored[i] = true;
                ExploredCount++;
                Activate(i);
            });
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

        void Activate(int i)
        {
            if (_isActive[i]) return;
            _isActive[i] = true;
            _active.Add(i);
        }

        void Tick(float elapsed)
        {
            _stamp++;
            int newly;
            using (s_visionMarker.Auto()) newly = UpdateVision();
            ExploredCount += newly;

            using (s_fadeMarker.Auto())
            {
                if (_refreshAll)
                {
                    _refreshAll = false;
                    for (int i = 0; i < _alpha.Length; i++) Activate(i);
                }
                Fade(elapsed <= 0f ? 1f : Mathf.Clamp01(elapsed * 5f));
            }
            using (s_renderMarker.Auto()) Render();

            foreach (var h in s_hideables) h.SetFogVisible(IsVisible(h.FogPosition));

            if (newly > 0) Core.EventBus.Publish(new FogUpdatedEvent { NewlyExploredCells = newly });
        }

        /// <summary>Stamps this tick's visible cells; cells entering or leaving sight start fading. Returns newly explored cells.</summary>
        int UpdateVision()
        {
            int newly = 0;
            (_seen, _seenBefore) = (_seenBefore, _seen);
            _seen.Clear();
            int lastTick = _stamp - 1;
            Services.TryGet<WorldService>(out var world);
            foreach (var src in s_sources)
            {
                var eye = src.VisionPosition;
                bool occlude = world != null && !src.SeesOverWalls;
                // Forests (P4-48): a third of the sight inside one, and seen from outside one they
                // block like a wall. Elevated eyes (town watch, watchtowers) ignore both.
                float radius = occlude ? ForestSight.Radius(world, eye, src.SightRadius) : src.SightRadius;
                bool forestBlocks = occlude && ForestSight.ForestBlocks(world, eye);
                int minX = Mathf.Max(0, Mathf.FloorToInt(eye.x - radius));
                int maxX = Mathf.Min(_w - 1, Mathf.FloorToInt(eye.x + radius));
                int minY = Mathf.Max(0, Mathf.FloorToInt(eye.y - radius));
                int maxY = Mathf.Min(_h - 1, Mathf.FloorToInt(eye.y + radius));
                float r2 = radius * radius;
                for (int y = minY; y <= maxY; y++)
                {
                    float dy = y + 0.5f - eye.y;
                    for (int x = minX; x <= maxX; x++)
                    {
                        float dx = x + 0.5f - eye.x;
                        if (dx * dx + dy * dy > r2) continue;
                        int i = y * _w + x;
                        int stamp = _visibleStamp[i];
                        if (stamp == _stamp) continue; // already seen this tick
                        // Hard cover (mountain, wall, building) hides what lies behind it; the
                        // blocking cell itself stays visible. Trees do not block the fog.
                        if (occlude && world.Raycast(eye, new Vector2(x + 0.5f, y + 0.5f), out _, out _, forestBlocks)) continue;
                        _visibleStamp[i] = _stamp;
                        _seen.Add(i);
                        if (stamp != lastTick) Activate(i);
                        if (!_explored[i]) { _explored[i] = true; newly++; }
                    }
                }
            }
            foreach (int i in _seenBefore)
                if (_visibleStamp[i] != _stamp) Activate(i);
            return newly;
        }

        /// <summary>Moves the active cells' alphas towards their targets; settled cells leave the list.</summary>
        void Fade(float fade)
        {
            for (int k = _active.Count - 1; k >= 0; k--)
            {
                int i = _active[k];
                float target = Disabled || _visibleStamp[i] == _stamp ? 0f : _explored[i] ? _exploredAlpha : 1f;
                float a = _alpha[i];
                if (Mathf.Abs(a - target) >= 0.004f)
                {
                    a = Mathf.Lerp(a, target, fade);
                    if (Mathf.Abs(a - target) < 0.01f) a = target;
                    _alpha[i] = a;
                    MarkCell(i);
                    if (a != target) continue;
                }
                _isActive[i] = false;
                _active[k] = _active[^1];
                _active.RemoveAt(_active.Count - 1);
            }
        }

        /// <summary>The blocks whose texels this cell's alpha reaches need redrawing.</summary>
        void MarkCell(int i)
        {
            int x = i % _w, y = i / _w;
            int bx0 = Mathf.Max(0, x - Reach) / Block, bx1 = Mathf.Min(_w - 1, x + Reach) / Block;
            int by0 = Mathf.Max(0, y - Reach) / Block, by1 = Mathf.Min(_h - 1, y + Reach) / Block;
            for (int by = by0; by <= by1; by++)
            for (int bx = bx0; bx <= bx1; bx++)
                MarkBlock(by * _bw + bx);
        }

        void MarkBlock(int b)
        {
            if (_blockDirty[b]) return;
            _blockDirty[b] = true;
            _dirtyBlocks.Add(b);
        }

        /// <summary>Redraws the dirty blocks straight into the texture and uploads it.</summary>
        void Render()
        {
            if (_dirtyBlocks.Count == 0) return;
            if (_palette == null) BuildPalette();
            if (_sx0 == null) BuildSampleTables();
            var texels = _texture.GetRawTextureData<Color32>();
            foreach (int b in _dirtyBlocks)
            {
                RenderBlock(b % _bw, b / _bw, texels);
                _blockDirty[b] = false;
            }
            _dirtyBlocks.Clear();
            _texture.Apply(false);
        }

        /// <summary>
        /// One block: cell alphas → 1-2-1 blur (edges clamped) → bilinear upsampling to RenderScale
        /// texels per cell; colour goes from the explored tint to near black as alpha rises from the
        /// explored level to fully unexplored.
        /// </summary>
        void RenderBlock(int bx, int by, NativeArray<Color32> texels)
        {
            int cx0 = bx * Block, cy0 = by * Block;
            int cx1 = Mathf.Min(_w, cx0 + Block) - 1, cy1 = Mathf.Min(_h, cy0 + Block) - 1;
            // The texels of these cells sample the blurred values one cell further out.
            BlurCells(Mathf.Max(0, cx0 - 1), Mathf.Max(0, cy0 - 1), Mathf.Min(_w - 1, cx1 + 1), Mathf.Min(_h - 1, cy1 + 1));

            var blurred = _blurred;
            var palette = _palette;
            int w = _w, tw = _tw;
            int tx0 = cx0 * RenderScale, tx1 = (cx1 + 1) * RenderScale - 1;
            for (int ty = cy0 * RenderScale, tyEnd = (cy1 + 1) * RenderScale; ty < tyEnd; ty++)
            {
                int r0 = _sy0[ty] * w, r1 = _sy1[ty] * w;
                float fy = _sfy[ty];
                int row = ty * tw;
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    int x0 = _sx0[tx], x1 = _sx1[tx];
                    float l = blurred[r0 + x0] + (blurred[r1 + x0] - blurred[r0 + x0]) * fy;
                    float r = blurred[r0 + x1] + (blurred[r1 + x1] - blurred[r0 + x1]) * fy;
                    float a = l + (r - l) * _sfx[tx];
                    int idx = (int)(a * 255f + 0.5f);
                    texels[row + tx] = palette[idx < 0 ? 0 : idx > 255 ? 255 : idx];
                }
            }
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
        float[] _sfx, _sfy;

        void BuildSampleTables()
        {
            Axis(_tw, _w, out _sx0, out _sx1, out _sfx);
            Axis(_th, _h, out _sy0, out _sy1, out _sfy);

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

        /// <summary>1-2-1 blur in both directions (edges clamped) of the cell alphas in a rectangle into <see cref="_blurred"/>.</summary>
        void BlurCells(int x0, int y0, int x1, int y1)
        {
            var src = _alpha;
            int w = _w, h = _h;
            for (int y = y0; y <= y1; y++)
            {
                int rd = (y > 0 ? y - 1 : 0) * w, rc = y * w, ru = (y < h - 1 ? y + 1 : y) * w;
                for (int x = x0; x <= x1; x++)
                {
                    int l = x > 0 ? x - 1 : 0, r = x < w - 1 ? x + 1 : x;
                    float down = src[rd + l] + 2f * src[rd + x] + src[rd + r];
                    float mid = src[rc + l] + 2f * src[rc + x] + src[rc + r];
                    float up = src[ru + l] + 2f * src[ru + x] + src[ru + r];
                    _blurred[rc + x] = (down + 2f * mid + up) * 0.0625f;
                }
            }
        }

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
