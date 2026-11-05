using System;
using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Notifications;
using UnityEngine;

namespace GuildMaster.Economy
{
    /// <summary>
    /// The guild's food (user decision 2026-10-29: "식량 비축 + 구매"). Each member eats one portion at midnight; the guild
    /// buys portions from the bakery. Members still pay for their meals (P4-68), so food is the guild's cost of those
    /// meals. With nothing left, the ones who go without lose heart and strength. Pure rules, covered by tests.
    /// </summary>
    public static class FoodRules
    {
        /// <summary>Gold per portion at the bakery (a meal costs a member 10G, so the guild keeps a little).</summary>
        public const int PortionPrice = 6;
        /// <summary>Portions bought at once with the bigger button cost a little less each.</summary>
        public const int BulkSize = 30, BulkPrice = 160;
        public const int SmallSize = 10;
        /// <summary>What the guild starts with: three days for the starting four.</summary>
        public const int StartingFood = 12;
        /// <summary>A warning once there is less than this many days' food left.</summary>
        public const int WarnDays = 2;
        public const float HungerStress = 8f, HungerFatigue = 10f;

        public static int Price(int portions) => portions == BulkSize ? BulkPrice : portions * PortionPrice;

        /// <summary>One night's meals: how many eat and how many go without.</summary>
        public static (int eaten, int hungry) Eat(int food, int eaters)
        {
            int eaten = Mathf.Min(Mathf.Max(0, food), Mathf.Max(0, eaters));
            return (eaten, Mathf.Max(0, eaters) - eaten);
        }

        /// <summary>Whole days the food lasts for this many members.</summary>
        public static int DaysLeft(int food, int eaters) => eaters <= 0 ? 99 : Mathf.Max(0, food) / eaters;
    }

    /// <summary>Raised after the night's meals.</summary>
    public struct FoodEatenEvent { public int Eaten; public int Hungry; public int Left; }

    /// <summary>Keeps the guild's food: eaten at midnight, bought from the bakery (the 살림 window). Saved as "food" (P4-119).</summary>
    public class FoodStore : IDisposable, ISaveable
    {
        readonly GuildRoster _roster;
        readonly GuildTreasury _treasury;

        public int Food { get; private set; }
        public int Eaters => _roster != null ? _roster.Members.Count : 0;
        public int DaysLeft => FoodRules.DaysLeft(Food, Eaters);
        /// <summary>Members who went without since the game began.</summary>
        public int HungryNights { get; private set; }

        public FoodStore(GuildRoster roster, GuildTreasury treasury, int food = FoodRules.StartingFood)
        {
            _roster = roster;
            _treasury = treasury;
            Food = Mathf.Max(0, food);
            EventBus.Subscribe<DayPassedEvent>(OnDay);
        }

        public void Dispose() => EventBus.Unsubscribe<DayPassedEvent>(OnDay);

        /// <summary>Buys portions with guild gold; false when there is not enough.</summary>
        public bool TryBuy(int portions)
        {
            if (portions <= 0) return false;
            if (!_treasury.TrySpendGold(FoodRules.Price(portions), GoldReason.Food)) return false;
            Food += portions;
            return true;
        }

        /// <summary>For tests and debug: sets the stock.</summary>
        public void SetFood(int food) => Food = Mathf.Max(0, food);

        void OnDay(DayPassedEvent e) => EatTonight();

        /// <summary>One night's meals (public for tests and debug).</summary>
        public void EatTonight()
        {
            var members = new List<Adventurer>();
            if (_roster != null) foreach (var m in _roster.Members) if (m != null && m.IsAlive) members.Add(m);
            var (eaten, hungry) = FoodRules.Eat(Food, members.Count);
            Food -= eaten;
            if (hungry > 0)
            {
                // The last ones in the list go without (the newest members, as the roster is in joining order).
                Services.TryGet<Welfare.GuildWelfare>(out var welfare);
                float hour = Services.TryGet<GameClock>(out var clock) ? clock.TotalHours : 0f;
                for (int i = members.Count - hungry; i < members.Count; i++)
                {
                    var a = members[i];
                    if (welfare != null) welfare.AdjustStress(a, FoodRules.HungerStress);
                    else a.Data.stress = Mathf.Clamp(a.Data.stress + FoodRules.HungerStress, 0f, 100f);
                    a.Data.fatigue = Mathf.Clamp(a.Data.fatigue + FoodRules.HungerFatigue, 0f, 100f);
                    AdventurerMemory.Add(a.Data.memories, hour, 1, "길드에 식량이 떨어져 저녁을 굶었다", "hungry");
                }
                HungryNights += hungry;
                Post(NotificationKind.Warning, "식량 부족", $"식량이 떨어져 길드원 {hungry}명이 굶었습니다. 살림(K) 창에서 식량을 사 두세요.");
            }
            else if (members.Count > 0 && Food < members.Count * FoodRules.WarnDays)
                Post(NotificationKind.Warning, "식량", $"식량이 {Food}개 남았습니다({FoodRules.DaysLeft(Food, members.Count)}일치). 살림(K) 창에서 사 둘 수 있습니다.");
            EventBus.Publish(new FoodEatenEvent { Eaten = eaten, Hungry = hungry, Left = Food });
        }

        static void Post(NotificationKind kind, string title, string text)
        {
            if (Services.TryGet<NotificationCenter>(out var center)) center.Post(kind, title, text, null);
        }

        // ---------------- Save (P4-119) ----------------

        [Serializable]
        class SaveData
        {
            public int food;
            public int hungry_nights;
        }

        public string SaveKey => "food";

        public string Capture() => JsonUtility.ToJson(new SaveData { food = Food, hungry_nights = HungryNights });

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            Food = Mathf.Max(0, s.food);
            HungryNights = Mathf.Max(0, s.hungry_nights);
        }
    }
}
