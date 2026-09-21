using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.Notifications;
using GuildMaster.Quests;
using GuildMaster.Reports;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>The daily accounts (F-3): gold sorted by reason, days closing with their balances, activity counters, wording.</summary>
    public class DailyLedgerTests
    {
        GuildTreasury _treasury;
        DailyLedger _ledger;
        int _closedEvents;

        [SetUp]
        public void SetUp()
        {
            EventBus.Clear();
            _treasury = new GuildTreasury(500, 20, 0f, 0);
            _ledger = new DailyLedger(_treasury, 1, 8f);
            _closedEvents = 0;
            EventBus.Subscribe<DailyReportClosedEvent>(_ => _closedEvents++);
        }

        [TearDown]
        public void TearDown()
        {
            _ledger.Dispose();
            EventBus.Clear();
        }

        /// <summary>The clock announces the first hour of a new day before anything else happens in it.</summary>
        static void NextDay(int day) => EventBus.Publish(new HourPassedEvent { Day = day, Hour = 0 });

        // ---------------- Treasury ----------------

        [Test]
        public void Treasury_PublishesFlows_WithSignedAmountsAndReasons()
        {
            var flows = new List<GoldFlowEvent>();
            EventBus.Subscribe<GoldFlowEvent>(flows.Add);
            _treasury.AddGold(10, GoldReason.Goods);
            _treasury.TrySpendGold(4, GoldReason.Loot);
            _treasury.TrySpend(6, 0);
            _treasury.AddGold(0);
            _treasury.TrySpendGold(0);
            _treasury.TrySpendGold(99999);

            Assert.That(flows, Has.Count.EqualTo(3), "zero amounts and refused spends are not flows");
            Assert.That(flows[0].Amount, Is.EqualTo(10));
            Assert.That(flows[0].Reason, Is.EqualTo(GoldReason.Goods));
            Assert.That(flows[1].Amount, Is.EqualTo(-4));
            Assert.That(flows[2].Amount, Is.EqualTo(-6));
            Assert.That(flows[2].Reason, Is.Null);
        }

        // ---------------- Categories ----------------

        [Test]
        public void Classify_UsesTheReason_AndTheQuestBoardHasNone()
        {
            Assert.That(GoldCategories.Classify(GoldReason.Tax, 50), Is.EqualTo(GoldCategory.Tax));
            Assert.That(GoldCategories.Classify(GoldReason.Goods, 12), Is.EqualTo(GoldCategory.GoodsSold));
            Assert.That(GoldCategories.Classify(GoldReason.Merchant, 40), Is.EqualTo(GoldCategory.MerchantSales));
            Assert.That(GoldCategories.Classify(GoldReason.Loot, -30), Is.EqualTo(GoldCategory.LootBought));
            Assert.That(GoldCategories.Classify(GoldReason.Craft, -10), Is.EqualTo(GoldCategory.Crafting));
            Assert.That(GoldCategories.Classify(GoldReason.Upgrade, -100), Is.EqualTo(GoldCategory.Upgrade));
            Assert.That(GoldCategories.Classify(null, -60), Is.EqualTo(GoldCategory.QuestEscrow));
            Assert.That(GoldCategories.Classify(null, 40), Is.EqualTo(GoldCategory.QuestRefund));
        }

        [Test]
        public void Classify_UnknownOrMismatchedReasons_FallIntoOther()
        {
            Assert.That(GoldCategories.Classify("mystery", 5), Is.EqualTo(GoldCategory.OtherIncome));
            Assert.That(GoldCategories.Classify("mystery", -5), Is.EqualTo(GoldCategory.OtherExpense));
            Assert.That(GoldCategories.Classify(GoldReason.Tax, -5), Is.EqualTo(GoldCategory.OtherExpense), "tax cannot be an expense");
            Assert.That(GoldCategories.Classify(GoldReason.Loot, 5), Is.EqualTo(GoldCategory.OtherIncome), "loot cannot be income");
        }

        [Test]
        public void EveryCategory_HasALabel_AndIsListedOnce()
        {
            var seen = new HashSet<GoldCategory>();
            foreach (var c in GoldCategories.Income) { Assert.That(seen.Add(c), Is.True); Assert.That(GoldCategories.IsIncome(c), Is.True); }
            foreach (var c in GoldCategories.Expense) { Assert.That(seen.Add(c), Is.True); Assert.That(GoldCategories.IsIncome(c), Is.False); }
            Assert.That(seen, Has.Count.EqualTo(System.Enum.GetValues(typeof(GoldCategory)).Length));
            foreach (var c in seen) Assert.That(GoldCategories.Label(c), Is.Not.Empty);
        }

        // ---------------- Gold ----------------

        [Test]
        public void Flows_AreSortedIntoIncomeAndExpense()
        {
            _treasury.AddGold(30, GoldReason.Goods);
            _treasury.AddGold(80, GoldReason.Merchant);
            _treasury.TrySpendGold(25, GoldReason.Loot);
            _treasury.TrySpendGold(40, GoldReason.Craft);
            _treasury.TrySpendGold(100, GoldReason.Upgrade);

            var r = _ledger.Current;
            Assert.That(r.Amount(GoldCategory.GoodsSold), Is.EqualTo(30));
            Assert.That(r.Amount(GoldCategory.MerchantSales), Is.EqualTo(80));
            Assert.That(r.Amount(GoldCategory.LootBought), Is.EqualTo(25));
            Assert.That(r.Amount(GoldCategory.Crafting), Is.EqualTo(40));
            Assert.That(r.Amount(GoldCategory.Upgrade), Is.EqualTo(100));
            Assert.That(r.Income, Is.EqualTo(110));
            Assert.That(r.Expense, Is.EqualTo(165));
            Assert.That(r.Net, Is.EqualTo(-55));
        }

        [Test]
        public void QuestEscrowAndRefunds_HaveNoReason_ButAreStillTold()
        {
            _treasury.TrySpend(120, 5);
            _treasury.AddGold(50);
            Assert.That(_ledger.Current.Amount(GoldCategory.QuestEscrow), Is.EqualTo(120));
            Assert.That(_ledger.Current.Amount(GoldCategory.QuestRefund), Is.EqualTo(50));
        }

        [Test]
        public void RaisedReward_MovesFromEscrowToTheNegotiationLine()
        {
            _treasury.TrySpend(100, 0);
            _treasury.TrySpendGold(30);
            EventBus.Publish(new QuestRewardRaisedEvent { Quest = null, Gold = 30 });

            Assert.That(_ledger.Current.Amount(GoldCategory.QuestEscrow), Is.EqualTo(100));
            Assert.That(_ledger.Current.Amount(GoldCategory.Negotiation), Is.EqualTo(30));
            Assert.That(_ledger.Current.Expense, Is.EqualTo(130), "the total spent does not change");
        }

        [Test]
        public void Net_AlwaysEqualsTheChangeInGold()
        {
            _treasury.AddGold(37, "something new");
            _treasury.AddGold(10, GoldReason.Goods);
            _treasury.TrySpendGold(21);
            _treasury.TrySpendGold(9, GoldReason.Loot);
            _treasury.AddGold(15);
            _treasury.TrySpendGold(5, "another thing");

            var r = _ledger.Current;
            Assert.That(r.GoldEnd, Is.EqualTo(_treasury.Gold));
            Assert.That(r.Net, Is.EqualTo(r.GoldEnd - r.GoldStart));
            Assert.That(r.Amount(GoldCategory.OtherIncome), Is.EqualTo(37));
            Assert.That(r.Amount(GoldCategory.OtherExpense), Is.EqualTo(5));
        }

        [Test]
        public void Exp_IsTrackedAsStartAndEnd()
        {
            _treasury.AddExp(15);
            _treasury.TrySpend(0, 5);
            Assert.That(_ledger.Current.ExpStart, Is.EqualTo(20));
            Assert.That(_ledger.Current.ExpEnd, Is.EqualTo(30));
        }

        // ---------------- Days ----------------

        [Test]
        public void NewDay_ClosesTheReportWithItsBalances_AndOpensTheNext()
        {
            _treasury.TrySpendGold(100, GoldReason.Craft);
            NextDay(2);

            Assert.That(_ledger.Reports, Has.Count.EqualTo(1));
            var closed = _ledger.Reports[0];
            Assert.That(closed.Closed, Is.True);
            Assert.That(closed.Day, Is.EqualTo(1));
            Assert.That(closed.StartHour, Is.EqualTo(8f));
            Assert.That(closed.GoldStart, Is.EqualTo(500));
            Assert.That(closed.GoldEnd, Is.EqualTo(400));
            Assert.That(closed.Net, Is.EqualTo(-100));

            Assert.That(_ledger.Current.Day, Is.EqualTo(2));
            Assert.That(_ledger.Current.Closed, Is.False);
            Assert.That(_ledger.Current.GoldStart, Is.EqualTo(400));
            Assert.That(_ledger.Current.StartHour, Is.EqualTo(0f));
            Assert.That(_closedEvents, Is.EqualTo(1));
        }

        [Test]
        public void HoursWithinTheSameDay_DoNotCloseIt()
        {
            EventBus.Publish(new HourPassedEvent { Day = 1, Hour = 9 });
            Assert.That(_ledger.Reports, Is.Empty);
            NextDay(2);
            EventBus.Publish(new HourPassedEvent { Day = 2, Hour = 1 });
            NextDay(2);
            Assert.That(_ledger.Reports, Has.Count.EqualTo(1));
            Assert.That(_closedEvents, Is.EqualTo(1));
        }

        [Test]
        public void DailyTax_BelongsToTheNewDay()
        {
            _treasury.DailyTax = 50;
            _treasury.AddGold(20, GoldReason.Goods);
            NextDay(2);
            EventBus.Publish(new DayPassedEvent { Day = 2 });

            Assert.That(_ledger.Reports[0].Amount(GoldCategory.Tax), Is.EqualTo(0));
            Assert.That(_ledger.Reports[0].Income, Is.EqualTo(20));
            Assert.That(_ledger.Current.Amount(GoldCategory.Tax), Is.EqualTo(50));
            Assert.That(_ledger.Current.GoldEnd, Is.EqualTo(_treasury.Gold));
        }

        [Test]
        public void OnlyTheNewestReportsAreKept_AndCanBeFoundByDay()
        {
            int rollovers = DailyLedger.MaxReports + 5;
            for (int d = 2; d <= rollovers + 1; d++) NextDay(d);

            Assert.That(_ledger.Reports, Has.Count.EqualTo(DailyLedger.MaxReports));
            Assert.That(_ledger.Reports[0].Day, Is.EqualTo(6), "days 1-5 were dropped");
            Assert.That(_ledger.Find(1), Is.Null);
            Assert.That(_ledger.Find(6), Is.SameAs(_ledger.Reports[0]));
            Assert.That(_ledger.Find(rollovers + 1), Is.SameAs(_ledger.Current));
            Assert.That(_ledger.Find(rollovers + 2), Is.Null, "a day that has not happened");
        }

        [Test]
        public void Disposed_StopsListening()
        {
            _ledger.Dispose();
            _treasury.AddGold(10, GoldReason.Goods);
            NextDay(2);
            Assert.That(_ledger.Reports, Is.Empty);
            Assert.That(_ledger.Current.Amount(GoldCategory.GoodsSold), Is.EqualTo(0));
        }

        // ---------------- Activity ----------------

        [Test]
        public void QuestEvents_AreCountedByOutcome()
        {
            EventBus.Publish(new QuestPostedEvent { Quest = null });
            EventBus.Publish(new QuestPostedEvent { Quest = null });
            EventBus.Publish(new QuestCompletedEvent { Quest = null });
            EventBus.Publish(new QuestClosedEvent { Quest = null, Status = QuestStatus.Failed });
            EventBus.Publish(new QuestClosedEvent { Quest = null, Status = QuestStatus.Cancelled });
            EventBus.Publish(new QuestClosedEvent { Quest = null, Status = QuestStatus.TurnedIn });
            EventBus.Publish(new QuestRewardPaidEvent { Quest = null, Taker = null, Gold = 30, Exp = 1 });
            EventBus.Publish(new QuestRewardPaidEvent { Quest = null, Taker = null, Gold = 20, Exp = 1 });

            var r = _ledger.Current;
            Assert.That(r.QuestsPosted, Is.EqualTo(2));
            Assert.That(r.QuestsCompleted, Is.EqualTo(1));
            Assert.That(r.QuestsFailed, Is.EqualTo(1));
            Assert.That(r.QuestsCancelled, Is.EqualTo(1), "a turned-in quest is not a failure or a cancel");
            Assert.That(r.QuestPaidGold, Is.EqualTo(50));
        }

        [Test]
        public void ActivityCounters_StartOverEachDay()
        {
            EventBus.Publish(new QuestPostedEvent { Quest = null });
            NextDay(2);
            Assert.That(_ledger.Reports[0].QuestsPosted, Is.EqualTo(1));
            Assert.That(_ledger.Current.QuestsPosted, Is.EqualTo(0));
        }

        // ---------------- Wording ----------------

        [Test]
        public void Money_UsesSeparators_AndSignedShowsDirection()
        {
            Assert.That(DailyReportTexts.Money(1234), Is.EqualTo("1,234G"));
            Assert.That(DailyReportTexts.Money(0), Is.EqualTo("0G"));
            Assert.That(DailyReportTexts.SignedMoney(60), Is.EqualTo("+60G"));
            Assert.That(DailyReportTexts.SignedMoney(-40), Is.EqualTo("-40G"));
            Assert.That(DailyReportTexts.SignedMoney(0), Is.EqualTo("0G"));
            Assert.That(DailyReportTexts.SignedMoney(-1500), Is.EqualTo("-1,500G"));
        }

        [Test]
        public void Summary_NamesTheDayAndTheResult()
        {
            var r = new DailyReport(3, 0f, 100, 0);
            r.Add(GoldCategory.Tax, 100);
            r.Add(GoldCategory.Crafting, 40);
            r.GoldEnd = 160;
            var text = DailyReportTexts.Summary(r);
            Assert.That(text, Does.Contain("3일차").And.Contain("+60G").And.Contain("수입 100G").And.Contain("지출 40G").And.Contain("잔액 160G"));
            Assert.That(text, Does.Not.Contain("사망"));

            r.Deaths.Add("노아");
            Assert.That(DailyReportTexts.Summary(r), Does.Contain("사망 1명"));
        }

        [Test]
        public void ActivityLines_OmitEmptyThings_AndSayWhenNothingHappened()
        {
            var quiet = DailyReportTexts.ActivityLines(new DailyReport(1, 8f, 0, 0));
            Assert.That(quiet, Has.Count.EqualTo(1));
            Assert.That(quiet[0].Tone, Is.EqualTo(LineTone.Muted));

            var r = new DailyReport(2, 0f, 0, 0) { QuestsPosted = 2, QuestsCompleted = 1, QuestPaidGold = 45 };
            r.Deaths.Add("노아");
            r.Recruited.Add("라일");
            var lines = DailyReportTexts.ActivityLines(r);
            Assert.That(lines.Exists(l => l.Text.Contains("등록 2") && l.Text.Contains("달성 1")), Is.True);
            Assert.That(lines.Exists(l => l.Text.Contains("45G")), Is.True);
            Assert.That(lines.Exists(l => l.Text.StartsWith("사망") && l.Text.Contains("노아") && l.Tone == LineTone.Bad), Is.True);
            Assert.That(lines.Exists(l => l.Text.StartsWith("영입") && l.Text.Contains("라일") && l.Tone == LineTone.Good), Is.True);
            Assert.That(lines.Exists(l => l.Tone == LineTone.Muted), Is.False, "the 'nothing happened' line only appears on quiet days");
        }

        [Test]
        public void Names_AreCappedWithACount()
        {
            var many = new List<string> { "a", "b", "c", "d", "e", "f", "g", "h" };
            var text = DailyReportTexts.Names(many);
            Assert.That(text, Does.Contain("a").And.Contain("f").And.Contain("외 2명"));
            Assert.That(text, Does.Not.Contain("g"));
            Assert.That(DailyReportTexts.Names(new List<string> { "a", "b" }), Is.EqualTo("a, b"));
            Assert.That(DailyReportTexts.Names(null), Is.Empty);
        }

        [Test]
        public void ReportLink_CarriesTheDay()
        {
            var link = NotificationLink.Report(3);
            Assert.That(link.Target, Is.EqualTo(NotificationTarget.Report));
            Assert.That(link.Id, Is.EqualTo("3"));
            Assert.That(link.IsNone, Is.False);
        }
    }
}
