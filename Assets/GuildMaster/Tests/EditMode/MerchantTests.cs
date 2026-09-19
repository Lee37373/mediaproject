using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    public class MerchantTests
    {
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
        public void Demand_IsHighWithoutSupply_AndFallsTowardsMinimum()
        {
            Assert.AreEqual(MerchantMarket.MaxDemand, MerchantMarket.DemandFor(0f), 1e-4f);
            Assert.Less(MerchantMarket.DemandFor(10f), MerchantMarket.DemandFor(5f));
            Assert.Greater(MerchantMarket.DemandFor(10000f), MerchantMarket.MinDemand);
            Assert.AreEqual(MerchantMarket.MinDemand, MerchantMarket.DemandFor(10000f), 0.01f);
        }

        [Test]
        public void Sell_ConvertsStockToGold_AndLowersNextPrice()
        {
            var t = new GuildTreasury(0, 0, 0, 0);
            var market = new MerchantMarket(t, _db);
            var ore = _db.Item("iron_ore");
            t.AddStock(ore.id, 5);
            int before = market.Price(ore);
            int quote = market.Quote(ore, 3);

            int gold = market.Sell(ore.id, 3);

            Assert.AreEqual(quote, gold);
            Assert.AreEqual(gold, t.Gold);
            Assert.AreEqual(2, t.GetStock(ore.id));
            Assert.Less(market.Price(ore), before);
            // Cannot sell more than the guild has.
            Assert.AreEqual(0, market.Sell(ore.id, 0));
            market.Sell(ore.id, 99);
            Assert.AreEqual(0, t.GetStock(ore.id));
        }

        [Test]
        public void GuildLootPurchases_AddSupply_WhichFadesDaily()
        {
            var t = new GuildTreasury(0, 0, 0, 0);
            var market = new MerchantMarket(t, _db);
            var ear = _db.Item("goblin_ear");
            var dagger = _db.Item("rusty_dagger");
            EventBus.Publish(new TradeEvent { ItemId = ear.id, Count = 20, GuildBought = true });
            EventBus.Publish(new TradeEvent { ItemId = dagger.id, Count = 20, GuildBought = false });

            Assert.AreEqual(20f, market.Supply(ear.id), 1e-4f);
            Assert.AreEqual(0f, market.Supply(dagger.id), 1e-4f);   // guild sales to adventurers are not market supply
            Assert.Less(market.Demand(ear.id), market.Demand(dagger.id));

            EventBus.Publish(new DayPassedEvent { Day = 2 });
            Assert.AreEqual(20f * MerchantMarket.DailyRetention, market.Supply(ear.id), 1e-3f);
        }
    }
}
