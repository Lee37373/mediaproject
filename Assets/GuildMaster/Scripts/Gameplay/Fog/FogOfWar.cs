using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Fog
{
    public interface IVisionSource
    {
        Vector2 VisionPosition { get; }
        float SightRadius { get; }
    }

    public interface IFogHideable
    {
        Vector2 FogPosition { get; }
        void SetFogVisible(bool visible);
    }

    public struct FogUpdatedEvent { public int NewlyExploredCells; }

    /// <summary>
    /// Grid fog of war with three states (unexplored / explored / visible).
    /// Vision is recomputed on a fixed tick; alpha fades smoothly into a 1-texel-per-tile
    /// texture rendered above the world with bilinear filtering for soft edges.
    /// </summary>
    public class FogOfWar : MonoBehaviour
    {
        public const int SortingOrder = 3000;

        static readonly List<IVisionSource> s_sources = new();
        static readonly List<IFogHideable> s_hideables = new();

        int _w, _h;
        bool[] _explored;
        int[] _visibleStamp;
        int _stamp;
        float[] _alpha;
        Color32[] _pixels;
        Texture2D _texture;
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
            _pixels = new Color32[n];
            for (int i = 0; i < n; i++) { _alpha[i] = 1f; _pixels[i] = new Color32(0, 0, 0, 255); }

            _texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "FogTexture"
            };
            _texture.SetPixels32(_pixels);
            _texture.Apply(false);

            var sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(_texture, new Rect(0, 0, width, height), Vector2.zero, 1f, 0, SpriteMeshType.FullRect);
            sr.sortingOrder = SortingOrder;
            transform.position = Vector3.zero;
            Tick(0f);
        }

        public bool IsVisible(Vector2Int c) => Disabled || (InBounds(c) && _visibleStamp[c.y * _w + c.x] == _stamp);
        public bool IsVisible(Vector2 world) => IsVisible(new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y)));
        public bool IsExplored(Vector2Int c) => Disabled || (InBounds(c) && _explored[c.y * _w + c.x]);
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
            foreach (var src in s_sources)
            {
                ForEachCellInCircle(src.VisionPosition, src.SightRadius, i =>
                {
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
                _pixels[i].a = (byte)(a * 255f);
                changed = true;
            }
            if (changed)
            {
                _texture.SetPixels32(_pixels);
                _texture.Apply(false);
                _dirty = false;
            }

            foreach (var h in s_hideables) h.SetFogVisible(IsVisible(h.FogPosition));

            if (newly > 0) Core.EventBus.Publish(new FogUpdatedEvent { NewlyExploredCells = newly });
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
