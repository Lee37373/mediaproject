using GuildMaster.Core;
using GuildMaster.Fog;
using UnityEngine;

namespace GuildMaster.Visuals
{
    /// <summary>Pooled lightweight combat effects: expanding rings, sparks, beams.</summary>
    public class FxService : MonoBehaviour
    {
        const string RingKey = "fx.ring";
        const string SparkKey = "fx.spark";
        const string LineKey = "fx.line";
        public const int SortingOrder = Fog.FogOfWar.SortingOrder - 40;
        /// <summary>A spark's opacity with the settings' calmer flashes (P6-3).</summary>
        public const float CalmSparkAlpha = 0.45f;

        PoolService _pool;

        public void Init(PoolService pool)
        {
            _pool = pool;
            _pool.Register(RingKey, () => Create("Ring", SpriteFactory.Circle(32, new Color(1, 1, 1, 0.15f), Color.white)), 8);
            _pool.Register(SparkKey, () => Create("Spark", SpriteFactory.SoftCircle(32)), 16);
            _pool.Register(LineKey, () => Create("Line", SpriteFactory.White), 4);
        }

        static GameObject Create(string name, Sprite sprite)
        {
            var go = new GameObject(name);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = SortingOrder;
            go.AddComponent<FxInstance>();
            return go;
        }

        static bool Visible(Vector2 p) => !Services.TryGet<FogOfWar>(out var fog) || fog.IsVisible(p);

        /// <summary>Ring expanding to 'radius' (AoE feedback).</summary>
        public void Ring(Vector2 center, float radius, Color color, float duration = 0.35f)
        {
            if (!Visible(center)) return;
            _pool.Spawn<FxInstance>(RingKey, center).Play(color, radius * 2f * 0.3f, radius * 2f, duration, 0f);
        }

        /// <summary>Soft flash (hits, blocked projectiles, blinks). Fainter with the settings' calmer flashes (P6-3).</summary>
        public void Spark(Vector2 at, Color color, float size = 0.7f, float duration = 0.2f)
        {
            if (!Visible(at)) return;
            if (Session.GameSettings.CalmFlashes) color.a *= CalmSparkAlpha;
            _pool.Spawn<FxInstance>(SparkKey, at).Play(color, size * 0.4f, size, duration, 0f);
        }

        /// <summary>Straight beam between two points (piercing shots).</summary>
        public void Beam(Vector2 a, Vector2 b, Color color, float width = 0.12f, float duration = 0.25f)
        {
            if (!Visible(a) && !Visible(b)) return;
            var fx = _pool.Spawn<FxInstance>(LineKey, (a + b) * 0.5f);
            var d = b - a;
            fx.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            fx.PlayStretched(color, new Vector2(d.magnitude, width), duration);
        }
    }

    /// <summary>Animates scale/alpha then returns itself to the pool.</summary>
    public class FxInstance : MonoBehaviour, IPoolable
    {
        SpriteRenderer _sr;
        Color _color;
        float _from, _to, _duration, _t;
        Vector2 _stretch;
        bool _stretched;

        void Awake() => _sr = GetComponent<SpriteRenderer>();

        public void Play(Color color, float fromSize, float toSize, float duration, float delay)
        {
            _color = color;
            _from = fromSize;
            _to = toSize;
            _duration = Mathf.Max(0.01f, duration);
            _t = -delay;
            _stretched = false;
            transform.rotation = Quaternion.identity;
            Apply(0f);
        }

        public void PlayStretched(Color color, Vector2 size, float duration)
        {
            _color = color;
            _stretch = size;
            _duration = Mathf.Max(0.01f, duration);
            _t = 0f;
            _stretched = true;
            Apply(0f);
        }

        public void OnSpawned() { }
        public void OnDespawned() { }

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / _duration);
            Apply(k);
            if (k >= 1f && Services.TryGet<PoolService>(out var pool)) pool.Despawn(gameObject);
        }

        void Apply(float k)
        {
            if (_stretched)
            {
                transform.localScale = new Vector3(_stretch.x, _stretch.y * (1f - k * 0.7f), 1f);
            }
            else
            {
                // Sprites are 1 unit for SoftCircle (ppu=size) and 2 units for the 32px ring (ppu=16).
                float baseSize = _sr.sprite.bounds.size.x;
                float s = Mathf.Lerp(_from, _to, 1f - (1f - k) * (1f - k)) / baseSize;
                transform.localScale = new Vector3(s, s, 1f);
            }
            _sr.color = new Color(_color.r, _color.g, _color.b, _color.a * (1f - k));
        }
    }
}
