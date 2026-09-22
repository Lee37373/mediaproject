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
            for (int t = (int)CombatTier.Novice; t <= (int)CombatTier.Heroic; t++)
            {
                Assert.Less(CombatTiers.RetargetInterval((CombatTier)t), CombatTiers.RetargetInterval((CombatTier)(t - 1)));
                Assert.Less(CombatTiers.ChaseGiveUp((CombatTier)t), CombatTiers.ChaseGiveUp((CombatTier)(t - 1)));
            }
        }

        [Test]
        public void TacticsUnlockByTier()
        {
            Assert.That(CombatTiers.RetaliatesOnly(CombatTier.Instinct), Is.True);
            Assert.That(CombatTiers.RetaliatesOnly(CombatTier.Novice), Is.False);
            Assert.That(CombatTiers.CanKite(CombatTier.Novice), Is.False, "novices do not kite");
            Assert.That(CombatTiers.CanKite(CombatTier.Skilled), Is.True);
            Assert.That(CombatTiers.SeeksFiringPosition(CombatTier.Skilled), Is.False, "cover and firing spots start at veteran");
            Assert.That(CombatTiers.SeeksFiringPosition(CombatTier.Veteran), Is.True);
        }

        [Test]
        public void CrowdPenalty_GrowsWithTier()
        {
            Assert.That(CombatTiers.CrowdPenalty(CombatTier.Instinct, 5), Is.EqualTo(0f), "instinct piles on");
            Assert.That(CombatTiers.CrowdPenalty(CombatTier.Novice, 3), Is.LessThan(CombatTiers.CrowdPenalty(CombatTier.Skilled, 3)));
            Assert.That(CombatTiers.CrowdPenalty(CombatTier.Skilled, 2), Is.LessThan(CombatTiers.CrowdPenalty(CombatTier.Veteran, 2)));
            Assert.That(CombatTiers.CrowdPenalty(CombatTier.Novice, 4) - CombatTiers.CrowdPenalty(CombatTier.Novice, 3), Is.GreaterThan(2f),
                "a novice's fourth attacker is heavily penalised");
        }

        [Test]
        public void MonsterSpecies_HaveTheirOwnTier()
        {
            var db = GuildMaster.Data.DefaultContent.Build();
            CombatTier TierOf(string id) => db.monsters.Find(m => m.id == id).combatTier;
            Assert.That(TierOf("slime"), Is.EqualTo(CombatTier.Instinct));
            Assert.That(TierOf("stone_slime"), Is.EqualTo(CombatTier.Instinct));
            Assert.That(TierOf("goblin"), Is.EqualTo(CombatTier.Novice));
            Assert.That(TierOf("skull"), Is.EqualTo(CombatTier.Novice));
            // P4-58: the level makes them smarter; the species only sets where they start.
            Assert.That(TierOf("goblin_warrior"), Is.EqualTo(CombatTier.Novice));
            Assert.That(TierOf("slime_king"), Is.EqualTo(CombatTier.Novice));
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
