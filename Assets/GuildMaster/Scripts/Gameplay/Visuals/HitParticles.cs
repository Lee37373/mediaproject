using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Visuals
{
    /// <summary>Settings for one particle burst (see <see cref="HitParticles.Burst"/>).</summary>
    public struct BurstSpec
    {
        public Color Color;
        public int Count;
        /// <summary>Start speed range (world units/s).</summary>
        public float SpeedMin, SpeedMax;
        /// <summary>Start size range (world units).</summary>
        public float SizeMin, SizeMax;
        public float Lifetime;
        /// <summary>Positive = falls, negative = rises.</summary>
        public float Gravity;
        /// <summary>Emission circle radius around the burst point.</summary>
        public float Radius;
    }

    /// <summary>
    /// Pooled ParticleSystem bursts for combat feedback (P4-8). One pooled system per burst; it
    /// returns to the pool once all its particles are gone. Uses the default sprite material with a
    /// soft round texture so particles are lit and sorted like the other effects.
    /// </summary>
    public class HitParticles : MonoBehaviour
    {
        const string PoolKey = "fx.particles";

        PoolService _pool;
        Material _material;

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        public void Init(PoolService pool)
        {
            _pool = pool;
            // Borrow the project's default sprite material (URP 2D) and give it a soft dot texture.
            var probe = new GameObject("MaterialProbe").AddComponent<SpriteRenderer>();
            _material = new Material(probe.sharedMaterial) { name = "HitParticles", mainTexture = SpriteFactory.SoftCircle(32).texture };
            Destroy(probe.gameObject);
            _pool.Register(PoolKey, CreateSystem, 6, maxSize: 48);
        }

        GameObject CreateSystem()
        {
            var go = new GameObject("Particles");
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.maxParticles = 64;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle; // XY plane
            shape.radius = 0.1f;

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 2.5f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _material;
            renderer.sortingOrder = FxService.SortingOrder + 1;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            go.AddComponent<ParticleBurst>();
            return go;
        }

        public void Burst(Vector2 at, BurstSpec spec)
        {
            if (spec.Count <= 0) return;
            var burst = _pool.Spawn<ParticleBurst>(PoolKey, at);
            var ps = burst.System;
            var main = ps.main;
            main.startColor = spec.Color;
            main.startSpeed = new ParticleSystem.MinMaxCurve(spec.SpeedMin, spec.SpeedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(spec.SizeMin, spec.SizeMax);
            main.startLifetime = new ParticleSystem.MinMaxCurve(spec.Lifetime * 0.7f, spec.Lifetime);
            main.gravityModifier = spec.Gravity;
            var shape = ps.shape;
            shape.radius = Mathf.Max(0.01f, spec.Radius);
            ps.Emit(Mathf.Min(spec.Count, 64));
            burst.Began();
        }
    }

    /// <summary>A pooled burst: despawns itself when its particles have died out.</summary>
    public class ParticleBurst : MonoBehaviour, IPoolable
    {
        public ParticleSystem System { get; private set; }
        bool _active;

        void Awake() => System = GetComponent<ParticleSystem>();

        internal void Began() => _active = true;

        public void OnSpawned() { }

        public void OnDespawned()
        {
            _active = false;
            System.Clear(true);
        }

        void Update()
        {
            if (!_active || System.particleCount > 0) return;
            _active = false;
            if (Services.TryGet<PoolService>(out var pool)) pool.Despawn(gameObject);
        }
    }
}
