using System.Linq;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>Terrain regions (P4-55): layout, and the movement and sight rules they bring.</summary>
    public class TerrainRegionTests
    {
        static MapData Generate(int seed) => MapGenerator.Generate(new MapGenSettings
        {
            Width = 256, Height = 256, Seed = seed, TownHalfSize = 11, NestCount = 64, MinNestDistanceFromTown = 20,
        });

        [TestCase(1)]
        [TestCase(42)]
        [TestCase(9999)]
        public void GrasslandRingsTheTown_AndEveryOtherRegionLiesFurtherOut(int seed)
        {
            var map = Generate(seed);
            Assert.That(map.RegionOf(map.TownCenter.x, map.TownCenter.y), Is.EqualTo(Region.Grassland));
            // The town, its clearing and the first rings (slimes) are grassland (warped borders: >= 28 cells).
            for (int a = 0; a < 16; a++)
            {
                var p = map.TownCenter + new Vector2(Mathf.Cos(a * Mathf.PI / 8f), Mathf.Sin(a * Mathf.PI / 8f)) * 25f;
                Assert.That(map.RegionOf(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y)), Is.EqualTo(Region.Grassland), $"25 cells out, heading {a}");
            }
            var present = map.Regions.Distinct().ToList();
            foreach (Region r in System.Enum.GetValues(typeof(Region)))
                Assert.That(present, Does.Contain(r), $"{r} on the map");
        }

        [TestCase(1)]
        [TestCase(42)]
        [TestCase(9999)]
        public void EveryLakeWoodAndRockMass_LiesWhollyInOneRegion(int seed)
        {
            // User decision 2026-09-22: borders follow the terrain (no lake half water, half lava).
            var map = Generate(seed);
            static int Kind(TileType t) => t is TileType.Water or TileType.Sand ? 1 : t == TileType.Forest ? 2 : t == TileType.Mountain ? 3 : 0;
            int crossing = 0;
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                int k = Kind(map[x, y]);
                if (k == 0) continue;
                if (x + 1 < map.Width && Kind(map[x + 1, y]) == k && map.RegionOf(x + 1, y) != map.RegionOf(x, y)) crossing++;
                if (y + 1 < map.Height && Kind(map[x, y + 1]) == k && map.RegionOf(x, y + 1) != map.RegionOf(x, y)) crossing++;
            }
            Assert.That(crossing, Is.Zero, "neighbouring cells of one feature in different regions");
        }

        [Test]
        public void HarshRegions_SlowWalkingOffTheRoads()
        {
            var map = new MapData(4, 1);
            map[0, 0] = TileType.Grass; map[1, 0] = TileType.Grass; map[2, 0] = TileType.Road; map[3, 0] = TileType.Forest;
            map.Regions[1] = Region.Swamp;
            map.Regions[2] = Region.Swamp;
            map.Regions[3] = Region.Snow;
            var grid = new GridMap(map);
            Assert.That(grid.Cost(0, 0), Is.EqualTo(1f));
            Assert.That(grid.Cost(1, 0), Is.EqualTo(TerrainRegions.MoveMultiplier(Region.Swamp)).Within(1e-4f), "swamp mud");
            Assert.That(grid.Cost(2, 0), Is.EqualTo(MapData.MoveCost(TileType.Road)), "roads stay roads");
            Assert.That(grid.Cost(3, 0), Is.EqualTo(MapData.MoveCost(TileType.Forest) * TerrainRegions.MoveMultiplier(Region.Snow)).Within(1e-4f), "a snowy forest is both");
        }

        [Test]
        public void MistAndSnow_ShortenSight_AndStackWithTheForest()
        {
            var map = new MapData(6, 1);
            for (int x = 0; x < 6; x++) map[x, 0] = TileType.Grass;
            map[4, 0] = TileType.Forest;
            map.Regions[2] = Region.Swamp;
            map.Regions[4] = Region.Snow;
            var world = new WorldService(map);
            Assert.That(ForestSight.Radius(world, new Vector2(0.5f, 0.5f), 10f), Is.EqualTo(10f));
            Assert.That(ForestSight.Radius(world, new Vector2(2.5f, 0.5f), 10f), Is.EqualTo(10f * TerrainRegions.SightMultiplier(Region.Swamp)).Within(1e-4f));
            Assert.That(ForestSight.Radius(world, new Vector2(4.5f, 0.5f), 9f), Is.EqualTo(9f * TerrainRegions.SightMultiplier(Region.Snow) / 3f).Within(1e-4f));
            Assert.That(ForestSight.Radius(world, new Vector2(2.5f, 0.5f), 10f, elevated: true), Is.EqualTo(10f), "a watchtower sees through the mist");
        }
    }
}
