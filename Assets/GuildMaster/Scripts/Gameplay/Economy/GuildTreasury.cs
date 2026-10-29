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
        /// <summary>A client paying for a finished quest, the guild's cut included (2026-10-29).</summary>
        public const string Commission = "commission";
        /// <summary>Money taken from a member by the guild master (2026-10-29).</summary>
        public const string Tribute = "tribute";
        /// <summary>The guild master's odd jobs for the townsfolk (2026-10-29).</summary>
        public const string SideJob = "sidejob";
        /// <summary>Food bought for the guild (2026-10-29).</summary>
        public const string Food = "food";
    }

    /// <summary>
    /// The player's guild funds: gold, the hourly EXP pool used for quest rewards,
    /// and the stockpile of materials/goods. All mutations publish change events.
    /// </summary>
    public class GuildTreasury
    {
        readonly Dictionary<string, int> _stock = new();

        public int Gold { get; private set; }
        public int Exp { get; private set; }
        public IReadOnlyDictionary<string, int> Stock => _stock;

        /// <summary>EXP generated per game hour (modified by buildings in later phases).</summary>
        public float ExpPerHour { get; set; }
        public int DailyTax { get; set; }

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

        void OnHour(HourPassedEvent e)
        {
            _expFraction += ExpPerHour;
            int whole = Mathf.FloorToInt(_expFraction);
            if (whole <= 0) return;
            _expFraction -= whole;
            AddExp(whole);
        }

        void OnDay(DayPassedEvent e)
        {
            if (DailyTax > 0) AddGold(DailyTax, GoldReason.Tax);
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
