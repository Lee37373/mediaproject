using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>
    /// Grid A* (8-directional, no corner cutting, octile heuristic, terrain costs).
    /// Node arrays are allocated once and reset lazily via a search stamp, so a search
    /// allocates nothing except the returned path.
    /// </summary>
    public class Pathfinder
    {
        const float Diagonal = 1.41421356f;

        static readonly Vector2Int[] s_dirs =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
            new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
        };

        readonly GridMap _grid;
        readonly float[] _g;
        readonly int[] _parent;
        readonly int[] _stamp;
        readonly bool[] _closed;
        readonly MinHeap _open;
        readonly RouteRisk _risk;
        int _currentStamp;

        public int MaxExpandedNodes { get; set; } = 20000;
        public int LastExpanded { get; private set; }

        /// <summary><paramref name="risk"/> is shared with the rest of the world (P4-63); alone it bakes its own.</summary>
        public Pathfinder(GridMap grid, RouteRisk risk = null)
        {
            _grid = grid;
            int n = grid.Width * grid.Height;
            _g = new float[n];
            _parent = new int[n];
            _stamp = new int[n];
            _closed = new bool[n];
            _open = new MinHeap(1024);
            _risk = risk ?? new RouteRisk(grid.Data);
        }

        /// <summary>Whether the search in progress may pass intact town gates (P4-39).</summary>
        bool _passesGates = true;

        /// <summary>Cells the walker believes shut though they are not (a breach its kind has not seen).</summary>
        System.Func<int, int, bool> _believedShut;

        /// <summary>How the search in progress weighs risky ground (P4-63, <see cref="RouteRisk"/>).</summary>
        float _caution;

        /// <summary>Heuristic scale for the search in progress (the cheapest a step can be for this walker).</summary>
        float _heuristicScale = HeuristicScale;

        bool Walkable(int x, int y) => _grid.IsWalkable(x, y, _passesGates) && (_believedShut == null || !_believedShut(x, y));

        /// <summary>Finds a path of cells from start to goal (inclusive). Returns false if unreachable.</summary>
        public bool FindPath(Vector2Int start, Vector2Int goal, List<Vector2Int> result) => FindPath(start, goal, result, true);

        /// <summary>
        /// As above; <paramref name="passesGates"/> false keeps the path out of intact gates (monsters),
        /// and <paramref name="caution"/> weighs the risk of the ground (P4-63, <see cref="RouteRisk"/>):
        /// above 0 walks round cover, below 0 prefers the forest, 0 takes the shortest way.
        /// </summary>
        public bool FindPath(Vector2Int start, Vector2Int goal, List<Vector2Int> result, bool passesGates,
            System.Func<int, int, bool> believedShut = null, float caution = 0f)
        {
            _passesGates = passesGates;
            _believedShut = believedShut;
            _caution = caution;
            _heuristicScale = HeuristicScale * RouteRisk.LowestMultiplier(caution);
            result.Clear();
            if (!_grid.InBounds(start) || !_grid.InBounds(goal)) return false;
            // Every gate shut: no way through the walls for a monster, so skip the whole-map search.
            if (!passesGates && _grid.IsInTown(start) != _grid.IsInTown(goal) && !AnyGateOpenTo(believedShut)) return false;
            if (!_grid.IsWalkable(goal) && !_grid.TryFindNearestWalkable(goal, 6, out goal)) return false;
            if (!_grid.IsWalkable(start)) _grid.TryFindNearestWalkable(start, 3, out start);
            if (start == goal) { result.Add(goal); return true; }

            int w = _grid.Width;
            _currentStamp++;
            _open.Clear();
            int startIdx = start.y * w + start.x;
            int goalIdx = goal.y * w + goal.x;
            Touch(startIdx);
            _g[startIdx] = 0f;
            _open.Push(startIdx, Heuristic(start, goal));

            LastExpanded = 0;
            while (_open.Count > 0)
            {
                int current = _open.Pop();
                if (_closed[current]) continue;
                _closed[current] = true;
                if (current == goalIdx) { Reconstruct(goalIdx, w, result); return true; }
                if (++LastExpanded > MaxExpandedNodes) break;

                int cx = current % w, cy = current / w;
                for (int d = 0; d < 8; d++)
                {
                    var dir = s_dirs[d];
                    int nx = cx + dir.x, ny = cy + dir.y;
                    if (!Walkable(nx, ny)) continue;
                    bool diagonal = d >= 4;
                    if (diagonal && (!Walkable(cx + dir.x, cy) || !Walkable(cx, cy + dir.y))) continue;

                    int ni = ny * w + nx;
                    Touch(ni);
                    if (_closed[ni]) continue;
                    float step = _grid.Cost(nx, ny) * (diagonal ? Diagonal : 1f) * _risk.Multiplier(nx, ny, _caution);
                    float g = _g[current] + step;
                    if (g >= _g[ni]) continue;
                    _g[ni] = g;
                    _parent[ni] = current;
                    _open.Push(ni, g + Heuristic(new Vector2Int(nx, ny), goal));
                }
            }
            return false;
        }

        void Touch(int i)
        {
            if (_stamp[i] == _currentStamp) return;
            _stamp[i] = _currentStamp;
            _g[i] = float.PositiveInfinity;
            _parent[i] = -1;
            _closed[i] = false;
        }

        /// <summary>Cheapest terrain cost on the map (a road): the heuristic stays admissible.</summary>
        const float HeuristicScale = 0.7f;

        float Heuristic(Vector2Int a, Vector2Int b)
        {
            // Octile distance scaled by the cheapest a step can be for this walker.
            int dx = Mathf.Abs(a.x - b.x), dy = Mathf.Abs(a.y - b.y);
            float h = (dx + dy) + (Diagonal - 2f) * Mathf.Min(dx, dy);
            return h * _heuristicScale;
        }

        void Reconstruct(int goalIdx, int w, List<Vector2Int> result)
        {
            for (int i = goalIdx; i >= 0; i = _parent[i])
                result.Add(new Vector2Int(i % w, i / w));
            result.Reverse();
        }

        public bool FindPathWorld(Vector2 from, Vector2 to, List<Vector2Int> result, bool passesGates = true,
            System.Func<int, int, bool> believedShut = null, float caution = 0f)
            => FindPath(GridMap.WorldToCell(from), GridMap.WorldToCell(to), result, passesGates, believedShut, caution);

        /// <summary>Some gate is open and known to be (else a monster cannot cross the wall at all: skip the search).</summary>
        bool AnyGateOpenTo(System.Func<int, int, bool> believedShut)
        {
            if (_grid.TownSealed) return false;
            foreach (var g in _grid.Data.Gates)
                if (!_grid.IsClosedGate(g) && (believedShut == null || !believedShut(g.x, g.y))) return true;
            return false;
        }

        /// <summary>Binary min-heap of (index, priority).</summary>
        class MinHeap
        {
            int[] _items;
            float[] _prio;
            public int Count { get; private set; }

            public MinHeap(int capacity)
            {
                _items = new int[capacity];
                _prio = new float[capacity];
            }

            public void Clear() => Count = 0;

            public void Push(int item, float priority)
            {
                if (Count == _items.Length)
                {
                    System.Array.Resize(ref _items, Count * 2);
                    System.Array.Resize(ref _prio, Count * 2);
                }
                int i = Count++;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (_prio[p] <= priority) break;
                    _items[i] = _items[p];
                    _prio[i] = _prio[p];
                    i = p;
                }
                _items[i] = item;
                _prio[i] = priority;
            }

            public int Pop()
            {
                int top = _items[0];
                int lastItem = _items[--Count];
                float lastPrio = _prio[Count];
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1;
                    if (l >= Count) break;
                    int r = l + 1;
                    int m = r < Count && _prio[r] < _prio[l] ? r : l;
                    if (_prio[m] >= lastPrio) break;
                    _items[i] = _items[m];
                    _prio[i] = _prio[m];
                    i = m;
                }
                _items[i] = lastItem;
                _prio[i] = lastPrio;
                return top;
            }
        }
    }
}
