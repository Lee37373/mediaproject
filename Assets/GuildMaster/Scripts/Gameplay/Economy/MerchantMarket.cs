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
    /// Demand follows hunting pressure the guild itself can know (GuildIntel rule): loot the guild
    /// bought from adventurers and stock it sold to the merchant add "supply" to that item, which
    /// makes it cheaper; supply fades every game day, so rarely hunted species' loot fetches more.
    /// </summary>
    public class MerchantMarket
    {
        public const float SaleRate = 0.8f;
        public const float MaxDemand = 1.3f;
        public const float MinDemand = 0.6f;
        /// <summary>Supply at which demand sits halfway between max and min.</summary>
        public const float HalfSupply = 10f;
        /// <summary>Fraction of supply left after each game day.</summary>
        public const float DailyRetention = 0.75f;

        readonly GuildTreasury _treasury;
        readonly GameDatabase _db;
        readonly Dictionary<string, float> _supply = new();

        public MerchantMarket(GuildTreasury treasury, GameDatabase db)
        {
            _treasury = treasury;
            _db = db;
            EventBus.Subscribe<TradeEvent>(OnTrade);
            EventBus.Subscribe<DayPassedEvent>(OnDay);
        }

        void OnTrade(TradeEvent e)
        {
            if (e.GuildBought) AddSupply(e.ItemId, e.Count);
        }

        void OnDay(DayPassedEvent e)
        {
            var keys = new List<string>(_supply.Keys);
            foreach (var k in keys)
            {
                float v = _supply[k] * DailyRetention;
                if (v < 0.05f) _supply.Remove(k);
                else _supply[k] = v;
            }
        }

        public void AddSupply(string itemId, float amount)
        {
            if (string.IsNullOrEmpty(itemId) || amount <= 0f) return;
            _supply[itemId] = Supply(itemId) + amount;
        }

        public float Supply(string itemId) => itemId != null && _supply.TryGetValue(itemId, out var s) ? s : 0f;

        /// <summary>Demand multiplier: <see cref="MaxDemand"/> with no supply, approaching <see cref="MinDemand"/>.</summary>
        public float Demand(string itemId) => DemandFor(Supply(itemId));

        public static float DemandFor(float supply)
            => MinDemand + (MaxDemand - MinDemand) * HalfSupply / (HalfSupply + Mathf.Max(0f, supply));

        /// <summary>Korean label for the UI ("수요 많음" etc.).</summary>
        public static string DemandLabel(float demand)
            => demand >= 1.15f ? "수요 많음" : demand >= 0.9f ? "수요 보통" : "수요 적음";

        public bool CanSell(ItemDefinition item) => item != null && item.baseValue > 0;

        /// <summary>Current price for one unit.</summary>
        public int Price(ItemDefinition item)
            => item == null ? 0 : Mathf.Max(1, Mathf.RoundToInt(item.baseValue * SaleRate * Demand(item.id)));

        /// <summary>Total for selling <paramref name="count"/> units now (each sold unit lowers the next price).</summary>
        public int Quote(ItemDefinition item, int count)
        {
            if (!CanSell(item) || count <= 0) return 0;
            float supply = Supply(item.id);
            int total = 0;
            for (int i = 0; i < count; i++, supply += 1f)
                total += Mathf.Max(1, Mathf.RoundToInt(item.baseValue * SaleRate * DemandFor(supply)));
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
            AddSupply(itemId, count);
            _treasury.AddGold(gold);
            EventBus.Publish(new MerchantSaleEvent { ItemId = itemId, Count = count, Gold = gold });
            return gold;
        }
    }
}
