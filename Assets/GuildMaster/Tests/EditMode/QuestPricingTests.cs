using GuildMaster.Data;
using GuildMaster.Quests;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>What a client's quest pays and how the guild's fee comes off it (P4-82).</summary>
    public class QuestPricingTests
    {
        [Test]
        public void Split_TheGuildKeepsAFifth_AndTheMembersTheRest()
        {
            Assert.That(QuestPricing.Split(100), Is.EqualTo((80, 20)));
            Assert.That(QuestPricing.Split(33), Is.EqualTo((26, 7)), "the fee is rounded, the members get what is left");
            Assert.That(QuestPricing.Split(0), Is.EqualTo((0, 0)));
            Assert.That(QuestPricing.Split(-5), Is.EqualTo((0, 0)));
            for (int total = 0; total <= 500; total += 7)
            {
                var (members, fee) = QuestPricing.Split(total);
                Assert.That(members + fee, Is.EqualTo(total));
            }
        }

        [Test]
        public void FarFactor_KeepsTheOldNearPrice_AndGrowsToTheFarthestDens()
        {
            Assert.That(QuestPricing.FarFactor(0f), Is.EqualTo(1f));
            Assert.That(QuestPricing.FarFactor(60f), Is.EqualTo(1.5f).Within(1e-5f), "the old proposal price at 60 cells");
            Assert.That(QuestPricing.FarFactor(180f), Is.EqualTo(2.5f).Within(1e-5f), "the inner edge (P4-81)");
            Assert.That(QuestPricing.FarFactor(300f), Is.EqualTo(3.5f).Within(1e-5f));
            Assert.That(QuestPricing.FarFactor(1000f), Is.EqualTo(QuestPricing.FarFactor(QuestPricing.FarCap)), "no more past the cap");
        }

        [Test]
        public void HuntReward_GrowsWithDangerKillsAndDistance_InWholeFives()
        {
            int low = QuestPricing.HuntReward(DangerLevel.Low, 5, 30f);
            int medium = QuestPricing.HuntReward(DangerLevel.Medium, 5, 30f);
            int high = QuestPricing.HuntReward(DangerLevel.High, 5, 30f);
            Assert.That(low, Is.LessThan(medium));
            Assert.That(medium, Is.LessThan(high));
            Assert.That(QuestPricing.HuntReward(DangerLevel.Low, 2, 30f), Is.LessThan(low), "fewer kills pay less");
            int far = QuestPricing.HuntReward(DangerLevel.High, 5, 250f);
            Assert.That(far, Is.GreaterThan(high), "the farther, the better it pays (user decision 2026-10-17)");
            foreach (int g in new[] { low, medium, high, far })
            {
                Assert.That(g % 5, Is.EqualTo(0));
                Assert.That(g, Is.GreaterThanOrEqualTo(10));
            }
            Assert.That(QuestPricing.HuntReward(DangerLevel.Low, 5, 0f), Is.EqualTo(QuestPricing.DangerGold(DangerLevel.Low)), "five kills next to town: the base price");
        }

        [Test]
        public void HuntCount_NeverAsksMoreThanWasSeen_ButAtLeastTwo()
        {
            Assert.That(QuestPricing.HuntCount(0), Is.EqualTo(QuestPricing.HuntKills), "nothing seen: the usual five");
            Assert.That(QuestPricing.HuntCount(1), Is.EqualTo(2));
            Assert.That(QuestPricing.HuntCount(3), Is.EqualTo(3));
            Assert.That(QuestPricing.HuntCount(40), Is.EqualTo(QuestPricing.HuntKills));
            Assert.That(QuestPricing.HuntCount(40, 8), Is.EqualTo(8));
        }

        [Test]
        public void PartyFor_LetsMoreGoTheMoreDangerous()
        {
            Assert.That(QuestPricing.PartyFor(DangerLevel.Low), Is.EqualTo(2));
            Assert.That(QuestPricing.PartyFor(DangerLevel.Medium), Is.EqualTo(3));
            Assert.That(QuestPricing.PartyFor(DangerLevel.High), Is.EqualTo(4));
        }
    }
}
