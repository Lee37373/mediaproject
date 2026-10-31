using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Data;
using UnityEngine;

namespace GuildMaster.Economy
{
    public struct TradeEvent
    {
        public string TraderName;
        public string ItemId;
        public int Count;
        public int Gold;
        public bool GuildBought; // true: guild bought loot, false: guild sold goods
    }

    /// <summary>A party able to trade with the guild (adventurers).</summary>
    public interface ITrader
    {
        string TraderName { get; }
        int Gold { get; set; }
        List<ItemStack> CarriedLoot { get; }
    }

    /// <summary>
    /// Guild-side trading rules:
    /// - buys adventurers' loot with guild gold (player-set buy rate),
    /// - sells goods (potions, gear) from the guild stockpile to adventurers.
    /// </summary>
    public class TradeService : ISaveable
    {
        readonly GuildTreasury _treasury;
        readonly GameDatabase _db;

        // ---------------- Save ----------------

        [System.Serializable]
        class SaveData
        {
            public float loot_buy_rate;
        }

        public string SaveKey => "trade";

        /// <summary>The loot buy rate the guild master set in the stock window (the goods rate is the balance's).</summary>
        public string Capture() => JsonUtility.ToJson(new SaveData { loot_buy_rate = LootBuyRate });

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            if (s.loot_buy_rate > 0f) LootBuyRate = s.loot_buy_rate;
        }

        /// <summary>Multiplier on base value the guild pays for loot (player-controlled).</summary>
        public float LootBuyRate { get; set; } = 1f;
        /// <summary>Multiplier on base value adventurers pay for guild goods.</summary>
        public float GoodsSellRate { get; set; } = 1.2f;

        public TradeService(GuildTreasury treasury, GameDatabase db)
        {
            _treasury = treasury;
            _db = db;
        }

        public int LootPrice(ItemDefinition item) => Mathf.Max(1, Mathf.RoundToInt(item.baseValue * LootBuyRate));
        /// <summary>What the guild pays for one: its price, or the finder's fee when the guild's rule makes loot the guild's (P4-112).</summary>
        public int LootPay(ItemDefinition item) => Mathf.Max(1, Mathf.RoundToInt(LootPrice(item) * Guild.GuildLaws.LootShare));
        /// <summary>The guild shop's price, after the shop rule (P4-112).</summary>
        public int GoodsPrice(ItemDefinition item) => Mathf.Max(1, Mathf.RoundToInt(item.baseValue * GoodsSellRate * Guild.GuildLaws.ShopFactor));

        /// <summary>Buys as much carried loot as the guild can afford. Returns gold paid.</summary>
        public int BuyLoot(ITrader seller)
        {
            int paid = 0;
            var loot = seller.CarriedLoot;
            for (int i = loot.Count - 1; i >= 0; i--)
            {
                var stack = loot[i];
                var item = _db.Item(stack.itemId);
                if (item == null) { loot.RemoveAt(i); continue; }
                int price = LootPay(item);
                int affordable = Mathf.Min(stack.count, _treasury.Gold / price);
                if (affordable <= 0) continue;
                int total = affordable * price;
                if (!_treasury.TrySpendGold(total, GoldReason.Loot)) continue;
                seller.Gold += total;
                _treasury.AddStock(item.id, affordable);
                paid += total;
                stack.count -= affordable;
                if (stack.count <= 0) loot.RemoveAt(i);
                else loot[i] = stack;
                EventBus.Publish(new TradeEvent { TraderName = seller.TraderName, ItemId = item.id, Count = affordable, Gold = total, GuildBought = true });
            }
            return paid;
        }

        /// <summary>Sells goods to an adventurer if in stock and affordable.</summary>
        public bool SellGoods(ITrader buyer, string itemId, int count = 1)
        {
            var item = _db.Item(itemId);
            if (item == null || count <= 0) return false;
            int total = GoodsPrice(item) * count;
            if (buyer.Gold < total || _treasury.GetStock(itemId) < count) return false;
            _treasury.TryRemoveStock(itemId, count);
            buyer.Gold -= total;
            _treasury.AddGold(total, GoldReason.Goods);
            EventBus.Publish(new TradeEvent { TraderName = buyer.TraderName, ItemId = itemId, Count = count, Gold = total, GuildBought = false });
            return true;
        }
    }
}
