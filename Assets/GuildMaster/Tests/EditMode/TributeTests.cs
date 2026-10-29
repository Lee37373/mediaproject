using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.Economy;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Tribute talk (상납금, user decision 2026-10-29: an LLM talk with a member decides how much is taken).</summary>
    public class TributeTests
    {
        static readonly List<string> NoTraits = new();

        [Test]
        public void Pot_DependsOnValuesAndTraits()
        {
            int plain = TributeRules.Pot(200, "명예", NoTraits);
            Assert.That(plain, Is.EqualTo(100));
            Assert.That(TributeRules.Pot(200, "돈", new List<string> { "greedy" }), Is.LessThan(plain), "a greedy money-lover gives less");
            Assert.That(TributeRules.Pot(200, "동료", new List<string> { "loyal" }), Is.GreaterThan(plain), "a loyal comrade gives more");
            Assert.That(TributeRules.Amount(100, TributeOffer.None), Is.EqualTo(0));
            Assert.That(TributeRules.Amount(100, TributeOffer.Little), Is.EqualTo(25));
            Assert.That(TributeRules.Amount(100, TributeOffer.Some), Is.EqualTo(50));
            Assert.That(TributeRules.Amount(100, TributeOffer.Much), Is.EqualTo(100));
        }

        [Test]
        public void ModelOffer_StaysWithinAStepOfTheCap()
        {
            Assert.That(TributeRules.Allowed(10).ToList(), Is.EqualTo(new[] { TributeOffer.None }));
            Assert.That(TributeRules.Allowed(60).ToList(), Is.EqualTo(new[] { TributeOffer.Little, TributeOffer.Some }));
            Assert.That(TributeRules.Clamp(TributeOffer.Much, 30), Is.EqualTo(TributeOffer.Little), "no more than the cap");
            Assert.That(TributeRules.Clamp(TributeOffer.None, 90), Is.EqualTo(TributeOffer.Some), "no less than a step under it");
        }

        [Test]
        public void ShippedChoices_ReadAsTheirKind()
        {
            foreach (var kv in TributeLines.Asks)
                foreach (var text in kv.Value)
                    Assert.That(TributeRules.Classify(text), Is.EqualTo(kv.Key), text);
            Assert.That(TributeRules.Classify("ㅇㅇ"), Is.EqualTo(TributeTactic.Low));
            Assert.That(TributeRules.Classify("이 멍청한 놈아 돈 내"), Is.EqualTo(TributeTactic.Rude));
            Assert.That(TributeRules.Reconcile(TributeTactic.Plead, TributeTactic.Pressure), Is.EqualTo(TributeTactic.Pressure), "a threat stays a threat");
            Assert.That(TributeRules.Reconcile(TributeTactic.Bargain, TributeTactic.Small), Is.EqualTo(TributeTactic.Bargain));
        }

        [Test]
        public void Offer_AlwaysHasAThreatAndThreeOtherKinds()
        {
            var rng = new GameRandom(3);
            for (int i = 0; i < 10; i++)
            {
                var offer = TributeLines.Offer(new HashSet<string>(), rng);
                Assert.That(offer.Count, Is.EqualTo(TributeLines.ChoiceCount));
                Assert.That(offer.Count(c => c.Tactic == TributeTactic.Pressure), Is.EqualTo(1));
                Assert.That(offer.Select(c => c.Tactic).Distinct().Count(), Is.EqualTo(TributeLines.ChoiceCount));
            }
        }

        [Test]
        public void KindWords_WinTheMoneyOverSeveralLines()
        {
            var talk = new TributeTalk(200, "동료", NoTraits, 20f, 0);
            Assert.That(talk.Offer, Is.EqualTo(TributeOffer.None));
            talk.Hear(TributeTactic.Plead, null);
            talk.Hear(TributeTactic.Reason, null);
            talk.Hear(TributeTactic.Bargain, null);
            Assert.That(talk.Offer, Is.GreaterThanOrEqualTo(TributeOffer.Some));
            Assert.That(talk.Sour, Is.EqualTo(0));
            Assert.That(talk.Pressured, Is.False);
            int taken = talk.Accept();
            Assert.That(taken, Is.GreaterThan(0));
            Assert.That(talk.Outcome, Is.EqualTo(TributeOutcome.Paid));
        }

        [Test]
        public void Threats_AreFastButAThirdOneEndsTheTalk()
        {
            var talk = new TributeTalk(200, "명예", NoTraits, 20f, 0);
            var step = talk.Hear(TributeTactic.Pressure, null);
            Assert.That(step.Stress, Is.GreaterThan(0f));
            Assert.That(talk.Offer, Is.EqualTo(TributeOffer.Some), "one threat is worth two kind words");
            talk.Hear(TributeTactic.Pressure, null);
            Assert.That(talk.Outcome, Is.EqualTo(TributeOutcome.Ongoing));
            talk.Hear(TributeTactic.Pressure, null);
            Assert.That(talk.Outcome, Is.EqualTo(TributeOutcome.Walkout));
            Assert.That(talk.Accept(), Is.EqualTo(0), "nothing to take after a walkout");
        }

        [Test]
        public void Repeats_WorkLess_AndTheTalkHasALastWord()
        {
            Assert.That(TributeRules.WillDelta(TributeTactic.Plead, "명예", NoTraits, 1), Is.LessThan(TributeRules.WillDelta(TributeTactic.Plead, "명예", NoTraits, 0)));
            Assert.That(TributeRules.WillDelta(TributeTactic.Pressure, "명예", new List<string> { "arrogant" }, 0), Is.LessThanOrEqualTo(5), "the arrogant do not scare");
            var talk = new TributeTalk(200, "명예", NoTraits, 20f, 0);
            for (int i = 0; i < TributeRules.MaxTurns; i++) talk.Hear(TributeTactic.Small, null);
            Assert.That(talk.Final, Is.True);
            Assert.That(talk.CanTalk, Is.False);
            Assert.That(TributeRules.StartWill(20f, NoTraits, 2), Is.LessThan(TributeRules.StartWill(20f, NoTraits, 0)), "a grudge makes the next talk harder");
        }

        [Test]
        public void WrittenLines_ExistForEveryAnswerAndSpeechLevel()
        {
            var keys = new[] { "open", "none", "little", "some", "much", "angry", "walkout", "paid", "pass" };
            foreach (var key in keys)
                foreach (NpcVoice.Register r in System.Enum.GetValues(typeof(NpcVoice.Register)))
                {
                    string line = TributeLines.Line(key, r, 30, null);
                    Assert.That(line, Is.Not.Empty.And.Not.EqualTo("…"), key + " " + r);
                    if (key is "little" or "some" or "much" or "paid") Assert.That(line, Does.Contain("30골드"), key + " " + r);
                }
        }

        [Test]
        public void Grammar_OnlyLetsTheModelPickAllowedOffers()
        {
            string g = TributePrompt.Grammar(60);
            Assert.That(g, Does.Contain("little").And.Contain("some"));
            Assert.That(g, Does.Not.Contain("much"));
            Assert.That(TributePrompt.BuildAside(60, 0, 100, false), Does.Contain("25골드").And.Contain("50골드"));
        }
    }
}
