using GuildMaster.Core;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Units
{
    /// <summary>
    /// One shot in flight (pooled by <see cref="ProjectileService"/>). It flies straight at where the target stood, following
    /// it only while it stays near that line, and terrain can stop it: hard cover always, trees by chance.
    /// Drawn after the defence project's shots (P6-9, <see cref="ShotLook"/>): an arrow turned to where it flies, or a glowing
    /// orb with a white core; either can trail a fading tail of soft ghosts, spaced by distance so it reads the same at any
    /// game speed.
    /// </summary>
    public class Projectile : MonoBehaviour, IPoolable
    {
        /// <summary>Tail ghosts, and the gap between them as a share of the shot's size (arrows: of a fixed spacing).</summary>
        const int TailCount = 5;
        const float OrbTailGap = 0.32f, ArrowTailGap = 0.13f;
        /// <summary>The orb's white core, as a share of its glow.</summary>
        const float CoreScale = 0.45f;
        /// <summary>Stand-in look for a spec with none.</summary>
        static readonly Color PlainColor = new(0.95f, 0.9f, 0.6f);

        Unit _attacker;
        Unit _target;
        Vector2 _aim;
        ProjectileSpec _spec;
        PoolService _pool;
        float _life;
        Vector2Int _lastCell;
        Vector2Int _startCell;
        WorldService _world;

        SpriteRenderer _sprite;
        SpriteRenderer _core;
        readonly SpriteRenderer[] _tail = new SpriteRenderer[TailCount];
        readonly Vector2[] _trail = new Vector2[TailCount];
        int _trailHead;
        float _sinceMark;
        float _tailGap;
        /// <summary>How far behind the shot's middle the tail starts (an arrow's fletching).</summary>
        float _tailBack;
        bool _turns;
        bool _hasTail;

        /// <summary>Pool factory: the shot and its core and tail renderers (switched on per look).</summary>
        public static GameObject Create()
        {
            var go = new GameObject("Projectile");
            var p = go.AddComponent<Projectile>();
            int order = Fog.FogOfWar.SortingOrder - 100;
            p._sprite = go.AddComponent<SpriteRenderer>();
            p._sprite.sortingOrder = order;
            p._core = Child(go.transform, "Core", order + 1);
            for (int i = 0; i < TailCount; i++) p._tail[i] = Child(go.transform, "Tail" + i, order - 1);
            return go;
        }

        static SpriteRenderer Child(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Glow();
            sr.sortingOrder = order;
            go.SetActive(false);
            return sr;
        }

        public void Fire(Unit attacker, Unit target, ProjectileSpec spec, PoolService pool)
        {
            _attacker = attacker;
            _target = target;
            _spec = spec;
            _pool = pool;
            _aim = target.Position + new Vector2(0f, 0.1f);
            _life = 3f;
            _world = Services.Get<WorldService>();
            _startCell = _lastCell = GridMap.WorldToCell(transform.position);
            Dress(spec.Look.IsEmpty ? ShotLook.Orb(PlainColor, 0.36f) : spec.Look);
            Face(_aim - (Vector2)transform.position);
            if (_hasTail) PlaceTail(); // behind it now that it has turned
        }

        public void OnSpawned() { }
        public void OnDespawned() { _attacker = null; _target = null; _spec.OnImpact = null; }

        /// <summary>Puts on the picture for this shot (pooled shots change looks).</summary>
        void Dress(ShotLook look)
        {
            transform.rotation = Quaternion.identity;
            _core.gameObject.SetActive(false);
            float scale;
            if (look.Kind == ShotKind.Arrow)
            {
                var sprite = look.Sprite != null ? look.Sprite : SpriteFactory.Arrow();
                _sprite.sprite = sprite;
                _sprite.color = Color.white;
                scale = look.Size / Mathf.Max(0.01f, sprite.bounds.size.x);
                _turns = true;
                _tailGap = ArrowTailGap;
                _tailBack = look.Size * 0.4f;
            }
            else
            {
                _sprite.sprite = SpriteFactory.Glow();
                _sprite.color = look.Color;
                scale = look.Size; // the glow is one unit across
                _core.color = new Color(1f, 1f, 1f, 0.95f);
                _core.transform.localScale = Vector3.one * CoreScale;
                _core.gameObject.SetActive(true);
                _turns = false;
                _tailGap = look.Size * OrbTailGap;
                _tailBack = 0f;
            }
            transform.localScale = Vector3.one * scale;
            _hasTail = look.HasTail;
            if (_hasTail) StartTail(look, scale);
            else foreach (var t in _tail) t.gameObject.SetActive(false);
        }

        /// <summary>Turns the tail ghosts on: fading back from the shot, from big (newest) to small, all at the start.</summary>
        void StartTail(ShotLook look, float parentScale)
        {
            bool orb = look.Kind == ShotKind.Orb;
            // World sizes: an orb's tail follows its glow, an arrow's is a thin streak of colour.
            float biggest = orb ? look.Size * 0.8f : 0.38f, smallest = orb ? look.Size * 0.3f : 0.15f;
            float alpha = orb ? 0.6f : 0.85f;
            var c = look.Tail;
            for (int i = 0; i < TailCount; i++)
            {
                var t = _tail[i];
                float k = 1f - (i + 1f) / (TailCount + 1f);
                t.color = new Color(c.r, c.g, c.b, alpha * k * c.a);
                t.transform.localScale = Vector3.one * (Mathf.Lerp(smallest, biggest, k) / parentScale);
                t.gameObject.SetActive(true);
            }
            Vector2 here = transform.position;
            for (int i = 0; i < _trail.Length; i++) _trail[i] = here;
            _trailHead = 0;
            _sinceMark = 0f;
            PlaceTail();
        }

        void Face(Vector2 dir)
        {
            if (_turns && dir.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        }

        void Update()
        {
            // Light homing: follow the target but only while it stays close to the original line.
            if (_target != null && _target.IsAlive && (_target.Position - _aim).sqrMagnitude < 1.5f)
                _aim = _target.Position + new Vector2(0f, 0.1f);

            Vector2 pos = transform.position;
            var next = Vector2.MoveTowards(pos, _aim, _spec.Speed * Time.deltaTime);
            transform.position = next;
            Face(next - pos);
            if (_hasTail) Trail(pos, next);

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

        /// <summary>
        /// Marks the path every <see cref="_tailGap"/> world units along this frame's step (several marks in one long step at
        /// high game speed, in between the two ends) and puts the ghosts on the newest marks.
        /// </summary>
        void Trail(Vector2 from, Vector2 to)
        {
            Vector2 d = to - from;
            float len = d.magnitude;
            if (len > 1e-5f && _tailGap > 1e-4f)
            {
                Vector2 dir = d / len;
                float at = _tailGap - _sinceMark;
                for (; at <= len; at += _tailGap)
                {
                    _trailHead = (_trailHead + 1) % _trail.Length;
                    _trail[_trailHead] = from + dir * at;
                }
                _sinceMark = len - (at - _tailGap);
            }
            PlaceTail();
        }

        void PlaceTail()
        {
            Vector2 back = _tailBack > 0f ? (Vector2)transform.right * -_tailBack : Vector2.zero;
            for (int i = 0; i < TailCount; i++)
            {
                int idx = ((_trailHead - i) % _trail.Length + _trail.Length) % _trail.Length;
                _tail[i].transform.position = _trail[idx] + back;
            }
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
