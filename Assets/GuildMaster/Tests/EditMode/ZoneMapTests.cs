using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>
    /// The map's zones (P4-102, user request 2026-10-24 "the map looks wrong"): zones in one piece spread over by the
    /// regions, region borders shut but for 2–3 ways across (P4-101's decision kept), all the land reachable, roads as
    /// one network, named zones with their own danger, village sites, dens in packs keeping to a zone's kind.
    /// </summary>
    public class ZoneMapTests
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

        /// <summary>The region as the zones laid it (before it was snapped to lakes and woods, or a river took its colours).</summary>
        static Region Raw(MapData map, int i) => map.Zones[map.ZoneIds[i]].Region;

        [Test]
        public void EveryZone_IsOnePiece_TheFieldsHoldTheTown_AndEveryRegionHasLand()
        {
            foreach (int seed in Seeds)
            {
                var map = Map(seed);
                int w = map.Width, h = map.SurfaceHeight;
                Assert.That(map.Zones.Count, Is.GreaterThanOrEqualTo(15), $"seed {seed}");
                var town = map.TownCenter;
                Assert.That(map.ZoneAt(town.x, town.y).Index, Is.EqualTo(ZoneMap.Home));
                Assert.That(map.Zones[ZoneMap.Home].Region, Is.EqualTo(Region.Grassland));
                for (int i = 0; i < w * h; i++) Assert.That(map.ZoneIds[i], Is.InRange(0, map.Zones.Count - 1));
                // One piece each: a flood from any cell of a zone reaches all its cells.
                var seen = new bool[w * h];
                var stack = new Stack<int>();
                foreach (var z in map.Zones)
                {
                    int start = System.Array.IndexOf(map.ZoneIds, (short)z.Index, 0, w * h);
                    int count = 0;
                    seen[start] = true;
                    stack.Push(start);
                    while (stack.Count > 0)
                    {
                        int i = stack.Pop();
                        count++;
                        int x = i % w, y = i / w;
                        foreach (var (ax, ay) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                        {
                            if (ax < 0 || ay < 0 || ax >= w || ay >= h) continue;
                            int j = ay * w + ax;
                            if (seen[j] || map.ZoneIds[j] != z.Index) continue;
                            seen[j] = true;
                            stack.Push(j);
                        }
                    }
                    Assert.That(count, Is.EqualTo(z.Cells), $"seed {seed}: zone {z.Index} in pieces");
                }
                int outer = map.Zones.Where(z => z.Index != ZoneMap.Home).Sum(z => z.Cells);
                foreach (var r in new[] { Region.Desert, Region.Snow, Region.Swamp, Region.Wasteland })
                    Assert.That(map.Zones.Where(z => z.Region == r).Sum(z => z.Cells), Is.GreaterThan(outer * 0.08f), $"seed {seed}: {r}");
            }
        }

        /// <summary>Pairs of regions sharing a border of at least <paramref name="minCells"/> cells.</summary>
        static HashSet<int> Borders(MapData map, int minCells)
        {
            var length = new Dictionary<int, int>();
            for (int y = 0; y < map.SurfaceHeight; y++)
            for (int x = 0; x + 1 < map.Width; x++)
            {
                var a = Raw(map, map.Index(x, y));
                var b = Raw(map, map.Index(x + 1, y));
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
                    Assert.That(ways, Is.InRange(2, 4), $"seed {seed}: {RegionBarriers.First(key)}–{RegionBarriers.Second(key)}");
                }
                Assert.That(map.Passes.Any(p => p.OnRoad), Is.True, $"seed {seed}: roads go through the passes");
            }
        }

        [Test]
        public void EveryBorder_IsDeepWoodsOrARiver_CutOnlyAtItsCrossings_AndNoRoadRunsThroughTheWoods()
        {
            // P4-103 (user request 2026-10-24: "a very dangerous forest instead of rock"): the woods can be walked, so the
            // borders are not shut any more — but away from its crossings every border lies under deep woods or a river.
            foreach (int seed in Seeds)
            {
                var map = Map(seed);
                int w = map.Width, h = map.SurfaceHeight;
                Assert.That(Enumerable.Range(0, w * h).Count(i => map.Deep[i]), Is.GreaterThan(2000), $"seed {seed}: deep woods");
                Assert.That(Enumerable.Range(0, w * h).All(i => !map.Deep[i] || map.Tiles[i] == TileType.Forest), Is.True, "deep woods are forest");
                Assert.That(Enumerable.Range(0, w * h).Any(i => map.Deep[i] && map.Tiles[i] == TileType.Road), Is.False, $"seed {seed}: a road through the deep woods");
                foreach (var p in map.Passes.Where(p => p.Kind == PassKind.Trail && p.A != p.B))
                    Assert.That(map.IsDeep(p.Cell.x, p.Cell.y), Is.False, $"seed {seed}: the trail at {p.Cell} is cleared");
                var covered = new bool[w * h];
                for (int i = 0; i < w * h; i++) covered[i] = map.Deep[i] || map.Tiles[i] == TileType.Water;
                var open = new List<string>();
                for (int y = 8; y < h - 8; y++)
                for (int x = 8; x + 1 < w - 8; x++)
                {
                    if (Raw(map, map.Index(x, y)) == Raw(map, map.Index(x + 1, y))) continue;
                    if (RegionBarriers.NearTown(x, y, map.TownCenter, map.BaseTownHalfSize + 4)) continue;
                    var cell = new Vector2Int(x, y);
                    if (map.Passes.Any(p => (p.Cell - cell).sqrMagnitude <= 34 * 34)) continue;
                    bool any = false;
                    for (int dy = -20; dy <= 20 && !any; dy++)
                    for (int dx = -20; dx <= 20; dx++)
                        if (map.InBounds(x + dx, y + dy) && y + dy < h && covered[map.Index(x + dx, y + dy)]) { any = true; break; }
                    if (!any && open.Count < 3) open.Add(cell.ToString());
                }
                Assert.That(open, Is.Empty, $"seed {seed}: a border with neither woods nor water near it, away from any crossing");
            }
        }

        [Test]
        public void DeepWoods_AreSlowerAndDarker_RiskierToCross_AndTheirMiasmaSparesMonsters()
        {
            var map = new MapData(3, 1);
            map[0, 0] = TileType.Grass; map[1, 0] = TileType.Forest; map[2, 0] = TileType.Forest;
            map.Deep[2] = true;
            var grid = new GridMap(map);
            Assert.That(grid.Cost(2, 0), Is.EqualTo(grid.Cost(1, 0) * DeepWoodRules.MoveFactor).Within(1e-4f), "slower than a plain forest");
            Assert.That(RouteRisk.Of(map, 2, 0), Is.GreaterThan(RouteRisk.Of(map, 1, 0)), "riskier to cross");
            var world = new WorldService(map);
            Assert.That(ForestSight.Radius(world, new Vector2(2.5f, 0.5f), 9f), Is.EqualTo(9f * ForestSight.InForestFactor * DeepWoodRules.SightFactor).Within(1e-4f), "darker");
            Assert.That(Hazards.Hurts(HazardKind.Miasma, monster: false), Is.True, "miasma hurts people");
            Assert.That(Hazards.Hurts(HazardKind.Miasma, monster: true), Is.False, "the woods' monsters live with it");
            Assert.That(Hazards.Hurts(HazardKind.Poison, monster: true), Is.True, "a disaster's poison hurts monsters as before");
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
        public void Roads_AreOneNetwork_AndReachMostZones()
        {
            foreach (int seed in Seeds)
            {
                var map = Map(seed);
                int w = map.Width, h = map.SurfaceHeight;
                static bool Paved(TileType t) => t is TileType.Road or TileType.Gate or TileType.TownFloor;
                var seen = new bool[w * h];
                var stack = new Stack<int>();
                int start = map.Index(map.TownCenter.x, map.TownCenter.y), reached = 0;
                seen[start] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    if (map.Tiles[i] == TileType.Road) reached++;
                    int x = i % w, y = i / w;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int ax = x + dx, ay = y + dy;
                        if (ax < 0 || ay < 0 || ax >= w || ay >= h) continue;
                        int j = ay * w + ax;
                        if (seen[j] || !Paved(map.Tiles[j])) continue;
                        seen[j] = true;
                        stack.Push(j);
                    }
                }
                // A bridge no road runs to is a road cell on its own out in the wilds; every other road leads home.
                var lone = map.Passes.Where(p => p.Kind == PassKind.Bridge && !p.OnRoad).Select(p => p.Cell).ToList();
                var cut = Enumerable.Range(0, w * h)
                    .Where(i => map.Tiles[i] == TileType.Road && !seen[i] && !lone.Any(c => (c - new Vector2Int(i % w, i / w)).sqrMagnitude <= 20 * 20))
                    .Select(i => new Vector2Int(i % w, i / w)).ToList();
                Assert.That(cut, Is.Empty, $"seed {seed}: road cells cut off from the town's ({reached} joined)");
                var outer = map.Zones.Where(z => z.Index != ZoneMap.Home).ToList();
                Assert.That(outer.Count(z => z.OnRoad), Is.GreaterThan(outer.Count / 2), $"seed {seed}: zones with a road");
            }
        }

        [Test]
        public void Zones_HaveNames_TheirOwnDanger_Landmarks_AndVillageSites()
        {
            foreach (int seed in Seeds)
            {
                var map = Map(seed);
                var names = map.Zones.Select(z => z.Name).ToList();
                Assert.That(names.All(n => !string.IsNullOrEmpty(n)), Is.True);
                Assert.That(names.Distinct().Count(), Is.EqualTo(names.Count), $"seed {seed}: names repeat");
                var pockets = map.Zones.Where(z => z.Kind == ZoneKind.Pocket).ToList();
                Assert.That(pockets.Count(z => z.Neighbours.Contains(ZoneMap.Home)), Is.EqualTo(1), $"seed {seed}: a pocket of danger on the fields' rim");
                Assert.That(pockets.All(z => z.DangerShift > 0.2f), Is.True);
                var havens = map.Zones.Where(z => z.Kind == ZoneKind.Haven).ToList();
                Assert.That(havens.Count, Is.InRange(1, 2), $"seed {seed}: havens");
                Assert.That(havens.All(z => z.DangerShift < -0.2f && Vector2.Distance(z.Centre, map.TownCenter) >= 200f), Is.True);
                Assert.That(map.Zones.Where(z => z.Kind == ZoneKind.Plain && z.Index != ZoneMap.Home).All(z => Mathf.Abs(z.DangerShift) <= ZoneLore.PlainSpread), Is.True);
                foreach (var r in new[] { Region.Desert, Region.Snow, Region.Swamp, Region.Wasteland })
                    Assert.That(map.Zones.Count(z => z.Region == r && z.Landmark), Is.EqualTo(1), $"seed {seed}: {r}'s landmark");

                Assert.That(map.SettlementSites.Count, Is.GreaterThanOrEqualTo(8), $"seed {seed}: village sites");
                foreach (var s in map.SettlementSites)
                {
                    for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        Assert.That(map[s.x + dx, s.y + dy] is TileType.Grass or TileType.Sand or TileType.Road, Is.True, $"seed {seed}: site {s}");
                    Assert.That(map.Nests.All(n => (n.Cell - s).sqrMagnitude >= 15 * 15), Is.True, $"seed {seed}: a den by the site {s}");
                }
            }
        }

        [Test]
        public void DensGatherInPacks_AndTheSharesStay()
        {
            var balance = ScriptableObject.CreateInstance<Session.GameBalance>();
            foreach (int seed in Seeds)
            {
                var map = Map(seed);
                Assert.That(map.Nests.Count, Is.EqualTo(balance.nestCount), $"seed {seed}");
                var t = map.TownCenter;
                int inner = map.Nests.Count(n => Mathf.Abs(n.Cell.x - t.x) <= balance.innerHalfSize && Mathf.Abs(n.Cell.y - t.y) <= balance.innerHalfSize);
                Assert.That(inner, Is.EqualTo(balance.innerNestCount), $"seed {seed}: the inner share (P4-81)");
                foreach (var n in map.Nests) Assert.That(n.Zone, Is.EqualTo((int)map.ZoneIds[map.Index(n.Cell.x, n.Cell.y)]));
                Assert.That(map.Passes.Count(p => p.GuardNest >= 0), Is.GreaterThanOrEqualTo(2), $"seed {seed}: dens holding passes");
                // Packs: the dens of a zone with several lie close to one another.
                var near = new List<float>();
                foreach (var g in map.Nests.GroupBy(n => n.Zone).Where(g => g.Key != ZoneMap.Home && g.Count() >= 3))
                    foreach (var n in g)
                        near.Add(g.Where(o => o.Cell != n.Cell).Min(o => Vector2Int.Distance(o.Cell, n.Cell)));
                Assert.That(near.Average(), Is.LessThan(30f), $"seed {seed}: dens of a zone should gather");
            }
            Object.DestroyImmediate(balance);
        }

        [Test]
        public void AZonesDens_KeepMostlyToOneKind()
        {
            static MonsterDefinition Kind(string id)
            {
                var m = ScriptableObject.CreateInstance<MonsterDefinition>();
                m.name = m.id = m.displayName = id;
                m.danger = DangerLevel.Low;
                m.regions.Add(Region.Desert);
                return m;
            }
            var kinds = new List<MonsterDefinition> { Kind("a"), Kind("b"), Kind("c") };
            float Share(int zoneOf)
            {
                float total = 0f;
                for (int seed = 1; seed <= 20; seed++)
                {
                    var sites = new List<NestSite>();
                    for (int i = 0; i < 24; i++)
                        sites.Add(new NestSite { Cell = new Vector2Int(i, 0), Region = Region.Desert, DistanceFromTown = 50 + i, Zone = zoneOf < 0 ? 1 + i / 8 : zoneOf });
                    var result = EcosystemManager.AssignSpecies(kinds, sites, new GameRandom(seed));
                    for (int z = 0; z < 3; z++)
                    {
                        var mine = Enumerable.Range(z * 8, 8).Select(i => result[i]).ToList();
                        total += mine.GroupBy(k => k).Max(g => g.Count()) / 8f;
                    }
                }
                return total / 60f;
            }
            float zoned = Share(-1), mixed = Share(0);
            Assert.That(zoned, Is.GreaterThan(0.55f), $"three zones of eight dens: the commonest kind's share {zoned:0.00}");
            Assert.That(zoned, Is.GreaterThan(mixed + 0.05f), $"zoned {zoned:0.00}, all in the fields {mixed:0.00}");
            foreach (var k in kinds) Object.DestroyImmediate(k);
        }

        [Test]
        public void SameSeed_SameLand()
        {
            var a = Map(2);
            s_maps.Remove((2, true));
            var b = Map(2);
            CollectionAssert.AreEqual(a.Tiles, b.Tiles);
            CollectionAssert.AreEqual(a.ZoneIds, b.ZoneIds);
            CollectionAssert.AreEqual(a.Passes.Select(p => p.Cell), b.Passes.Select(p => p.Cell));
            CollectionAssert.AreEqual(a.Nests.Select(n => n.Cell), b.Nests.Select(n => n.Cell));
            CollectionAssert.AreEqual(a.Zones.Select(z => z.Name), b.Zones.Select(z => z.Name));
        }
    }
}
