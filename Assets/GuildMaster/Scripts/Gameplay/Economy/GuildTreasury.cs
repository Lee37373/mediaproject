using System.Collections.Generic;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Economy
{
    public struct TreasuryChangedEvent { public int Gold; }
    public struct InventoryChangedEvent { public string ItemId; public int Count; }

    /// <summary>
    /// One movement of gold (positive = in, negative = out) and what it was for, so the daily report (F-3)
    /// can tell income and spending apart. Reason null = the quest board's escrow and refunds.
    /// </summary>
    public struct GoldFlowEvent { public int Amount; public string Reason; }

    /// <summary>Reasons passed to <see cref="GuildTreasury"/> when gold moves.</summary>
    public static class GoldReason
    {
        public const string Tax = "tax";
        public const string Goods = "goods";
        public const string Loot = "loot";
        public const string Merchant = "merchant";
        public const string Craft = "craft";
        public const string Upgrade = "upgrade";
        /// <summary>A bonus paid to a guild member (F-2).</summary>
        public const string Bonus = "bonus";
        /// <summary>A memorial for a fallen member (F-6).</summary>
        public const string Memorial = "memorial";
        /// <summary>Treating a badly wounded member (F-6).</summary>
        public const string Medical = "medical";
        /// <summary>Members' meals and lodging, and debts for them repaid (P4-68).</summary>
        public const string Living = "living";
        /// <summary>Placing a building, and the half refunded on demolishing it (P4-13); pushing the town walls out (P4-86).</summary>
        public const string Build = "build";
        /// <summary>Repairing a gate (P4-39) or a building (P4-74).</summary>
        public const string Repair = "repair";
        /// <summary>The recruitment notice (P4-38), and its refund.</summary>
        public const string Recruit = "recruit";
        /// <summary>The guild's fee on a client's quest (P4-82).</summary>
        public const string Commission = "commission";
        /// <summary>The town's grant for a new guild rank (P4-106).</summary>
        public const string Grant = "grant";
        /// <summary>What a decision card cost or brought in (P4-111c): the report's "other".</summary>
        public const string Decision = "decision";
        /// <summary>Money taken from a member by the guild master (2026-10-29).</summary>
        public const string Tribute = "tribute";
        /// <summary>The guild master's odd jobs for the townsfolk (2026-10-29).</summary>
        public const string SideJob = "sidejob";
        /// <summary>Food bought for the guild (2026-10-29).</summary>
        public const string Food = "food";
        /// <summary>A resident's thanks for a request (P4-134), counted with the odd jobs.</summary>
        public const string Favor = "favor";
    }

    /// <summary>
    /// The player's guild funds: gold and the stockpile of materials/goods. All mutations publish change
    /// events. P4-82 (user decision 2026-10-17): there is no guild EXP any more — quest EXP comes from the
    /// clients, and members train in town at the training ground.
    /// </summary>
    public class GuildTreasury : ISaveable
    {
        readonly Dictionary<string, int> _stock = new();

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SaveData
        {
            public int gold;
            public int daily_tax;
            public float tax_cut_today;
            public List<Data.ItemStack> stock = new();
        }

        public string SaveKey => "treasury";

        public string Capture()
        {
            var s = new SaveData { gold = Gold, daily_tax = DailyTax, tax_cut_today = TaxCutToday };
            var ids = new List<string>(_stock.Keys);
            ids.Sort(System.StringComparer.Ordinal); // the same order every time, whatever was added first
            foreach (var id in ids) s.stock.Add(new Data.ItemStack { itemId = id, count = _stock[id] });
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            Gold = s.gold;
            DailyTax = s.daily_tax;
            TaxCutToday = s.tax_cut_today;
            _stock.Clear();
            foreach (var st in s.stock) _stock[st.itemId] = st.count;
            Notify();
        }

        public int Gold { get; private set; }
        public IReadOnlyDictionary<string, int> Stock => _stock;

        public int DailyTax { get; set; }

        /// <summary>Share of today's tax lost (P4-72: a disaster at the walls); reset when the tax comes in.</summary>
        public float TaxCutToday { get; private set; }

        /// <summary>Today's tax is cut by this share (the largest cut of the day holds).</summary>
        public void CutTodaysTax(float share) => TaxCutToday = Mathf.Clamp01(Mathf.Max(TaxCutToday, share));

        public GuildTreasury(int gold, int dailyTax)
        {
            Gold = gold;
            DailyTax = dailyTax;
            EventBus.Subscribe<DayPassedEvent>(OnDay);
        }

        void OnDay(DayPassedEvent e)
        {
            // The chief's favour (P4-134): +10% a step.
            int tax = Mathf.RoundToInt(DailyTax * (1f - TaxCutToday) * (1f + Social.ResidentPerks.TaxBonus));
            TaxCutToday = 0f;
            if (tax > 0) AddGold(tax, GoldReason.Tax);
        }

        public bool CanAfford(int gold) => Gold >= gold;

        public void AddGold(int amount, string reason = null)
        {
            if (amount == 0) return;
            Gold += amount;
            Notify();
            EventBus.Publish(new GoldFlowEvent { Amount = amount, Reason = reason });
        }

        public bool TrySpendGold(int amount, string reason = null)
        {
            if (amount < 0 || Gold < amount) return false;
            Gold -= amount;
            Notify();
            if (amount > 0) EventBus.Publish(new GoldFlowEvent { Amount = -amount, Reason = reason });
            return true;
        }

        public int GetStock(string itemId) => itemId != null && _stock.TryGetValue(itemId, out var n) ? n : 0;

        public void AddStock(string itemId, int count)
        {
            if (count <= 0) return;
            _stock[itemId] = GetStock(itemId) + count;
            EventBus.Publish(new InventoryChangedEvent { ItemId = itemId, Count = _stock[itemId] });
        }

        public bool TryRemoveStock(string itemId, int count)
        {
            int have = GetStock(itemId);
            if (count <= 0 || have < count) return false;
            _stock[itemId] = have - count;
            EventBus.Publish(new InventoryChangedEvent { ItemId = itemId, Count = _stock[itemId] });
            return true;
        }

        void Notify() => EventBus.Publish(new TreasuryChangedEvent { Gold = Gold });
    }
}
