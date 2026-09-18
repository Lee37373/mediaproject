using System.Collections.Generic;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Economy
{
    public struct TreasuryChangedEvent { public int Gold; public int Exp; }
    public struct InventoryChangedEvent { public string ItemId; public int Count; }

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
            if (DailyTax > 0) AddGold(DailyTax);
        }

        public bool CanAfford(int gold, int exp = 0) => Gold >= gold && Exp >= exp;

        public void AddGold(int amount)
        {
            if (amount == 0) return;
            Gold += amount;
            Notify();
        }

        public bool TrySpendGold(int amount)
        {
            if (amount < 0 || Gold < amount) return false;
            Gold -= amount;
            Notify();
            return true;
        }

        public void AddExp(int amount)
        {
            if (amount == 0) return;
            Exp += amount;
            Notify();
        }

        public bool TrySpend(int gold, int exp)
        {
            if (gold < 0 || exp < 0 || !CanAfford(gold, exp)) return false;
            Gold -= gold;
            Exp -= exp;
            Notify();
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
