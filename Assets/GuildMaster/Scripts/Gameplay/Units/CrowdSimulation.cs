using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Units
{
    /// <summary>
    /// Crowd layer on top of path following: a spatial hash of all units plus boids-style
    /// separation so units never stack. Displacements are mass-weighted (big monsters shove
    /// small ones) and never push a unit into unwalkable terrain. Also serves fast
    /// neighbour queries for AI.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class CrowdSimulation : MonoBehaviour
    {
        const float CellSize = 2f;
        const float Strength = 5f;
        const float MaxStep = 0.12f;

        readonly Dictionary<int, List<Unit>> _cells = new();
        readonly Stack<List<Unit>> _free = new();
        readonly List<Unit> _units = new();
        readonly List<Vector2> _push = new();
        WorldService _world;

        static int Key(int cx, int cy) => (cx & 0xFFFF) | (cy << 16);

        void Rebuild()
        {
            foreach (var list in _cells.Values) { list.Clear(); _free.Push(list); }
            _cells.Clear();
            _units.Clear();
            foreach (var u in UnitRegistry.All)
            {
                if (!u.IsAlive) continue;
                _units.Add(u);
                var p = u.Position;
                int key = Key(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.y / CellSize));
                if (!_cells.TryGetValue(key, out var list))
                {
                    list = _free.Count > 0 ? _free.Pop() : new List<Unit>(8);
                    _cells[key] = list;
                }
                list.Add(u);
            }
        }

        /// <summary>Units within radius of a point (uses the hash built this frame).</summary>
        public void Query(Vector2 p, float radius, List<Unit> results)
        {
            results.Clear();
            int minX = Mathf.FloorToInt((p.x - radius) / CellSize), maxX = Mathf.FloorToInt((p.x + radius) / CellSize);
            int minY = Mathf.FloorToInt((p.y - radius) / CellSize), maxY = Mathf.FloorToInt((p.y + radius) / CellSize);
            float r2 = radius * radius;
            for (int cy = minY; cy <= maxY; cy++)
            for (int cx = minX; cx <= maxX; cx++)
            {
                if (!_cells.TryGetValue(Key(cx, cy), out var list)) continue;
                foreach (var u in list)
                    if (u.IsAlive && (u.Position - p).sqrMagnitude <= r2) results.Add(u);
            }
        }

        void Update()
        {
            _world ??= Services.Get<WorldService>();
            Rebuild();
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            _push.Clear();
            for (int i = 0; i < _units.Count; i++)
            {
                var u = _units[i];
                var p = u.Position;
                int cx = Mathf.FloorToInt(p.x / CellSize), cy = Mathf.FloorToInt(p.y / CellSize);
                Vector2 push = Vector2.zero;
                for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (!_cells.TryGetValue(Key(cx + ox, cy + oy), out var list)) continue;
                    foreach (var o in list)
                    {
                        if (o == u) continue;
                        float min = u.Radius + o.Radius;
                        var d = p - o.Position;
                        float sq = d.sqrMagnitude;
                        if (sq >= min * min) continue;
                        float dist = Mathf.Sqrt(sq);
                        // Deterministic sideways split for perfectly overlapping units.
                        var dir = dist > 1e-4f ? d / dist : new Vector2(Mathf.Sign(u.GetInstanceID() - o.GetInstanceID()), 0.3f).normalized;
                        float overlap = (min - dist) / min;
                        float massRatio = o.Mass / (u.Mass + o.Mass);
                        push += dir * overlap * massRatio * 2f;
                    }
                }
                _push.Add(push);
            }

            for (int i = 0; i < _units.Count; i++)
            {
                var push = _push[i];
                if (push.sqrMagnitude < 1e-6f) continue;
                var u = _units[i];
                var step = Vector2.ClampMagnitude(push * Strength * dt, MaxStep);
                var next = u.Position + step;
                if (!_world.Grid.IsWalkable(GridMap.WorldToCell(next)))
                {
                    // Slide along one axis if the diagonal is blocked.
                    var nx = u.Position + new Vector2(step.x, 0f);
                    var ny = u.Position + new Vector2(0f, step.y);
                    if (_world.Grid.IsWalkable(GridMap.WorldToCell(nx))) next = nx;
                    else if (_world.Grid.IsWalkable(GridMap.WorldToCell(ny))) next = ny;
                    else continue;
                }
                u.transform.position = new Vector3(next.x, next.y, u.transform.position.z);
            }
        }
    }
}
