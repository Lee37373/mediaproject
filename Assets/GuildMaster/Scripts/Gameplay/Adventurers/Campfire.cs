using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Visuals;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// The fire of a camp out in the wild (P4-66): stones and logs, a flickering flame, a pool of
    /// warm light that grows at night, sparks, and the few cells of fog it lights up. Put out, it
    /// leaves blackened logs for a while. Pooled (<see cref="PoolKey"/>).
    /// </summary>
    public class Campfire : MonoBehaviour, IPoolable
    {
        public const string PoolKey = "adventurers.campfire";

        SpriteRenderer _logs;
        SpriteRenderer _flame;
        SpriteRenderer _glow;
        VisionSource _vision;
        GameClock _clock;
        float _animTime;
        float _sparkTimer;
        bool _burning;
        float _removeAtHour;

        public bool Burning => _burning;

        public static GameObject Create()
        {
            var go = new GameObject("Campfire");
            go.AddComponent<Campfire>().Build();
            return go;
        }

        void Build()
        {
            _glow = NewRenderer("Glow", SpriteFactory.SoftCircle(64));
            _logs = NewRenderer("Logs", CartoonArt.CampfireLogs());
            _flame = NewRenderer("Flame", CartoonArt.Flame(0));
            _flame.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            _vision = gameObject.AddComponent<VisionSource>();
            _vision.enabled = false;
        }

        SpriteRenderer NewRenderer(string name, Sprite sprite)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            return r;
        }

        /// <summary>Lights the fire where it stands.</summary>
        public void Light()
        {
            _clock = Services.TryGet<GameClock>(out var clock) ? clock : null;
            _burning = true;
            _removeAtHour = float.MaxValue;
            _logs.sprite = CartoonArt.CampfireLogs();
            _flame.enabled = true;
            _glow.enabled = true;
            _vision.SightRadius = CampRules.FireSight;
            _vision.enabled = true;
            // Drawn among the characters, by its feet like everything else on the ground.
            int order = CharacterVisual.BaseSortingOrder - Mathf.RoundToInt(transform.position.y * 8f);
            _logs.sortingOrder = order;
            _flame.sortingOrder = order + 1;
            _glow.sortingOrder = order - 400; // under the people sitting round it
        }

        /// <summary>Puts it out: a puff of smoke, and the blackened logs stay for <paramref name="remainHours"/>.</summary>
        public void PutOut(float remainHours = 1f)
        {
            if (!_burning) return;
            _burning = false;
            _flame.enabled = false;
            _glow.enabled = false;
            _vision.enabled = false;
            _logs.sprite = CartoonArt.CampfireLogs(burntOut: true);
            if (Services.TryGet<HitParticles>(out var particles))
                particles.Burst(transform.position, new BurstSpec
                {
                    Color = new Color(0.55f, 0.55f, 0.58f, 0.7f), Count = 14, SpeedMin = 0.2f, SpeedMax = 0.6f,
                    SizeMin = 0.25f, SizeMax = 0.5f, Lifetime = 1.6f, Gravity = -0.35f, Radius = 0.25f,
                });
            _removeAtHour = (_clock != null ? _clock.TotalHours : 0f) + remainHours;
        }

        void Update()
        {
            if (!_burning)
            {
                if (_clock != null && _clock.TotalHours >= _removeAtHour && Services.TryGet<PoolService>(out var pool))
                    pool.Despawn(gameObject);
                return;
            }
            _animTime += Time.deltaTime;
            _flame.sprite = CartoonArt.Flame(Mathf.FloorToInt(_animTime * 7f));
            float flicker = 1f + 0.06f * Mathf.Sin(_animTime * 17f) + 0.04f * Mathf.Sin(_animTime * 29f + 1.3f);
            _flame.transform.localScale = new Vector3(1f, flicker, 1f);

            // The light spreads with the dark: a small warm patch by day, a wide pool at night.
            float dark = _clock != null ? 1f - _clock.Daylight : 0.5f;
            float size = Mathf.Lerp(2.2f, 7f, dark) * flicker;
            _glow.transform.localScale = new Vector3(size, size * 0.75f, 1f);
            _glow.color = new Color(1f, 0.66f, 0.3f, Mathf.Lerp(0.12f, 0.55f, dark));

            _sparkTimer -= Time.deltaTime;
            if (_sparkTimer > 0f || !Services.TryGet<HitParticles>(out var sparks)) return;
            _sparkTimer = Random.Range(0.35f, 0.9f);
            sparks.Burst((Vector2)transform.position + new Vector2(0f, 0.3f), new BurstSpec
            {
                Color = new Color(1f, 0.7f, 0.3f), Count = 2, SpeedMin = 0.3f, SpeedMax = 0.8f,
                SizeMin = 0.05f, SizeMax = 0.1f, Lifetime = 0.9f, Gravity = -0.6f, Radius = 0.15f,
            });
        }

        public void OnSpawned() { }

        public void OnDespawned()
        {
            _burning = false;
            if (_vision != null) _vision.enabled = false;
        }
    }
}
