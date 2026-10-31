using System;
using GuildMaster.Core;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Units
{
    public struct ProjectileBlockedEvent { public Vector2 Position; public Unit Attacker; }

    public struct ProjectileSpec
    {
        public float Speed;
        public float Size;
        public Color Color;
        /// <summary>Chance a tree cell absorbs the projectile (hard cover always blocks).</summary>
        public float TreeBlockChance;
        /// <summary>Custom impact (point, unit hit or null when blocked). Default: single-target hit.</summary>
        public Action<Vector2, Unit> OnImpact;
        /// <summary>Cells the shot leaves from without being stopped by them (a tower shooting off its own roof, P4-113); none by default.</summary>
        public RectInt Launcher;

        public static ProjectileSpec Arrow(Color c) => new() { Speed = 11f, Size = 0.35f, Color = c, TreeBlockChance = 0.45f };
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
            _pool.Register(PoolKey, () =>
            {
                var go = new GameObject("Projectile");
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = SpriteFactory.Circle(6, Color.white, new Color(0f, 0f, 0f, 0.6f));
                sr.sortingOrder = Fog.FogOfWar.SortingOrder - 100;
                go.AddComponent<Projectile>();
                return go;
            }, prewarm: 16);
        }

        public void Launch(Unit attacker, Unit target)
        {
            var color = attacker is IProjectileStyle style ? style.ProjectileColor : new Color(0.95f, 0.9f, 0.6f);
            Launch(attacker, target, ProjectileSpec.Arrow(color));
        }

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
    }

    /// <summary>Optional per-unit projectile tint (e.g. arrows vs magic).</summary>
    public interface IProjectileStyle
    {
        Color ProjectileColor { get; }
    }

    public class Projectile : MonoBehaviour, IPoolable
    {
        Unit _attacker;
        Unit _target;
        Vector2 _aim;
        ProjectileSpec _spec;
        PoolService _pool;
        SpriteRenderer _sr;
        float _life;
        Vector2Int _lastCell;
        Vector2Int _startCell;
        WorldService _world;

        void Awake() => _sr = GetComponent<SpriteRenderer>();

        public void Fire(Unit attacker, Unit target, ProjectileSpec spec, PoolService pool)
        {
            _attacker = attacker;
            _target = target;
            _spec = spec;
            _pool = pool;
            _sr.color = spec.Color;
            transform.localScale = Vector3.one * (spec.Size / 0.375f);
            _aim = target.Position + new Vector2(0f, 0.1f);
            _life = 3f;
            _world = Services.Get<WorldService>();
            _startCell = _lastCell = GridMap.WorldToCell(transform.position);
        }

        public void OnSpawned() { }
        public void OnDespawned() { _attacker = null; _target = null; _spec.OnImpact = null; }

        void Update()
        {
            // Light homing: follow the target but only while it stays close to the original line.
            if (_target != null && _target.IsAlive && (_target.Position - _aim).sqrMagnitude < 1.5f)
                _aim = _target.Position + new Vector2(0f, 0.1f);

            Vector2 pos = transform.position;
            var next = Vector2.MoveTowards(pos, _aim, _spec.Speed * Time.deltaTime);
            transform.position = next;

            var cell = GridMap.WorldToCell(next);
            if (cell != _lastCell)
            {
                _lastCell = cell;
                var targetCell = _target != null ? GridMap.WorldToCell(_target.Position) : cell;
                if (cell != _startCell && cell != targetCell && !_spec.Launcher.Contains(cell))
                {
                    bool blocked = _world.Grid.IsHardCover(cell.x, cell.y)
                                   || (_world.Grid.HasTree(cell.x, cell.y) && GameRandom.Shared.Chance(_spec.TreeBlockChance));
                    if (blocked) { Impact(next, null, true); return; }
                }
            }

            _life -= Time.deltaTime;
            if ((next - _aim).sqrMagnitude < 0.01f)
            {
                var hit = _target != null && _target.IsAlive && (_target.Position - next).sqrMagnitude < 0.6f ? _target : null;
                Impact(next, hit, false);
            }
            else if (_life <= 0f) _pool.Despawn(gameObject);
        }

        void Impact(Vector2 at, Unit hit, bool blockedByTerrain)
        {
            if (_spec.OnImpact != null) _spec.OnImpact(at, hit);
            else if (hit != null) Combat.ApplyHit(_attacker, hit);

            if (blockedByTerrain)
            {
                if (Services.TryGet<FxService>(out var fx)) fx.Spark(at, new Color(0.9f, 0.85f, 0.7f), 0.5f);
                EventBus.Publish(new ProjectileBlockedEvent { Position = at, Attacker = _attacker });
            }
            _pool.Despawn(gameObject);
        }
    }
}
