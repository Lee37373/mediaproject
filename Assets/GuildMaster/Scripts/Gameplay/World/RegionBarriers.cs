using System.Collections.Generic;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>
    /// Ridges and rivers on the region borders (P4-101, user request 2026-10-21: "on this map you just walk straight").
    /// Every border — the grassland's rim and the lines between the outer regions — is shut by a mountain ridge or a
    /// river and crossed at 2–3 passes, bridges or fords (user decision), plus wherever a gate's road meets it.
    /// Borders touching the wasteland are ridges: its "water" is lava, and a river there would run half lava, half water.
    /// </summary>
    public static class RegionBarriers
    {
        public enum Kind : byte { Ridge, River }

        /// <summary>No barrier this close (cells past the starting walls, either axis): the walls move out up to 12 (P4-86).</summary>
        public const int TownClearance = 18;
        /// <summary>Half widths (cells either side of the border), the low and high ends of their wobble.</summary>
        const float RidgeHalfMin = 2.6f, RidgeHalfMax = 4.8f, RiverHalfMin = 1.3f, RiverHalfMax = 2.3f;
        /// <summary>Sand bank either side of a river.</summary>
        const float Bank = 1.2f;
        /// <summary>Ways across each border (user decision: 2–3).</summary>
        const int MinPasses = 2, MaxPasses = 3;
        /// <summary>Rivers on a map at least (where the borders allow): the user asked for ridges and rivers both.</summary>
        const int MinRivers = 2;
        const float RiverChance = 0.5f, BridgeChance = 0.6f;
        /// <summary>A way across this close to another border (where three regions meet) could open into the wrong one.</summary>
        const float JunctionClearance = 14f, ShortJunctionClearance = 8f;
        /// <summary>A pass slot this close to a road crossing or another pass is taken by it.</summary>
        const float SlotSpacing = 25f;
        const int EdgeMargin = 8;
        /// <summary>Half widths of the ways across: the cut between the rocks, a bridge's deck, a ford's shallows.</summary>
        const float PassHalf = 3f, BridgeHalf = 1f, FordHalf = 2.5f;
        /// <summary>How far past the barrier a way across also clears lakes and rocks, so it opens onto land.</summary>
        const float Approach = 5f;
        /// <summary>A lake a river runs into takes the river's colours up to this size; a bigger one is kept apart by a sand bar.</summary>
        const int LakeRecolourLimit = 600;

        static readonly int[] Dx4 = { 1, -1, 0, 0 }, Dy4 = { 0, 0, 1, -1 };

        /// <summary>The borders as laid: per cell the distance to the nearest border and which border it is; the barrier cells.</summary>
        public sealed class Layout
        {
            public float[] Distance;
            /// <summary><see cref="Key"/> of the nearest border, -1 on a map with a single region.</summary>
            public int[] Pair;
            public bool[] Barrier;
            public readonly Dictionary<int, Kind> Kinds = new();
            /// <summary>The region whose colours a river takes (all of it, so it is never half one water, half another).</summary>
            public readonly Dictionary<int, Region> RiverRegion = new();
            public float NoiseX, NoiseY;
        }

        /// <summary>One key per pair of regions, either order.</summary>
        public static int Key(Region a, Region b) => a < b ? (int)a * 8 + (int)b : (int)b * 8 + (int)a;
        public static Region First(int key) => (Region)(key / 8);
        public static Region Second(int key) => (Region)(key % 8);

        /// <summary>Ground kept for the town and its walls (P4-86: they move out up to 12 cells).</summary>
        public static bool NearTown(int x, int y, Vector2Int town, int townHalf)
            => Mathf.Max(Mathf.Abs(x - town.x), Mathf.Abs(y - town.y)) <= townHalf + TownClearance;

        /// <summary>
        /// Lays the ridges and rivers along the borders of <paramref name="raw"/> (the regions as the layout draws them,
        /// before they were snapped to lakes and woods). The ways across come later (<see cref="OpenPasses"/>, after the roads).
        /// </summary>
        public static Layout Lay(MapData map, Region[] raw, Vector2Int town, int townHalf, GameRandom rng)
        {
            int w = map.Width, h = map.Height;
            var l = new Layout { NoiseX = rng.Range(0f, 5000f), NoiseY = rng.Range(0f, 5000f), Barrier = new bool[w * h] };
            BorderField(raw, w, h, l);
            ChooseKinds(l, rng);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x, key = l.Pair[i];
                if (key < 0 || l.Distance[i] > RidgeHalfMax + Bank + 1f || NearTown(x, y, town, townHalf)) continue;
                float d = l.Distance[i];
                if (l.Kinds[key] == Kind.Ridge || NearWasteland(raw, w, h, x, y))
                {
                    // Where a river meets the wasteland (three regions at a point) it rises in the rocks instead.
                    float half = l.Kinds[key] == Kind.Ridge ? Half(Kind.Ridge, x, y, l) : Half(Kind.River, x, y, l);
                    if (d > half) continue;
                    Set(map, i, TileType.Mountain);
                    l.Barrier[i] = true;
                    continue;
                }
                float riverHalf = Half(Kind.River, x, y, l);
                if (d > riverHalf + Bank) continue;
                map.Regions[i] = l.RiverRegion[key];
                if (d <= riverHalf)
                {
                    Set(map, i, TileType.Water);
                    l.Barrier[i] = true;
                }
                else if (map.Tiles[i] != TileType.Water) Set(map, i, TileType.Sand);
            }
            MatchLakes(map, l);
            return l;
        }

        static void Set(MapData map, int i, TileType t)
        {
            map.Tiles[i] = t;
            map.Trees[i] = false;
        }

        /// <summary>The barrier's half width here: it swells and narrows along its length.</summary>
        static float Half(Kind kind, int x, int y, Layout l)
        {
            float n = Mathf.PerlinNoise(x * 0.07f + l.NoiseX, y * 0.07f + l.NoiseY);
            return kind == Kind.Ridge ? Mathf.Lerp(RidgeHalfMin, RidgeHalfMax, n) : Mathf.Lerp(RiverHalfMin, RiverHalfMax, n);
        }

        static bool NearWasteland(Region[] raw, int w, int h, int x, int y)
        {
            for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < w && ny < h && raw[ny * w + nx] == Region.Wasteland) return true;
            }
            return false;
        }

        /// <summary>
        /// Distance to the nearest border cell (a cell beside another region) and which border, by passing each cell's
        /// nearest border cell on in four sweeps (close to the true distance; exactness does not matter for a ridge).
        /// </summary>
        static void BorderField(Region[] raw, int w, int h, Layout l)
        {
            int n = w * h;
            var dist = new float[n];
            var pair = new int[n];
            var src = new int[n];
            for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; pair[i] = -1; src[i] = -1; }
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx4[k], ny = y + Dy4[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h || raw[ny * w + nx] == raw[i]) continue;
                    dist[i] = 0f;
                    src[i] = i;
                    pair[i] = Key(raw[i], raw[ny * w + nx]);
                    break;
                }
            }

            void Try(int x, int y, int nx, int ny)
            {
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) return;
                int s = src[ny * w + nx];
                if (s < 0) return;
                int sx = s % w, sy = s / w;
                float d = Mathf.Sqrt((x - sx) * (x - sx) + (y - sy) * (y - sy));
                int i = y * w + x;
                if (d >= dist[i]) return;
                dist[i] = d;
                src[i] = s;
                pair[i] = pair[ny * w + nx];
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++) { Try(x, y, x - 1, y); Try(x, y, x - 1, y - 1); Try(x, y, x, y - 1); Try(x, y, x + 1, y - 1); }
                for (int x = w - 1; x >= 0; x--) Try(x, y, x + 1, y);
            }
            for (int y = h - 1; y >= 0; y--)
            {
                for (int x = w - 1; x >= 0; x--) { Try(x, y, x + 1, y); Try(x, y, x + 1, y + 1); Try(x, y, x, y + 1); Try(x, y, x - 1, y + 1); }
                for (int x = 0; x < w; x++) Try(x, y, x - 1, y);
            }
            l.Distance = dist;
            l.Pair = pair;
        }

        static void ChooseKinds(Layout l, GameRandom rng)
        {
            var keys = new SortedSet<int>();
            foreach (int p in l.Pair) if (p >= 0) keys.Add(p);
            var open = new List<int>();
            int rivers = 0;
            foreach (int key in keys)
            {
                bool waste = First(key) == Region.Wasteland || Second(key) == Region.Wasteland;
                var kind = !waste && rng.Chance(RiverChance) ? Kind.River : Kind.Ridge;
                l.Kinds[key] = kind;
                if (kind == Kind.River) rivers++;
                else if (!waste) open.Add(key);
            }
            rng.Shuffle(open);
            foreach (int key in open)
            {
                if (rivers >= MinRivers) break;
                l.Kinds[key] = Kind.River;
                rivers++;
            }
            foreach (int key in keys)
                if (l.Kinds[key] == Kind.River) l.RiverRegion[key] = RiverColours(First(key), Second(key));
        }

        /// <summary>Clear water first: the grassland's and desert's blue, then the swamp's murk, the snowfield's ice last.</summary>
        static Region RiverColours(Region a, Region b)
        {
            static int Rank(Region r) => r switch { Region.Grassland => 0, Region.Desert => 1, Region.Swamp => 2, _ => 3 };
            return Rank(a) <= Rank(b) ? a : b;
        }

        /// <summary>
        /// A lake a river runs into takes the river's colours (a lake half ice, half water read as a fault, like the
        /// lava one of 2026-09-22); a big one is kept apart by a sand bar instead.
        /// </summary>
        static void MatchLakes(MapData map, Layout l)
        {
            int w = map.Width, h = map.Height;
            var seen = new bool[w * h];
            var lake = new List<int>();
            var stack = new Stack<int>();
            for (int i = 0; i < seen.Length; i++)
            {
                if (!l.Barrier[i] || map.Tiles[i] != TileType.Water) continue;
                var colours = map.Regions[i];
                int x = i % w, y = i / w;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx4[k], ny = y + Dy4[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = ny * w + nx;
                    if (seen[j] || l.Barrier[j] || map.Tiles[j] != TileType.Water || map.Regions[j] == colours) continue;
                    lake.Clear();
                    seen[j] = true;
                    stack.Push(j);
                    while (stack.Count > 0)
                    {
                        int c = stack.Pop();
                        lake.Add(c);
                        int cx = c % w, cy = c / w;
                        for (int q = 0; q < 4; q++)
                        {
                            int ax = cx + Dx4[q], ay = cy + Dy4[q];
                            if (ax < 0 || ay < 0 || ax >= w || ay >= h) continue;
                            int a = ay * w + ax;
                            if (seen[a] || l.Barrier[a] || map.Tiles[a] != TileType.Water || map.Regions[a] == colours) continue;
                            seen[a] = true;
                            stack.Push(a);
                        }
                    }
                    if (lake.Count <= LakeRecolourLimit)
                    {
                        foreach (int c in lake)
                        {
                            map.Regions[c] = colours;
                            int cx = c % w, cy = c / w;
                            for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int ax = cx + dx, ay = cy + dy;
                                if (ax >= 0 && ay >= 0 && ax < w && ay < h && map.Tiles[ay * w + ax] == TileType.Sand) map.Regions[ay * w + ax] = colours;
                            }
                        }
                    }
                    else
                    {
                        foreach (int c in lake)
                            if (NextToRiver(map, l, c % w, c / w)) Set(map, c, TileType.Sand);
                    }
                }
            }
        }

        static bool NextToRiver(MapData map, Layout l, int x, int y)
        {
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < map.Width && ny < map.Height && l.Barrier[ny * map.Width + nx]) return true;
            }
            return false;
        }

        // ---------------- Ways across ----------------

        /// <summary>
        /// Opens the barriers: where a gate's road runs through one (a cut through the rocks round it; over a river the road
        /// is the bridge), then 2–3 passes, bridges or fords spread along each border, slots a road crossing already fills skipped.
        /// </summary>
        public static void OpenPasses(MapData map, Region[] raw, Layout l, Vector2Int town, int townHalf, GameRandom rng)
        {
            int w = map.Width, h = map.Height;
            RoadCrossings(map, raw, l, town);

            var byPair = new SortedDictionary<int, List<int>>();
            for (int i = 0; i < l.Pair.Length; i++)
            {
                if (l.Distance[i] > 0f || l.Pair[i] < 0) continue;
                if (!byPair.TryGetValue(l.Pair[i], out var list)) byPair[l.Pair[i]] = list = new List<int>();
                list.Add(i);
            }
            foreach (var kv in byPair)
            {
                int key = kv.Key;
                var line = LongestStretch(kv.Value, w);
                // Slots clear of the corners; a short border (seed 7: the grassland's rim to the swamp, one road
                // between two corners) may also use ones nearer to them for its second way across.
                var slots = new List<int>();
                var nearCorners = new List<int>();
                foreach (int i in line)
                {
                    int x = i % w, y = i / w;
                    if (x < EdgeMargin || y < EdgeMargin || x >= w - EdgeMargin || y >= h - EdgeMargin) continue;
                    if (NearTown(x, y, town, townHalf + 8)) continue;
                    if (NearJunction(l, w, h, x, y, key, ShortJunctionClearance)) continue;
                    nearCorners.Add(i);
                    if (!NearJunction(l, w, h, x, y, key, JunctionClearance)) slots.Add(i);
                }
                if (nearCorners.Count == 0) continue;
                SortAlong(slots, w, town, key);
                int count = rng.Range(MinPasses, MaxPasses + 1);
                int have = 0;
                foreach (var p in map.Passes) if (Key(p.A, p.B) == key) have++;
                // Spread along the border; a slot near a road crossing (or another pass) is taken by it.
                for (int j = 0; j < count && have < count && slots.Count > 0; j++)
                {
                    float q = (j + 0.5f + rng.Range(-0.2f, 0.2f)) / count;
                    int i = slots[Mathf.Clamp(Mathf.FloorToInt(q * slots.Count), 0, slots.Count - 1)];
                    if (Nearest(map, i, w) < SlotSpacing) continue;
                    Open(map, raw, l, i, key, town, rng);
                    have++;
                }
                // A short border still gets two (the grassland's rim between a road and the corners): the slot farthest from the others.
                while (have < MinPasses)
                {
                    int best = -1;
                    float bestD = MinSpacing;
                    foreach (int i in nearCorners)
                    {
                        float d = Nearest(map, i, w);
                        if (d > bestD) { bestD = d; best = i; }
                    }
                    if (best < 0) break;
                    Open(map, raw, l, best, key, town, rng);
                    have++;
                }
            }
        }

        /// <summary>The closest a way across can come to another when a border is short.</summary>
        const float MinSpacing = 15f;

        static void Open(MapData map, Region[] raw, Layout l, int i, int key, Vector2Int town, GameRandom rng)
        {
            var cell = new Vector2Int(i % map.Width, i / map.Width);
            var kind = l.Kinds[key] == Kind.Ridge ? PassKind.MountainPass : rng.Chance(BridgeChance) ? PassKind.Bridge : PassKind.Ford;
            var across = Across(raw, map.Width, map.Height, cell, First(key), Second(key), town);
            Carve(map, l, cell, across, kind);
            map.Passes.Add(new PassSite { Cell = cell, Kind = kind, A = First(key), B = Second(key), Across = across, GuardNest = -1 });
        }

        /// <summary>Cells to the nearest way across already open.</summary>
        static float Nearest(MapData map, int i, int w)
        {
            var cell = new Vector2(i % w, i / w);
            float best = float.MaxValue;
            foreach (var p in map.Passes) best = Mathf.Min(best, Vector2.Distance(p.Cell, cell));
            return best;
        }

        /// <summary>Road cells laid on a barrier, one crossing per connected stretch.</summary>
        static void RoadCrossings(MapData map, Region[] raw, Layout l, Vector2Int town)
        {
            int w = map.Width, h = map.Height;
            var seen = new bool[w * h];
            var cells = new List<int>();
            var stack = new Stack<int>();
            for (int start = 0; start < seen.Length; start++)
            {
                if (seen[start] || !l.Barrier[start] || map.Tiles[start] != TileType.Road) continue;
                cells.Clear();
                seen[start] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    cells.Add(i);
                    int x = i % w, y = i / w;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int j = ny * w + nx;
                        if (seen[j] || !l.Barrier[j] || map.Tiles[j] != TileType.Road) continue;
                        seen[j] = true;
                        stack.Push(j);
                    }
                }
                int key = l.Pair[start];
                bool ridge = l.Kinds[key] == Kind.Ridge;
                var sum = Vector2.zero;
                foreach (int i in cells)
                {
                    int x = i % w, y = i / w;
                    sum += new Vector2(x, y);
                    // A cut through the rocks either side of the road (one cell of road alone reads as a crack);
                    // over a river the road is the bridge and the water stays.
                    for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int j = ny * w + nx;
                        if (map.Tiles[j] == TileType.Mountain) Set(map, j, TileType.Grass);
                    }
                }
                var mid = sum / cells.Count;
                int best = cells[0];
                float bestD = float.MaxValue;
                foreach (int i in cells)
                {
                    float d = (new Vector2(i % w, i / w) - mid).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
                var cell = new Vector2Int(best % w, best / w);
                map.Passes.Add(new PassSite
                {
                    Cell = cell, Kind = ridge ? PassKind.MountainPass : PassKind.Bridge, A = First(key), B = Second(key),
                    Across = Across(raw, w, h, cell, First(key), Second(key), town), OnRoad = true, GuardNest = -1,
                });
            }
        }

        /// <summary>The biggest 8-connected stretch of a border's cells (a region's stray island is left to the stranded-land cut).</summary>
        static List<int> LongestStretch(List<int> cells, int w)
        {
            var set = new HashSet<int>(cells);
            var seen = new HashSet<int>();
            var best = new List<int>();
            var stack = new Stack<int>();
            foreach (int start in cells)
            {
                if (seen.Contains(start)) continue;
                var part = new List<int>();
                seen.Add(start);
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    part.Add(i);
                    int x = i % w, y = i / w;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int j = (y + dy) * w + x + dx;
                        if (x + dx < 0 || x + dx >= w || !set.Contains(j) || seen.Contains(j)) continue;
                        seen.Add(j);
                        stack.Push(j);
                    }
                }
                if (part.Count > best.Count) best = part;
            }
            return best;
        }

        static bool NearJunction(Layout l, int w, int h, int x, int y, int key, float clearance)
        {
            int r = Mathf.CeilToInt(clearance);
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || dx * dx + dy * dy > r * r) continue;
                int j = ny * w + nx;
                if (l.Distance[j] == 0f && l.Pair[j] != key) return true;
            }
            return false;
        }

        /// <summary>Orders a border's cells along it: round the town for the grassland's rim, outward for the rest.</summary>
        static void SortAlong(List<int> cells, int w, Vector2Int town, int key)
        {
            if (First(key) == Region.Grassland || Second(key) == Region.Grassland)
            {
                var mean = Vector2.zero;
                foreach (int i in cells) mean += (new Vector2(i % w, i / w) - (Vector2)town).normalized;
                float baseAngle = Mathf.Atan2(mean.y, mean.x) * Mathf.Rad2Deg;
                cells.Sort((a, b) => Angle(a).CompareTo(Angle(b)));
                float Angle(int i) => Mathf.DeltaAngle(baseAngle, Mathf.Atan2(i / w - town.y, i % w - town.x) * Mathf.Rad2Deg);
            }
            else cells.Sort((a, b) => Far(a).CompareTo(Far(b)));
            float Far(int i) => (new Vector2(i % w, i / w) - (Vector2)town).sqrMagnitude;
        }

        /// <summary>The way across the border at <paramref name="cell"/>: from where <paramref name="a"/> lies round it to where <paramref name="b"/> does.</summary>
        static Vector2 Across(Region[] raw, int w, int h, Vector2Int cell, Region a, Region b, Vector2Int town)
        {
            Vector2 sa = Vector2.zero, sb = Vector2.zero;
            int na = 0, nb = 0;
            for (int dy = -8; dy <= 8; dy++)
            for (int dx = -8; dx <= 8; dx++)
            {
                int x = cell.x + dx, y = cell.y + dy;
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                var r = raw[y * w + x];
                if (r == a) { sa += new Vector2(dx, dy); na++; }
                else if (r == b) { sb += new Vector2(dx, dy); nb++; }
            }
            var d = na > 0 && nb > 0 ? sb / nb - sa / na : Vector2.zero;
            if (d.sqrMagnitude < 0.01f) d = cell - town;
            return d.normalized;
        }

        /// <summary>Cuts a way across: a gap in the rocks, a road deck over the river, or sand shallows through it.</summary>
        static void Carve(MapData map, Layout l, Vector2Int at, Vector2 across, PassKind kind)
        {
            float half = kind switch { PassKind.MountainPass => PassHalf, PassKind.Bridge => BridgeHalf, _ => FordHalf };
            float barrier = kind == PassKind.MountainPass ? RidgeHalfMax : RiverHalfMax + Bank;
            float reach = barrier + Approach;
            var along = new Vector2(-across.y, across.x);
            int r = Mathf.CeilToInt(reach + half + 2f);
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = at.x + dx, y = at.y + dy;
                if (!map.InBounds(x, y)) continue;
                var o = new Vector2(dx, dy);
                float side = Mathf.Abs(Vector2.Dot(o, along)), depth = Mathf.Abs(Vector2.Dot(o, across));
                if (depth > reach || side > half + 0.5f) continue;
                int i = y * map.Width + x;
                var t = map.Tiles[i];
                if (t is TileType.Wall or TileType.Gate or TileType.TownFloor or TileType.Road) continue;
                switch (kind)
                {
                    case PassKind.Bridge when depth <= barrier + 1f:
                        // The deck runs bank to bank (a road; the painter lays planks where it has water both sides).
                        if (t is TileType.Water or TileType.Sand || l.Barrier[i]) Set(map, i, TileType.Road);
                        break;
                    case PassKind.Ford when t == TileType.Water:
                        Set(map, i, TileType.Sand);
                        break;
                    default:
                        if (t is TileType.Water or TileType.Mountain) Set(map, i, TileType.Grass);
                        break;
                }
            }
        }
    }
}
