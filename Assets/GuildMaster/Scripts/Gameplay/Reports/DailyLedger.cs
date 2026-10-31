using System;
using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.Quests;

namespace GuildMaster.Reports
{
    /// <summary>Published when a game day ends and its report is final.</summary>
    public struct DailyReportClosedEvent { public DailyReport Report; }

    /// <summary>
    /// The guild's daily accounts (F-3). Listens to the treasury and to gameplay events, keeps one
    /// <see cref="DailyReport"/> per game day, and closes it when the next day begins. Plain logic with no
    /// Unity objects, so it is covered by EditMode tests; the report window and the "정산" notification read from it.
    /// </summary>
    public class DailyLedger : IDisposable, ISaveable
    {
        /// <summary>Older closed reports are dropped beyond this many.</summary>
        public const int MaxReports = 30;

        readonly GuildTreasury _treasury;
        readonly List<DailyReport> _reports = new();

        /// <summary>Closed days, oldest first.</summary>
        public IReadOnlyList<DailyReport> Reports => _reports;
        /// <summary>The day in progress.</summary>
        public DailyReport Current { get; private set; }

        public DailyLedger(GuildTreasury treasury, int day, float startHour)
        {
            _treasury = treasury;
            Current = new DailyReport(day, startHour, treasury.Gold);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<TreasuryChangedEvent>(OnTreasuryChanged);
            EventBus.Subscribe<GoldFlowEvent>(OnGoldFlow);
            EventBus.Subscribe<QuestPostedEvent>(OnQuestPosted);
            EventBus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
            EventBus.Subscribe<QuestClosedEvent>(OnQuestClosed);
            EventBus.Subscribe<QuestRewardPaidEvent>(OnQuestPaid);
            EventBus.Subscribe<QuestRewardRaisedEvent>(OnRewardRaised);
            EventBus.Subscribe<AdventurerJoinedEvent>(OnJoined);
            EventBus.Subscribe<AdventurerLeftEvent>(OnLeft);
            EventBus.Subscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Subscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Subscribe<Town.RaidSightedEvent>(OnRaidSighted);
            EventBus.Subscribe<Town.BuildingDestroyedEvent>(OnBuildingDestroyed);
            EventBus.Subscribe<CraftCompletedEvent>(OnCrafted);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            EventBus.Unsubscribe<TreasuryChangedEvent>(OnTreasuryChanged);
            EventBus.Unsubscribe<GoldFlowEvent>(OnGoldFlow);
            EventBus.Unsubscribe<QuestPostedEvent>(OnQuestPosted);
            EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
            EventBus.Unsubscribe<QuestClosedEvent>(OnQuestClosed);
            EventBus.Unsubscribe<QuestRewardPaidEvent>(OnQuestPaid);
            EventBus.Unsubscribe<QuestRewardRaisedEvent>(OnRewardRaised);
            EventBus.Unsubscribe<AdventurerJoinedEvent>(OnJoined);
            EventBus.Unsubscribe<AdventurerLeftEvent>(OnLeft);
            EventBus.Unsubscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Unsubscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Unsubscribe<Town.RaidSightedEvent>(OnRaidSighted);
            EventBus.Unsubscribe<Town.BuildingDestroyedEvent>(OnBuildingDestroyed);
            EventBus.Unsubscribe<CraftCompletedEvent>(OnCrafted);
        }

        // ---------------- Save (P5-4) ----------------

        [Serializable]
        class SavedReport
        {
            public int day;
            public float start;
            public bool closed;
            public int gold_start, gold_end;
            public int posted, completed, failed, cancelled, quest_paid, crafted, raids;
            public List<string> recruited = new(), departed = new(), deaths = new(), level_ups = new(), buildings_lost = new();
            public List<int> amounts = new();
        }

        [Serializable]
        class SaveData
        {
            public List<SavedReport> reports = new();
            public SavedReport current;
        }

        public string SaveKey => "ledger";

        static SavedReport CaptureReport(DailyReport r)
        {
            var s = new SavedReport
            {
                day = r.Day, start = r.StartHour, closed = r.Closed, gold_start = r.GoldStart, gold_end = r.GoldEnd,
                posted = r.QuestsPosted, completed = r.QuestsCompleted, failed = r.QuestsFailed,
                cancelled = r.QuestsCancelled, quest_paid = r.QuestPaidGold, crafted = r.Crafted, raids = r.Raids,
            };
            s.recruited.AddRange(r.Recruited);
            s.departed.AddRange(r.Departed);
            s.deaths.AddRange(r.Deaths);
            s.level_ups.AddRange(r.LevelUps);
            s.buildings_lost.AddRange(r.BuildingsLost);
            s.amounts.AddRange(r.Amounts);
            return s;
        }

