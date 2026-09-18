using System.Collections.Generic;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    public class WorldTests
    {
        static MapData OpenMap(int w, int h)
        {
            var map = new MapData(w, h);
            for (int i = 0; i < map.Tiles.Length; i++) map.Tiles[i] = TileType.Grass;
            return map;
        }

        [Test]
        public void AStar_FindsStraightPath_OnOpenGrid()
        {
            var pf = new Pathfinder(new GridMap(OpenMap(20, 20)));
            var path = new List<Vector2Int>();
            Assert.IsTrue(pf.FindPath(new Vector2Int(0, 0), new Vector2Int(10, 0), path));
            Assert.AreEqual(new Vector2Int(0, 0), path[0]);
            Assert.AreEqual(new Vector2Int(10, 0), path[^1]);
            Assert.AreEqual(11, path.Count);
        }

        [Test]
        public void AStar_RoutesAroundWall_AndNeverCutsCorners()
        {
            var map = OpenMap(20, 20);
            for (int y = 0; y < 15; y++) map[10, y] = TileType.Wall;
            var grid = new GridMap(map);
            var pf = new Pathfinder(grid);
            var path = new List<Vector2Int>();
            Assert.IsTrue(pf.FindPath(new Vector2Int(5, 2), new Vector2Int(15, 2), path));
            for (int i = 0; i < path.Count; i++)
            {
                Assert.IsTrue(grid.IsWalkable(path[i]), $"step {i} not walkable");
                if (i == 0) continue;
                var d = path[i] - path[i - 1];
                Assert.LessOrEqual(Mathf.Abs(d.x), 1);
                Assert.LessOrEqual(Mathf.Abs(d.y), 1);
                if (d.x != 0 && d.y != 0)
                {
                    Assert.IsTrue(grid.IsWalkable(path[i - 1] + new Vector2Int(d.x, 0)));
                    Assert.IsTrue(grid.IsWalkable(path[i - 1] + new Vector2Int(0, d.y)));
                }
            }
        }

        [Test]
        public void AStar_ReturnsFalse_WhenEnclosed()
        {
            var map = OpenMap(10, 10);
            for (int x = 0; x < 10; x++) map[x, 5] = TileType.Water;
            var pf = new Pathfinder(new GridMap(map));
            Assert.IsFalse(pf.FindPath(new Vector2Int(1, 1), new Vector2Int(1, 8), new List<Vector2Int>()));
        }

        [Test]
        public void AStar_PrefersRoads()
        {
            var map = OpenMap(30, 11);
            for (int x = 0; x < 30; x++) { map[x, 5] = TileType.Road; map[x, 0] = TileType.Forest; }
            for (int y = 1; y < 5; y++) for (int x = 0; x < 30; x++) map[x, y] = TileType.Forest;
            var pf = new Pathfinder(new GridMap(map));
            var path = new List<Vector2Int>();
            Assert.IsTrue(pf.FindPath(new Vector2Int(0, 4), new Vector2Int(29, 4), path));
            int onRoad = 0;
            foreach (var c in path) if (map[c] == TileType.Road) onRoad++;
            Assert.Greater(onRoad, path.Count / 2);
        }

        [TestCase(1)]
        [TestCase(42)]
        [TestCase(9999)]
        public void MapGenerator_TownIsReachable_AndNestsAreReachable(int seed)
        {
            var map = MapGenerator.Generate(new MapGenSettings
            {
                Width = 128, Height = 128, Seed = seed, TownHalfSize = 11, NestCount = 14, MinNestDistanceFromTown = 24,
            });
            Assert.AreEqual(4, map.Gates.Count);
            Assert.Greater(map.Nests.Count, 6, "too few nests");
            var dist = MapGenerator.FloodDistances(map, map.TownCenter);
            foreach (var n in map.Nests) Assert.GreaterOrEqual(dist[map.Index(n.Cell.x, n.Cell.y)], 24);
            Assert.Greater(map.EdgeSpawnCells.Count, 10);
        }

        [Test]
        public void MapGenerator_IsDeterministic()
        {
            var s = new MapGenSettings { Width = 64, Height = 64, Seed = 7, TownHalfSize = 8, NestCount = 5, MinNestDistanceFromTown = 12 };
            CollectionAssert.AreEqual(MapGenerator.Generate(s).Tiles, MapGenerator.Generate(s).Tiles);
        }
    }
}
