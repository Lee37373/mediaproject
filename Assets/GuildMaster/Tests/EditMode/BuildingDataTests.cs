using GuildMaster.Data;
using GuildMaster.Town;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Building upgrade data (P4-1) must only reference items that exist.</summary>
    public class BuildingDataTests
    {
        [Test]
        public void EveryUpgradableBuildingHasThreeLevels_AndValidCosts()
        {
            var db = DefaultContent.Build();
            db.BuildIndex();
            foreach (BuildingType type in System.Enum.GetValues(typeof(BuildingType)))
            {
                var def = db.Building(type);
                Assert.That(def, Is.Not.Null, type.ToString());
                Assert.That(def.MaxLevel, Is.EqualTo(3), type.ToString());
                Assert.That(def.Level(1).goldCost, Is.EqualTo(0), "Lv.1 is free: " + type);
                for (int lv = 2; lv <= def.MaxLevel; lv++)
                {
                    Assert.That(def.Level(lv).goldCost, Is.GreaterThan(def.Level(lv - 1).goldCost), $"{type} Lv.{lv}");
                    foreach (var c in def.Level(lv).itemCosts)
                        Assert.That(db.Item(c.itemId), Is.Not.Null, $"{type} Lv.{lv} needs unknown item '{c.itemId}'");
                }
            }
        }

        [Test]
        public void TownHallAndGuildHallFollowSpec()
        {
            var db = DefaultContent.Build();
            db.BuildIndex();
            var hall = db.Building(BuildingType.TownHall);
            Assert.That(new[] { hall.Level(1).maxHp, hall.Level(2).maxHp, hall.Level(3).maxHp }, Is.EqualTo(new[] { 600f, 900f, 1300f }));
            Assert.That(new[] { hall.Level(1).dailyTax, hall.Level(2).dailyTax, hall.Level(3).dailyTax }, Is.EqualTo(new[] { 60, 90, 130 }));
            var guild = db.Building(BuildingType.GuildHall);
            Assert.That(new[] { guild.Level(1).rosterCapacity, guild.Level(2).rosterCapacity, guild.Level(3).rosterCapacity }, Is.EqualTo(new[] { 6, 9, 12 }));
            Assert.That(new[] { guild.Level(1).expPerHour, guild.Level(2).expPerHour, guild.Level(3).expPerHour }, Is.EqualTo(new[] { 8f, 14f, 22f }));
        }
    }
}
