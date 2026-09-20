using GuildMaster.Session;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Extinction is not permanent (P5-1, user decision 2026-09-20): a species wiped out settles
    /// again after a few days, so the last nest of a species can never be lost for good.
    /// </summary>
    public class ExtinctionTests
    {
        [Test]
        public void BalanceAssetSchedulesAReturn()
        {
            var balance = UnityEngine.ScriptableObject.CreateInstance<GameBalance>();
            Assert.That(balance.speciesReturnDays, Is.GreaterThan(0),
                        "0 would make extinction final again");
            Assert.That(balance.speciesReturnDays, Is.LessThanOrEqualTo(7),
                        "a species should come back inside a week, not vanish for the run");
        }

        [Test]
        public void RaidersReachingTheTownFindDefendersAlreadyOnTheirWay()
        {
            var balance = UnityEngine.ScriptableObject.CreateInstance<GameBalance>();
            // The map is 128 cells across; members must come home from a hunt on the far side.
            Assert.That(balance.raidDefendRadius, Is.GreaterThanOrEqualTo(balance.mapWidth * 0.5f),
                        "members out on a distant quest were missing the defence entirely");
        }

        [Test]
        public void BuildingsTakeLessThanAFullHit()
        {
            var balance = UnityEngine.ScriptableObject.CreateInstance<GameBalance>();
            Assert.That(balance.monsterBuildingDamageMul, Is.LessThan(1f));
            Assert.That(balance.monsterBuildingDamageMul, Is.GreaterThan(0f));
        }
    }
}
