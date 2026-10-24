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
        /// <summary>
        /// The full world: P4-101 ridges and rivers on the region borders with passes and dens holding some; P4-102 the
        /// zones' lie of the land, landmarks, the road network, danger by zone, village sites and dens in packs. Off (small
        /// test maps): zones and regions alone, the gates' roads run to the edge, dens anywhere.
        /// </summary>
        public bool Landforms;
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
        /// A region's cut-offs as the zone's lie of the land shifts them (P4-102): zones of one region differ — open dunes
        /// next to a rocky upland next to an oasis — instead of the same speckle everywhere.
        /// </summary>
        static TerrainThresholds Thresholds(Region r, ZoneCharacter ch)
        {
            var t = Thresholds(r);
            switch (ch)
            {
                case ZoneCharacter.Open: t.Water -= 0.06f; t.Sand -= 0.04f; t.Mountain += 0.08f; t.Forest += 0.10f; break;
                case ZoneCharacter.Lakes: t.Water += 0.065f; t.Sand += 0.075f; t.Mountain += 0.06f; break;
                case ZoneCharacter.Woods: t.Forest -= 0.11f; t.Mountain += 0.04f; t.Water -= 0.02f; t.Sand -= 0.02f; break;
                case ZoneCharacter.Rocky: t.Mountain -= 0.075f; t.Water -= 0.04f; t.Sand -= 0.04f; break;
            }
            return t;
        }

        /// <summary>
        /// Cut-offs averaged over the regions (and zones) around a cell, so terrain changes gradually across a
        /// border. Where lava meets water there is no pool at all: a lake half blue, half lava read
        /// as a rendering fault.
        /// </summary>
        static TerrainThresholds BlendedThresholds(Region[] around, ZoneCharacter[] lie)
        {
            TerrainThresholds sum = default;
            for (int k = 0; k < around.Length; k++)
            {
                var t = Thresholds(around[k], lie[k]);
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
            var c = new Vector2Int(s.Width / 2, s.Height / 2);
            // Zones and regions (P4-102): the town's fields, then about 25 zones spread over by the four outer regions.
            ZoneMap.Lay(map, c, new GameRandom(s.Seed ^ 0x20E5));
            // P4-101/102 draw from their own stream, so the draws below stay as they were on a map without them.
            var land = new GameRandom(s.Seed ^ 0x51F1);
            if (s.Landforms) ZoneTerrain.AssignCharacters(map, land);

            // 1. Terrain from height + moisture noise; each region has its own mix (few trees in the
            //    desert, many pools in the swamp, rocks in the wasteland whose "water" is lava), and since P4-102
            //    each zone its lie of the land, in broader shapes.
            var around = new Region[9];
            var lie = new ZoneCharacter[9];
            float freq = s.Landforms ? 0.024f : 0.035f;
            for (int y = 0; y < s.Height; y++)
            for (int x = 0; x < s.Width; x++)
            {
                TerrainRegions.SampleAround(map, x, y, around);
                ZoneTerrain.SampleLie(map, x, y, lie);
                var th = BlendedThresholds(around, lie);
                float h = Fbm(x * freq + ox, y * freq + oy);
                float m = Fbm(x * 0.05f + mx, y * 0.05f + my);
                TileType t;
                if (h < th.Water) t = TileType.Water;
                else if (h < th.Sand) t = TileType.Sand;
                else if (h > th.Mountain) t = TileType.Mountain;
                else if (m > th.Forest) t = TileType.Forest;
                else t = TileType.Grass;
                map[x, y] = t;
            }
            // The regions as laid out, before snapping: the barriers run on these smooth borders (P4-101).
            var raw = s.Landforms ? (Region[])map.Regions.Clone() : null;
            // Regions snap to the terrain before roads and the town clearing cut features in two.
            SnapRegionsToFeatures(map);

            RegionBarriers.Layout borders = null;
            if (s.Landforms)
            {
                borders = RegionBarriers.Lay(map, raw, c, s.TownHalfSize, land);
                ZoneTerrain.Dividers(map, borders, c, s.TownHalfSize, land);
                ZoneTerrain.Landmarks(map, c, land);
            }

            // 2. Town: cleared surroundings, floor, walls and 4 gates.
            TownWalls.Lay(map, c, s.TownHalfSize);
            // The gates' roads: to the map's edge, or (P4-102) only as far as the walls can move out — the road network goes on from there.
            for (int g = 0; g < TownWalls.GateDirections.Length; g++)
                CarveRoad(map, map.Gates[g] + TownWalls.GateDirections[g], TownWalls.GateDirections[g], rng,
                    s.Landforms ? RoadNetwork.StubReach - s.TownHalfSize : int.MaxValue);
            TownWalls.LayPlazaRoads(map);
            if (s.Landforms)
            {
                RegionBarriers.OpenPasses(map, raw, borders, c, s.TownHalfSize, land);
                // Land the barriers and landforms shut in gets a way out (smaller pockets are filled at the end).
                ConnectStranded(map, c, StrandedMinCells);
                ZoneTerrain.Hearts(map, FloodDistances(map, c));
                ZoneLore.Lay(map, c, land);
                RoadNetwork.Lay(map, c, map.SettlementSites, land);
            }

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
                // P4-102: dens keep off the roads and the village sites.
                if (s.Landforms && (NearRoad(map, x, y, DenRoadClearance) || NearSettlement(map, x, y))) continue;
                candidates.Add(new Vector2Int(x, y));
            }
            rng.Shuffle(candidates);
            // P4-81: on the 512 map an even spread put a quarter of the nests on the old 256 map round
            // the town; the inner ones are placed first, then the outer land, then anywhere left.
            bool split = s.InnerHalfSize > 0 && s.InnerNestCount > 0;
            bool Inner(Vector2Int cell) => Mathf.Abs(cell.x - c.x) <= s.InnerHalfSize && Mathf.Abs(cell.y - c.y) <= s.InnerHalfSize;
            // P4-101: dens holding passes come first; they count towards the inner and outer shares like any other.
            if (s.Landforms) PlaceGuards(map, dist, c, s, land);
            if (split)
            {
                int outerGuards = 0;
                foreach (var n in map.Nests) if (!Inner(n.Cell)) outerGuards++;
                int inner = Mathf.Min(s.InnerNestCount + outerGuards, s.NestCount);
                // P4-102: in packs round the zones' hearts first, then as before.
                if (s.Landforms) PlaceClusters(map, candidates, inner, dist, Inner, rng);
                PlaceNests(map, candidates, inner, dist, Inner);
                if (s.Landforms) PlaceClusters(map, candidates, s.NestCount, dist, cell => !Inner(cell), rng);
                PlaceNests(map, candidates, s.NestCount, dist, cell => !Inner(cell));
            }
            else if (s.Landforms) PlaceClusters(map, candidates, s.NestCount, dist, null, rng);
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
                if (CrowdedByNests(map, cell)) continue;
                AddNest(map, cell, dist);
            }
        }

        /// <summary>Dens keep this far apart.</summary>
        const int NestSpacing = 15;
        /// <summary>Dens keep this far off the roads (P4-102), and a pack's dens lie within this of its middle, packs this far apart.</summary>
        const int DenRoadClearance = 4;
        const float PackRadius = 24f, PackSpacing = 34f;
        /// <summary>Dens per pack, about, and at most per zone (two packs).</summary>
        const float DensPerPack = 3f;
        const int MaxDensPerZone = 5;

        static bool CrowdedByNests(MapData map, Vector2Int cell)
        {
            foreach (var n in map.Nests)
                if ((n.Cell - cell).sqrMagnitude < NestSpacing * NestSpacing) return true;
            return false;
        }

        static void AddNest(MapData map, Vector2Int cell, int[] dist)
        {
            map.Nests.Add(new NestSite
            {
                Cell = cell,
                Biome = ClassifyBiome(map, cell),
                Region = map.RegionOf(cell.x, cell.y),
                DistanceFromTown = dist[map.Index(cell.x, cell.y)],
                Zone = map.ZoneIds[map.Index(cell.x, cell.y)],
            });
        }

        static bool NearRoad(MapData map, int x, int y, int r)
        {
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (map.InBounds(x + dx, y + dy) && map[x + dx, y + dy] == TileType.Road) return true;
            return false;
        }

        static bool NearSettlement(MapData map, int x, int y)
        {
            foreach (var s in map.SettlementSites)
                if ((s.x - x) * (s.x - x) + (s.y - y) * (s.y - y) < NestSpacing * NestSpacing) return true;
            return false;
        }

        /// <summary>
        /// P4-102: dens in packs. The dens still to place (up to <paramref name="upTo"/>, among the cells
        /// <paramref name="where"/> allows) are shared out over the zones by how much ground each offers; a zone's share
        /// goes in packs of about three round spots near its heart, the packs apart, then anywhere in it.
        /// </summary>
        static void PlaceClusters(MapData map, List<Vector2Int> candidates, int upTo, int[] dist, System.Func<Vector2Int, bool> where, GameRandom rng)
        {
            int need = upTo - map.Nests.Count;
            if (need <= 0) return;
            var byZone = new SortedDictionary<int, List<Vector2Int>>();
            int total = 0;
            foreach (var cell in candidates)
            {
                if (where != null && !where(cell)) continue;
                int z = map.ZoneIds[map.Index(cell.x, cell.y)];
                if (z < 0 || z >= map.Zones.Count) continue;
                if (!byZone.TryGetValue(z, out var list)) byZone[z] = list = new List<Vector2Int>();
                list.Add(cell);
                total++;
            }
            if (total == 0) return;
            // Shares by the largest remainder, no zone over its cap (a wide open zone by the town took nine dens and was a
            // death trap in the measured run); what is left goes round the others.
            var quota = new Dictionary<int, int>();
            var rest = new List<(float r, int z)>();
            int given = 0;
            foreach (var kv in byZone)
            {
                float exact = need * kv.Value.Count / (float)total;
                quota[kv.Key] = Mathf.Min(MaxDensPerZone, Mathf.FloorToInt(exact));
                given += quota[kv.Key];
                rest.Add((exact - quota[kv.Key], kv.Key));
            }
            rest.Sort((a, b) => b.r.CompareTo(a.r));
            for (bool any = true; given < need && any;)
            {
                any = false;
                foreach (var (_, z) in rest)
                {
                    if (given >= need) break;
                    if (quota[z] >= MaxDensPerZone) continue;
                    quota[z]++;
                    given++;
                    any = true;
                }
            }

            foreach (var kv in byZone)
            {
                int q = quota[kv.Key];
                if (q <= 0) continue;
                var heart = map.Zones[kv.Key].Heart;
                var cells = kv.Value;
                var byHeart = new List<Vector2Int>(cells);
                byHeart.Sort((a, b) => (a - heart).sqrMagnitude.CompareTo((b - heart).sqrMagnitude));
                int packs = Mathf.CeilToInt(q / DensPerPack), placed = 0;
                var centres = new List<Vector2Int>();
                for (int p = 0; p < packs; p++)
                {
                    // Near the heart (the closer half of the zone's ground), apart from the other packs.
                    var options = new List<Vector2Int>();
                    for (int i = 0; i < byHeart.Count / 2 && options.Count < 24; i++)
                    {
                        bool apart = true;
                        foreach (var o in centres) if (Vector2.Distance(o, byHeart[i]) < PackSpacing) { apart = false; break; }
                        if (apart && !CrowdedByNests(map, byHeart[i])) options.Add(byHeart[i]);
                    }
                    if (options.Count == 0) break;
                    centres.Add(options[rng.Range(0, options.Count)]);
                }
                int perPack = centres.Count == 0 ? 0 : Mathf.CeilToInt(q / (float)centres.Count);
                foreach (var centre in centres)
                {
                    int inPack = 0;
                    var near = cells.FindAll(cell => Vector2.Distance(cell, centre) <= PackRadius);
                    near.Sort((a, b) => (a - centre).sqrMagnitude.CompareTo((b - centre).sqrMagnitude));
                    foreach (var cell in near)
                    {
                        if (inPack >= perPack || placed >= q || map.Nests.Count >= upTo) break;
                        if (CrowdedByNests(map, cell)) continue;
                        AddNest(map, cell, dist);
                        inPack++;
                        placed++;
                    }
                }
                foreach (var cell in cells)
                {
                    if (placed >= q || map.Nests.Count >= upTo) break;
                    if (CrowdedByNests(map, cell)) continue;
                    AddNest(map, cell, dist);
                    placed++;
                }
            }
        }

        /// <summary>A patch shut in by the barriers and landforms at least this big gets a way out; smaller ones are filled.</summary>
        const int StrandedMinCells = 80;
        /// <summary>A den holding a pass (P4-101) for this share of them, this far past the pass on the side away from the town.</summary>
        const float GuardChance = 0.6f, GuardMin = 8f, GuardMax = 13f;

        /// <summary>
        /// P4-101: walkable land of at least <paramref name="minCells"/> the town cannot reach gets the shortest way through
        /// to ground it can (rocks cut back to grass, water forded with sand, three cells wide), recorded as a pass within
        /// its region. Several rounds, as one cut can leave another patch nearer to land now reached.
        /// </summary>
        static void ConnectStranded(MapData map, Vector2Int town, int minCells)
        {
            int w = map.Width, h = map.Height, n = w * h;
            var parent = new int[n];
            var stamp = new int[n];
            int mark = 0;
            var patch = new List<int>();
            var stack = new Stack<int>();
            var queue = new Queue<int>();
            for (int round = 0; round < 4; round++)
            {
                var dist = FloodDistances(map, town);
                var seen = new bool[n];
                bool cut = false;
                for (int start = 0; start < n; start++)
                {
                    if (seen[start] || dist[start] >= 0 || !MapData.IsWalkable(map.Tiles[start])) continue;
                    patch.Clear();
                    seen[start] = true;
                    stack.Push(start);
                    while (stack.Count > 0)
                    {
                        int i = stack.Pop();
                        patch.Add(i);
                        int x = i % w, y = i / w;
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int j = ny * w + nx;
                            if (seen[j] || dist[j] >= 0 || !MapData.IsWalkable(map.Tiles[j])) continue;
                            seen[j] = true;
                            stack.Push(j);
                        }
                    }
                    if (patch.Count < minCells) continue;

                    // Outward from the patch through any ground but the town's walls, to the nearest reached cell.
                    mark++;
                    queue.Clear();
                    foreach (int i in patch) { stamp[i] = mark; parent[i] = -1; queue.Enqueue(i); }
                    int found = -1;
                    while (queue.Count > 0 && found < 0)
                    {
                        int i = queue.Dequeue();
                        int x = i % w, y = i / w;
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int j = ny * w + nx;
                            if (stamp[j] == mark || map.Tiles[j] is TileType.Wall or TileType.Gate or TileType.TownFloor) continue;
                            stamp[j] = mark;
                            parent[j] = i;
                            if (dist[j] >= 0) { found = j; break; }
                            queue.Enqueue(j);
                        }
                    }
                    if (found < 0) continue;

                    var way = new List<int>();
                    for (int i = parent[found]; i >= 0 && parent[i] >= 0; i = parent[i]) way.Add(i);
                    bool water = false;
                    foreach (int i in way)
                    {
                        int x = i % w, y = i / w;
                        for (int k = 0; k < 5; k++)
                        {
                            int nx = x + (k == 1 ? 1 : k == 2 ? -1 : 0), ny = y + (k == 3 ? 1 : k == 4 ? -1 : 0);
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int j = ny * w + nx;
                            if (map.Tiles[j] == TileType.Water) { map.Tiles[j] = TileType.Sand; water = true; }
                            else if (map.Tiles[j] == TileType.Mountain) map.Tiles[j] = TileType.Grass;
                            else continue;
                            map.Trees[j] = false;
                        }
                    }
                    // The patch is reached now: later patches this round may make for it.
                    foreach (int i in patch) dist[i] = 0;
                    foreach (int i in way) dist[i] = 0;
                    if (way.Count > 0)
                    {
                        int mid = way[way.Count / 2];
                        var from = new Vector2(patch[0] % w, patch[0] / w);
                        var region = map.Regions[mid];
                        map.Passes.Add(new PassSite
                        {
                            Cell = new Vector2Int(mid % w, mid / w), Kind = water ? PassKind.Ford : PassKind.MountainPass,
                            A = region, B = region, Across = (new Vector2(found % w, found / w) - from).normalized, GuardNest = -1,
                        });
                    }
                    cut = true;
                }
                if (!cut) return;
            }
        }

        /// <summary>
        /// P4-101 (user decision: dens hold the passes): a den for <see cref="GuardChance"/> of the passes between regions,
        /// a little past the pass on the side away from the town (either side on a border running outward).
        /// </summary>
        static void PlaceGuards(MapData map, int[] dist, Vector2Int town, MapGenSettings s, GameRandom rng)
        {
            for (int p = 0; p < map.Passes.Count; p++)
            {
                var pass = map.Passes[p];
                // P4-102: a road out of the town's fields is not held (the first hunts go that way).
                if (pass.A == pass.B || pass.OnRoad && (pass.A == Region.Grassland || pass.B == Region.Grassland) || !rng.Chance(GuardChance)) continue;
                var outward = (Vector2)(pass.Cell - town);
                float facing = Vector2.Dot(pass.Across, outward.normalized);
                float side = Mathf.Abs(facing) < 0.3f ? (rng.Chance(0.5f) ? 1f : -1f) : Mathf.Sign(facing);
                var along = new Vector2(-pass.Across.y, pass.Across.x);
                for (int attempt = 0; attempt < 16; attempt++)
                {
                    var spot = (Vector2)pass.Cell + pass.Across * side * rng.Range(GuardMin, GuardMax) + along * rng.Range(-4f, 4f);
                    var cell = new Vector2Int(Mathf.RoundToInt(spot.x), Mathf.RoundToInt(spot.y));
                    if (cell.x < 2 || cell.y < 2 || cell.x >= s.Width - 2 || cell.y >= s.Height - 2) continue;
                    int d = dist[map.Index(cell.x, cell.y)];
                    if (d < s.MinNestDistanceFromTown || (cell - town).sqrMagnitude < s.MinNestDistanceFromTown * s.MinNestDistanceFromTown) continue;
                    var t = map[cell.x, cell.y];
                    if (t == TileType.Road || !MapData.IsWalkable(t)) continue;
                    bool crowded = false;
                    foreach (var other in map.Nests)
                        if ((other.Cell - cell).sqrMagnitude < 15 * 15) { crowded = true; break; }
                    if (crowded) continue;
                    pass.GuardNest = map.Nests.Count;
                    map.Passes[p] = pass;
                    AddNest(map, cell, dist);
                    break;
                }
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

        static void CarveRoad(MapData map, Vector2Int start, Vector2Int dir, GameRandom rng, int length)
        {
            var p = start;
            var side = new Vector2Int(dir.y, dir.x);
            int wobbleCooldown = 6;
            for (int laid = 0; map.InBounds(p) && laid < length; laid++)
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
