using System;
using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Units
{
    /// <summary>
    /// Telegraphed area attacks: a ground marker fills up during the wind-up, then the effect
    /// resolves. AI queries active telegraphs to dodge out of hostile ones.
    /// </summary>
    public class TelegraphService : MonoBehaviour
    {
        const string PoolKey = "fx.telegraph";

        class Telegraph
        {
            public Vector2 Center;
            public float Radius;
            public float Start;
            public float End;
            public Unit Owner;
            public Action OnResolve;
            public GameObject Visual;
            public Transform Fill;
        }

        readonly List<Telegraph> _active = new();
        PoolService _pool;

        public void Init(PoolService pool)
        {
            _pool = pool;
            _pool.Register(PoolKey, () =>
            {
                var root = new GameObject("Telegraph");
                var ring = new GameObject("Ring").AddComponent<SpriteRenderer>();
                ring.transform.SetParent(root.transform, false);
                ring.sprite = SpriteFactory.Circle(48, new Color(1f, 0.3f, 0.15f, 0.18f), new Color(1f, 0.35f, 0.2f, 0.9f));
                ring.sortingOrder = 5;
                var fill = new GameObject("Fill").AddComponent<SpriteRenderer>();
                fill.transform.SetParent(root.transform, false);
                fill.sprite = SpriteFactory.Circle(48, new Color(1f, 0.25f, 0.1f, 0.35f), new Color(1f, 0.25f, 0.1f, 0.35f));
                fill.sortingOrder = 6;
                return root;
            }, 4);
        }

        /// <summary>Starts a telegraph; 'onResolve' runs when the wind-up completes (if the owner is still alive).</summary>
        public void Create(Unit owner, Vector2 center, float radius, float windup, Action onResolve)
        {
            var go = _pool.Spawn(PoolKey, center);
            float spriteSize = 48f / SpriteFactory.PixelsPerUnit; // world units of the 48px circle
            float s = radius * 2f / spriteSize;
            go.transform.localScale = new Vector3(s, s, 1f);
            var t = new Telegraph
            {
                Center = center, Radius = radius, Start = Time.time, End = Time.time + windup,
                Owner = owner, OnResolve = onResolve, Visual = go, Fill = go.transform.Find("Fill"),
            };
            t.Fill.localScale = Vector3.zero;
            _active.Add(t);
        }

        void Update()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var t = _active[i];
                float k = Mathf.InverseLerp(t.Start, t.End, Time.time);
                t.Fill.localScale = Vector3.one * k;
                if (k < 1f) continue;
                _active.RemoveAt(i);
                _pool.Despawn(t.Visual);
                if (t.Owner != null && t.Owner.IsAlive) t.OnResolve?.Invoke();
            }
        }

        /// <summary>If the unit stands in a telegraph owned by an enemy, returns a nearby safe point.</summary>
        public bool TryGetEscape(Unit unit, out Vector2 safePoint)
        {
            safePoint = default;
            foreach (var t in _active)
            {
                if (t.Owner == null || t.Owner == unit || !(t.Owner.IsHostileTo(unit) || unit.IsHostileTo(t.Owner))) continue;
                var d = unit.Position - t.Center;
                float need = t.Radius + unit.Radius + 0.35f;
                if (d.sqrMagnitude > need * need) continue;
                var world = Services.Get<WorldService>();
                var dir = d.sqrMagnitude > 1e-4f ? d.normalized : UnityEngine.Random.insideUnitCircle.normalized;
                // Try straight out, then rotate until a walkable exit is found.
                for (int k = 0; k < 8; k++)
                {
                    float ang = (k % 2 == 0 ? 1 : -1) * (k + 1) / 2 * 40f;
                    var cand = t.Center + (Vector2)(Quaternion.Euler(0, 0, ang) * dir) * (need + 0.3f);
                    if (world.Grid.IsWalkable(GridMap.WorldToCell(cand))) { safePoint = cand; return true; }
                }
            }
            return false;
        }

        public bool IsInsideHostileTelegraph(Vector2 p, Unit viewer)
        {
            foreach (var t in _active)
            {
                if (t.Owner == null || !(t.Owner.IsHostileTo(viewer) || viewer.IsHostileTo(t.Owner))) continue;
                if ((p - t.Center).sqrMagnitude <= t.Radius * t.Radius) return true;
            }
            return false;
        }
    }
}
