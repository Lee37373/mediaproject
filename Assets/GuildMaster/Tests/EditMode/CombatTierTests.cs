using GuildMaster.Adventurers;
using GuildMaster.Units;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    public class CombatTierTests
    {
        [TestCase(1, 0, CombatTier.Novice)]
        [TestCase(4, 0, CombatTier.Novice)]
        [TestCase(5, 0, CombatTier.Skilled)]
        [TestCase(10, 0, CombatTier.Veteran)]
        [TestCase(15, 0, CombatTier.Heroic)]
        [TestCase(1, 1, CombatTier.Skilled)]    // rare recruit fights like a skilled one from the start
        [TestCase(1, 2, CombatTier.Veteran)]    // heroic recruit
        [TestCase(12, 2, CombatTier.Heroic)]    // capped
        public void FromLevel_AddsGradeToLevelBand(int level, int grade, CombatTier expected)
            => Assert.AreEqual(expected, CombatTiers.FromLevel(level, grade));

        [Test]
        public void HigherTiers_ThinkFasterAndGiveUpSooner()
        {
            for (int t = 1; t <= 3; t++)
            {
                Assert.Less(CombatTiers.RetargetInterval((CombatTier)t), CombatTiers.RetargetInterval((CombatTier)(t - 1)));
                Assert.Less(CombatTiers.ChaseGiveUp((CombatTier)t), CombatTiers.ChaseGiveUp((CombatTier)(t - 1)));
            }
        }

        [Test]
        public void Grade_RollFollowsDistribution()
        {
            Assert.AreEqual(AdventurerGrade.Heroic, AdventurerData.RollGrade(0.01f));
            Assert.AreEqual(AdventurerGrade.Rare, AdventurerData.RollGrade(0.2f));
            Assert.AreEqual(AdventurerGrade.Common, AdventurerData.RollGrade(0.5f));
        }
    }
}
