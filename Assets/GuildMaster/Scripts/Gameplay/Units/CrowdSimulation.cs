using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Units
{
    /// <summary>
    /// Crowd layer on top of path following: boids-style separation so units never stack.
    /// Displacements are mass-weighted (big monsters shove small ones) and never push a unit into
    /// unwalkable terrain. Neighbours come from the <see cref="UnitRegistry"/> bucket grid, which
    /// this refreshes once a frame (P5-2: it used to keep a hash of its own).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class CrowdSimulation : MonoBehaviour
    {
        const float Strength = 5f;
        const float MaxStep = 0.12f;
        /// <summary>
        /// No two bodies touch from further apart than this (a slime king boss, the biggest body, has a 0.55 radius
        /// and two of them touch from 1.1), which is what lets a unit look at only the 2×2 buckets nearest it.
        /// </summary>
        const float MaxContact = UnitRegistry.BucketSize * 0.5f;

        readonly List<Vector2> _push = new();
        WorldService _world;

        void Update()
        {
            _world ??= Services.Get<WorldService>();
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UnitRegistry.RebuildIndex();
            int n = UnitRegistry.SlotCount;
            _push.Clear();
            for (int i = 0; i < n; i++)
            {
                var u = UnitRegistry.SlotUnit(i);
                if (!u.IsAlive) { _push.Add(Vector2.zero); continue; }
                var p = UnitRegistry.SlotPosition(i);
                float ur = u.Radius, um = u.Mass;
                int bx = UnitRegistry.BucketOf(p.x), by = UnitRegistry.BucketOf(p.y);
                // Only the 2×2 buckets nearest the unit can hold anyone touching it.
                int x0 = p.x - bx * UnitRegistry.BucketSize < UnitRegistry.BucketSize * 0.5f ? bx - 1 : bx;
                int y0 = p.y - by * UnitRegistry.BucketSize < UnitRegistry.BucketSize * 0.5f ? by - 1 : by;
                Vector2 push = Vector2.zero;
                for (int cy = y0; cy <= y0 + 1; cy++)
                for (int cx = x0; cx <= x0 + 1; cx++)
                for (int j = UnitRegistry.BucketHead(cx, cy); j >= 0; j = UnitRegistry.NextInBucket(j))
                {
                    if (j == i) continue;
                    var d = p - UnitRegistry.SlotPosition(j);
                    float sq = d.sqrMagnitude;
                    if (sq >= MaxContact * MaxContact) continue;
                    var o = UnitRegistry.SlotUnit(j);
                    float min = ur + o.Radius;
                    if (sq >= min * min || !o.IsAlive) continue;
                    float dist = Mathf.Sqrt(sq);
                    // Deterministic sideways split for perfectly overlapping units.
                    var dir = dist > 1e-4f ? d / dist : new Vector2(Mathf.Sign(u.GetInstanceID() - o.GetInstanceID()), 0.3f).normalized;
                    float overlap = (min - dist) / min;
                    float massRatio = o.Mass / (um + o.Mass);
                    push += dir * overlap * massRatio * 2f;
                }
                _push.Add(push);
            }

            for (int i = 0; i < n; i++)
            {
                var push = _push[i];
                if (push.sqrMagnitude < 1e-6f) continue;
                var u = UnitRegistry.SlotUnit(i);
                var pos = UnitRegistry.SlotPosition(i);
                var step = Vector2.ClampMagnitude(push * Strength * dt, MaxStep);
                var next = pos + step;
                if (!_world.Grid.IsWalkable(GridMap.WorldToCell(next), u.PassesGates))
                {
                    // Slide along one axis if the diagonal is blocked.
                    var nx = pos + new Vector2(step.x, 0f);
                    var ny = pos + new Vector2(0f, step.y);
                    if (_world.Grid.IsWalkable(GridMap.WorldToCell(nx), u.PassesGates)) next = nx;
                    else if (_world.Grid.IsWalkable(GridMap.WorldToCell(ny), u.PassesGates)) next = ny;
                    else continue;
                }
                var t = u.transform;
                t.position = new Vector3(next.x, next.y, t.position.z);
            }
        }
    }
}
