using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Data;
using UnityEngine;

namespace GuildMaster.Economy
{
    /// <summary>The guild sold stock to the travelling merchant (P4-3).</summary>
    public struct MerchantSaleEvent { public string ItemId; public int Count; public int Gold; }

    /// <summary>
    /// Outside market reached through the travelling merchant (P4-3). The guild sells materials
    /// and goods at base value × <see cref="SaleRate"/> × demand.
    ///
    /// Demand = scarcity × saturation:
    /// - scarcity follows the real abundance of the species that drop the item (the merchant comes
    ///   from outside, so it is not bound by the guild's information rule, and its prices double as
    ///   a hint: loot of a vanishing species fetches a lot);
    /// - saturation drops as the guild sells the same item, and recovers every game day.
    /// Goods without a source species (potions, gear) only have saturation.
    /// </summary>
    public class MerchantMarket
    {
        public const float SaleRate = 0.8f;
        /// <summary>Scarcity multiplier when no source species is left.</summary>
        public const float RareDemand = 1.5f;
        /// <summary>Scarcity multiplier when source species fill their nests.</summary>
        public const float CommonDemand = 0.7f;
        /// <summary>Units sold at which saturation is 0.75.</summary>
        public const float HalfSold = 10f;
        /// <summary>Fraction of sold volume the market still remembers after each game day.</summary>
        public const float DailyRetention = 0.75f;

        readonly GuildTreasury _treasury;
        readonly GameDatabase _db;
        readonly System.Func<ItemDefinition, float?> _abundance;
        readonly Dictionary<string, float> _sold = new();

        /// <param name="abundance">0..1 population fill of the species dropping an item, or null if no species drops it.</param>
        public MerchantMarket(GuildTreasury treasury, GameDatabase db, System.Func<ItemDefinition, float?> abundance)
        {
            _treasury = treasury;
            _db = db;
            _abundance = abundance;
            EventBus.Subscribe<DayPassedEvent>(OnDay);
        }

        void OnDay(DayPassedEvent e)
        {
            var keys = new List<string>(_sold.Keys);
            foreach (var k in keys)
            {
                float v = _sold[k] * DailyRetention;
                if (v < 0.05f) _sold.Remove(k);
                else _sold[k] = v;
            }
        }

        public float Sold(string itemId) => itemId != null && _sold.TryGetValue(itemId, out var s) ? s : 0f;

        /// <summary>Save (P5-4): what the merchants have bought lately, item by item.</summary>
        internal Dictionary<string, float> SoldTable => _sold;

        /// <summary>Scarcity part of demand: 1 for goods, <see cref="RareDemand"/>..<see cref="CommonDemand"/> for loot.</summary>
        public float Scarcity(ItemDefinition item)
        {
            var a = item != null ? _abundance?.Invoke(item) : null;
            return a.HasValue ? ScarcityFor(a.Value) : 1f;
        }

        public static float ScarcityFor(float abundance) => Mathf.Lerp(RareDemand, CommonDemand, Mathf.Clamp01(abundance));

        public static float SaturationFor(float sold) => 0.5f + 0.5f * HalfSold / (HalfSold + Mathf.Max(0f, sold));

        public float Demand(ItemDefinition item) => item == null ? 1f : Scarcity(item) * SaturationFor(Sold(item.id));

        /// <summary>Korean label for the UI ("수요 많음" etc.).</summary>
        public static string DemandLabel(float demand)
            => demand >= 1.15f ? "수요 많음" : demand >= 0.9f ? "수요 보통" : "수요 적음";

        public bool CanSell(ItemDefinition item) => item != null && item.baseValue > 0;

        int UnitPrice(ItemDefinition item, float sold)
            => Mathf.Max(1, Mathf.RoundToInt(item.baseValue * SaleRate * Scarcity(item) * SaturationFor(sold)));

        /// <summary>Current price for one unit.</summary>
        public int Price(ItemDefinition item) => item == null ? 0 : UnitPrice(item, Sold(item.id));

        /// <summary>Total for selling <paramref name="count"/> units now (each sold unit lowers the next price).</summary>
        public int Quote(ItemDefinition item, int count)
        {
            if (!CanSell(item) || count <= 0) return 0;
            float sold = Sold(item.id);
            int total = 0;
            for (int i = 0; i < count; i++, sold += 1f) total += UnitPrice(item, sold);
            return total;
        }

        /// <summary>Sells up to <paramref name="count"/> units from the guild stock. Returns gold earned.</summary>
        public int Sell(string itemId, int count)
        {
            var item = _db.Item(itemId);
            count = Mathf.Min(count, _treasury.GetStock(itemId));
            if (!CanSell(item) || count <= 0) return 0;
            int gold = Quote(item, count);
            if (!_treasury.TryRemoveStock(itemId, count)) return 0;
            _sold[itemId] = Sold(itemId) + count;
            _treasury.AddGold(gold, GoldReason.Merchant);
            EventBus.Publish(new MerchantSaleEvent { ItemId = itemId, Count = count, Gold = gold });
            return gold;
        }
    }
}
