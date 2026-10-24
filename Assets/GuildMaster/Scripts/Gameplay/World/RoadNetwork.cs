using System.Collections.Generic;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>
    /// Roads over the land (P4-102, user decision 2026-10-24: roads that follow the ground). The gates' roads run
    /// straight only as far as the town's walls can move out (<see cref="StubReach"/>); from there roads lead to the
    /// zones' hearts and the village sites, nearest first, each by the cheapest way over the ground to the roads already
    /// laid — so they join into trunks and branches, wind with the land, go round lakes and rocks and over the barriers
    /// only by their passes. The deeper a zone, the likelier it is left without a road (the wilds).
    /// </summary>
    public static class RoadNetwork
    {
        /// <summary>
        /// The gates' roads run straight to this far from the town's centre: the walls move out up to 12 cells (P4-86) and
        /// lead their roads out to where the old ones leave the clearing (<see cref="TownWalls"/>, up to 41 cells).
        /// </summary>
        public const int StubReach = 42;
        /// <summary>Chance a zone gets a road, by how far its middle lies from the town.</summary>
        static float RoadChance(float distance) => distance < 250f ? 0.9f : distance < 330f ? 0.55f : 0.25f;
        /// <summary>A road search gives up after this many cells.</summary>
        const int SearchLimit = 250000;

        static readonly int[] Dx8 = { 1, -1, 0, 0, 1, 1, -1, -1 }, Dy8 = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>Lays the roads from the zones' hearts and <paramref name="extra"/> spots to the roads already on the map.</summary>
        public static void Lay(MapData map, Vector2Int town, IReadOnlyList<Vector2Int> extra, GameRandom rng)
        {
            int w = map.Width, h = map.SurfaceHeight;
            float nx = rng.Range(0f, 5000f), ny = rng.Range(0f, 5000f);
            var cost = new float[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                cost[y * w + x] = Cost(map, x, y, nx, ny);

            var targets = new List<(Vector2Int cell, ZoneSite zone)>();
            foreach (var z in map.Zones)
                if (z.Index != ZoneMap.Home && rng.Chance(RoadChance(Vector2.Distance(z.Centre, town)))) targets.Add((z.Heart, z));
            foreach (var c in extra) targets.Add((c, null));
            targets.Sort((a, b) => (a.cell - town).sqrMagnitude.CompareTo((b.cell - town).sqrMagnitude));

            // The roads joined to the town's (a bridge out in the wilds is a road cell too, but leads nowhere yet).
            var network = new bool[w * h];
            Join(map, network, town.y * w + town.x);
            var search = new Search(w, h);
            var path = new List<int>();
            foreach (var (cell, zone) in targets)
            {
                if (!search.ToRoad(map, cost, network, cell, town, path)) continue;
                // Rounded off where the ground allows (an open plain gave straight runs meeting at corners).
                _ = Rounded(cost, path, w, 7) || Rounded(cost, path, w, 4);
                // A diagonal step gets a corner cell too (one that can carry a road), so the road is one piece cell to cell.
                for (int k = 1; k < path.Count; k++)
                {
                    int a = path[k - 1], b = path[k];
                    if (a % w == b % w || a / w == b / w) continue;
                    int corner = a / w * w + b % w, other = b / w * w + a % w;
                    if (float.IsInfinity(cost[corner])) corner = other;
                    if (!float.IsInfinity(cost[corner])) LayRoad(map, cost, corner);
                }
                foreach (int i in path) LayRoad(map, cost, i);
                // The new road joins the network, and with it any bridge it ran over.
                if (path.Count > 0) Join(map, network, path[^1]);
                if (zone != null) zone.OnRoad = true;
            }

            // Passes a road from the town runs through (a bridge is road itself: only the network counts).
            for (int p = 0; p < map.Passes.Count; p++)
            {
                var pass = map.Passes[p];
                if (pass.OnRoad) continue;
                for (int dy = -3; dy <= 3 && !pass.OnRoad; dy++)
                for (int dx = -3; dx <= 3; dx++)
                    if (map.InBounds(pass.Cell.x + dx, pass.Cell.y + dy) && network[map.Index(pass.Cell.x + dx, pass.Cell.y + dy)]) { pass.OnRoad = true; break; }
                map.Passes[p] = pass;
            }
        }

        /// <summary>
        /// The path through every <paramref name="spacing"/>th cell, its corners cut twice (Chaikin) and drawn again cell by
        /// cell; replaces <paramref name="path"/> only if every new cell can carry a road.
        /// </summary>
        static bool Rounded(float[] cost, List<int> path, int w, int spacing)
        {
            if (path.Count < spacing * 2) return false;
            var pts = new List<Vector2>();
            for (int k = 0; k < path.Count; k += spacing) pts.Add(new Vector2(path[k] % w, path[k] / w));
            var end = new Vector2(path[^1] % w, path[^1] / w);
            if (pts[^1] != end) pts.Add(end);
            for (int round = 0; round < 2; round++)
            {
                var cut = new List<Vector2> { pts[0] };
                for (int k = 0; k + 1 < pts.Count; k++)
                {
                    cut.Add(Vector2.Lerp(pts[k], pts[k + 1], 0.25f));
                    cut.Add(Vector2.Lerp(pts[k], pts[k + 1], 0.75f));
                }
                cut.Add(pts[^1]);
                pts = cut;
            }
            var cells = new List<int>();
            for (int k = 0; k + 1 < pts.Count; k++)
            {
                float len = Vector2.Distance(pts[k], pts[k + 1]);
                int steps = Mathf.Max(1, Mathf.CeilToInt(len * 2f));
                for (int s = 0; s <= steps; s++)
                {
                    var p = Vector2.Lerp(pts[k], pts[k + 1], s / (float)steps);
                    int i = Mathf.RoundToInt(p.y) * w + Mathf.RoundToInt(p.x);
                    if (i < 0 || i >= cost.Length || float.IsInfinity(cost[i])) return false;
                    if (cells.Count == 0 || cells[^1] != i) cells.Add(i);
                }
            }
            path.Clear();
            path.AddRange(cells);
            return true;
        }

        /// <summary>Marks the paved ground joined (8 ways) to <paramref name="from"/> as part of the network.</summary>
        static void Join(MapData map, bool[] network, int from)
        {
            int w = map.Width, h = map.SurfaceHeight;
            var stack = new Stack<int>();
            stack.Push(from);
            network[from] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                int x = i % w, y = i / w;
                for (int k = 0; k < 8; k++)
                {
                    int ax = x + Dx8[k], ay = y + Dy8[k];
                    if (ax < 0 || ay < 0 || ax >= w || ay >= h) continue;
                    int j = ay * w + ax;
                    if (network[j] || map.Tiles[j] is not (TileType.Road or TileType.Gate or TileType.TownFloor)) continue;
                    network[j] = true;
                    stack.Push(j);
                }
            }
        }

        static void LayRoad(MapData map, float[] cost, int i)
        {
            if (map.Tiles[i] is TileType.Road or TileType.Gate or TileType.TownFloor or TileType.Wall) return;
            map.Tiles[i] = TileType.Road;
            map.Trees[i] = false;
            cost[i] = RoadCost;
        }

        const float RoadCost = 0.3f;

        /// <summary>
        /// What a road pays to run over a cell: roads already laid are cheap (they join), open ground, sand, wood dearer;
        /// water, rock and the deep woods it cannot cross (bridges and trails are cut before — P4-103: a road through the deep
        /// woods is a trail); a slow noise makes it wind, and a cell beside water or rock costs more, so roads keep off the
        /// shores and the cliffs.
        /// </summary>
        static float Cost(MapData map, int x, int y, float nx, float ny)
        {
            var t = map[x, y];
            if (map.IsDeep(x, y)) return float.PositiveInfinity;
            float c = t switch
            {
                TileType.Road => RoadCost,
                TileType.Grass => 1f,
                TileType.Sand => 1.3f,
                TileType.Forest => 2.2f,
                _ => float.PositiveInfinity,
            };
            if (float.IsInfinity(c) || t == TileType.Road) return c;
            c *= 0.55f + 1.3f * Mathf.PerlinNoise(x * 0.02f + nx, y * 0.02f + ny);
            for (int k = 0; k < 4; k++)
            {
                int ax = x + Dx8[k], ay = y + Dy8[k];
                if (!map.InBounds(ax, ay) || map[ax, ay] is TileType.Water or TileType.Mountain) { c += 0.6f; break; }
            }
            return c;
        }

        /// <summary>A* from a spot to the nearest road cell over <see cref="Cost"/> (8 ways, no cutting corners).</summary>
        sealed class Search
        {
            readonly int _w, _h;
            readonly float[] _g;
            readonly int[] _from, _stamp;
            int _mark;
            readonly List<(float f, int i)> _heap = new();

            public Search(int w, int h)
            {
                _w = w;
                _h = h;
                _g = new float[w * h];
                _from = new int[w * h];
                _stamp = new int[w * h];
            }

            public bool ToRoad(MapData map, float[] cost, bool[] network, Vector2Int start, Vector2Int town, List<int> path)
            {
                path.Clear();
                int s = start.y * _w + start.x;
                if (float.IsInfinity(cost[s])) return false;
                if (network[s]) return true;
                _mark++;
                _heap.Clear();
                _g[s] = 0f;
                _from[s] = -1;
                _stamp[s] = _mark;
                Push(H(start.x, start.y, town), s);
                int expanded = 0;
                while (_heap.Count > 0 && expanded++ < SearchLimit)
                {
                    var (_, i) = Pop();
                    if (network[i])
                    {
                        for (int c = i; c >= 0; c = _from[c]) path.Add(c);
                        return true;
                    }
                    int x = i % _w, y = i / _w;
                    for (int k = 0; k < 8; k++)
                    {
                        int ax = x + Dx8[k], ay = y + Dy8[k];
                        if (ax < 0 || ay < 0 || ax >= _w || ay >= _h) continue;
                        int j = ay * _w + ax;
                        if (float.IsInfinity(cost[j])) continue;
                        bool diagonal = k >= 4;
                        if (diagonal && (float.IsInfinity(cost[y * _w + ax]) || float.IsInfinity(cost[ay * _w + x]))) continue;
                        float g = _g[i] + cost[j] * (diagonal ? 1.4142f : 1f);
                        if (_stamp[j] == _mark && g >= _g[j]) continue;
                        _stamp[j] = _mark;
                        _g[j] = g;
                        _from[j] = i;
                        Push(g + H(ax, ay, town), j);
                    }
                }
                return false;
            }

            /// <summary>Roads run out from the town to about <see cref="StubReach"/>: the way left to there at the cheapest rate.</summary>
            static float H(int x, int y, Vector2Int town)
                => RoadCost * Mathf.Max(0f, Mathf.Sqrt((x - town.x) * (x - town.x) + (y - town.y) * (y - town.y)) - StubReach);

            void Push(float f, int i)
            {
                _heap.Add((f, i));
                int c = _heap.Count - 1;
                while (c > 0)
                {
                    int p = (c - 1) / 2;
                    if (_heap[p].f <= _heap[c].f) break;
                    (_heap[p], _heap[c]) = (_heap[c], _heap[p]);
                    c = p;
                }
            }

            (float, int) Pop()
            {
                var top = _heap[0];
                int last = _heap.Count - 1;
                _heap[0] = _heap[last];
                _heap.RemoveAt(last);
                int c = 0;
                while (true)
                {
                    int l = c * 2 + 1, r = l + 1, m = c;
                    if (l < _heap.Count && _heap[l].f < _heap[m].f) m = l;
                    if (r < _heap.Count && _heap[r].f < _heap[m].f) m = r;
                    if (m == c) break;
                    (_heap[m], _heap[c]) = (_heap[c], _heap[m]);
                    c = m;
                }
                return top;
            }
        }
    }
}
