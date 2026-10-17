using GuildMaster.Core;
using GuildMaster.Disasters;
using GuildMaster.Economy;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Natural disasters (P4-72): the ground each needs, what its hazards do to walking, sight and
    /// the forest, what the town pays, and the tax a disaster at the walls costs.
    /// </summary>
    public class DisasterTests
    {
        [SetUp]
        public void SetUp() => EventBus.Clear();

        [TearDown]
        public void TearDown() => EventBus.Clear();

        [Test]
        public void EachKind_NeedsItsOwnGround_AndNeverTheTown()
        {
            Assert.That(DisasterRules.CanSpreadTo(DisasterKind.Wildfire, TileType.Forest, Region.Snow, HazardKind.None, false, false), Is.True,
                "a fire burns any forest");
            Assert.That(DisasterRules.CanSpreadTo(DisasterKind.Wildfire, TileType.Grass, Region.Grassland, HazardKind.None, false, false), Is.False,
                "but not open grass");
            Assert.That(DisasterRules.CanSpreadTo(DisasterKind.Wildfire, TileType.Forest, Region.Grassland, HazardKind.Burnt, false, false), Is.False,
                "nor what has burnt already");
            Assert.That(DisasterRules.CanSpreadTo(DisasterKind.PoisonFog, TileType.Grass, Region.Swamp, HazardKind.None, false, false), Is.True);
            Assert.That(DisasterRules.CanSpreadTo(DisasterKind.PoisonFog, TileType.Grass, Region.Desert, HazardKind.None, false, false), Is.False,
                "poison stays in the swamp");
            Assert.That(DisasterRules.CanSpreadTo(DisasterKind.Avalanche, TileType.Grass, Region.Snow, HazardKind.None, false, nearMountain: false), Is.False,
                "no avalanche without a mountain above");
            Assert.That(DisasterRules.CanSpreadTo(DisasterKind.Avalanche, TileType.Grass, Region.Snow, HazardKind.None, false, nearMountain: true), Is.True);
            Assert.That(DisasterRules.CanSpreadTo(DisasterKind.Quake, TileType.Grass, Region.Wasteland, HazardKind.None, inTown: true, false), Is.False,
                "never inside the walls");
            Assert.That(DisasterRules.CanSpreadTo(DisasterKind.Quake, TileType.Water, Region.Wasteland, HazardKind.None, false, false), Is.False,
                "nor on water");
            Assert.That(DisasterRules.GroundFits(DisasterKind.Sandstorm, TileType.Sand, Region.Desert, false), Is.True);
            Assert.That(DisasterRules.GroundFits(DisasterKind.Sandstorm, TileType.Grass, Region.Grassland, false), Is.False);
        }

        [Test]
        public void Hazards_ChangeWalking_Sight_AndTheForest()
        {
            var map = new MapData(8, 8);
            for (int i = 0; i < map.Tiles.Length; i++) map.Tiles[i] = TileType.Forest;
            map.Trees[map.Index(3, 3)] = true;
            var grid = new GridMap(map);
            var c = new Vector2Int(3, 3);
            float cost = grid.Cost(c);

            grid.SetHazard(c, HazardKind.Fire);
            Assert.That(grid.Cost(c), Is.GreaterThan(cost), "walkers go round a fire");
            Assert.That(grid.IsForest(c), Is.True, "still forest while it burns");

            grid.SetHazard(c, HazardKind.Burnt);
            Assert.That(grid.IsForest(c), Is.False, "burnt forest hides nothing");
            Assert.That(grid.HasTree(c.x, c.y), Is.False, "and gives no cover");
            Assert.That(grid.IsWalkable(c), Is.True);

            int version = grid.Version;
            grid.SetHazard(c, HazardKind.Snow);
            Assert.That(grid.Cost(c), Is.GreaterThanOrEqualTo(Hazards.WallCost), "snow piled up walls the way: walkers go round it");
            Assert.That(grid.IsWalkable(c), Is.True, "but one shut in can still climb out (P4-73)");
            Assert.That(grid.Version, Is.GreaterThan(version), "and paths are checked again");

            Assert.That(Hazards.SightFactor(HazardKind.Sandstorm), Is.LessThan(Hazards.SightFactor(HazardKind.Poison)));
            Assert.That(Hazards.SightFactor(HazardKind.Poison), Is.LessThan(1f));
            Assert.That(Hazards.Hurts(HazardKind.Fire) && Hazards.Hurts(HazardKind.Poison), Is.True);
            Assert.That(Hazards.Hurts(HazardKind.Snow), Is.False);
        }

        [Test]
        public void TheTownPaysMore_TheNearerItIs()
        {
            var near = DisasterRules.Reward(DisasterKind.Wildfire, 20f);
            var far = DisasterRules.Reward(DisasterKind.Wildfire, 150f);
            Assert.That(near.gold, Is.GreaterThan(far.gold));
            Assert.That(near.exp, Is.GreaterThan(far.exp));
            Assert.That(far.gold, Is.GreaterThan(0));
        }

        [Test]
        public void TheSandstorm_GrowsOverItsLife()
        {
            Assert.That(DisasterRules.StormRadius(0f), Is.EqualTo(DisasterRules.StormStartRadius).Within(0.01f));
            Assert.That(DisasterRules.StormRadius(100f), Is.EqualTo(DisasterRules.StormFullRadius).Within(0.01f));
        }

        [Test]
        public void ADisasterAtTheWalls_CutsThatDaysTaxOnly()
        {
            var t = new GuildTreasury(0, 100);
            t.CutTodaysTax(DisasterRules.TaxCut);
            t.CutTodaysTax(0.1f); // a smaller cut later that day does not undo the larger one
            EventBus.Publish(new DayPassedEvent { Day = 2 });
            Assert.AreEqual(Mathf.RoundToInt(100 * (1f - DisasterRules.TaxCut)), t.Gold);
            EventBus.Publish(new DayPassedEvent { Day = 3 });
            Assert.AreEqual(Mathf.RoundToInt(100 * (1f - DisasterRules.TaxCut)) + 100, t.Gold, "the next day's tax is whole again");
        }
    }
}
