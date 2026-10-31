using System;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.World;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// One day of the members' keep (P4-68), published at midnight right after the meals are charged.
    /// Meals, Lodging and Repaid are gold the guild received; NewDebt is what went on the slate that
    /// day, Outstanding what all members owe now.
    /// </summary>
    public struct LivingCostsDayEvent
    {
        public int Day;
        public int Meals;
        public int Lodging;
        public int Repaid;
        public int NewDebt;
        public int Outstanding;
        public int Received => Meals + Lodging + Repaid;
    }

    /// <summary>
    /// Charges the members' keep and hands it to the guild (P4-68, user decision 2026-09-27). Quest
    /// rewards and loot sales left the treasury for the members' purses and barely came back (5-day
    /// run: guild 800 → 36G, members 350–411G each), so meals and lodging close the loop and bring
    /// back the money pressure the decision prompt reads. Meals are charged once a day at midnight
    /// wherever a member is; lodging every game hour a member rests in town (<see cref="RestState"/>);
    /// a camp in the wild costs nothing. The rules are <see cref="LivingCostRules"/>.
    /// </summary>
    public class LivingCosts : IDisposable, ISaveable
    {
        readonly GuildRoster _roster;
        readonly GuildTreasury _treasury;
        readonly WorldService _world;

        public int MealGold { get; }
        public int LodgingGoldPerHour { get; }

        // The day being tallied for LivingCostsDayEvent.
        int _meals, _lodging, _repaid, _newDebt;

        /// <summary>All gold received since the game began (balance runs).</summary>
        public int TotalReceived { get; private set; }

        // ---------------- Save (P5-4) ----------------

        [Serializable]
        class SaveData
        {
            public int meals, lodging, repaid, new_debt, total;
        }

        public string SaveKey => "living";

        /// <summary>The day's tally so far (each member's debt is in its own data).</summary>
        public string Capture() => UnityEngine.JsonUtility.ToJson(new SaveData
        {
            meals = _meals, lodging = _lodging, repaid = _repaid, new_debt = _newDebt, total = TotalReceived,
        });

        public void Restore(string json, SaveContext ctx)
        {
            var s = UnityEngine.JsonUtility.FromJson<SaveData>(json);
            _meals = s.meals;
            _lodging = s.lodging;
            _repaid = s.repaid;
            _newDebt = s.new_debt;
            TotalReceived = s.total;
        }

        public LivingCosts(GuildRoster roster, GuildTreasury treasury, WorldService world, int mealGold, int lodgingGoldPerHour)
        {
            _roster = roster;
            _treasury = treasury;
            _world = world;
            MealGold = mealGold;
            LodgingGoldPerHour = lodgingGoldPerHour;
            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<DayPassedEvent>(OnDay);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            EventBus.Unsubscribe<DayPassedEvent>(OnDay);
        }

        /// <summary>What all members owe right now.</summary>
        public int Outstanding
        {
            get
            {
                int sum = 0;
                foreach (var a in _roster.Members) if (a != null) sum += a.Data.debt;
                return sum;
            }
        }

        /// <summary>Resting at the guild in town: the hours that pay for a bed.</summary>
        public bool IsLodging(Adventurer a)
            => a != null && a.IsAlive && a.Fsm.Current == a.RestState && _world.Grid.IsInTown(a.Position);

        void OnHour(HourPassedEvent e)
        {
            // The guild's rule may keep its members at its own cost (P4-112): nothing is charged, and nothing comes in.
            bool charged = !Guild.GuildLaws.GuildPaysUpkeep;
            foreach (var a in _roster.Members)
            {
                if (a == null || !a.IsAlive) continue;
                if (charged && IsLodging(a)) _lodging += Charge(a, LodgingGoldPerHour);
                // Catches gold that arrived by a path that does not settle on its own (a bonus, say).
                Settle(a);
            }
        }

        void OnDay(DayPassedEvent e)
        {
            int meal = UnityEngine.Mathf.RoundToInt(MealGold * Seasons.SeasonService.Meal); // dearer in winter (P4-111b)
            if (!Guild.GuildLaws.GuildPaysUpkeep)
                foreach (var a in _roster.Members)
                    if (a != null && a.IsAlive) _meals += Charge(a, meal);

            EventBus.Publish(new LivingCostsDayEvent
            {
                Day = e.Day - 1,
                Meals = _meals,
                Lodging = _lodging,
                Repaid = _repaid,
                NewDebt = _newDebt,
                Outstanding = Outstanding,
            });
            _meals = _lodging = _repaid = _newDebt = 0;
        }

        /// <summary>Charges one member; what the purse cannot cover goes on the slate. Returns gold paid.</summary>
        public int Charge(Adventurer a, int amount)
        {
            var d = a.Data;
            int gold = d.gold, debt = d.debt;
            int paid = LivingCostRules.Charge(ref gold, ref debt, amount);
            _newDebt += debt - d.debt;
            d.gold = gold;
            d.debt = debt;
            Receive(paid);
            return paid;
        }

        /// <summary>
        /// Clears a member's debt from their purse. Called where gold comes in (loot sold, quest
        /// turned in, party share paid) so it is paid before anything is bought, and hourly as a net.
        /// </summary>
        public int Settle(Adventurer a)
        {
            if (a == null || a.Data.debt <= 0 || a.Data.gold <= 0) return 0;
            var d = a.Data;
            int gold = d.gold, debt = d.debt;
            int paid = LivingCostRules.Repay(ref gold, ref debt);
            d.gold = gold;
            d.debt = debt;
            _repaid += paid;
            Receive(paid);
            return paid;
        }

        void Receive(int gold)
        {
            if (gold <= 0) return;
            TotalReceived += gold;
            _treasury.AddGold(gold, GoldReason.Living);
        }
    }
}
