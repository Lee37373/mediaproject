using System.Collections.Generic;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>
    /// Wood belts and rivers on the region borders (P4-101, user request 2026-10-21: "on this map you just walk straight";
    /// P4-103, user request 2026-10-24: "not rock between the lands, a very dangerous forest"). Every border — the
    /// grassland's rim and the lines between the outer regions — is a belt of deep woods (walkable, but slow and dark, with
    /// miasma, traps and dens: <see cref="DeepWoods"/>) or a river, crossed at 2–3 trails, bridges or fords (user decision).
    /// Borders touching the wasteland are woods: its "water" is lava, and a river there would run half lava, half water.
    /// </summary>
    public static class RegionBarriers
    {
        public enum Kind : byte { Wood, River }

        /// <summary>No barrier this close (cells past the starting walls, either axis): the walls move out up to 12 (P4-86).</summary>
        public const int TownClearance = 18;
        /// <summary>
        /// Half widths (cells either side of the barrier's line), the low and high ends of their swell. P4-103: a wood belt is
        /// walkable, so it is broad — a long, slow, dark walk across.
        /// </summary>
        const float WoodHalfMin = 4f, WoodHalfMax = 9f, RiverHalfMin = 1.3f, RiverHalfMax = 2.6f;
        /// <summary>The woods bulge out into broad masses this far past a belt's edge where a slow noise is high, so a belt is no even band.</summary>
        const float Bulge = 8f;
        /// <summary>Round the town's fields a belt lies this share of its widest half out beyond the border: the fields stay open.</summary>
        const float FieldsKeepClear = 0.6f;
        /// <summary>The woods round the town's fields are this much of a belt's width, with no masses.</summary>
        const float FieldsRim = 0.6f;
        /// <summary>A belt's edge is cut ragged by a fine noise up to this share of its half width either way (its core stays whole).</summary>
        const float Ragged = 0.55f, Core = 0.45f;
        /// <summary>
        /// P4-102: how far the barrier's line strays from the border either way (a slow noise), so it bends and wanders
        /// instead of tracing the regions' outline.
        /// </summary>
        const float Stray = 8f;
        /// <summary>Tongues and copses of (plain) forest off a belt, out to this far past its edge.</summary>
        const float SpurReach = 14f;
        /// <summary>Sand bank either side of a river.</summary>
        const float Bank = 1.2f;
        /// <summary>The farthest from its border a barrier's cell can be.</summary>
        const float BarrierReach = Stray + WoodHalfMax * (1f + Ragged + FieldsKeepClear) + Bulge;
        /// <summary>Ways across each border (user decision: 2–3).</summary>
        const int MinPasses = 2, MaxPasses = 3;
        /// <summary>Rivers on a map at least (where the borders allow): the user asked for barriers of both kinds.</summary>
        const int MinRivers = 2;
        const float RiverChance = 0.5f, BridgeChance = 0.6f;
        /// <summary>A way across this close to another border (where three regions meet) could open into the wrong one.</summary>
        const float JunctionClearance = 14f, ShortJunctionClearance = 8f;
        /// <summary>A pass slot this close to a road crossing or another pass is taken by it.</summary>
        const float SlotSpacing = 25f;
        const int EdgeMargin = 8;
        /// <summary>Half widths of the ways across: the trail cleared through the woods, a bridge's deck, a ford's shallows.</summary>
        const float PassHalf = 2.5f, BridgeHalf = 1f, FordHalf = 2.5f;
        /// <summary>How far past the barrier a way across also clears lakes and rocks, so it opens onto land.</summary>
        const float Approach = 5f;
        /// <summary>A lake a river runs into takes the river's colours up to this size; a bigger one is kept apart by a sand bar.</summary>
        const int LakeRecolourLimit = 600;

        static readonly int[] Dx4 = { 1, -1, 0, 0 }, Dy4 = { 0, 0, 1, -1 };

        /// <summary>Per cell the distance to the nearest border between two labels and which border it is (<see cref="Key(int, int)"/>).</summary>
        public class BorderDistance
        {
            public float[] Distance;
            /// <summary>Key of the nearest border, -1 where there is none (a single label).</summary>
            public int[] Pair;
        }

        /// <summary>The borders as laid: the distance field, the barrier cells, each border's kind.</summary>
        public sealed class Layout : BorderDistance
        {
            public bool[] Barrier;
            public readonly Dictionary<int, Kind> Kinds = new();
            /// <summary>The region whose colours a river takes (all of it, so it is never half one water, half another).</summary>
            public readonly Dictionary<int, Region> RiverRegion = new();
            public float NoiseX, NoiseY;
        }

        /// <summary>Keys pack two labels (regions, or zones — P4-102) below this.</summary>
        public const int KeyBase = 256;
        /// <summary>One key per pair of labels, either order.</summary>
        public static int Key(int a, int b) => a < b ? a * KeyBase + b : b * KeyBase + a;
        public static int Key(Region a, Region b) => Key((int)a, (int)b);
        public static Region First(int key) => (Region)(key / KeyBase);
        public static Region Second(int key) => (Region)(key % KeyBase);

        /// <summary>Ground kept for the town and its walls (P4-86: they move out up to 12 cells).</summary>
        public static bool NearTown(int x, int y, Vector2Int town, int townHalf)
            => Mathf.Max(Mathf.Abs(x - town.x), Mathf.Abs(y - town.y)) <= townHalf + TownClearance;

        /// <summary>
        /// Lays the wood belts and rivers along the borders of <paramref name="raw"/> (the regions as the zones laid them,
        /// before they were snapped to lakes and woods). The ways across come later (<see cref="OpenPasses"/>).
        /// P4-102: the line strays off the border, swells and thins. P4-103: deep woods where the ridges were, forest tongues off them.
        /// </summary>
        public static Layout Lay(MapData map, Region[] raw, Vector2Int town, int townHalf, GameRandom rng)
        {
            int w = map.Width, h = map.Height;
            var l = new Layout { NoiseX = rng.Range(0f, 5000f), NoiseY = rng.Range(0f, 5000f), Barrier = new bool[w * h] };
            var labels = new int[w * h];
            for (int i = 0; i < labels.Length; i++) labels[i] = (int)raw[i];
            var field = Field(labels, w, h);
            l.Distance = field.Distance;
            l.Pair = field.Pair;
            ChooseKinds(l, rng);
            var junction = JunctionDistance(raw, w, h);
            var flank = new float[w * h];
            for (int i = 0; i < flank.Length; i++) flank[i] = float.MaxValue;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x, key = l.Pair[i];
                if (key < 0 || l.Distance[i] > BarrierReach + SpurReach || NearTown(x, y, town, townHalf)) continue;
                // Off the barrier's line: the border's distance signed by side, less the line's stray here.
                float side = (int)raw[i] == key / KeyBase ? 1f : -1f;
                // Where three regions meet the lines come back to the borders, so the barriers join with no gap.
                float fade = Mathf.Clamp01((junction[i] - JunctionCalm) / JunctionFade);
                float off = (Mathf.PerlinNoise(x * 0.012f + l.NoiseY, y * 0.012f + l.NoiseX) - 0.5f) * 2f * Stray * fade;
                bool wood = l.Kinds[key] == Kind.Wood || NearWasteland(raw, w, h, x, y);
                // The town's fields keep their open ground: a belt round them lies out beyond the border (grassland is a pair's first).
                if (wood && key / KeyBase == (int)Region.Grassland) off -= WoodHalfMax * FieldsKeepClear * fade;
                float e = Mathf.Abs(side * l.Distance[i] - off);
                if (wood)
                {
                    // Where a river meets the wasteland (three regions at a point) it runs into the woods instead.
                    float half = l.Kinds[key] == Kind.Wood ? Half(Kind.Wood, x, y, l) : Half(Kind.River, x, y, l) + 2f;
                    // Round the town's fields a thinner rim, no masses: the first hunts and the first scouting go out through it
                    // (a measured run with the full belt there knew 12 dens of 64 after ten days).
                    bool fields = key / KeyBase == (int)Region.Grassland;
                    if (fields) half *= FieldsRim;
                    // A ragged edge: glades and outliers by a fine noise, the core whole so the belt never breaks.
                    float jag = (Mathf.PerlinNoise(x * 0.11f + l.NoiseX + 13f, y * 0.11f + l.NoiseY + 7f) - 0.5f) * 2f;
                    half = Mathf.Max(half * Core, half * (1f + Ragged * jag));
                    // Broad masses where a slow noise is high: the belt bulges into the land on either side.
                    bool bulge = l.Kinds[key] == Kind.Wood && !fields && e <= half + Bulge
                                 && Mathf.PerlinNoise(x * 0.035f + l.NoiseY + 51f, y * 0.035f + l.NoiseX + 17f) > 0.58f + 0.012f * (e - half);
                    if (e <= half || bulge)
                    {
                        // Deep woods; a lake inside stays a forest lake.
                        if (map.Tiles[i] != TileType.Water) Set(map, i, TileType.Forest);
                        map.Deep[i] = map.Tiles[i] == TileType.Forest;
                    }
                    else if (l.Kinds[key] == Kind.Wood) flank[i] = e - half;
                    continue;
                }
                if (l.Distance[i] > BarrierReach + Bank + 1f) continue;
                float riverHalf = Half(Kind.River, x, y, l);
                if (e > riverHalf + Bank) continue;
                map.Regions[i] = l.RiverRegion[key];
                if (e <= riverHalf)
                {
                    Set(map, i, TileType.Water);
                    l.Barrier[i] = true;
                }
                else if (map.Tiles[i] != TileType.Water) Set(map, i, TileType.Sand);
            }
            Spurs(map, l, flank);
            MatchLakes(map, l);
            return l;
        }

        /// <summary>A barrier's line keeps to its border this close to a meeting of three regions, and strays fully this much farther.</summary>
        const float JunctionCalm = 4f, JunctionFade = 20f;

        /// <summary>Steps (4 ways) from each cell to the nearest point where three regions meet, up to the fade's reach.</summary>
        static float[] JunctionDistance(Region[] raw, int w, int h)
        {
            var d = new float[w * h];
            System.Array.Fill(d, float.MaxValue);
            var queue = new Queue<int>();
            var seen = new HashSet<Region>();
            for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                seen.Clear();
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    seen.Add(raw[(y + dy) * w + x + dx]);
                if (seen.Count < 3) continue;
                d[y * w + x] = 0f;
                queue.Enqueue(y * w + x);
            }
            float reach = JunctionCalm + JunctionFade;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                if (d[i] >= reach) continue;
                int x = i % w, y = i / w;
                for (int k = 0; k < 4; k++)
                {
                    int ax = x + Dx4[k], ay = y + Dy4[k];
                    if (ax < 0 || ay < 0 || ax >= w || ay >= h) continue;
                    int j = ay * w + ax;
                    if (d[j] <= d[i] + 1f) continue;
                    d[j] = d[i] + 1f;
                    queue.Enqueue(j);
                }
            }
            return d;
        }

        /// <summary>
        /// Tongues (where a ridged noise peaks, fewer the farther out) and copses of plain forest off a belt's edges (P4-103: they
        /// were rock spurs off the ridges), so the deep woods fade into the land instead of ending at a line.
        /// </summary>
        static void Spurs(MapData map, Layout l, float[] flank)
        {
            int w = map.Width;
            for (int i = 0; i < flank.Length; i++)
            {
                float f = flank[i];
                if (f > SpurReach || map.Tiles[i] is TileType.Mountain or TileType.Water or TileType.Forest) continue;
                int x = i % w, y = i / w;
                float ridged = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(x * 0.07f + l.NoiseX + 31f, y * 0.07f + l.NoiseY) - 1f);
                float knoll = Mathf.PerlinNoise(x * 0.16f + l.NoiseY + 77f, y * 0.16f + l.NoiseX);
                if (ridged > 0.86f + 0.008f * f || knoll > 0.7f + 0.014f * f) Set(map, i, TileType.Forest);
            }
        }

        static void Set(MapData map, int i, TileType t)
        {
            map.Tiles[i] = t;
            map.Trees[i] = false;
        }

        /// <summary>The barrier's half width here: it swells and narrows along its length.</summary>
        static float Half(Kind kind, int x, int y, Layout l)
        {
            // Slow along a belt (long broad woods and long narrow ones), quicker along a river.
            float f = kind == Kind.Wood ? 0.022f : 0.05f;
            float n = Mathf.PerlinNoise(x * f + l.NoiseX, y * f + l.NoiseY);
            return kind == Kind.Wood ? Mathf.Lerp(WoodHalfMin, WoodHalfMax, n) : Mathf.Lerp(RiverHalfMin, RiverHalfMax, n);
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
        /// Distance to the nearest border cell (a cell beside another label) and which border, by passing each cell's
        /// nearest border cell on in four sweeps (close to the true distance; exactness does not matter for a ridge).
        /// Labels are regions or zones (P4-102), below <see cref="KeyBase"/>; negative ones are no land.
        /// </summary>
        public static BorderDistance Field(int[] labels, int w, int h)
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
                if (labels[i] < 0) continue;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx4[k], ny = y + Dy4[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h || labels[ny * w + nx] < 0 || labels[ny * w + nx] == labels[i]) continue;
                    dist[i] = 0f;
                    src[i] = i;
                    pair[i] = Key(labels[i], labels[ny * w + nx]);
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
            return new BorderDistance { Distance = dist, Pair = pair };
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
                var kind = !waste && rng.Chance(RiverChance) ? Kind.River : Kind.Wood;
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
            var kind = l.Kinds[key] == Kind.Wood ? PassKind.Trail : rng.Chance(BridgeChance) ? PassKind.Bridge : PassKind.Ford;
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
                bool ridge = l.Kinds[key] == Kind.Wood;
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
                    Cell = cell, Kind = ridge ? PassKind.Trail : PassKind.Bridge, A = First(key), B = Second(key),
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

        /// <summary>Cuts a way across: a trail cleared through the woods (safe from their dangers), a road deck over the river, or sand shallows through it.</summary>
        static void Carve(MapData map, Layout l, Vector2Int at, Vector2 across, PassKind kind)
        {
            float half = kind switch { PassKind.Trail => PassHalf, PassKind.Bridge => BridgeHalf, _ => FordHalf };
            // The barrier may lie anywhere its line strays to (P4-102), so the cut reaches that far either way.
            float barrier = kind == PassKind.Trail ? BarrierReach : Stray + RiverHalfMax + Bank;
            float reach = barrier + Approach;
            var along = new Vector2(-across.y, across.x);
            int r = Mathf.CeilToInt(reach + half + 2f);
            // A bridge's deck runs in one piece from the river's first water to its last along the way across (the river
            // may lie anywhere its line strays to), a cell onto each bank.
            float deckFrom = float.MaxValue, deckTo = float.MinValue;
            if (kind == PassKind.Bridge)
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = at.x + dx, y = at.y + dy;
                    if (!map.InBounds(x, y) || !l.Barrier[y * map.Width + x]) continue;
                    var o = new Vector2(dx, dy);
                    if (Mathf.Abs(Vector2.Dot(o, along)) > half + 0.5f) continue;
                    float t = Vector2.Dot(o, across);
                    if (Mathf.Abs(t) > barrier + 1f) continue;
                    deckFrom = Mathf.Min(deckFrom, t - 1.5f);
                    deckTo = Mathf.Max(deckTo, t + 1.5f);
                }
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = at.x + dx, y = at.y + dy;
                if (!map.InBounds(x, y)) continue;
                var o = new Vector2(dx, dy);
                float side = Mathf.Abs(Vector2.Dot(o, along)), signed = Vector2.Dot(o, across), depth = Mathf.Abs(signed);
                if (depth > reach || side > half + 0.5f) continue;
                int i = y * map.Width + x;
                var t = map.Tiles[i];
                if (t is TileType.Wall or TileType.Gate or TileType.TownFloor or TileType.Road) continue;
                switch (kind)
                {
                    case PassKind.Bridge when signed >= deckFrom && signed <= deckTo:
                        // The deck (a road; the painter lays planks where it has water both sides).
                        Set(map, i, TileType.Road);
                        break;
                    case PassKind.Bridge when l.Barrier[i]:
                        break;
                    case PassKind.Ford when t == TileType.Water:
                        Set(map, i, TileType.Sand);
                        break;
                    case PassKind.Trail when map.Deep[i] || t == TileType.Forest && depth <= barrier:
                        // The trail: the trees felled, no deep woods on it (P4-103: it is the safe way through).
                        Set(map, i, TileType.Grass);
                        map.Deep[i] = false;
                        break;
                    default:
                        if (t is TileType.Water or TileType.Mountain) Set(map, i, TileType.Grass);
                        break;
                }
            }
        }
    }
}