        static DailyReport RestoreReport(SavedReport s)
        {
            var r = new DailyReport(s.day, s.start, s.gold_start)
            {
                Closed = s.closed, GoldEnd = s.gold_end, QuestsPosted = s.posted, QuestsCompleted = s.completed,
                QuestsFailed = s.failed, QuestsCancelled = s.cancelled, QuestPaidGold = s.quest_paid, Crafted = s.crafted, Raids = s.raids,
            };
            r.Recruited.AddRange(s.recruited);
            r.Departed.AddRange(s.departed);
            r.Deaths.AddRange(s.deaths);
            r.LevelUps.AddRange(s.level_ups);
            r.BuildingsLost.AddRange(s.buildings_lost);
            for (int i = 0; i < s.amounts.Count && i < r.Amounts.Length; i++) r.Amounts[i] = s.amounts[i];
            return r;
        }

        public string Capture()
        {
            var s = new SaveData { current = CaptureReport(Current) };
            foreach (var r in _reports) s.reports.Add(CaptureReport(r));
            return UnityEngine.JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = UnityEngine.JsonUtility.FromJson<SaveData>(json);
            _reports.Clear();
            foreach (var r in s.reports) _reports.Add(RestoreReport(r));
            if (s.current != null) Current = RestoreReport(s.current);
        }

        /// <summary>The report of a day, closed or still running; null when it is not kept (too old or in the future).</summary>
        public DailyReport Find(int day)
        {
            if (Current.Day == day) return Current;
            return _reports.Find(r => r.Day == day);
        }

        // ---------------- Days ----------------

        /// <summary>
        /// The new day's first hour event arrives before the treasury's daily tax, so the tax lands in the new
        /// day's report, and everything before it stays in the old one.
        /// </summary>
        void OnHour(HourPassedEvent e)
        {
            if (e.Day <= Current.Day) return;
            var closed = Current;
            closed.GoldEnd = _treasury.Gold;
            closed.Closed = true;
            _reports.Add(closed);
            if (_reports.Count > MaxReports) _reports.RemoveRange(0, _reports.Count - MaxReports);
            Current = new DailyReport(e.Day, 0f, _treasury.Gold);
            EventBus.Publish(new DailyReportClosedEvent { Report = closed });
        }

        void OnTreasuryChanged(TreasuryChangedEvent e) => Current.GoldEnd = e.Gold;

        // ---------------- Gold ----------------

        void OnGoldFlow(GoldFlowEvent e)
        {
            if (e.Amount == 0) return;
            Current.Add(GoldCategories.Classify(e.Reason, e.Amount), Math.Abs(e.Amount));
        }

        /// <summary>A raised reward is spent like a new escrow; it is shown on its own line.</summary>
        void OnRewardRaised(QuestRewardRaisedEvent e)
            => Current.Move(GoldCategory.QuestEscrow, GoldCategory.Negotiation, e.Gold);

        // ---------------- Quests ----------------

        void OnQuestPosted(QuestPostedEvent e) => Current.QuestsPosted++;

        void OnQuestCompleted(QuestCompletedEvent e) => Current.QuestsCompleted++;

        void OnQuestClosed(QuestClosedEvent e)
        {
            if (e.Status == QuestStatus.Failed) Current.QuestsFailed++;
            else if (e.Status == QuestStatus.Cancelled) Current.QuestsCancelled++;
        }

        void OnQuestPaid(QuestRewardPaidEvent e) => Current.QuestPaidGold += Math.Max(0, e.Gold);

        // ---------------- Guild members ----------------

        void OnJoined(AdventurerJoinedEvent e) => AddName(Current.Recruited, e.Adventurer);

        void OnLeft(AdventurerLeftEvent e) => AddName(Current.Departed, e.Adventurer);

        void OnDied(AdventurerDiedEvent e) => AddName(Current.Deaths, e.Adventurer);

        void OnLevelUp(AdventurerLevelUpEvent e)
        {
            if (e.Adventurer != null) Current.LevelUps.Add($"{e.Adventurer.Data.name} Lv.{e.Level}");
        }

        static void AddName(List<string> list, Adventurer a)
        {
            if (a != null) list.Add(a.Data.name);
        }

        // ---------------- Town ----------------

        void OnRaidSighted(Town.RaidSightedEvent e) => Current.Raids++;

        void OnBuildingDestroyed(Town.BuildingDestroyedEvent e)
        {
            if (e.Building != null) Current.BuildingsLost.Add(e.Building.DisplayName);
        }

        void OnCrafted(CraftCompletedEvent e) => Current.Crafted += Math.Max(0, e.Count);
    }
}
