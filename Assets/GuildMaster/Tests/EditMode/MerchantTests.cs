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
        public void Scarcity_FollowsSpeciesAbundance_GoodsAreNeutral()
        {
            var t = new GuildTreasury(0, 0, 0, 0);
            float? earAbundance = 1f;
            var market = new MerchantMarket(t, _db, item => item.id == "goblin_ear" ? earAbundance : null);
            var ear = _db.Item("goblin_ear");

            Assert.AreEqual(MerchantMarket.CommonDemand, market.Scarcity(ear), 1e-4f);
            earAbundance = 0f;   // species wiped out: the outside market pays the most
            Assert.AreEqual(MerchantMarket.RareDemand, market.Scarcity(ear), 1e-4f);
            Assert.AreEqual(1f, market.Scarcity(_db.Item(DefaultContent.Potion)), 1e-4f);
        }

        [Test]
        public void Sell_ConvertsStockToGold_AndLowersNextPrice()
        {
            var t = new GuildTreasury(0, 0, 0, 0);
            var market = new MerchantMarket(t, _db, _ => 0.5f);
            var ore = _db.Item("iron_ore");
            t.AddStock(ore.id, 5);
            int before = market.Price(ore);
            int quote = market.Quote(ore, 3);

            int gold = market.Sell(ore.id, 3);

            Assert.AreEqual(quote, gold);
            Assert.AreEqual(gold, t.Gold);
            Assert.AreEqual(2, t.GetStock(ore.id));
            Assert.Less(market.Price(ore), before);
            Assert.AreEqual(0, market.Sell(ore.id, 0));
            market.Sell(ore.id, 99);
            Assert.AreEqual(0, t.GetStock(ore.id));
        }

        [Test]
        public void SoldVolume_FadesDaily()
        {
            var t = new GuildTreasury(0, 0, 0, 0);
            var market = new MerchantMarket(t, _db, _ => null);
            t.AddStock("goblin_ear", 20);
            market.Sell("goblin_ear", 20);
            Assert.AreEqual(20f, market.Sold("goblin_ear"), 1e-4f);
            Assert.Less(MerchantMarket.SaturationFor(20f), 1f);

            EventBus.Publish(new DayPassedEvent { Day = 2 });
            Assert.AreEqual(20f * MerchantMarket.DailyRetention, market.Sold("goblin_ear"), 1e-3f);
        }
    }
}
