using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Visuals
{
    /// <summary>
    /// Pooled marker bobbing above a unit (P4-27: the target a party leader called for focus fire).
    /// Follows the unit, hides in the fog, and returns itself to the pool when the unit dies or
    /// <see cref="Release"/> is called.
    /// </summary>
    public class TargetMarker : MonoBehaviour, IPoolable
    {
        public const string PoolKey = "fx.targetMarker";

        static readonly Dictionary<char, Color> s_palette = new()
        {
            ['o'] = new Color(0.25f, 0.05f, 0.05f),
            ['r'] = new Color(1f, 0.3f, 0.25f),
            ['y'] = new Color(1f, 0.85f, 0.35f),
        };

        static readonly string[] s_arrow =
        {
            "ooooooooo",
            "orrrrrrro",
            ".oryyyro.",
            "..oryro..",
            "...oro...",
            "....o....",
        };

        SpriteRenderer _sr;
        Unit _unit;
        float _phase;

        /// <summary>Registers the pool (called once by the system that shows markers).</summary>
        public static void RegisterPool(PoolService pool)
        {
            if (pool.Has(PoolKey)) return;
            pool.Register(PoolKey, () =>
            {
                var go = new GameObject("TargetMarker");
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = SpriteFactory.FromTemplate("marker:focus", s_arrow, s_palette, new Vector2(0.5f, 0f));
                sr.sortingOrder = FxService.SortingOrder + 5;
                go.AddComponent<TargetMarker>();
                return go;
            }, prewarm: 2, maxSize: 16);
        }

        public static TargetMarker Show(PoolService pool, Unit unit)
        {
            var m = pool.Spawn<TargetMarker>(PoolKey, unit.Position);
            m._unit = unit;
            m.Place();
            return m;
        }

        public Unit Unit => _unit;

        public void Release()
        {
            if (_unit == null && !gameObject.activeSelf) return;
            _unit = null;
            if (Services.TryGet<PoolService>(out var pool)) pool.Despawn(gameObject);
        }

        void Awake() => _sr = GetComponent<SpriteRenderer>();

        public void OnSpawned() => _phase = 0f;
        public void OnDespawned() => _unit = null;

        void LateUpdate()
        {
            if (_unit == null || !_unit.IsAlive) { Release(); return; }
            _phase += Time.unscaledDeltaTime;
            Place();
        }

        void Place()
        {
            float height = _unit is Monsters.Monster m && m.Def != null ? 1.05f * m.Scale : 1.05f;
            transform.position = (Vector3)(_unit.Position + new Vector2(0f, height + Mathf.Sin(_phase * 6f) * 0.06f));
            _sr.enabled = !Services.TryGet<FogOfWar>(out var fog) || fog.IsVisible(_unit.Position);
        }
    }
}
