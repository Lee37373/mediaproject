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
                // Player-placed buildings (P4-13) are bought outright, so their one level is the
                // build cost; ConstructionTests checks those instead.
                if (System.Array.IndexOf(ConstructionService.Placeable, type) >= 0) continue;
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
        public void RecipesUseKnownItems_AndReachableTiers()
        {
            var db = DefaultContent.Build();
            db.BuildIndex();
            Assert.That(db.recipes.Count, Is.GreaterThanOrEqualTo(4));
            foreach (var r in db.recipes)
            {
                Assert.That(db.Item(r.outputItemId), Is.Not.Null, r.id);
                foreach (var c in r.inputs) Assert.That(db.Item(c.itemId), Is.Not.Null, $"{r.id} needs unknown '{c.itemId}'");
                var station = db.Building(r.station);
                Assert.That(station, Is.Not.Null, r.id);
                Assert.That(station.Level(station.MaxLevel).craftTier, Is.GreaterThanOrEqualTo(r.requiredTier), $"{r.id} tier unreachable");
                Assert.That(r.hours, Is.GreaterThan(0f), r.id);
            }
        }

        [Test]
        public void TownHallAndGuildHallFollowSpec()
        {
            var db = DefaultContent.Build();
            db.BuildIndex();
            var hall = db.Building(BuildingType.TownHall);
            // P5-1 raised these from 600/900/1300: the hall fell to the first raid in seconds.
            Assert.That(new[] { hall.Level(1).maxHp, hall.Level(2).maxHp, hall.Level(3).maxHp }, Is.EqualTo(new[] { 1000f, 1500f, 2100f }));
            Assert.That(new[] { hall.Level(1).dailyTax, hall.Level(2).dailyTax, hall.Level(3).dailyTax }, Is.EqualTo(new[] { 90, 120, 160 }), "level 1 is the tax the game starts with (P4-69)");
            var guild = db.Building(BuildingType.GuildHall);
            Assert.That(new[] { guild.Level(1).visitorLevelBonus, guild.Level(2).visitorLevelBonus, guild.Level(3).visitorLevelBonus }, Is.EqualTo(new[] { 0, 1, 2 }));
            // P4-92: there is no roster limit, so its text must not promise room for more.
            for (int lv = 1; lv <= 3; lv++) Assert.That(guild.Level(lv).effectText, Does.Not.Contain("정원"));
            // P4-82: the guild hall makes no EXP any more, so its text must not promise any.
            for (int lv = 1; lv <= 3; lv++) Assert.That(guild.Level(lv).effectText, Does.Not.Contain("EXP"));
        }
    }
}
