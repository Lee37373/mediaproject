using System.Collections.Generic;
using GuildMaster.Data;
using GuildMaster.Town;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>P4-13: where the player may put a building, and what a demolition gives back.</summary>
    public class ConstructionTests
    {
        GridMap _grid;

        [SetUp]
        public void SetUp()
        {
            // 20x20 of grass with a pond, a mountain ridge and a wall segment with one gate.
            var data = new MapData(20, 20);
            for (int y = 0; y < 20; y++)
            for (int x = 0; x < 20; x++)
                data[x, y] = TileType.Grass;
            data[3, 3] = TileType.Water;
            data[5, 5] = TileType.Mountain;
            data[10, 0] = data[10, 1] = TileType.Wall;
            data[10, 2] = TileType.Gate;
            data.Gates.Add(new Vector2Int(10, 2));
            data.TownRect = new RectInt(8, 0, 5, 5);
            _grid = new GridMap(data);
        }

        static RectInt At(int x, int y, int size = 2) => ConstructionRules.FootprintAt(new Vector2(x + 0.5f, y + 0.5f), size);

        [Test]
        public void OpenGround_IsFine_InsideTownAndOut()
        {
            Assert.That(ConstructionRules.CannotPlace(_grid, null, null, At(15, 15)), Is.Null, "far from town");
            Assert.That(ConstructionRules.CannotPlace(_grid, null, null, At(9, 4)), Is.Null, "inside the town rect");
        }

        [Test]
        public void GroundThatCarriesNothing_IsRefused()
        {
            Assert.That(ConstructionRules.CannotPlace(_grid, null, null, At(3, 3)), Is.Not.Null, "water");
            Assert.That(ConstructionRules.CannotPlace(_grid, null, null, At(5, 5)), Is.Not.Null, "mountain");
            Assert.That(ConstructionRules.CannotPlace(_grid, null, null, At(10, 1)), Is.Not.Null, "wall");
            Assert.That(ConstructionRules.CannotPlace(_grid, null, null, At(0, 0, 3)), Is.Not.Null, "hangs off the map");
        }

        [Test]
        public void Gates_KeepTheirClearance()
        {
            // The gate is at (10, 2); anything within two cells of it would plug the way in.
            Assert.That(ConstructionRules.CannotPlace(_grid, null, _grid.Data.Gates, At(10, 4)), Is.Not.Null, "right outside the gate");
            Assert.That(ConstructionRules.CannotPlace(_grid, null, _grid.Data.Gates, At(10, 6)), Is.Null, "far enough from the gate");
        }

        [Test]
        public void ExistingBuildings_AreNotBuiltOver()
        {
            var b = new GameObject("B").AddComponent<Building>();
            b.Init(BuildingType.GuildHall, new RectInt(14, 14, 3, 3), new Vector2Int(15, 13), true, 100f);
            var buildings = new List<Building> { b };
            Assert.That(ConstructionRules.CannotPlace(_grid, buildings, null, At(15, 15)), Is.Not.Null, "overlaps");
            Assert.That(ConstructionRules.CannotPlace(_grid, buildings, null, At(15, 18)), Is.Null, "clear of it");
            Object.DestroyImmediate(b.gameObject);
        }

        [Test]
        public void AFacility_GoesInsideTheWalls_OffTheRoads_AndNoBuildingCoversADoor()
        {
            // P4-87 (user decision 2026-10-19): the guild's facilities stand inside the walls; the roads stay open.
            Assert.That(ConstructionRules.CannotPlace(_grid, null, null, At(15, 15), insideWalls: true), Does.Contain("성벽 안"));
            Assert.That(ConstructionRules.CannotPlace(_grid, null, null, At(9, 3, 1), insideWalls: true), Is.Null, "inside the town rect");
            _grid.Data[9, 3] = TileType.Road;
            Assert.That(ConstructionRules.CannotPlace(_grid, null, null, At(9, 3, 1), insideWalls: true), Does.Contain("길"));
            Assert.That(ConstructionRules.CannotPlace(_grid, null, null, At(9, 3, 1)), Is.Null, "a tower may stand on a road (P4-13)");

            var b = new GameObject("B").AddComponent<Building>();
            b.Init(BuildingType.GuildHall, new RectInt(14, 14, 3, 3), new Vector2Int(15, 13), true, 100f);
            Assert.That(ConstructionRules.CannotPlace(_grid, new List<Building> { b }, null, At(15, 12)), Does.Contain("출입문"));
            Object.DestroyImmediate(b.gameObject);
        }

        [Test]
        public void Footprint_IsCentredOnTheClickedCell()
        {
            Assert.That(ConstructionRules.FootprintAt(new Vector2(10.9f, 10.1f), 1), Is.EqualTo(new RectInt(10, 10, 1, 1)));
            Assert.That(ConstructionRules.FootprintAt(new Vector2(10.5f, 10.5f), 2), Is.EqualTo(new RectInt(10, 10, 2, 2)));
            Assert.That(ConstructionRules.FootprintAt(new Vector2(10.5f, 10.5f), 3), Is.EqualTo(new RectInt(9, 9, 3, 3)));
        }

        [Test]
        public void Demolition_GivesBackHalf_RoundedDown()
        {
            Assert.That(ConstructionRules.Refund(150), Is.EqualTo(75));
            Assert.That(ConstructionRules.Refund(5), Is.EqualTo(2));
            Assert.That(ConstructionRules.Refund(1), Is.EqualTo(0));
        }

        [Test]
        public void Watchtower_HasABuildCostAndSight()
        {
            var db = DefaultContent.Build();
            db.BuildIndex();
            var tower = db.Building(BuildingType.Watchtower);
            Assert.That(tower, Is.Not.Null);
            Assert.That(tower.MaxLevel, Is.EqualTo(1), "a placed building has no upgrade path");
            var level = tower.Level(1);
            Assert.That(level.goldCost, Is.GreaterThan(0));
            Assert.That(level.itemCosts, Is.Not.Empty);
            Assert.That(level.sightRadius, Is.GreaterThan(0f));
            Assert.That(level.maxHp, Is.GreaterThan(0f));
            foreach (var c in level.itemCosts)
                Assert.That(db.Item(c.itemId), Is.Not.Null, $"unknown material {c.itemId}");
        }
    }
}
