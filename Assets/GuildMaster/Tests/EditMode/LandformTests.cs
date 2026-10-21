using System.Collections.Generic;
using System.Linq;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>
    /// The map's ridges, rivers and landforms (P4-101, user request 2026-10-21: "you just walk straight"): every
    /// region border is shut but for 2–3 ways across, all the land stays reachable, the walk to the dens bends, and
    /// dens hold some of the passes.
    /// </summary>
    public class LandformTests
    {
        static readonly int[] Seeds = { 1, 2, 3 };
        static readonly Dictionary<(int, bool), MapData> s_maps = new();

        static MapData Map(int seed, bool landforms = true)
        {
            if (s_maps.TryGetValue((seed, landforms), out var map)) return map;
            var balance = ScriptableObject.CreateInstance<Session.GameBalance>();
            map = MapGenerator.Generate(new MapGenSettings
            {
                Width = balance.mapWidth, Height = balance.mapHeight, Seed = seed, TownHalfSize = balance.townHalfSize,
                NestCount = balance.nestCount, MinNestDistanceFromTown = balance.minNestDistanceFromTown,
                InnerHalfSize = balance.innerHalfSize, InnerNestCount = balance.innerNestCount, Landforms = landforms,
            });
            Object.DestroyImmediate(balance);
            return s_maps[(seed, landforms)] = map;
        }

        static Region Raw(MapData map, int x, int y) => TerrainRegions.At(map.RegionLayout, x + 0.5f, y + 0.5f);

        /// <summary>Pairs of regions sharing a border of at least <paramref name="minCells"/> cells (stray islands left out).</summary>
        static HashSet<int> Borders(MapData map, int minCells)
        {
            var length = new Dictionary<int, int>();
            for (int y = 0; y < map.SurfaceHeight; y++)
            for (int x = 0; x + 1 < map.Width; x++)
            {
                var a = Raw(map, x, y);
                var b = Raw(map, x + 1, y);
                if (a == b) continue;
                int key = RegionBarriers.Key(a, b);
                length[key] = length.TryGetValue(key, out int n) ? n + 1 : 1;
            }
            return new HashSet<int>(length.Where(kv => kv.Value >= minCells).Select(kv => kv.Key));
        }

        [Test]
        public void EveryBorder_HasTwoOrThreeWaysAcross()
        {
            foreach (int seed in Seeds)
            {
                var map = Map(seed);
                foreach (int key in Borders(map, 40))
                {
                    int ways = map.Passes.Count(p => p.A != p.B && RegionBarriers.Key(p.A, p.B) == key);
                    // 2–3 (user decision); a gate's road crossing as well can make one more.
                    Assert.That(ways, Is.InRange(2, 4), $"seed {seed}: {RegionBarriers.First(key)}–{RegionBarriers.Second(key)}");
                }
                Assert.That(map.Passes.Any(p => p.OnRoad), Is.True, $"seed {seed}: the gates' roads cross the barriers");
            }
        }

        [Test]
        public void Borders_AreShutBetweenTheWaysAcross()
        {
            foreach (int seed in Seeds)
            {
                var map = Map(seed);
                var town = map.TownCenter;
                int leaks = 0;
                var sample = new List<string>();
                for (int y = 0; y + 1 < map.SurfaceHeight; y++)
                for (int x = 0; x + 1 < map.Width; x++)
                for (int k = 0; k < 2; k++)
                {
                    int nx = x + (k == 0 ? 1 : 0), ny = y + (k == 1 ? 1 : 0);
                    if (Raw(map, x, y) == Raw(map, nx, ny)) continue;
                    if (!MapData.IsWalkable(map[x, y]) || !MapData.IsWalkable(map[nx, ny])) continue;
                    if (RegionBarriers.NearTown(x, y, town, map.BaseTownHalfSize)) continue;
                    var cell = new Vector2Int(x, y);
                    if (map.Passes.Any(p => (p.Cell - cell).sqrMagnitude <= 14 * 14)) continue;
                    leaks++;
                    if (sample.Count < 5) sample.Add(cell.ToString());
                }
                Assert.That(leaks, Is.Zero, $"seed {seed}: open ground across a border away from any way across, e.g. {string.Join(" ", sample)}");
            }
        }

        [Test]
        public void AllTheLand_StaysReachable_AndEveryRegionKeepsItsGround()
        {
            foreach (int seed in Seeds)
            {
                var map = Map(seed);
                var dist = MapGenerator.FloodDistances(map, map.TownCenter);
                var cells = new Dictionary<Region, int>();
                var open = new Dictionary<Region, int>();
                for (int i = 0; i < map.Width * map.SurfaceHeight; i++)
                {
                    var r = map.Regions[i];
                    cells[r] = cells.TryGetValue(r, out int c) ? c + 1 : 1;
                    if (!MapData.IsWalkable(map.Tiles[i])) continue;
                    Assert.That(dist[i], Is.GreaterThanOrEqualTo(0), $"seed {seed}: walkable cell ({i % map.Width}, {i / map.Width}) out of reach");
                    open[r] = open.TryGetValue(r, out int o) ? o + 1 : 1;
                }
                foreach (var kv in cells.Where(kv => kv.Value > 2000))
                    Assert.That((float)open[kv.Key] / kv.Value, Is.GreaterThan(0.6f), $"seed {seed}: {kv.Key} mostly walled up");
            }
        }

        [Test]
        public void TheWalkToTheDens_BendsMoreThanOnTheOpenMap()
        {
            // Steps over |dx| + |dy|: 1 is a walk with nothing in the way. Measured 2026-10-21: 1.01 before, 1.03–1.08 after.
            static float Bend(MapData map)
            {
                var dist = MapGenerator.FloodDistances(map, map.TownCenter);
                var t = map.TownCenter;
                return map.Nests.Average(n => (float)dist[map.Index(n.Cell.x, n.Cell.y)] / (Mathf.Abs(n.Cell.x - t.x) + Mathf.Abs(n.Cell.y - t.y)));
            }
            float open = Seeds.Average(s => Bend(Map(s, false)));
            float shaped = Seeds.Average(s => Bend(Map(s)));
            Assert.That(shaped, Is.GreaterThan(open + 0.015f), $"open {open:0.000}, with landforms {shaped:0.000}");
        }

        [Test]
        public void DensHoldSomePasses_AndTheShareOfDensStays()
        {
            var balance = ScriptableObject.CreateInstance<Session.GameBalance>();
            foreach (int seed in Seeds)
            {
                var map = Map(seed);
                Assert.That(map.Nests.Count, Is.EqualTo(balance.nestCount), $"seed {seed}");
                var t = map.TownCenter;
                int inner = map.Nests.Count(n => Mathf.Abs(n.Cell.x - t.x) <= balance.innerHalfSize && Mathf.Abs(n.Cell.y - t.y) <= balance.innerHalfSize);
                Assert.That(inner, Is.EqualTo(balance.innerNestCount), $"seed {seed}: the inner share (P4-81)");
                var held = map.Passes.Where(p => p.GuardNest >= 0).ToList();
                Assert.That(held.Count, Is.GreaterThanOrEqualTo(4), $"seed {seed}");
                foreach (var p in held)
                {
                    Assert.That(p.A, Is.Not.EqualTo(p.B), "only passes between regions are held");
                    float d = Vector2Int.Distance(map.Nests[p.GuardNest].Cell, p.Cell);
                    Assert.That(d, Is.InRange(4f, 16f), $"seed {seed}: the den by the pass at {p.Cell}");
                }
            }
            Object.DestroyImmediate(balance);
        }

        [Test]
        public void SameSeed_SameLand()
        {
            var a = Map(2);
            s_maps.Remove((2, true));
            var b = Map(2);
            CollectionAssert.AreEqual(a.Tiles, b.Tiles);
            CollectionAssert.AreEqual(a.Passes.Select(p => p.Cell), b.Passes.Select(p => p.Cell));
            CollectionAssert.AreEqual(a.Nests.Select(n => n.Cell), b.Nests.Select(n => n.Cell));
        }
    }
}
