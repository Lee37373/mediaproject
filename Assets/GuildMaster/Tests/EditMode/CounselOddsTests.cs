using GuildMaster.Welfare;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Counselling lines can fall flat (user request 2026-10-31): odds, failures adding stress, walking out.</summary>
    public class CounselOddsTests
    {
        static int Plain(CounselReaction r, bool touched = false, bool offTopic = false)
            => CounselOdds.Chance(r, touched, offTopic, "ISTP", 0, 50f, false, false, false);

        [Test]
        public void TheRealWorry_IsTheBestBet_AWrongGuessRarelyLands()
        {
            Assert.That(Plain(CounselReaction.Empathy, touched: true), Is.GreaterThan(Plain(CounselReaction.Empathy)));
            Assert.That(Plain(CounselReaction.Solution, touched: true), Is.GreaterThan(Plain(CounselReaction.Solution)));
            Assert.That(Plain(CounselReaction.Empathy, offTopic: true), Is.EqualTo(CounselOdds.OffTopicChance));
            Assert.That(Plain(CounselReaction.Listen), Is.GreaterThan(Plain(CounselReaction.Praise)), "a question is safer than praise");
        }

        [Test]
        public void HarshLines_AreNotRolled()
        {
            Assert.That(CounselOdds.Rolls(CounselReaction.Dismiss), Is.False);
            Assert.That(CounselOdds.Rolls(CounselReaction.Rude), Is.False);
            Assert.That(CounselOdds.Rolls(CounselReaction.LowEffort), Is.False);
            Assert.That(Plain(CounselReaction.Dismiss), Is.EqualTo(0));
        }

        [Test]
        public void Modifiers_PushTheOddsBothWays()
        {
            int baseF = CounselOdds.Chance(CounselReaction.Empathy, false, false, "INFP", 0, 50f, false, false, false);
            int baseT = CounselOdds.Chance(CounselReaction.Empathy, false, false, "INTP", 0, 50f, false, false, false);
            Assert.That(baseF - baseT, Is.EqualTo(CounselOdds.TemperamentBonus), "feelers take comfort better");
            int easy = CounselOdds.Chance(CounselReaction.Empathy, false, false, "INTP", 0, 50f, true, true, false);
            Assert.That(easy - baseT, Is.EqualTo(CounselOdds.AskedBonus + CounselOdds.OpenBonus));
            int hard = CounselOdds.Chance(CounselReaction.Empathy, false, false, "INTP", 5, 90f, false, false, true);
            Assert.That(baseT - hard, Is.EqualTo(CounselOdds.MaxGrudgePenalty + CounselOdds.HighStressPenalty + CounselOdds.RepeatPenalty));
            Assert.That(CounselOdds.Chance(CounselReaction.Empathy, false, true, "INTP", 5, 90f, false, false, true), Is.EqualTo(CounselOdds.MinChance));
            Assert.That(CounselOdds.Chance(CounselReaction.Listen, false, false, "INFP", 0, 10f, true, true, false), Is.LessThanOrEqualTo(CounselOdds.MaxChance));
        }

        [Test]
        public void Rolls_AndWhatAFailureCosts()
        {
            Assert.That(CounselOdds.Roll(60, 0.59, 0.0), Is.EqualTo(CounselRoll.Success));
            Assert.That(CounselOdds.Roll(60, 0.61, 0.5), Is.EqualTo(CounselRoll.Fail));
            Assert.That(CounselOdds.Roll(60, 0.61, 0.1), Is.EqualTo(CounselRoll.CritFail));
            Assert.That(CounselOdds.Harm(CounselRoll.Success), Is.EqualTo(0));
            Assert.That(CounselOdds.Harm(CounselRoll.CritFail), Is.GreaterThan(CounselOdds.Harm(CounselRoll.Fail)));
        }

        [Test]
        public void ThreeFailures_AndTheMemberWalksOut()
        {
            Assert.That(CounselSession.Judge(10, 8, 0, 4, 60f, CounselOdds.FailLimit - 1), Is.EqualTo(CounselOutcome.Ongoing));
            Assert.That(CounselSession.Judge(10, 12, 0, 4, 60f, CounselOdds.FailLimit), Is.EqualTo(CounselOutcome.Walkout));
        }

        [Test]
        public void ACaringTalk_UsuallyHelps_ButNotAlways()
        {
            // Monte Carlo of a sensible talk (ask, then lines on the real worry) for an average member.
            var rng = new System.Random(7);
            int relieved = 0, walkouts = 0;
            const int talks = 2000;
            for (int n = 0; n < talks; n++)
            {
                int relief = 0, harm = 0, hurtful = 0, fails = 0;
                var outcome = CounselOutcome.Ongoing;
                for (int turn = 1; outcome == CounselOutcome.Ongoing; turn++)
                {
                    var r = turn == 1 ? CounselReaction.Listen : turn % 2 == 0 ? CounselReaction.Empathy : CounselReaction.Solution;
                    bool touched = turn > 1;
                    int chance = CounselOdds.Chance(r, touched, false, "ISFJ", 0, 60f, false, turn > 1, false);
                    var roll = CounselOdds.Roll(chance, rng.NextDouble(), rng.NextDouble());
                    if (roll == CounselRoll.Success) relief += CounselScoring.Cap(CounselScoring.Score(r, 0, "ISFJ", touched, false), relief, harm);
                    else
                    {
                        harm -= CounselScoring.Cap(-CounselOdds.Harm(roll), relief, harm);
                        fails++;
                        if (roll == CounselRoll.CritFail) hurtful++;
                    }
                    outcome = CounselSession.Judge(relief, harm, hurtful, turn, 60f - relief + harm, fails);
                }
                if (outcome == CounselOutcome.Relieved) relieved++;
                if (outcome == CounselOutcome.Walkout) walkouts++;
            }
            float rate = relieved / (float)talks;
            Assert.That(rate, Is.InRange(0.6f, 0.9f), $"relieved {rate:P0}");
            Assert.That(walkouts / (float)talks, Is.GreaterThan(0.08f), "it goes wrong often enough to matter");
        }
    }
}
