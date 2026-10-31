using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Visuals;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GuildMaster.Flares
{
    /// <summary>
    /// One flare (P4-94): a spark that climbs from the shooter and arcs over to where it bursts, then a bright star that
    /// hangs there sinking slowly for <see cref="FlareRules.BurnSeconds"/>, a wide pool of light under it (stronger at
    /// night) and the fog lifted for <see cref="FlareRules.Radius"/> cells round it — over walls and into forests, it is
    /// light from above. Pooled (<see cref="FlareService.PoolKey"/>); the root stands where it bursts.
    /// </summary>
    public class SkyFlare : MonoBehaviour, IPoolable
    {
        enum Phase { Idle, Flying, Burning }

        /// <summary>How high over the ground the star hangs when it bursts, and when it is about to go out.</summary>
        const float BurstHeight = 3.2f, LowHeight = 1.4f;
        /// <summary>The flight's extra rise in the middle (over the straight line up).</summary>
        const float ArcHeight = 2.5f;
        /// <summary>The last seconds the light dims (the fog stays lifted to the end).</summary>
        const float FadeSeconds = 3f;
        static readonly Color StarColor = new(1f, 0.97f, 0.85f);
        static readonly Color LightColor = new(1f, 0.93f, 0.72f);

        SpriteRenderer _star, _halo, _glow;
        /// <summary>A real 2D light: at night it lifts the dark off everything under it (the glow alone read as mist).</summary>
        Light2D _light;
        VisionSource _vision;
        GameClock _clock;
        Phase _phase;
        Vector2 _from;
        float _t, _sparkTimer;

        /// <summary>Where it bursts (the middle of its light on the ground).</summary>
        public Vector2 Centre => transform.position;
        public bool IsBurning => _phase == Phase.Burning;
        public float SecondsLeft => _phase == Phase.Burning ? Mathf.Max(0f, FlareRules.BurnSeconds - _t) : 0f;

        public static GameObject Create()
        {
            var go = new GameObject("SkyFlare");
            go.AddComponent<SkyFlare>().Build();
            return go;
        }

        void Build()
        {
            _glow = NewRenderer("Glow", SpriteFactory.SoftCircle(64));
            _halo = NewRenderer("Halo", SpriteFactory.SoftCircle(32));
            _star = NewRenderer("Star", SpriteFactory.Circle(9, StarColor, new Color(1f, 0.85f, 0.45f)));
            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(transform, false);
            _light = lightGo.AddComponent<Light2D>();
            _light.lightType = Light2D.LightType.Point;
            _light.color = LightColor;
            _light.pointLightInnerRadius = FlareRules.Radius * 0.3f;
            _light.pointLightOuterRadius = FlareRules.Radius;
            _light.falloffIntensity = 0.6f;
            _light.intensity = 0f;
            _light.enabled = false;
            _vision = gameObject.AddComponent<VisionSource>();
            _vision.SeesOverWalls = true;
            _vision.enabled = false;
        }

        SpriteRenderer NewRenderer(string name, Sprite sprite)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.enabled = false;
            return r;
        }

        /// <summary>Sets off from <paramref name="from"/>; the root is already where it bursts.</summary>
        public void Launch(Vector2 from, Vector2 at)
        {
            _clock = Services.TryGet<GameClock>(out var clock) ? clock : null;
            transform.position = at;
            _from = from;
            _t = 0f;
            _phase = Phase.Flying;
            // The star and its halo over the characters, the pool of light under them (as a campfire's).
            _star.sortingOrder = FxService.SortingOrder + 3;
            _halo.sortingOrder = FxService.SortingOrder + 2;
            _glow.sortingOrder = CharacterVisual.BaseSortingOrder - Mathf.RoundToInt(at.y * 8f) - 400;
            _star.enabled = _halo.enabled = true;
            _glow.enabled = false;
            _halo.transform.localScale = Vector3.one * 1.6f;
            _halo.color = new Color(1f, 0.9f, 0.6f, 0.8f);
            _star.color = StarColor;
            PlaceStar(0f);
        }

        /// <summary>Flight progress 0..1: up from the shooter, over, and to the burst height above the centre.</summary>
        void PlaceStar(float s)
        {
            var top = (Vector2)transform.position + new Vector2(0f, BurstHeight);
            var p = Vector2.Lerp(_from, top, s) + new Vector2(0f, ArcHeight * Mathf.Sin(Mathf.PI * s));
            _star.transform.position = p;
            _halo.transform.position = p;
        }

        void Burst()
        {
            _phase = Phase.Burning;
            _t = 0f;
            _glow.enabled = true;
            _light.intensity = 0f;
            _light.enabled = true;
            _vision.SightRadius = FlareRules.Radius;
            _vision.enabled = true;
            FlareService.Lit(this);
            if (Services.TryGet<HitParticles>(out var particles))
                particles.Burst(_star.transform.position, new BurstSpec
                {
                    Color = StarColor, Count = 18, SpeedMin = 1.2f, SpeedMax = 3f,
                    SizeMin = 0.08f, SizeMax = 0.16f, Lifetime = 0.9f, Gravity = 1.2f, Radius = 0.2f,
                });
        }

        void Update()
        {
            if (_phase == Phase.Idle) return;
            _t += Time.deltaTime;
            if (_phase == Phase.Flying)
            {
                PlaceStar(Mathf.Clamp01(_t / FlareRules.FlightSeconds));
                if (_t >= FlareRules.FlightSeconds) Burst();
                return;
            }
            if (_t >= FlareRules.BurnSeconds)
            {
                if (Services.TryGet<PoolService>(out var pool)) pool.Despawn(gameObject);
                else GoOut();
                return;
            }

            float life = _t / FlareRules.BurnSeconds;
            float fade = Mathf.Clamp01((FlareRules.BurnSeconds - _t) / FadeSeconds);
            float flicker = 1f + 0.08f * Mathf.Sin(_t * 23f) + 0.05f * Mathf.Sin(_t * 37f + 0.7f);
            var star = (Vector2)transform.position + new Vector2(0.25f * Mathf.Sin(_t * 0.9f), Mathf.Lerp(BurstHeight, LowHeight, life));
            _star.transform.position = star;
            _halo.transform.position = star;
            _halo.transform.localScale = Vector3.one * (3.2f * flicker);
            _halo.color = new Color(1f, 0.92f, 0.65f, 0.9f * fade);
            _star.color = new Color(StarColor.r, StarColor.g, StarColor.b, Mathf.Lerp(0.3f, 1f, fade));

            // A pool of light on the ground: a warm wash by day, at night the light itself clears the dark round it.
            float dark = _clock != null ? 1f - _clock.Daylight : 0.5f;
            float size = FlareRules.Radius * 2f * (0.97f + 0.03f * flicker);
            _glow.transform.localScale = new Vector3(size, size * 0.8f, 1f);
            _glow.color = new Color(LightColor.r, LightColor.g, LightColor.b, Mathf.Lerp(0.28f, 0.12f, dark) * fade);
            _light.transform.position = new Vector3(star.x, Mathf.Lerp(star.y, transform.position.y, 0.6f), 0f);
            _light.intensity = Mathf.Lerp(0.25f, 0.95f, dark) * fade * flicker;

            _sparkTimer -= Time.deltaTime;
            if (_sparkTimer > 0f || !Services.TryGet<HitParticles>(out var sparks)) return;
            _sparkTimer = Random.Range(0.12f, 0.3f);
            sparks.Burst(star, new BurstSpec
            {
                Color = new Color(1f, 0.85f, 0.5f), Count = 2, SpeedMin = 0.1f, SpeedMax = 0.4f,
                SizeMin = 0.05f, SizeMax = 0.1f, Lifetime = 1.1f, Gravity = 0.8f, Radius = 0.12f,
            });
        }

        void GoOut()
        {
            _phase = Phase.Idle;
            FlareService.Out(this);
            if (_vision != null) _vision.enabled = false;
            if (_light != null) _light.enabled = false;
            if (_star != null) _star.enabled = _halo.enabled = _glow.enabled = false;
        }

        public void OnSpawned() { }

        public void OnDespawned() => GoOut();

        void OnDisable() => FlareService.Out(this);
    }
}
