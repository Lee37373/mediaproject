using System.Collections.Generic;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.World
{
    public struct MapGenSettings
    {
        public int Width;
        public int Height;
        public int Seed;
        public int TownHalfSize;
        public int NestCount;
        public int MinNestDistanceFromTown;
        /// <summary>
        /// P4-81: the first <see cref="InnerNestCount"/> nests go within this many cells of the town
        /// along x and y, the rest outside it (then anywhere, if the outside runs out). 0 = spread evenly.
        /// </summary>
        public int InnerHalfSize;
        public int InnerNestCount;
    }

    /// <summary>
    /// Procedural map: noise terrain, walled town in the center with 4 gated roads,
    /// reachable nest sites for the monster ecosystem, and edge cells for raid waves.
    /// </summary>
    public static class MapGenerator
    {
        /// <summary>Noise cut-offs for one region's terrain (height below Water/Sand, above Mountain; moisture above Forest).</summary>
        struct TerrainThresholds { public float Water, Sand, Mountain, Forest; }

        static TerrainThresholds Thresholds(Region r) => r switch
        {
            Region.Desert => new TerrainThresholds { Water = 0.24f, Sand = 0.40f, Mountain = 0.74f, Forest = 0.70f },    // oases, dunes, few palms
            Region.Snow => new TerrainThresholds { Water = 0.30f, Sand = 0.33f, Mountain = 0.68f, Forest = 0.52f },      // frozen lakes, snowy pines
            Region.Swamp => new TerrainThresholds { Water = 0.36f, Sand = 0.40f, Mountain = 0.82f, Forest = 0.50f },     // many pools (0.40 flooded it), dark trees
            Region.Wasteland => new TerrainThresholds { Water = 0.26f, Sand = 0.30f, Mountain = 0.60f, Forest = 0.76f }, // lava, rocks, dead trees
            _ => new TerrainThresholds { Water = 0.30f, Sand = 0.34f, Mountain = 0.72f, Forest = 0.56f },
        };

        /// <summary>
        /// Cut-offs averaged over the regions around a cell, so terrain changes gradually across a
        /// border. Where lava meets water there is no pool at all: a lake half blue, half lava read
        /// as a rendering fault.
        /// </summary>
        static TerrainThresholds BlendedThresholds(Region[] around)
        {
            TerrainThresholds sum = default;
            foreach (var r in around)
            {
                var t = Thresholds(r);
                sum.Water += t.Water; sum.Sand += t.Sand; sum.Mountain += t.Mountain; sum.Forest += t.Forest;
            }
            float n = around.Length;
            return new TerrainThresholds { Water = sum.Water / n, Sand = sum.Sand / n, Mountain = sum.Mountain / n, Forest = sum.Forest / n };
        }

        /// <summary>Which terrain feature a tile belongs to (0 = open ground). A lake and its shore are one.</summary>
        static int FeatureKind(TileType t) => t switch
        {
            TileType.Water or TileType.Sand => 1,
            TileType.Forest => 2,
            TileType.Mountain => 3,
            _ => 0,
        };

        /// <summary>
        /// Borders follow the terrain (user decision 2026-09-22): each connected lake (with its shore),
        /// wood or rock mass lies wholly in the region holding most of it, so a lake is never half
        /// water, half lava; the border runs round it. Open ground keeps the winding line.
        /// </summary>
        static void SnapRegionsToFeatures(MapData map)
        {
            int w = map.Width, h = map.Height;
            var seen = new bool[w * h];
            var cells = new List<int>();
            var stack = new Stack<int>();
            var votes = new int[System.Enum.GetValues(typeof(Region)).Length];
            for (int start = 0; start < seen.Length; start++)
            {
                int kind = FeatureKind(map.Tiles[start]);
                if (seen[start] || kind == 0) continue;
                cells.Clear();
                System.Array.Clear(votes, 0, votes.Length);
                seen[start] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    cells.Add(i);
                    votes[(int)map.Regions[i]]++;
                    int x = i % w, y = i / w;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int n = ny * w + nx;
                        if (seen[n] || FeatureKind(map.Tiles[n]) != kind) continue;
                        seen[n] = true;
                        stack.Push(n);
                    }
                }
                int best = 0;
                for (int r = 1; r < votes.Length; r++) if (votes[r] > votes[best]) best = r;
                foreach (int i in cells) map.Regions[i] = (Region)best;
            }
        }

        public static MapData Generate(MapGenSettings s)
        {
            var map = new MapData(s.Width, s.Height);
            var rng = new GameRandom(s.Seed);
            float ox = rng.Range(0f, 10000f), oy = rng.Range(0f, 10000f);
            float mx = rng.Range(0f, 10000f), my = rng.Range(0f, 10000f);
            // Regions (P4-55): grassland round the town, then by direction desert, snow, swamp and wasteland.
            var layout = TerrainRegions.Layout(new Vector2(s.Width / 2f, s.Height / 2f), Mathf.Min(s.Width, s.Height),
                rng.Range(0f, Mathf.PI * 2f), rng.Range(0f, 5000f), rng.Range(0f, 5000f));
            map.RegionLayout = layout;

            // 1. Terrain from height + moisture noise; each region has its own mix (few trees in the
            //    desert, many pools in the swamp, rocks in the wasteland whose "water" is lava).
            var around = new Region[9];
            for (int y = 0; y < s.Height; y++)
            for (int x = 0; x < s.Width; x++)
            {
                TerrainRegions.SampleAround(layout, x + 0.5f, y + 0.5f, around);
                var region = around[0];
                map.Regions[map.Index(x, y)] = region;
                var th = BlendedThresholds(around);
                float h = Fbm(x * 0.035f + ox, y * 0.035f + oy);
                float m = Fbm(x * 0.05f + mx, y * 0.05f + my);
                TileType t;
                if (h < th.Water) t = TileType.Water;
                else if (h < th.Sand) t = TileType.Sand;
                else if (h > th.Mountain) t = TileType.Mountain;
                else if (m > th.Forest) t = TileType.Forest;
                else t = TileType.Grass;
                map[x, y] = t;
            }
            // Regions snap to the terrain before roads and the town clearing cut features in two.
            SnapRegionsToFeatures(map);

            // 2. Town: cleared surroundings, floor, walls and 4 gates.
            var c = new Vector2Int(s.Width / 2, s.Height / 2);
            TownWalls.Lay(map, c, s.TownHalfSize);
            for (int g = 0; g < TownWalls.GateDirections.Length; g++)
                CarveRoad(map, map.Gates[g] + TownWalls.GateDirections[g], TownWalls.GateDirections[g], rng);
            TownWalls.LayPlazaRoads(map);

            // Trees on forest cells (partial cover). Deterministic from the seed.
            for (int i = 0; i < map.Tiles.Length; i++)
                map.Trees[i] = map.Tiles[i] == TileType.Forest && rng.Chance(0.55f);

            // 3. Reachability from town.
            var dist = FloodDistances(map, c);

            // 4. Nest sites.
            var candidates = new List<Vector2Int>();
            for (int y = 2; y < s.Height - 2; y++)
            for (int x = 2; x < s.Width - 2; x++)
            {
                int d = dist[map.Index(x, y)];
                if (d < s.MinNestDistanceFromTown) continue;
                // Steps alone let a nest sit diagonally within the town's sight (seen at the start,
                // which breaks "every nest is found by looking"); keep the straight line as far.
                if ((new Vector2Int(x, y) - c).sqrMagnitude < s.MinNestDistanceFromTown * s.MinNestDistanceFromTown) continue;
                var t = map[x, y];
                if (t == TileType.Road || !MapData.IsWalkable(t)) continue;
                candidates.Add(new Vector2Int(x, y));
            }
            rng.Shuffle(candidates);
            // P4-81: on the 512 map an even spread put a quarter of the nests on the old 256 map round
            // the town; the inner ones are placed first, then the outer land, then anywhere left.
            bool split = s.InnerHalfSize > 0 && s.InnerNestCount > 0;
            bool Inner(Vector2Int cell) => Mathf.Abs(cell.x - c.x) <= s.InnerHalfSize && Mathf.Abs(cell.y - c.y) <= s.InnerHalfSize;
            if (split)
            {
                PlaceNests(map, candidates, Mathf.Min(s.InnerNestCount, s.NestCount), dist, Inner);
                PlaceNests(map, candidates, s.NestCount, dist, cell => !Inner(cell));
            }
            PlaceNests(map, candidates, s.NestCount, dist, null);

            // 5. Edge spawn cells for raid waves.
            for (int y = 0; y < s.Height; y++)
            for (int x = 0; x < s.Width; x++)
            {
                bool edge = x < 2 || y < 2 || x >= s.Width - 2 || y >= s.Height - 2;
                if (edge && dist[map.Index(x, y)] >= 0) map.EdgeSpawnCells.Add(new Vector2Int(x, y));
            }

            // 6. Pockets (P4-80): walkable ground nobody can reach from town — the middle of a rock mass,
            //    an islet — becomes the ground round it. A nearest-walkable search could still pick it
            //    (a campfire, the start of a path for someone pushed into the rock), and whoever ended up
            //    there was walled in for good: a ranger stood in a two-cell hole in the rock for ten days.
            FillPockets(map, dist);

            return map;
        }

        /// <summary>
        /// Adds nest sites from the shuffled candidates (those <paramref name="where"/> allows) until the
        /// map has <paramref name="upTo"/>; sites keep 15 cells apart.
        /// </summary>
        static void PlaceNests(MapData map, List<Vector2Int> candidates, int upTo, int[] dist, System.Func<Vector2Int, bool> where)
        {
            foreach (var cell in candidates)
            {
                if (map.Nests.Count >= upTo) break;
                if (where != null && !where(cell)) continue;
                bool tooClose = false;
                foreach (var n in map.Nests)
                    if ((n.Cell - cell).sqrMagnitude < 15 * 15) { tooClose = true; break; }
                if (tooClose) continue;
                map.Nests.Add(new NestSite
                {
                    Cell = cell,
                    Biome = ClassifyBiome(map, cell),
                    Region = map.RegionOf(cell.x, cell.y),
                    DistanceFromTown = dist[map.Index(cell.x, cell.y)],
                });
            }
        }

        /// <summary>Turns each walkable patch <paramref name="dist"/> never reached into the blocking ground that surrounds it most.</summary>
        static void FillPockets(MapData map, int[] dist)
        {
            int w = map.Width, h = map.Height;
            var seen = new bool[w * h];
            var cells = new List<int>();
            var stack = new Stack<int>();
            var votes = new Dictionary<TileType, int>();
            for (int start = 0; start < seen.Length; start++)
            {
                if (seen[start] || dist[start] >= 0 || !MapData.IsWalkable(map.Tiles[start])) continue;
                cells.Clear();
                votes.Clear();
                seen[start] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    cells.Add(i);
                    int x = i % w, y = i / w;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int n = ny * w + nx;
                        var t = map.Tiles[n];
                        if (!MapData.IsWalkable(t))
                        {
                            // Natural ground only: a hole by the town wall is not walled up with wall.
                            if (t != TileType.Wall) votes[t] = votes.TryGetValue(t, out int v) ? v + 1 : 1;
                            continue;
                        }
                        if (seen[n]) continue;
                        seen[n] = true;
                        stack.Push(n);
                    }
                }
                var fill = TileType.Mountain;
                int most = 0;
                foreach (var kv in votes)
                    if (kv.Value > most) { most = kv.Value; fill = kv.Key; }
                foreach (int i in cells)
                {
                    map.Tiles[i] = fill;
                    map.Trees[i] = false;
                }
            }
        }

        static float Fbm(float x, float y)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int i = 0; i < 4; i++)
            {
                sum += Mathf.PerlinNoise(x * freq, y * freq) * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return sum / norm;
        }

        static void CarveRoad(MapData map, Vector2Int start, Vector2Int dir, GameRandom rng)
        {
            var p = start;
            var side = new Vector2Int(dir.y, dir.x);
            int wobbleCooldown = 6;
            while (map.InBounds(p))
            {
                map[p] = TileType.Road;
                if (--wobbleCooldown <= 0 && rng.Chance(0.25f))
                {
                    var step = side * (rng.Chance(0.5f) ? 1 : -1);
                    if (map.InBounds(p + step)) { p += step; map[p] = TileType.Road; }
                    wobbleCooldown = 4;
                }
                p += dir;
            }
        }

        /// <summary>The surroundings of a cell as a nest site sees them (also where a rare monster appears, P4-85).</summary>
        public static Biome ClassifyBiome(MapData map, Vector2Int cell)
        {
            int forest = 0, mountain = 0, water = 0;
            for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
            {
                var p = new Vector2Int(cell.x + dx, cell.y + dy);
                if (!map.InBounds(p)) continue;
                switch (map[p])
                {
                    case TileType.Forest: forest++; break;
                    case TileType.Mountain: mountain++; break;
                    case TileType.Water:
                    case TileType.Sand: water++; break;
                }
            }
            if (mountain >= 6) return Biome.Mountain;
            if (water >= 8) return Biome.Shore;
            if (forest >= 12) return Biome.Forest;
            return Biome.Plains;
        }

        /// <summary>BFS step distance from origin over walkable tiles; -1 = unreachable.</summary>
        public static int[] FloodDistances(MapData map, Vector2Int origin)
        {
            var dist = new int[map.Width * map.Height];
            for (int i = 0; i < dist.Length; i++) dist[i] = -1;
            var q = new Queue<Vector2Int>();
            dist[map.Index(origin.x, origin.y)] = 0;
            q.Enqueue(origin);
            var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            while (q.Count > 0)
            {
                var p = q.Dequeue();
                int d = dist[map.Index(p.x, p.y)];
                foreach (var dir in dirs)
                {
                    var n = p + dir;
                    if (!map.InBounds(n) || !MapData.IsWalkable(map[n])) continue;
                    int ni = map.Index(n.x, n.y);
                    if (dist[ni] >= 0) continue;
                    dist[ni] = d + 1;
                    q.Enqueue(n);
                }
            }
            return dist;
        }
    }
}
