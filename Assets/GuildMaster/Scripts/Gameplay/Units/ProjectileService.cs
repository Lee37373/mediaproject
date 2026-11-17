using System;
using GuildMaster.Core;
using GuildMaster.Visuals;
using UnityEngine;

namespace GuildMaster.Units
{
    public struct ProjectileBlockedEvent { public Vector2 Position; public Unit Attacker; }

    public struct ProjectileSpec
    {
        public float Speed;
        /// <summary>What it looks like (P6-9); an empty look is drawn as a plain orb.</summary>
        public ShotLook Look;
        /// <summary>Chance a tree cell absorbs the projectile (hard cover always blocks).</summary>
        public float TreeBlockChance;
        /// <summary>Custom impact (point, unit hit or null when blocked). Default: single-target hit.</summary>
        public Action<Vector2, Unit> OnImpact;
        /// <summary>Cells the shot leaves from without being stopped by them (a tower shooting off its own roof, P4-113); none by default.</summary>
        public RectInt Launcher;

        /// <summary>An ordinary shot (an arrow or a bolt of magic): speed 11, trees stop 45%.</summary>
        public static ProjectileSpec Of(ShotLook look) => new() { Speed = 11f, Look = look, TreeBlockChance = 0.45f };
    }

    /// <summary>
    /// Pooled projectiles that fly in (slightly homing) straight lines and can be stopped by
    /// terrain: mountains/walls/buildings always block, trees block by chance (partial cover).
    /// </summary>
    public class ProjectileService : MonoBehaviour
    {
        public const string PoolKey = "fx.projectile";

        PoolService _pool;

        public void Init(PoolService pool)
        {
            _pool = pool;
            _pool.Register(PoolKey, Projectile.Create, prewarm: 16);
        }

        /// <summary>An attacker's ordinary shot, looking as it says (<see cref="IProjectileStyle"/>).</summary>
        public void Launch(Unit attacker, Unit target) => Launch(attacker, target, ProjectileSpec.Of(LookOf(attacker)));

        public void Launch(Unit attacker, Unit target, ProjectileSpec spec)
        {
            var p = _pool.Spawn<Projectile>(PoolKey, attacker.Position + new Vector2(0f, 0.15f));
            p.Fire(attacker, target, spec, _pool);
        }

        /// <summary>A shot with nobody behind it (an arrow tower, P4-113): from a point, its harm all in <see cref="ProjectileSpec.OnImpact"/>.</summary>
        public void LaunchFrom(Vector2 from, Unit target, ProjectileSpec spec)
        {
            var p = _pool.Spawn<Projectile>(PoolKey, from);
            p.Fire(null, target, spec, _pool);
        }

        /// <summary>What <paramref name="shooter"/>'s shots look like; an arrow for one that does not say.</summary>
        public static ShotLook LookOf(Unit shooter)
            => shooter is IProjectileStyle style ? style.ShotLook : ShotLook.Arrow(shooter != null && shooter.Visual != null ? shooter.Visual.HeldArrow : null);
    }

    /// <summary>What a unit's ordinary shots look like (P6-9: arrows, magic orbs, spit).</summary>
    public interface IProjectileStyle
    {
        ShotLook ShotLook { get; }
    }
}
