using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Economy;
using GuildMaster.Quests;
using GuildMaster.Reports;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Economy round 2 (user decision 2026-10-29): clients pay for finished quests, the guild buys food.</summary>
    public class EconomyRound2Tests
    {
        [Test]
        public void Clients_PayTheRewardUpToTheGoingRate_PlusACutThatGrowsWithDanger()
        {
            Assert.That(CommissionRules.Rate(DangerLevel.Low), Is.LessThan(CommissionRules.Rate(DangerLevel.Medium)));
            Assert.That(CommissionRules.Rate(DangerLevel.Medium), Is.LessThan(CommissionRules.Rate(DangerLevel.High)));
            int budget = CommissionRules.Budget(QuestType.Hunt, DangerLevel.Medium, 5, 0f);
            Assert.That(budget, Is.GreaterThan(0));
            int fee = CommissionRules.Fee(budget, DangerLevel.Medium);
            Assert.That(fee, Is.GreaterThan(0));
            Assert.That(CommissionRules.Payment(budget / 2, budget, fee), Is.EqualTo(budget / 2 + fee), "a small reward comes back whole");
            Assert.That(CommissionRules.Payment(budget * 3, budget, fee), Is.EqualTo(budget + fee), "paying above the going rate is the guild's own money");
            Assert.That(CommissionRules.Fee(10, DangerLevel.Low), Is.GreaterThanOrEqualTo(5), "never nothing");
            Assert.That(CommissionRules.Client(QuestType.Hunt, "Q_3"), Is.EqualTo(CommissionRules.Client(QuestType.Hunt, "Q_3")), "the same client every time");
            Assert.That(CommissionRules.Client(QuestType.Escort, "Q_7"), Is.Not.Empty);
        }

        [Test]
        public void Food_IsEatenOneEachANight_AndTheRestGoWithout()
        {
            Assert.That(FoodRules.Eat(10, 4), Is.EqualTo((4, 0)));
            Assert.That(FoodRules.Eat(2, 4), Is.EqualTo((2, 2)));
            Assert.That(FoodRules.Eat(0, 3), Is.EqualTo((0, 3)));
            Assert.That(FoodRules.DaysLeft(12, 4), Is.EqualTo(3));
            Assert.That(FoodRules.Price(FoodRules.BulkSize), Is.LessThan(FoodRules.BulkSize * FoodRules.PortionPrice), "buying in bulk is cheaper");
            Assert.That(FoodRules.PortionPrice, Is.LessThan(10), "the guild keeps a little of each 10G meal");
        }

        [Test]
        public void BuyingFood_CostsGuildGold()
        {
            var treasury = new GuildTreasury(100, 0);
            var store = new FoodStore(null, treasury, 2);
            try
            {
                Assert.That(store.TryBuy(FoodRules.SmallSize), Is.True);
                Assert.That(store.Food, Is.EqualTo(2 + FoodRules.SmallSize));
                Assert.That(treasury.Gold, Is.EqualTo(100 - FoodRules.Price(FoodRules.SmallSize)));
                Assert.That(store.TryBuy(FoodRules.BulkSize), Is.False, "not enough gold");
                Assert.That(store.Food, Is.EqualTo(2 + FoodRules.SmallSize));
            }
            finally { store.Dispose(); }
        }

        [Test]
        public void TheDailyReport_SortsTheNewMoney()
        {
            Assert.That(GoldCategories.Classify(GoldReason.Commission, 120), Is.EqualTo(GoldCategory.Commission));
            Assert.That(GoldCategories.Classify(GoldReason.Tribute, 30), Is.EqualTo(GoldCategory.Tribute));
            Assert.That(GoldCategories.Classify(GoldReason.SideJob, 15), Is.EqualTo(GoldCategory.SideJob));
            Assert.That(GoldCategories.Classify(GoldReason.Food, -60), Is.EqualTo(GoldCategory.Food));
            Assert.That(GoldCategories.IsIncome(GoldCategory.Commission) && GoldCategories.IsIncome(GoldCategory.SideJob), Is.True);
            Assert.That(GoldCategories.IsIncome(GoldCategory.Food), Is.False);
        }
    }
}
