using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;

namespace GuildMaster.Adventurers
{
    public struct AdventurerGearUpgradedEvent { public Adventurer Adventurer; public int Tier; public int Price; }

    /// <summary>
    /// Adventurers buying gear from the guild stock (P4-4): only the next tier, only when it is in
    /// stock and affordable. Buying raises <see cref="AdventurerData.gearTier"/> and the stats.
    /// </summary>
    public static class GearShopping
    {
        public static string GearItemId(int tier) => $"gear_t{tier}";

        /// <summary>The next-tier gear item, or null at the top tier.</summary>
        public static ItemDefinition NextGear(AdventurerData d, GameDatabase db)
            => db?.Item(GearItemId(d.gearTier + 1));

        /// <summary>True when the guild has the next tier in stock and the adventurer can pay for it.</summary>
        public static bool CanBuy(Adventurer a)
        {
            if (!Services.TryGet<GameDatabase>(out var db) || !Services.TryGet<GuildTreasury>(out var t)
                || !Services.TryGet<TradeService>(out var trade)) return false;
            var item = NextGear(a.Data, db);
            return item != null && t.GetStock(item.id) > 0 && a.Data.gold >= trade.GoodsPrice(item);
        }

        /// <summary>Buys and equips the next tier if possible. Returns true on purchase.</summary>
        public static bool TryBuy(Adventurer a)
        {
            if (!CanBuy(a)) return false;
            var db = Services.Get<GameDatabase>();
            var trade = Services.Get<TradeService>();
            var item = NextGear(a.Data, db);
            int price = trade.GoodsPrice(item);
            if (!trade.SellGoods(a, item.id)) return false;
            a.Data.gearTier = item.gearTier;
            a.RecalculateStats();
            a.Say($"{item.displayName}를 샀다! 한결 든든하군.");
            EventBus.Publish(new AdventurerGearUpgradedEvent { Adventurer = a, Tier = item.gearTier, Price = price });
            return true;
        }
    }
}
