using GuildMaster.Adventurers;
using GuildMaster.Economy;
using GuildMaster.Recruitment;
using GuildMaster.Reports;
using GuildMaster.Session;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Members' keep and the balance run's recruiting (P4-68, user decisions 2026-09-27).</summary>
    public class LivingCostTests
    {
        [Test]
        public void Charge_PaysWhatThePurseHolds_AndTheRestGoesOnTheSlate()
        {
            int gold = 30, debt = 0;
            Assert.That(LivingCostRules.Charge(ref gold, ref debt, 10), Is.EqualTo(10));
            Assert.That((gold, debt), Is.EqualTo((20, 0)), "paid in full");

            gold = 4;
            Assert.That(LivingCostRules.Charge(ref gold, ref debt, 10), Is.EqualTo(4));
            Assert.That((gold, debt), Is.EqualTo((0, 6)), "what it could not pay is owed");

            Assert.That(LivingCostRules.Charge(ref gold, ref debt, 2), Is.EqualTo(0));
            Assert.That(debt, Is.EqualTo(8), "an empty purse runs the slate up");

            Assert.That(LivingCostRules.Charge(ref gold, ref debt, 0), Is.EqualTo(0));
            Assert.That(LivingCostRules.Charge(ref gold, ref debt, -5), Is.EqualTo(0));
            Assert.That((gold, debt), Is.EqualTo((0, 8)), "nothing to charge, nothing changes");
        }

        [Test]
        public void Repay_ClearsTheSlateFirst_AsFarAsTheGoldGoes()
        {
            int gold = 5, debt = 8;
            Assert.That(LivingCostRules.Repay(ref gold, ref debt), Is.EqualTo(5));
            Assert.That((gold, debt), Is.EqualTo((0, 3)), "part of it");

            gold = 50;
            Assert.That(LivingCostRules.Repay(ref gold, ref debt), Is.EqualTo(3));
            Assert.That((gold, debt), Is.EqualTo((47, 0)), "all of it, the rest is theirs");

            Assert.That(LivingCostRules.Repay(ref gold, ref debt), Is.EqualTo(0), "no debt, nothing taken");
        }

        [Test]
        public void Debt_CountsAgainstMoney_OnScreen()
        {
            Assert.That(LivingCostRules.NetWorth(0, 20), Is.LessThan(0), "owing is short of money");
            Assert.That(LivingCostRules.Purse(120, 0), Is.EqualTo("골드 120"));
            Assert.That(LivingCostRules.Purse(0, 30), Is.EqualTo("골드 0 (빚 30)"));
        }

        [Test]
        public void KeepIsIncome_InTheDailyReport()
        {
            Assert.That(GoldCategories.Classify(GoldReason.Living, 10), Is.EqualTo(GoldCategory.LivingCosts));
            Assert.That(GoldCategories.IsIncome(GoldCategory.LivingCosts), Is.True);
            Assert.That(GoldCategories.Income, Does.Contain(GoldCategory.LivingCosts));
            Assert.That(GoldCategories.Classify(GoldReason.Living, -10), Is.EqualTo(GoldCategory.OtherExpense), "keep is never paid out");
        }
    }
}
