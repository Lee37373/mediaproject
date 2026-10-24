using System.Collections.Generic;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>
    /// The map's zones and regions (P4-102, user request 2026-10-24 "the map looks wrong"). The town's own fields (zone 0,
    /// grassland) lie round it in a lobed patch; the rest of the land is cut into about 25 zones (Poisson seeds, a warped
    /// and weighted Voronoi). Four zones out on the far ring, about a quarter turn apart, seed the four outer regions,
    /// which then spread over the zones' neighbours at random costs — so a region is a run of whole zones with a ragged
    /// outline, not a slice of the compass round the town as before (P4-55).
    /// </summary>
    public static class ZoneMap
    {
        public const int Home = 0;

        /// <summary>The town's fields reach about this far (the 256 map's grassland: the early rings as tuned).</summary>
        public static float GrassRadius(float mapSize) => Mathf.Min(mapSize, 256f) * 0.21f;

        /// <summary>The fields' edge swings this share in and out round the town (inward less: the walls move out, P4-86).</summary>
        const float LobeIn = 0.15f, LobeOut = 0.35f;
        /// <summary>Cells a point is pushed before the fields' edge is read (as the old grassland's border).</summary>
        const float HomeWarp = 14f;
        /// <summary>Zone seeds keep this share of the map apart (at least <see cref="MinSpacing"/> cells).</summary>
        const float SpacingShare = 0.17f, MinSpacing = 40f;
        /// <summary>How far a point is pushed before its zone is read, as a share of the spacing: winding borders.</summary>
        const float WarpShare = 0.3f;
        /// <summary>Seeds weigh up to this share of the spacing either way: zones of different sizes.</summary>
        const float WeightSpread = 0.18f;
        /// <summary>Zones sharing at least this many border cells are neighbours.</summary>
        const int NeighbourCells = 6;
        /// <summary>A region that gets less than this share of the outer land is drawn again.</summary>
        const float MinRegionShare = 0.12f;

        static readonly Region[] Outer = { Region.Desert, Region.Snow, Region.Swamp, Region.Wasteland };

        /// <summary>Lays the zones over the surface, sets <see cref="MapData.ZoneIds"/>, <see cref="MapData.Zones"/> and every cell's region.</summary>
        public static void Lay(MapData map, Vector2Int town, GameRandom rng)
        {
            int w = map.Width, h = map.SurfaceHeight;
            float size = Mathf.Min(w, h);
            float grass = GrassRadius(size);
            float spacing = Mathf.Max(MinSpacing, size * SpacingShare);
            float nx = rng.Range(0f, 5000f), ny = rng.Range(0f, 5000f);

            // 1. Seeds: darts thrown until the land is full, none in the fields.
            var seeds = new List<Vector2>();
            var weights = new List<float>();
            float fromTown = grass * (1f + LobeOut) + spacing * 0.3f;
            for (int attempt = 0; attempt < 4000 && seeds.Count < 250; attempt++)
            {
                var p = new Vector2(rng.Range(0f, w), rng.Range(0f, h));
                if (Vector2.Distance(p, town) < fromTown) continue;
                bool close = false;
                foreach (var s in seeds) if ((s - p).sqrMagnitude < spacing * spacing) { close = true; break; }
                if (close) continue;
                seeds.Add(p);
                weights.Add(rng.Range(-WeightSpread, WeightSpread) * spacing);
            }

            // 2. Every cell: the fields, or the nearest seed from the warped point.
            var ids = map.ZoneIds;
            float warp = spacing * WarpShare, freq = 1f / (spacing * 0.9f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float cx = x + 0.5f, cy = y + 0.5f;
                int i = y * w + x;
                if (seeds.Count == 0 || IsHome(cx, cy, town, grass, nx, ny)) { ids[i] = Home; continue; }
                float wx = cx + (TerrainRegions.Noise(cx * freq + nx + 91f, cy * freq + ny) - 0.5f) * 2f * warp;
                float wy = cy + (TerrainRegions.Noise(cx * freq + nx, cy * freq + ny + 57f) - 0.5f) * 2f * warp;
                int best = 0;
                float bestD = float.MaxValue;
                for (int k = 0; k < seeds.Count; k++)
                {
                    float dx = wx - seeds[k].x, dy = wy - seeds[k].y;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - weights[k];
                    if (d < bestD) { bestD = d; best = k; }
                }
                ids[i] = (short)(best + 1);
            }
            int count = seeds.Count + 1;
            KeepWhole(ids, w, h, count);

            // 3. Sizes, middles and neighbours.
            var zones = new List<ZoneSite>();
            for (int z = 0; z < count; z++) zones.Add(new ZoneSite { Index = z });
            var sum = new Vector2[count];
            var shared = new Dictionary<int, int>();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x, z = ids[i];
                zones[z].Cells++;
                sum[z] += new Vector2(x + 0.5f, y + 0.5f);
                if (x + 1 < w && ids[i + 1] != z) Count(shared, z, ids[i + 1]);
                if (y + 1 < h && ids[i + w] != z) Count(shared, z, ids[i + w]);
            }
            for (int z = 0; z < count; z++) zones[z].Centre = zones[z].Cells > 0 ? sum[z] / zones[z].Cells : (Vector2)town;
            foreach (var kv in shared)
            {
                if (kv.Value < NeighbourCells) continue;
                int a = kv.Key >> 8, b = kv.Key & 0xFF;
                zones[a].Neighbours.Add(b);
                zones[b].Neighbours.Add(a);
            }
            foreach (var z in zones) z.Neighbours.Sort();

            // 4. Regions.
            var regionOf = Regions(zones, town, rng);
            for (int z = 0; z < count; z++) zones[z].Region = regionOf[z];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                map.Regions[y * w + x] = regionOf[ids[y * w + x]];
            map.Zones.Clear();
            map.Zones.AddRange(zones);
        }

        static void Count(Dictionary<int, int> shared, int a, int b)
        {
            int key = a < b ? (a << 8) | b : (b << 8) | a;
            shared[key] = shared.TryGetValue(key, out int n) ? n + 1 : 1;
        }

        /// <summary>The town's fields: a warped, lobed patch round the town.</summary>
        static bool IsHome(float x, float y, Vector2 town, float grass, float nx, float ny)
        {
            float wx = x + (TerrainRegions.Noise(x * 0.035f + nx + 191f, y * 0.035f + ny) - 0.5f) * 2f * HomeWarp;
            float wy = y + (TerrainRegions.Noise(x * 0.035f + nx, y * 0.035f + ny + 157f) - 0.5f) * 2f * HomeWarp;
            var d = new Vector2(wx, wy) - town;
            float a = Mathf.Atan2(d.y, d.x);
            float n = TerrainRegions.Noise(Mathf.Cos(a) * 1.4f + nx + 333f, Mathf.Sin(a) * 1.4f + ny + 333f);
            return d.magnitude < grass * Mathf.Lerp(1f - LobeIn, 1f + LobeOut, n);
        }

        /// <summary>
        /// A zone torn in pieces by the warp keeps its biggest piece; each other piece joins the zone it borders most.
        /// The fields keep theirs too (the warp can leave a stray patch of them out in the wilds).
        /// </summary>
        static void KeepWhole(short[] ids, int w, int h, int count)
        {
            int n = w * h;
            var piece = new int[n];
            System.Array.Fill(piece, -1);
            var sizes = new List<int>();
            var zoneOf = new List<int>();
            var stack = new Stack<int>();
            for (int start = 0; start < n; start++)
            {
                if (piece[start] >= 0) continue;
                int id = sizes.Count, z = ids[start], size = 0;
                piece[start] = id;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    size++;
                    int x = i % w, y = i / w;
                    for (int k = 0; k < 4; k++)
                    {
                        int ax = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ay = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (ax < 0 || ay < 0 || ax >= w || ay >= h) continue;
                        int j = ay * w + ax;
                        if (piece[j] >= 0 || ids[j] != z) continue;
                        piece[j] = id;
                        stack.Push(j);
                    }
                }
                sizes.Add(size);
                zoneOf.Add(z);
            }
            var biggest = new int[count];
            System.Array.Fill(biggest, -1);
            for (int p = 0; p < sizes.Count; p++)
                if (biggest[zoneOf[p]] < 0 || sizes[p] > sizes[biggest[zoneOf[p]]]) biggest[zoneOf[p]] = p;
            // Each stray piece takes the zone it borders most (counted over its rim).
            var votes = new Dictionary<int, Dictionary<int, int>>();
            for (int i = 0; i < n; i++)
            {
                int p = piece[i];
                if (biggest[zoneOf[p]] == p) continue;
                int x = i % w, y = i / w;
                for (int k = 0; k < 4; k++)
                {
                    int ax = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ay = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (ax < 0 || ay < 0 || ax >= w || ay >= h) continue;
                    int other = ids[ay * w + ax];
                    if (piece[ay * w + ax] == p) continue;
                    if (!votes.TryGetValue(p, out var v)) votes[p] = v = new Dictionary<int, int>();
                    v[other] = v.TryGetValue(other, out int c) ? c + 1 : 1;
                }
            }
            var into = new Dictionary<int, short>();
            foreach (var kv in votes)
            {
                int best = -1, most = 0;
                foreach (var v in kv.Value)
                    if (v.Value > most || v.Value == most && v.Key < best) { most = v.Value; best = v.Key; }
                if (best >= 0) into[kv.Key] = (short)best;
            }
            for (int i = 0; i < n; i++)
                if (into.TryGetValue(piece[i], out var z)) ids[i] = z;
        }

        /// <summary>
        /// The fields are grassland; four zones out on the far ring, about a quarter turn apart, take the outer regions
        /// and spread over the neighbours at costs drawn per border (cheapest first). Drawn again while a region gets too
        /// little of the land.
        /// </summary>
        static Region[] Regions(List<ZoneSite> zones, Vector2 town, GameRandom rng)
        {
            int count = zones.Count;
            var result = new Region[count];
            if (count <= 1) return result;
            var order = new List<Region>(Outer);
            rng.Shuffle(order);
            float far = 0f;
            int outerCells = 0;
            for (int z = 1; z < count; z++)
            {
                far = Mathf.Max(far, Vector2.Distance(zones[z].Centre, town));
                outerCells += zones[z].Cells;
            }

            for (int round = 0; round < 8; round++)
            {
                // Anchors on the far ring, a quarter turn apart give or take 25°.
                float turn = rng.Range(0f, Mathf.PI * 2f);
                var anchors = new int[Outer.Length];
                for (int k = 0; k < anchors.Length; k++)
                {
                    float target = turn + k * Mathf.PI / 2f + rng.Range(-0.44f, 0.44f);
                    int best = -1;
                    float bestOff = float.MaxValue;
                    for (int z = 1; z < count; z++)
                    {
                        var d = zones[z].Centre - town;
                        if (d.magnitude < far * 0.55f || System.Array.IndexOf(anchors, z, 0, k) >= 0) continue;
                        float off = Mathf.Abs(Mathf.DeltaAngle(target * Mathf.Rad2Deg, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg));
                        if (off < bestOff) { bestOff = off; best = z; }
                    }
                    anchors[k] = best;
                }

                // Spread: cheapest first over the neighbours (never through the fields), each border's cost drawn once.
                var cost = new float[count];
                var owner = new int[count];
                var done = new bool[count];
                for (int z = 0; z < count; z++) { cost[z] = float.MaxValue; owner[z] = -1; }
                for (int k = 0; k < anchors.Length; k++)
                    if (anchors[k] > 0) { cost[anchors[k]] = 0f; owner[anchors[k]] = k; }
                var edge = new Dictionary<int, float>();
                for (int a = 1; a < count; a++)
                    foreach (int b in zones[a].Neighbours)
                        if (b > a) edge[(a << 8) | b] = Vector2.Distance(zones[a].Centre, zones[b].Centre) * rng.Range(0.7f, 1.3f);
                while (true)
                {
                    int next = -1;
                    for (int z = 1; z < count; z++)
                        if (!done[z] && owner[z] >= 0 && (next < 0 || cost[z] < cost[next])) next = z;
                    if (next < 0) break;
                    done[next] = true;
                    foreach (int b in zones[next].Neighbours)
                    {
                        if (b == Home || done[b]) continue;
                        float c = cost[next] + edge[next < b ? (next << 8) | b : (b << 8) | next];
                        if (c < cost[b]) { cost[b] = c; owner[b] = owner[next]; }
                    }
                }
                // Zones the spread never reached (touching only the fields): the anchor nearest round the compass.
                for (int z = 1; z < count; z++)
                {
                    if (owner[z] >= 0) continue;
                    float bestOff = float.MaxValue;
                    var d = zones[z].Centre - town;
                    for (int k = 0; k < anchors.Length; k++)
                    {
                        if (anchors[k] <= 0) continue;
                        var a = zones[anchors[k]].Centre - town;
                        float off = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, Mathf.Atan2(a.y, a.x) * Mathf.Rad2Deg));
                        if (off < bestOff) { bestOff = off; owner[z] = k; }
                    }
                    if (owner[z] < 0) owner[z] = 0;
                }

                var share = new int[Outer.Length];
                for (int z = 1; z < count; z++) share[owner[z]] += zones[z].Cells;
                bool even = true;
                foreach (int s in share) if (s < outerCells * MinRegionShare) even = false;
                result[Home] = Region.Grassland;
                for (int z = 1; z < count; z++) result[z] = order[owner[z]];
                if (even) break;
            }
            return result;
        }
    }
}
