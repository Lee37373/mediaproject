using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using GuildMaster.Persuasion;
using GuildMaster.Quests;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>Gold negotiation over untaken quests (P4-25): rules, counter-offers and the reward raise itself.</summary>
    public class NegotiationTests
    {
        [SetUp]
        public void SetUp() => EventBus.Clear();

        [TearDown]
        public void TearDown() => EventBus.Clear();

        // ---- greed ----

        [Test]
        public void Greed_FollowsValuesAndTrait_AndIsClamped()
        {
            Assert.That(NegotiationRules.Greed("동료", 1f), Is.EqualTo(1f).Within(0.001f));
            Assert.That(NegotiationRules.Greed("돈", 1f), Is.EqualTo(1.3f).Within(0.001f));
            Assert.That(NegotiationRules.Greed("명예", 1f), Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(NegotiationRules.Greed("돈", 1.5f), Is.EqualTo(1.95f).Within(0.001f));
            Assert.That(NegotiationRules.Greed("돈", 3f), Is.EqualTo(2f).Within(0.001f), "upper bound");
            Assert.That(NegotiationRules.Greed("명예", 0.2f), Is.EqualTo(0.5f).Within(0.001f), "lower bound");
        }

        // ---- limits ----

        [Test]
        public void RaiseCap_IsTheBaseRewardWithAFloor()
        {
            Assert.That(NegotiationRules.RaiseCap(200), Is.EqualTo(200));
            Assert.That(NegotiationRules.RaiseCap(10), Is.EqualTo(NegotiationRules.MinCapGold));
        }

        [Test]
        public void MaxOffer_IsLimitedByCapAndTreasury_InWholeSteps()
        {
            Assert.That(NegotiationRules.MaxOffer(100, 0, 1000), Is.EqualTo(100));
            Assert.That(NegotiationRules.MaxOffer(100, 60, 1000), Is.EqualTo(40), "cap left after earlier offers");
            Assert.That(NegotiationRules.MaxOffer(100, 0, 47), Is.EqualTo(45), "treasury, rounded down to a step");
            Assert.That(NegotiationRules.MaxOffer(100, 0, 4), Is.EqualTo(0), "less than one step");
            Assert.That(NegotiationRules.MaxOffer(100, 100, 1000), Is.EqualTo(0), "cap reached");
        }

        [Test]
        public void PercentOffer_RoundsToStepsAndNeverBelowOne()
        {
            Assert.That(NegotiationRules.PercentOffer(100, 25), Is.EqualTo(25));
            Assert.That(NegotiationRules.PercentOffer(90, 10), Is.EqualTo(10), "9 rounds to 10");
            Assert.That(NegotiationRules.PercentOffer(10, 10), Is.EqualTo(NegotiationRules.Step));
        }

        // ---- willingness from gold ----

        [Test]
        public void WillingnessFromExtra_GrowsWithGold_AndWithGreed_AndIsCapped()
        {
            Assert.That(NegotiationRules.WillingnessFromExtra(0, 100, 1f), Is.EqualTo(0));
            Assert.That(NegotiationRules.WillingnessFromExtra(50, 100, 1f), Is.EqualTo(20), "half the reward = +20 for greed 1");
            Assert.That(NegotiationRules.WillingnessFromExtra(100, 100, 1f), Is.EqualTo(40));
            Assert.That(NegotiationRules.WillingnessFromExtra(50, 100, 1.5f), Is.GreaterThan(NegotiationRules.WillingnessFromExtra(50, 100, 1f)));
            Assert.That(NegotiationRules.WillingnessFromExtra(50, 100, 0.5f), Is.LessThan(NegotiationRules.WillingnessFromExtra(50, 100, 1f)));
            Assert.That(NegotiationRules.WillingnessFromExtra(100000, 100, 2f), Is.EqualTo(NegotiationRules.MaxGain));
        }

        [Test]
        public void WillingnessFromExtra_DependsOnTheTotal_SoSplittingOffersGainsNothing()
        {
            int whole = NegotiationRules.WillingnessFromExtra(60, 100, 1.3f);
            int a = NegotiationRules.WillingnessFromExtra(20, 100, 1.3f);
            int b = NegotiationRules.WillingnessFromExtra(60, 100, 1.3f) - a;
            Assert.That(a + b, Is.EqualTo(whole));
        }

        [Test]
        public void ExtraForWillingness_IsTheLeastStepThatReachesTheGain()
        {
            foreach (float greed in new[] { 0.5f, 1f, 1.3f, 2f })
            foreach (int gain in new[] { 1, 10, 25, 40, 60 })
            {
                int extra = NegotiationRules.ExtraForWillingness(gain, 100, greed);
                Assert.That(extra % NegotiationRules.Step, Is.EqualTo(0), $"step (gain {gain}, greed {greed})");
                Assert.That(NegotiationRules.WillingnessFromExtra(extra, 100, greed), Is.GreaterThanOrEqualTo(gain), $"enough (gain {gain}, greed {greed})");
                if (extra > NegotiationRules.Step)
                    Assert.That(NegotiationRules.WillingnessFromExtra(extra - NegotiationRules.Step, 100, greed), Is.LessThan(gain), $"least (gain {gain}, greed {greed})");
            }
            Assert.That(NegotiationRules.ExtraForWillingness(0, 100, 1f), Is.EqualTo(0));
            Assert.That(NegotiationRules.ExtraForWillingness(NegotiationRules.MaxGain + 1, 100, 1f), Is.EqualTo(-1), "gold alone cannot do that");
        }

        // ---- counter-offers ----

        [Test]
        public void CounterOffer_None_WhenAlreadyWillingOrForbiddenOrTooFar()
        {
            Assert.That(NegotiationRules.CounterOffer(60, 50, 0, 100, 1f, true, 100), Is.EqualTo(0), "already willing");
            Assert.That(NegotiationRules.CounterOffer(40, 50, 0, 100, 1f, false, 100), Is.EqualTo(0), "may not accept (badly hurt)");
            Assert.That(NegotiationRules.CounterOffer(40, 50, 0, 100, 1f, true, 0), Is.EqualTo(0), "nothing left to offer");
            Assert.That(NegotiationRules.CounterOffer(5, 75, 0, 100, 0.5f, true, 100), Is.EqualTo(0), "more than gold can buy");
        }

        [Test]
        public void CounterOffer_AskingPriceClosesTheGap_InSteps_AndIsAtLeastTheLeastNeeded()
        {
            foreach (float greed in new[] { 0.8f, 1f, 1.3f })
            foreach (int willingness in new[] { 30, 40, 46 })
            {
                int ask = NegotiationRules.CounterOffer(willingness, 50, 0, 100, greed, true, 500);
                if (ask == 0) continue;
                Assert.That(ask % NegotiationRules.Step, Is.EqualTo(0));
                int gain = NegotiationRules.WillingnessFromExtra(ask, 100, greed);
                Assert.That(willingness + gain, Is.GreaterThanOrEqualTo(50), $"paying the ask reaches the threshold (w {willingness}, greed {greed})");
            }
        }

        [Test]
        public void CounterOffer_GreedyAskLess_AndCloserAskLess()
        {
            int neutral = NegotiationRules.CounterOffer(30, 50, 0, 100, 1f, true, 500);
            int greedy = NegotiationRules.CounterOffer(30, 50, 0, 100, 1.5f, true, 500);
            int closer = NegotiationRules.CounterOffer(42, 50, 0, 100, 1f, true, 500);
            Assert.That(neutral, Is.GreaterThan(0));
            Assert.That(greedy, Is.LessThan(neutral), "gold moves a greedy adventurer faster");
            Assert.That(closer, Is.LessThan(neutral), "a smaller gap needs less gold");
        }

        [Test]
        public void CounterOffer_IsNeverAboveWhatCanBeOffered()
        {
            int unlimited = NegotiationRules.CounterOffer(30, 50, 0, 100, 1f, true, 500);
            int ask = NegotiationRules.CounterOffer(30, 50, 0, 100, 1f, true, unlimited - NegotiationRules.Step);
            Assert.That(ask, Is.LessThanOrEqualTo(unlimited - NegotiationRules.Step));
        }

        [Test]
        public void CounterOffer_CountsGoldAlreadyOffered()
        {
            // 30 willingness + the gain of the 30G already promised.
            int gainSoFar = NegotiationRules.WillingnessFromExtra(30, 100, 1f);
            int fresh = NegotiationRules.CounterOffer(30, 50, 0, 100, 1f, true, 500);
            int later = NegotiationRules.CounterOffer(30 + gainSoFar, 50, 30, 100, 1f, true, 500);
            Assert.That(later, Is.GreaterThan(0));
            Assert.That(later, Is.LessThan(fresh), "the first part of the price is already on the table");
        }

        // ---- lines ----

        [Test]
        public void Lines_AreNeverEmpty_AndTheAskNamesThePrice()
        {
            var rng = new GameRandom(7);
            foreach (float greed in new[] { 0.6f, 1f, 1.6f })
            {
                Assert.That(NegotiationLines.OfferReply(greed, 80, 50, true, rng), Is.Not.Empty);
                Assert.That(NegotiationLines.OfferReply(greed, 40, 50, true, rng), Is.Not.Empty);
                Assert.That(NegotiationLines.OfferReply(greed, 10, 50, true, rng), Is.Not.Empty);
                Assert.That(NegotiationLines.CounterLine(greed, 35, rng), Does.Contain("35G"));
                Assert.That(NegotiationLines.DealLine(greed, rng), Is.Not.Empty);
            }
            Assert.That(NegotiationLines.DeclinedLine(rng), Is.Not.Empty);
        }

        // ---- the reward raise itself ----

        [Test]
        public void TryRaiseReward_TakesGoldFromTheTreasury_AndRaisesTheEscrowedReward()
        {
            var db = DefaultContent.Build();
            db.BuildIndex();
            var treasury = new GuildTreasury(100, 0);
            var go = new GameObject("board");
            try
            {
                var board = go.AddComponent<QuestBoard>();
                board.Init(treasury, null, null);
                var q = new Quest("Q_1", new QuestDraft { Type = QuestType.Hunt, Species = db.Monster("slime"), Count = 1, RewardGold = 50, RewardExp = 0, MaxParty = 1 }, 0f);

                Assert.That(board.TryRaiseReward(q, 30), Is.True);
                Assert.That(q.RewardGold, Is.EqualTo(80));
                Assert.That(q.BonusGold, Is.EqualTo(30));
                Assert.That(q.PayGold, Is.EqualTo(80), "the payout follows the raised reward");
                Assert.That(treasury.Gold, Is.EqualTo(70));

                Assert.That(board.TryRaiseReward(q, 71), Is.False, "the treasury cannot pay it");
                Assert.That(board.TryRaiseReward(q, 0), Is.False);
                Assert.That(board.TryRaiseReward(null, 5), Is.False);
                Assert.That(q.RewardGold, Is.EqualTo(80), "failed raises change nothing");
                Assert.That(treasury.Gold, Is.EqualTo(70));

                q.Status = QuestStatus.Cancelled;
                Assert.That(board.TryRaiseReward(q, 5), Is.False, "a closed quest cannot be sweetened");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TryRaiseReward_PublishesAnEvent()
        {
            var db = DefaultContent.Build();
            db.BuildIndex();
            var treasury = new GuildTreasury(100, 0);
            var go = new GameObject("board");
            try
            {
                var board = go.AddComponent<QuestBoard>();
                board.Init(treasury, null, null);
                var q = new Quest("Q_2", new QuestDraft { Type = QuestType.Hunt, Species = db.Monster("slime"), Count = 1, RewardGold = 50, MaxParty = 1 }, 0f);
                int paid = 0;
                EventBus.Subscribe<QuestRewardRaisedEvent>(e => paid += e.Gold);
                board.TryRaiseReward(q, 20);
                Assert.That(paid, Is.EqualTo(20));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
