using System;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using GuildMaster.Notifications;
using GuildMaster.Reports;
using GuildMaster.Welfare;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Stress and its consequences (F-2): levels, quest gates, demands, resignation, the guild master's tools, wording.</summary>
    public class WelfareTests
    {
        [SetUp]
        public void SetUp() => EventBus.Clear();

        [TearDown]
        public void TearDown() => EventBus.Clear();

        static Func<float> Always(float v) => () => v;

        static CareInput In(float now, float stress, string values = "동료", float fatigue = 10f, bool onQuest = false, bool free = true)
            => new() { Now = now, Stress = stress, Fatigue = fatigue, Values = values, OnQuest = onQuest, InTownFree = free };

        // ---------------- Levels and quest gates ----------------

        [Test]
        public void LevelOf_FollowsTheThresholds()
        {
            Assert.That(StressRules.LevelOf(0f), Is.EqualTo(StressLevel.Calm));
            Assert.That(StressRules.LevelOf(39.9f), Is.EqualTo(StressLevel.Calm));
            Assert.That(StressRules.LevelOf(40f), Is.EqualTo(StressLevel.Tense));
            Assert.That(StressRules.LevelOf(59.9f), Is.EqualTo(StressLevel.Tense));
            Assert.That(StressRules.LevelOf(60f), Is.EqualTo(StressLevel.Strained));
            Assert.That(StressRules.LevelOf(79.9f), Is.EqualTo(StressLevel.Strained));
            Assert.That(StressRules.LevelOf(80f), Is.EqualTo(StressLevel.Breaking));
            Assert.That(StressRules.LevelOf(100f), Is.EqualTo(StressLevel.Breaking));
        }

        [Test]
        public void EveryLevel_HasALabelAndAnEffectText()
        {
            foreach (StressLevel level in Enum.GetValues(typeof(StressLevel)))
            {
                Assert.That(StressRules.Label(level), Is.Not.Empty);
                Assert.That(StressRules.Effect(level), Is.Not.Empty);
            }
        }

        [Test]
        public void AllowsQuest_StrainedAvoidsHighDanger_BreakingTakesNothing()
        {
            foreach (DangerLevel danger in Enum.GetValues(typeof(DangerLevel)))
            {
                Assert.That(StressRules.AllowsQuest(10f, danger), Is.True, $"calm {danger}");
                Assert.That(StressRules.AllowsQuest(50f, danger), Is.True, $"tense {danger}");
                Assert.That(StressRules.AllowsQuest(90f, danger), Is.False, $"breaking {danger}");
            }
            Assert.That(StressRules.AllowsQuest(70f, DangerLevel.Low), Is.True);
            Assert.That(StressRules.AllowsQuest(70f, DangerLevel.Medium), Is.True);
            Assert.That(StressRules.AllowsQuest(70f, DangerLevel.High), Is.False);
        }

        [Test]
        public void QuestScoreFactor_OnlyTenseMembersAreLessKeen()
        {
            Assert.That(StressRules.QuestScoreFactor(10f), Is.EqualTo(1f));
            Assert.That(StressRules.QuestScoreFactor(50f), Is.LessThan(1f));
            Assert.That(StressRules.QuestScoreFactor(70f), Is.EqualTo(1f), "the gate decides from here on");
        }

        // ---------------- Numbers ----------------

        [Test]
        public void ChooseDemand_FollowsValues_AndTirednessAsksForRest()
        {
            Assert.That(CareRules.ChooseDemand("돈", 10f), Is.EqualTo(CareDemand.Bonus));
            Assert.That(CareRules.ChooseDemand("안전", 10f), Is.EqualTo(CareDemand.Leave));
            Assert.That(CareRules.ChooseDemand("모험", 10f), Is.EqualTo(CareDemand.Leave));
            Assert.That(CareRules.ChooseDemand("동료", 10f), Is.EqualTo(CareDemand.Talk));
            Assert.That(CareRules.ChooseDemand("명예", 10f), Is.EqualTo(CareDemand.Talk));
            Assert.That(CareRules.ChooseDemand("지식", 10f), Is.EqualTo(CareDemand.Talk));
            Assert.That(CareRules.ChooseDemand("돈", 70f), Is.EqualTo(CareDemand.Leave), "exhausted: rest first");
        }

        [Test]
        public void InterviewRelief_DependsOnPersonality()
        {
            float plain = CareRules.InterviewRelief("지식", "INTJ");
            Assert.That(CareRules.InterviewRelief("동료", "INTJ"), Is.GreaterThan(plain));
            Assert.That(CareRules.InterviewRelief("돈", "INTJ"), Is.LessThan(plain));
            Assert.That(CareRules.InterviewRelief("지식", "ENFP"), Is.GreaterThan(plain), "talkers gain more");
            Assert.That(CareRules.InterviewRelief(null, null), Is.GreaterThan(0f));
        }

        [Test]
        public void BonusRelief_MoneyPeopleGainMost()
        {
            Assert.That(CareRules.BonusRelief("돈"), Is.GreaterThan(CareRules.BonusRelief("동료")));
            Assert.That(CareRules.BonusRelief("명예"), Is.LessThan(CareRules.BonusRelief("동료")));
        }

        [Test]
        public void BonusCost_GrowsWithLevel_AndNeverBelowLevelOne()
        {
            Assert.That(CareRules.BonusCost(1), Is.EqualTo(50));
            Assert.That(CareRules.BonusCost(5), Is.EqualTo(90));
            Assert.That(CareRules.BonusCost(0), Is.EqualTo(CareRules.BonusCost(1)));
        }

        // ---------------- Demands ----------------

        [Test]
        public void Demand_NeedsStrainedStress_AndAWinningRoll()
        {
            var c = new MemberCare();
            Assert.That(c.Tick(In(10, 59.9f), Always(0f)), Is.EqualTo(CareStep.None), "not stressed enough");
            Assert.That(c.Tick(In(11, 70f), Always(0.9f)), Is.EqualTo(CareStep.None), "roll too high");
            Assert.That(c.Demand, Is.EqualTo(CareDemand.None));

            var step = c.Tick(In(12, 70f), Always(0.1f));
            Assert.That(step, Is.EqualTo(CareStep.DemandRaised));
            Assert.That(c.Demand, Is.EqualTo(CareDemand.Talk));
            Assert.That(c.DemandRaisedAt, Is.EqualTo(12f));
        }

        [Test]
        public void Demand_KindFollowsTheMembersValues()
        {
            var money = new MemberCare();
            money.Tick(In(10, 70f, "돈"), Always(0f));
            Assert.That(money.Demand, Is.EqualTo(CareDemand.Bonus));

            var tired = new MemberCare();
            tired.Tick(In(10, 70f, "돈", fatigue: 80f), Always(0f));
            Assert.That(tired.Demand, Is.EqualTo(CareDemand.Leave));
        }

        [Test]
        public void Demand_NoRollIsWastedWhileCalm()
        {
            int rolls = 0;
            var c = new MemberCare();
            c.Tick(In(10, 20f), () => { rolls++; return 0f; });
            Assert.That(rolls, Is.Zero);
        }

        [Test]
        public void Demand_IsWithdrawnWhenTheMemberCalmsDown()
        {
            var c = new MemberCare();
            c.Tick(In(10, 70f), Always(0f));
            var step = c.Tick(In(11, 30f), Always(0.9f));
            Assert.That(step, Is.EqualTo(CareStep.DemandWithdrawn));
            Assert.That(c.Demand, Is.EqualTo(CareDemand.None));
            Assert.That(c.Grudges, Is.Zero, "no grudge for a demand that settled itself");
        }

        [Test]
        public void Demand_IgnoredForTooLong_LeavesAGrudge_AndACooldown()
        {
            var c = new MemberCare();
            c.Tick(In(10, 70f), Always(0f));
            Assert.That(c.Tick(In(21.9f, 70f), Always(0.9f)), Is.EqualTo(CareStep.None));

            var step = c.Tick(In(22f, 70f), Always(0.9f));
            Assert.That(step, Is.EqualTo(CareStep.DemandIgnored));
            Assert.That(c.Grudges, Is.EqualTo(1));
            Assert.That(c.Demand, Is.EqualTo(CareDemand.None));

            Assert.That(c.Tick(In(23f, 70f), Always(0f)), Is.EqualTo(CareStep.None), "no new demand during the cooldown");
            Assert.That(c.Tick(In(34f, 70f), Always(0f)), Is.EqualTo(CareStep.DemandRaised), "asks again after it");
        }

        [Test]
        public void DemandHoursLeft_CountsDown()
        {
            var c = new MemberCare();
            Assert.That(c.DemandHoursLeft(10f), Is.Zero, "no demand");
            c.Tick(In(10, 70f), Always(0f));
            Assert.That(c.DemandHoursLeft(10f), Is.EqualTo(CareRules.DemandHours));
            Assert.That(c.DemandHoursLeft(16f), Is.EqualTo(CareRules.DemandHours - 6f));
            Assert.That(c.DemandHoursLeft(100f), Is.Zero);
        }

        // ---------------- Resignation ----------------

        /// <summary>Raises a demand and lets it run out, twice: the second time the member gives notice.</summary>
        static MemberCare NeglectedTwice(out float noticeAt)
        {
            var c = new MemberCare();
            c.Tick(In(10, 70f), Always(0f));
            c.Tick(In(22f, 70f), Always(0.9f));
            c.Tick(In(34f, 70f), Always(0f));
            var step = c.Tick(In(46f, 70f), Always(0.9f));
            Assert.That(step, Is.EqualTo(CareStep.DemandIgnored | CareStep.ResignNotice));
            noticeAt = 46f;
            return c;
        }

        [Test]
        public void TwoIgnoredDemands_MakeTheMemberGiveNotice_ThenLeave()
        {
            var c = NeglectedTwice(out float at);
            Assert.That(c.Resigning, Is.True);
            Assert.That(c.Cause, Is.EqualTo(QuitCause.Neglect));
            Assert.That(c.ResignHoursLeft(at), Is.EqualTo(CareRules.ResignNoticeHours));

            Assert.That(c.Tick(In(at + 5f, 70f), Always(0.9f)), Is.EqualTo(CareStep.None), "still in the notice period");
            Assert.That(c.Tick(In(at + CareRules.ResignNoticeHours, 70f), Always(0.9f)), Is.EqualTo(CareStep.Quit));
            Assert.That(c.Resigning, Is.False);
        }

        [Test]
        public void Quitting_WaitsUntilTheMemberIsBackInTown()
        {
            var c = NeglectedTwice(out float at);
            float due = at + CareRules.ResignNoticeHours;
            Assert.That(c.Tick(In(due, 70f, onQuest: true, free: false), Always(0.9f)), Is.EqualTo(CareStep.None));
            Assert.That(c.Resigning, Is.True, "still means to leave");
            Assert.That(c.Tick(In(due + 1f, 70f), Always(0.9f)), Is.EqualTo(CareStep.Quit));
        }

        [Test]
        public void Reassure_CancelsResignation_OnlyWhenReallyCalmer()
        {
            var c = NeglectedTwice(out _);
            Assert.That(c.Reassure(StressRules.StrainedAt), Is.False, "still too stressed");
            Assert.That(c.Resigning, Is.True);
            Assert.That(c.Reassure(StressRules.StrainedAt - 1f), Is.True);
            Assert.That(c.Resigning, Is.False);
            Assert.That(c.Reassure(10f), Is.False, "nothing left to cancel");
        }

        [Test]
        public void Resignation_IsDropped_WhenTheMemberCalmsDownByThemselves()
        {
            var c = NeglectedTwice(out float at);
            Assert.That(c.Tick(In(at + 1f, 20f), Always(0.9f)), Is.EqualTo(CareStep.None));
            Assert.That(c.Resigning, Is.False);
        }

        [Test]
        public void Grudges_FadeOverTime_AndAnAnsweredDemandForgivesOne()
        {
            var c = new MemberCare();
            c.Tick(In(10, 70f), Always(0f));
            c.Tick(In(22f, 70f), Always(0.9f));
            Assert.That(c.Grudges, Is.EqualTo(1));
            c.Tick(In(22f + CareRules.GrudgeFadeHours, 10f), Always(0.9f));
            Assert.That(c.Grudges, Is.Zero, "faded");

            var d = new MemberCare();
            d.Tick(In(10, 70f), Always(0f));
            d.Tick(In(22f, 70f), Always(0.9f));
            d.Tick(In(34f, 70f), Always(0f));
            Assert.That(d.Demand, Is.EqualTo(CareDemand.Talk));
            Assert.That(d.RecordInterview(35f), Is.True, "it was what they asked for");
            Assert.That(d.Grudges, Is.Zero);
            Assert.That(d.Demand, Is.EqualTo(CareDemand.None));
        }

        // ---------------- The breaking point ----------------

        [Test]
        public void BreakingPoint_WarnsOnceAfterAFewHours_AndStartsOverWhenCalmer()
        {
            var c = new MemberCare();
            var flags = CareStep.None;
            for (int h = 1; h <= 3; h++) flags |= c.Tick(In(h, 85f), Always(0.99f));
            Assert.That(flags.HasFlag(CareStep.LimitWarning), Is.False);
            Assert.That(c.Tick(In(4f, 85f), Always(0.99f)).HasFlag(CareStep.LimitWarning), Is.True);
            Assert.That(c.Tick(In(5f, 85f), Always(0.99f)).HasFlag(CareStep.LimitWarning), Is.False, "only once");

            c.Tick(In(6f, 70f), Always(0.99f));
            Assert.That(c.BreakingHours, Is.Zero);
            for (int h = 7; h <= 9; h++) c.Tick(In(h, 85f), Always(0.99f));
            Assert.That(c.Tick(In(10f, 85f), Always(0.99f)).HasFlag(CareStep.LimitWarning), Is.True, "warns again after a break");
        }

        [Test]
        public void Sabotage_OnlyWhileOnAQuestAtTheBreakingPoint()
        {
            var away = new MemberCare();
            Assert.That(away.Tick(In(10, 85f, onQuest: true, free: false), Always(0.1f)).HasFlag(CareStep.Sabotage), Is.True);

            var lucky = new MemberCare();
            Assert.That(lucky.Tick(In(10, 85f, onQuest: true, free: false), Always(0.99f)).HasFlag(CareStep.Sabotage), Is.False, "the roll decides");

            var strained = new MemberCare();
            Assert.That(strained.Tick(In(10, 70f, onQuest: true, free: false), Always(0.1f)).HasFlag(CareStep.Sabotage), Is.False, "not at the breaking point");

            var idle = new MemberCare();
            Assert.That(idle.Tick(In(10, 85f), Always(0.1f)).HasFlag(CareStep.Sabotage), Is.False, "no quest to walk away from");
        }

        [Test]
        public void Burnout_QuitsAfterManyHoursAtTheBreakingPoint()
        {
            var c = new MemberCare();
            var last = CareStep.None;
            for (int h = 1; h < CareRules.QuitAfterBreakingHours; h++)
            {
                last = c.Tick(In(h, 90f), Always(0f));
                Assert.That(last.HasFlag(CareStep.Quit), Is.False, $"hour {h}");
            }
            last = c.Tick(In(CareRules.QuitAfterBreakingHours, 90f), Always(0f));
            Assert.That(last.HasFlag(CareStep.Quit), Is.True);
            Assert.That(c.Cause, Is.EqualTo(QuitCause.Burnout));
        }

        [Test]
        public void Burnout_NeedsALosingRollToBeSpared()
        {
            var c = new MemberCare();
            for (int h = 1; h <= 15; h++)
                Assert.That(c.Tick(In(h, 90f), Always(0.99f)).HasFlag(CareStep.Quit), Is.False);
        }

        // ---------------- The guild master's tools ----------------

        [Test]
        public void Interview_HasACooldown()
        {
            var c = new MemberCare();
            Assert.That(c.CanInterview(10f).Ok, Is.True);
            Assert.That(c.RecordInterview(10f), Is.False, "nothing was asked for");
            Assert.That(c.CanInterview(10f + CareRules.InterviewCooldownHours - 0.1f).Ok, Is.False);
            Assert.That(c.CanInterview(10f + CareRules.InterviewCooldownHours).Ok, Is.True);
            Assert.That(c.CanInterview(12f).Reason, Is.Not.Empty);
        }

        [Test]
        public void Bonus_HasACooldown_AndAnAnsweredDemandIsCleared()
        {
            var c = new MemberCare();
            c.Tick(In(10, 70f, "돈"), Always(0f));
            Assert.That(c.Demand, Is.EqualTo(CareDemand.Bonus));
            Assert.That(c.RecordBonus(11f), Is.True);
            Assert.That(c.Demand, Is.EqualTo(CareDemand.None));
            Assert.That(c.CanGiveBonus(12f).Ok, Is.False);
            Assert.That(c.CanGiveBonus(11f + CareRules.BonusCooldownHours).Ok, Is.True);
        }

        [Test]
        public void AnotherKindOfHelp_LeavesTheDemandStanding()
        {
            var c = new MemberCare();
            c.Tick(In(10, 70f, "돈"), Always(0f));
            Assert.That(c.RecordInterview(11f), Is.False);
            Assert.That(c.Demand, Is.EqualTo(CareDemand.Bonus));
        }

        [Test]
        public void Leave_LastsAWhile_ThenEnds_AndNeedsACooldownBeforeTheNext()
        {
            var c = new MemberCare();
            Assert.That(c.CanGrantLeave(10f, true).Ok, Is.True);
            Assert.That(c.CanGrantLeave(10f, false).Ok, Is.False, "busy with a quest or a fight");
            c.RecordLeave(10f);
            Assert.That(c.OnLeave(10f + CareRules.LeaveHours - 0.1f), Is.True);
            Assert.That(c.OnLeave(10f + CareRules.LeaveHours), Is.False);
            Assert.That(c.LeaveHoursLeft(12f), Is.EqualTo(CareRules.LeaveHours - 2f).Within(0.001f));
            Assert.That(c.CanGrantLeave(12f, true).Ok, Is.False, "already on leave");

            var step = c.Tick(In(10f + CareRules.LeaveHours, 10f), Always(0.9f));
            Assert.That(step, Is.EqualTo(CareStep.LeaveEnded));
            Assert.That(c.OnLeave(30f), Is.False);
            Assert.That(c.CanGrantLeave(20f, true).Ok, Is.False, "too soon after the last one");
            Assert.That(c.CanGrantLeave(10f + CareRules.LeaveHours + CareRules.LeaveCooldownHours, true).Ok, Is.True);
        }

        // ---------------- Money ----------------

        [Test]
        public void Bonus_IsItsOwnExpenseInTheDailyReport()
        {
            var treasury = new GuildTreasury(500, 0);
            var ledger = new DailyLedger(treasury, 1, 8f);
            try
            {
                Assert.That(treasury.TrySpendGold(60, GoldReason.Bonus), Is.True);
                Assert.That(ledger.Current.Amount(GoldCategory.Bonus), Is.EqualTo(60));
                Assert.That(ledger.Current.Net, Is.EqualTo(-60));
                Assert.That(GoldCategories.IsIncome(GoldCategory.Bonus), Is.False);
                Assert.That(GoldCategories.Label(GoldCategory.Bonus), Is.Not.Empty);
                Assert.That(GoldCategories.Classify(GoldReason.Bonus, 30), Is.EqualTo(GoldCategory.OtherIncome), "a bonus cannot be income");
            }
            finally { ledger.Dispose(); }
        }

        // ---------------- Wording and links ----------------

        [Test]
        public void Texts_NameTheMemberAndTheirRequest()
        {
            foreach (CareDemand d in new[] { CareDemand.Talk, CareDemand.Leave, CareDemand.Bonus })
            {
                Assert.That(CareTexts.DemandTitle(d), Is.Not.Empty);
                Assert.That(CareTexts.DemandLine(d), Is.Not.Empty);
                Assert.That(CareTexts.DemandNotice("카엘", d, 65), Does.Contain("카엘").And.Contain("65"));
                Assert.That(CareTexts.MemoryAsked(d), Does.Contain(CareRules.Label(d)));
            }
            Assert.That(CareTexts.DemandIgnored("카엘", 1), Does.Contain("카엘"));
            Assert.That(CareTexts.DemandIgnored("카엘", CareRules.QuitGrudges), Does.Contain("떠날"));
            Assert.That(CareTexts.ResignNotice("카엘"), Does.Contain("카엘"));
            Assert.That(CareTexts.LimitWarning("카엘", 88), Does.Contain("88"));
            Assert.That(CareTexts.Sabotage("카엘", "슬라임 사냥"), Does.Contain("슬라임 사냥"));
            Assert.That(CareTexts.Quit("카엘", QuitCause.Neglect), Does.Contain("카엘"));
            Assert.That(CareTexts.Quit("카엘", QuitCause.Burnout), Does.Contain("카엘"));
            Assert.That(CareTexts.QuitLine(QuitCause.Neglect), Is.Not.EqualTo(CareTexts.QuitLine(QuitCause.Burnout)));
            Assert.That(CareTexts.MemoryBonus(70), Does.Contain("70"));
        }

        [Test]
        public void Replies_ExistForEveryValue_WithOrWithoutARandomSource()
        {
            var rng = new GameRandom(3);
            foreach (var v in AdventurerGenerator.Values)
            {
                Assert.That(CareTexts.InterviewReply(v, rng), Is.Not.Empty, v);
                Assert.That(CareTexts.InterviewReply(v, null), Is.Not.Empty, v);
                Assert.That(CareTexts.BonusReply(v), Is.Not.Empty, v);
            }
            Assert.That(CareTexts.InterviewReply(null, rng), Is.Not.Empty);
            Assert.That(CareTexts.InterviewReply("??", null), Is.Not.Empty);
        }

        [Test]
        public void Results_ShowTheChangeInStress()
        {
            Assert.That(CareTexts.Relieved("면담", 18f, 62, 44), Does.Contain("62").And.Contain("44").And.Contain("18"));
            Assert.That(CareTexts.BonusResult(70, 30f, 82, 52), Does.Contain("70G").And.Contain("52"));
        }

        // ---------------- Grief for a fallen comrade ----------------

        [Test]
        public void DeathStress_IsZeroWhenTheyDidNotCare()
        {
            Assert.That(StressRules.DeathStress(-1), Is.EqualTo(0f));
            Assert.That(StressRules.DeathStress(-100), Is.EqualTo(0f));
        }

        [Test]
        public void DeathStress_StartsAtTheMinimum_AndGrowsWithAffection()
        {
            Assert.That(StressRules.DeathStress(0), Is.EqualTo(StressRules.DeathStressMin).Within(0.0001f));
            Assert.That(StressRules.DeathStress(20), Is.GreaterThan(StressRules.DeathStress(10)));
            Assert.That(StressRules.DeathStress(40), Is.GreaterThan(StressRules.DeathStress(20)));
        }

        [Test]
        public void DeathStress_PeaksAtFullGrief_AndNeverExceedsIt()
        {
            Assert.That(StressRules.DeathStress(StressRules.DeathGriefFullAt), Is.EqualTo(StressRules.DeathStressMax).Within(0.0001f));
            Assert.That(StressRules.DeathStress(100), Is.EqualTo(StressRules.DeathStressMax).Within(0.0001f));
        }

        [Test]
        public void DeathStress_AFriendFeelsMoreThanAnAcquaintance()
            => Assert.That(StressRules.DeathStress(StressRules.FriendRelation), Is.GreaterThan(StressRules.DeathStress(0)));

        [Test]
        public void ManagementLink_CarriesTheAdventurerId()
        {
            var link = NotificationLink.Management("NPC_007");
            Assert.That(link.Target, Is.EqualTo(NotificationTarget.Management));
            Assert.That(link.Id, Is.EqualTo("NPC_007"));
            Assert.That(link.IsNone, Is.False);
        }
    }
}
