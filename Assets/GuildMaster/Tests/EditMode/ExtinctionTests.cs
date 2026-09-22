using GuildMaster.Session;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// There is no extinction (P4-43, user decision 2026-09-22): an emptied nest is settled again
    /// after a few hours whatever else happened to its kind. Plus raid and building balance checks.
    /// </summary>
    public class ExtinctionTests
    {
        [Test]
        public void AnEmptiedNestIsSettledAgainWithinHours()
        {
            Assert.That(Ecosystem.EcosystemManager.RefillHours, Is.InRange(1, 12));
            Assert.That(Ecosystem.EcosystemManager.RefillCount, Is.GreaterThanOrEqualTo(2), "a pair, so it can breed");
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
