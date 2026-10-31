using System.Collections.Generic;
using GuildMaster.Units;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Choosing a route by nerve (P4-63): risky ground is ground with a short view, and the party's
    /// caution decides how far round it the group walks.
    /// </summary>
    public class RouteTests
    {
        /// <summary>Open grass with a belt of forest across the middle, leaving a clear way round the top.</summary>
        static MapData ForestBelt()
        {
            var map = new MapData(40, 24);
            for (int i = 0; i < map.Tiles.Length; i++) map.Tiles[i] = TileType.Grass;
            for (int y = 0; y <= 16; y++)
            for (int x = 14; x <= 25; x++)
                map[x, y] = TileType.Forest;
            return map;
        }

        static int ForestCells(GridMap grid, List<Vector2Int> path)
        {
            int n = 0;
            foreach (var c in path) if (grid.IsForest(c)) n++;
            return n;
        }

        static float Length(List<Vector2Int> path)
        {
            float d = 0f;
            for (int i = 1; i < path.Count; i++) d += (path[i] - path[i - 1]).sqrMagnitude > 1 ? 1.4142f : 1f;
            return d;
        }

        [Test]
        public void On512Map_ALongWoodlandRouteIsNotCutShort()
        {
            // P4-81: a forest-loving band coming in from the edge (seed 20260920) needed 20,554 nodes for
            // 137 cells of swamp; the old flat cap of 20,000 failed it 1045 times in one run.
            var balance = ScriptableObject.CreateInstance<Session.GameBalance>();
            try
            {
                var map = MapGenerator.Generate(new MapGenSettings
                {
                    Width = balance.mapWidth, Height = balance.mapHeight, Seed = 20260920, TownHalfSize = balance.townHalfSize,
                    NestCount = balance.nestCount, MinNestDistanceFromTown = balance.minNestDistanceFromTown,
                    InnerHalfSize = balance.innerHalfSize, InnerNestCount = balance.innerNestCount,
                });
                var finder = new Pathfinder(new GridMap(map));
                var path = new List<Vector2Int>();
                Assert.That(finder.FindPath(new Vector2Int(508, 300), new Vector2Int(371, 299), path, false, null, RouteChoice.FromCover), Is.True);
                Assert.That(finder.LastExpanded, Is.GreaterThan(finder.MaxExpandedNodes), "the case the flat cap cut short");
            }
            finally
            {
                Object.DestroyImmediate(balance);
            }
        }

        [Test]
        public void RiskIsTheViewLost_ForestAndWeather()
        {
            var map = ForestBelt();
            Assert.That(RouteRisk.Of(map, 2, 2), Is.EqualTo(0f).Within(0.001f), "open grassland is safe ground");
            Assert.That(RouteRisk.Of(map, 20, 2), Is.EqualTo(1f - ForestSight.InForestFactor).Within(0.001f));
            // The same cell in the mist of a swamp hides even more.
            map.Regions[map.Index(20, 2)] = Region.Swamp;
            Assert.That(RouteRisk.Of(map, 20, 2),
                Is.EqualTo(1f - ForestSight.InForestFactor * TerrainRegions.SightMultiplier(Region.Swamp)).Within(0.001f));
            map.Regions[map.Index(2, 2)] = Region.Snow;
            Assert.That(RouteRisk.Of(map, 2, 2), Is.EqualTo(1f - TerrainRegions.SightMultiplier(Region.Snow)).Within(0.001f));
        }

        [Test]
        public void HurriedCutsThroughTheWoods_CautiousGoesRound()
        {
            var grid = new GridMap(ForestBelt());
            var pf = new Pathfinder(grid);
            var hurried = new List<Vector2Int>();
            var careful = new List<Vector2Int>();
            var from = new Vector2Int(2, 8);
            var to = new Vector2Int(37, 8);

            Assert.IsTrue(pf.FindPath(from, to, hurried, true, null, RouteChoice.Hurried));
            Assert.IsTrue(pf.FindPath(from, to, careful, true, null, RouteChoice.Careful));

            Assert.That(ForestCells(grid, careful), Is.LessThan(ForestCells(grid, hurried)), "a careful party keeps out of the trees");
            Assert.That(Length(careful), Is.GreaterThan(Length(hurried)), "and pays for it in distance");
        }

        [Test]
        public void NerveOrdersTheDetour_BraveCutsMoreCornersThanACoward()
        {
            var grid = new GridMap(ForestBelt());
            var pf = new Pathfinder(grid);
            var brave = new List<Vector2Int>();
            var coward = new List<Vector2Int>();
            var from = new Vector2Int(2, 8);
            var to = new Vector2Int(37, 8);

            Assert.IsTrue(pf.FindPath(from, to, brave, true, null, RouteChoice.FromNerve(-0.5f)));
            Assert.IsTrue(pf.FindPath(from, to, coward, true, null, RouteChoice.FromNerve(1f)));
            Assert.That(ForestCells(grid, brave), Is.GreaterThanOrEqualTo(ForestCells(grid, coward)));
            Assert.That(Length(brave), Is.LessThanOrEqualTo(Length(coward)));
        }

        [Test]
        public void WoodlandPackTakesTheTrees_WhereOthersWalkRound()
        {
            // A forest patch off the straight line: only a kind that likes cover goes out of its way for it.
            var map = new MapData(30, 20);
            for (int i = 0; i < map.Tiles.Length; i++) map.Tiles[i] = TileType.Grass;
            for (int y = 8; y <= 12; y++)
            for (int x = 10; x <= 20; x++)
                map[x, y] = TileType.Forest;
            var grid = new GridMap(map);
            var pf = new Pathfinder(grid);
            var plain = new List<Vector2Int>();
            var woodland = new List<Vector2Int>();
            var from = new Vector2Int(2, 10);
            var to = new Vector2Int(27, 10);

            Assert.IsTrue(pf.FindPath(from, to, plain, true, null, RouteChoice.Hurried));
            Assert.IsTrue(pf.FindPath(from, to, woodland, true, null, RouteChoice.FromCover));
            Assert.That(ForestCells(grid, woodland), Is.GreaterThan(ForestCells(grid, plain)), "the trees are its road");
        }

        [Test]
        public void CautionStaysWithinThePersonalityRange()
        {
            Assert.That(RouteChoice.FromNerve(0f), Is.EqualTo(RouteChoice.Normal).Within(0.001f));
            Assert.That(RouteChoice.FromNerve(-5f), Is.EqualTo(RouteChoice.Bold).Within(0.001f));
            Assert.That(RouteChoice.FromNerve(5f), Is.EqualTo(RouteChoice.Careful).Within(0.001f));
            Assert.That(RouteChoice.FromNerve(-0.5f), Is.LessThan(RouteChoice.FromNerve(1f)), "the brave weigh risk less");
        }

        [Test]
        public void ZeroCautionLeavesEveryOtherPathUntouched()
        {
            var grid = new GridMap(ForestBelt());
            var pf = new Pathfinder(grid);
            var before = new List<Vector2Int>();
            var after = new List<Vector2Int>();
            Assert.IsTrue(pf.FindPath(new Vector2Int(2, 8), new Vector2Int(37, 8), before));
            Assert.IsTrue(pf.FindPath(new Vector2Int(2, 8), new Vector2Int(37, 8), after, true, null, 0f));
            CollectionAssert.AreEqual(before, after);
        }
    }
}
