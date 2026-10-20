using GuildMaster.Core;
using GuildMaster.Fog;
using UnityEngine;

namespace GuildMaster.Visuals
{
    /// <summary>
    /// The dragon's fire on screen (P4-93): a flickering cone (<see cref="DragonArt.Flames"/>) from its mouth to where it
    /// breathed, stretched to reach, then gone. Pooled; drawn only where the guild can see.
    /// </summary>
    public class DragonFireFx : MonoBehaviour
    {
        const string PoolKey = "fx.dragonfire";
        public const float Duration = 0.55f;
        const float FrameSeconds = 0.07f;

        PoolService _pool;

        public void Init(PoolService pool)
        {
            _pool = pool;
            _pool.Register(PoolKey, () =>
            {
                var go = new GameObject("DragonFire");
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = FxService.SortingOrder + 1;
                go.AddComponent<DragonFireInstance>();
                return go;
            }, 2);
        }

        /// <summary>A blast from <paramref name="mouth"/> to <paramref name="at"/>.</summary>
        public void Play(Vector2 mouth, Vector2 at)
        {
            if (Services.TryGet<FogOfWar>(out var fog) && !fog.IsVisible(mouth) && !fog.IsVisible(at)) return;
            var d = at - mouth;
            float length = Mathf.Max(0.5f, d.magnitude);
            var fx = _pool.Spawn<DragonFireInstance>(PoolKey, mouth);
            fx.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            float natural = DragonArt.FlameWidth / (float)SpriteFactory.PixelsPerUnit;
            float stretch = length / natural;
            fx.transform.localScale = new Vector3(stretch, Mathf.Clamp(stretch, 0.8f, 1.6f), 1f);
            fx.Play(DragonArt.Flames(), Duration, FrameSeconds);
        }
    }

    /// <summary>One blast: cycles the flame frames, fades out at the end, then goes back to the pool.</summary>
    public class DragonFireInstance : MonoBehaviour, IPoolable
    {
        SpriteRenderer _sr;
        Sprite[] _frames;
        float _t, _duration, _frameSeconds;

        void Awake() => _sr = GetComponent<SpriteRenderer>();

        public void Play(Sprite[] frames, float duration, float frameSeconds)
        {
            _frames = frames;
            _duration = duration;
            _frameSeconds = frameSeconds;
            _t = 0f;
            _sr.sprite = frames[0];
            _sr.color = Color.white;
        }

        public void OnSpawned() { }
        public void OnDespawned() { _frames = null; }

        void Update()
        {
            if (_frames == null) return;
            _t += Time.deltaTime;
            _sr.sprite = _frames[Mathf.FloorToInt(_t / _frameSeconds) % _frames.Length];
            float fade = Mathf.InverseLerp(_duration, _duration * 0.6f, _t);
            _sr.color = new Color(1f, 1f, 1f, Mathf.Clamp01(fade));
            if (_t >= _duration && Services.TryGet<PoolService>(out var pool)) pool.Despawn(gameObject);
        }
    }
}
