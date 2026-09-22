using GuildMaster.Adventurers;
using GuildMaster.Data;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Progression and raid-damage tuning (P5-1). These pin the numbers the balance run was
    /// measured against, so a later edit that quietly undoes them fails here.
    /// </summary>
    public class ProgressionTests
    {
        [Test]
        public void FirstLevelUpFitsInsideOneDayOfHunting()
        {
            var d = new AdventurerData { level = 1 };
            var db = DefaultContent.Build();
            var goblin = db.monsters.Find(m => m.id == "goblin");

            // A first-day quest is a handful of goblins: the kills alone should nearly do it, and
            // the quest's own EXP reward finishes it.
            int killExp = 4 * goblin.expValue;
            Assert.That(killExp, Is.GreaterThanOrEqualTo(d.ExpToNextLevel),
                        "four goblins should be enough for the first level-up");
        }

        [Test]
        public void ExpCurveGrowsButStaysReachable()
        {
            var d = new AdventurerData { level = 1 };
            int lv2 = d.ExpToNextLevel;
            d.level = 5;
            int lv6 = d.ExpToNextLevel;
            Assert.That(lv2, Is.LessThan(lv6));
            Assert.That(lv6, Is.LessThan(lv2 * 4), "the curve must not explode in the first few levels");
        }

        [Test]
        public void MonsterExpRisesWithDanger()
        {
            var db = DefaultContent.Build();
            int Exp(string id) => db.monsters.Find(m => m.id == id).expValue;
            Assert.That(Exp("slime"), Is.LessThan(Exp("goblin")));
            Assert.That(Exp("goblin"), Is.LessThan(Exp("goblin_warrior")));
            Assert.That(Exp("goblin_warrior"), Is.LessThan(Exp("slime_king")));
        }

        [Test]
        public void SpeciesLiveFurtherOutTheMoreDangerousTheyAre()
        {
            var db = DefaultContent.Build();
            float Band(string id) => db.monsters.Find(m => m.id == id).distanceBand;
            Assert.That(Band("slime"), Is.LessThan(Band("goblin")));
            Assert.That(Band("goblin"), Is.LessThan(Band("goblin_warrior")));
            Assert.That(Band("goblin_warrior"), Is.LessThan(Band("slime_king")));
        }

        [Test]
        public void TheTownHallOutlastsAFirstRaidLongEnoughToDefendIt()
        {
            var db = DefaultContent.Build();
            var hall = db.buildings.Find(b => b.type == Town.BuildingType.TownHall);
            var goblin = db.monsters.Find(m => m.id == "goblin");

            // Eight raiders on the walls, at the shared building-damage multiplier.
            float dps = 8 * goblin.attack / goblin.attackCooldown * Monsters.RaidState.BuildingDamageMul;
            float secondsToFall = hall.levels[0].maxHp / dps;
            Assert.That(secondsToFall, Is.GreaterThan(30f),
                        "defenders need time to walk back and fight before the hall falls");
        }
    }
}
