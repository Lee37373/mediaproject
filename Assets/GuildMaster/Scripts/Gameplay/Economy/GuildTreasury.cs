using System.Collections.Generic;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Economy
{
    public struct TreasuryChangedEvent { public int Gold; public int Exp; }
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
        /// <summary>Placing a building, and the half refunded on demolishing it (P4-13).</summary>
        public const string Build = "build";
        /// <summary>Repairing a gate (P4-39) or a building (P4-74).</summary>
        public const string Repair = "repair";
        /// <summary>The recruitment notice (P4-38), and its refund.</summary>
        public const string Recruit = "recruit";
    }

    /// <summary>
    /// The player's guild funds: gold, the hourly EXP pool used for quest rewards,
    /// and the stockpile of materials/goods. All mutations publish change events.
    /// </summary>
    public class GuildTreasury : ISaveable
    {
        readonly Dictionary<string, int> _stock = new();

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SaveData
        {
            public int gold;
            public int exp;
            public float exp_per_hour;
            public int daily_tax;
            public float tax_cut_today;
            public float exp_fraction;
            public List<Data.ItemStack> stock = new();
        }

        public string SaveKey => "treasury";

        public string Capture()
        {
            var s = new SaveData
            {
                gold = Gold, exp = Exp, exp_per_hour = ExpPerHour, daily_tax = DailyTax,
                tax_cut_today = TaxCutToday, exp_fraction = _expFraction,
            };
            var ids = new List<string>(_stock.Keys);
            ids.Sort(System.StringComparer.Ordinal); // the same order every time, whatever was added first
            foreach (var id in ids) s.stock.Add(new Data.ItemStack { itemId = id, count = _stock[id] });
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            Gold = s.gold;
            Exp = s.exp;
            ExpPerHour = s.exp_per_hour;
            DailyTax = s.daily_tax;
            TaxCutToday = s.tax_cut_today;
            _expFraction = s.exp_fraction;
            _stock.Clear();
            foreach (var st in s.stock) _stock[st.itemId] = st.count;
            Notify();
        }

        public int Gold { get; private set; }
        public int Exp { get; private set; }
        public IReadOnlyDictionary<string, int> Stock => _stock;

        /// <summary>EXP generated per game hour (modified by buildings in later phases).</summary>
        public float ExpPerHour { get; set; }
        public int DailyTax { get; set; }

        /// <summary>Share of today's tax lost (P4-72: a disaster at the walls); reset when the tax comes in.</summary>
        public float TaxCutToday { get; private set; }

        /// <summary>Today's tax is cut by this share (the largest cut of the day holds).</summary>
        public void CutTodaysTax(float share) => TaxCutToday = Mathf.Clamp01(Mathf.Max(TaxCutToday, share));

        float _expFraction;

        public GuildTreasury(int gold, int exp, float expPerHour, int dailyTax)
        {
            Gold = gold;
            Exp = exp;
            ExpPerHour = expPerHour;
            DailyTax = dailyTax;
            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<DayPassedEvent>(OnDay);
        }

        /// <summary>
        /// The hourly EXP is the guild hall's: none comes while it lies in ruins, until it is stood up again
        /// (P4-78, user decision 2026-10-10 — as crafting and the training ground stop, P4-74·76).
        /// Without a town (EditMode tests) it always flows.
        /// </summary>
        public bool ExpStopped
            => Services.TryGet<Town.TownService>(out var town) && town.Get(Town.BuildingType.GuildHall) is { IsDestroyed: true };

        void OnHour(HourPassedEvent e)
        {
            if (ExpStopped) return;
            _expFraction += ExpPerHour;
            int whole = Mathf.FloorToInt(_expFraction);
            if (whole <= 0) return;
            _expFraction -= whole;
            AddExp(whole);
        }

        void OnDay(DayPassedEvent e)
        {
            int tax = Mathf.RoundToInt(DailyTax * (1f - TaxCutToday));
            TaxCutToday = 0f;
            if (tax > 0) AddGold(tax, GoldReason.Tax);
        }

        public bool CanAfford(int gold, int exp = 0) => Gold >= gold && Exp >= exp;

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

        public void AddExp(int amount)
        {
            if (amount == 0) return;
            Exp += amount;
            Notify();
        }

        public bool TrySpend(int gold, int exp, string reason = null)
        {
            if (gold < 0 || exp < 0 || !CanAfford(gold, exp)) return false;
            Gold -= gold;
            Exp -= exp;
            Notify();
            if (gold > 0) EventBus.Publish(new GoldFlowEvent { Amount = -gold, Reason = reason });
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

        void Notify() => EventBus.Publish(new TreasuryChangedEvent { Gold = Gold, Exp = Exp });
    }
}
