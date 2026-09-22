using GuildMaster.Session;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Emptied nests (user decision 2026-09-22): no refill by the same kind any more; the outer
    /// sites are taken by newcomers from beyond the map edge. Plus raid and building balance checks.
    /// </summary>
    public class ExtinctionTests
    {
        [Test]
        public void EmptyOuterSites_AreTakenByNewcomersFromBeyondTheEdge()
        {
            Assert.That(Ecosystem.MigrationService.ArrivalChancePerHour, Is.InRange(0.05f, 1f), "within a day or so");
            Assert.That(Ecosystem.MigrationService.MaxArrivals, Is.GreaterThanOrEqualTo(1));
            Assert.That(Ecosystem.MigrationService.OuterSites, Is.InRange(0.3f, 0.8f), "the outer rings, not the town's");
        }

        [Test]
        public void RaidersReachingTheTownFindDefendersAlreadyOnTheirWay()
        {
            var balance = UnityEngine.ScriptableObject.CreateInstance<GameBalance>();
            // Members must come home from a hunt on the far side of the map, however big it is.
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
