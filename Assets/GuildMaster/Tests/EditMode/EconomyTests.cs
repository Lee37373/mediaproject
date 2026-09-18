using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using GuildMaster.Quests;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    public class EconomyTests
    {
        class FakeTrader : ITrader
        {
            public string TraderName => "tester";
            public int Gold { get; set; }
            public List<ItemStack> CarriedLoot { get; } = new();
        }

        class FakeTaker : IQuestTaker
        {
            public FakeTaker(string id) { TakerId = id; }
            public string TakerId { get; }
            public string TakerName => TakerId;
            public bool IsAvailableForQuest => true;
        }

        GameDatabase _db;

        [SetUp]
        public void SetUp()
        {
            EventBus.Clear();
            _db = DefaultContent.Build();
            _db.BuildIndex();
        }

        [TearDown]
        public void TearDown() => EventBus.Clear();

        [Test]
        public void Treasury_GeneratesExpPerHour_AndTaxPerDay()
        {
            var t = new GuildTreasury(100, 0, 2.5f, 50);
            EventBus.Publish(new HourPassedEvent { Day = 1, Hour = 9 });
            EventBus.Publish(new HourPassedEvent { Day = 1, Hour = 10 });
            Assert.AreEqual(5, t.Exp);
            EventBus.Publish(new DayPassedEvent { Day = 2 });
            Assert.AreEqual(150, t.Gold);
        }

        [Test]
        public void Treasury_CannotOverspend()
        {
            var t = new GuildTreasury(10, 5, 0, 0);
            Assert.IsFalse(t.TrySpend(11, 0));
            Assert.IsFalse(t.TrySpend(0, 6));
            Assert.IsTrue(t.TrySpend(10, 5));
            Assert.AreEqual(0, t.Gold);
        }

        [Test]
        public void BuyLoot_PaysSellerAndStocksGuild_LimitedByGuildGold()
        {
            var t = new GuildTreasury(20, 0, 0, 0);
            var trade = new TradeService(t, _db) { LootBuyRate = 1f };
            var seller = new FakeTrader();
            seller.CarriedLoot.Add(new ItemStack("wolf_pelt", 3)); // base 10 each

            int paid = trade.BuyLoot(seller);

            Assert.AreEqual(20, paid);
            Assert.AreEqual(20, seller.Gold);
            Assert.AreEqual(0, t.Gold);
            Assert.AreEqual(2, t.GetStock("wolf_pelt"));
            Assert.AreEqual(1, seller.CarriedLoot[0].count, "unaffordable loot stays with the seller");
        }

        [Test]
        public void SellGoods_RequiresStockAndBuyerGold()
        {
            var t = new GuildTreasury(0, 0, 0, 0);
            var trade = new TradeService(t, _db) { GoodsSellRate = 1.5f };
            var buyer = new FakeTrader { Gold = 100 };
            Assert.IsFalse(trade.SellGoods(buyer, DefaultContent.Potion), "no stock");
            t.AddStock(DefaultContent.Potion, 1);
            Assert.IsTrue(trade.SellGoods(buyer, DefaultContent.Potion));
            Assert.AreEqual(70, buyer.Gold); // 20 * 1.5 = 30
            Assert.AreEqual(30, t.Gold);
            Assert.AreEqual(0, t.GetStock(DefaultContent.Potion));
        }

        [Test]
        public void QuestPayouts_AreContributionWeighted_AndSumToReward()
        {
            var q = new Quest("Q_1", new QuestDraft { Type = QuestType.Hunt, Species = _db.Monster("slime"), Count = 4, RewardGold = 90, RewardExp = 31, MaxParty = 2 }, 0f);
            var a = new FakeTaker("A");
            var b = new FakeTaker("B");
            q.AddAssignee(a);
            q.AddAssignee(b);
            q.AddContribution(a, 3); // weight 4
            q.AddContribution(b, 1); // weight 2
            q.ComputePayouts();

            Assert.IsTrue(q.TryTakePayout(a, out int ga, out int ea));
            Assert.IsTrue(q.TryTakePayout(b, out int gb, out int eb));
            Assert.AreEqual(60, ga);
            Assert.AreEqual(30, gb);
            Assert.AreEqual(31, ea + eb);
            Assert.IsFalse(q.TryTakePayout(a, out _, out _), "cannot collect twice");
        }
    }
}
