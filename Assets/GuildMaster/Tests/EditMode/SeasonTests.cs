using GuildMaster.Disasters;
using GuildMaster.Seasons;
using GuildMaster.Storytelling;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>
    /// The seasons (P4-111b, H11 — user decisions 2026-10-30: five days each, a twenty-day year, a tint and numbers only):
    /// which day is which, the day before and the first day, what each season does, the festival, the tints.
    /// </summary>
    public class SeasonTests
    {
        [Test]
        public void FiveDaysASeason_TwentyAYear_StartingInSpring()
        {
            Assert.That(SeasonRules.Of(1), Is.EqualTo(Season.Spring));
            Assert.That(SeasonRules.Of(5), Is.EqualTo(Season.Spring));
            Assert.That(SeasonRules.Of(6), Is.EqualTo(Season.Summer));
            Assert.That(SeasonRules.Of(11), Is.EqualTo(Season.Autumn));
            Assert.That(SeasonRules.Of(16), Is.EqualTo(Season.Winter), "the first winter on day 16");
            Assert.That(SeasonRules.Of(21), Is.EqualTo(Season.Spring), "and round again");
            Assert.That((SeasonRules.DayOf(1), SeasonRules.DayOf(5), SeasonRules.DayOf(6), SeasonRules.DayOf(18)), Is.EqualTo((1, 5, 1, 3)));
            Assert.That((SeasonRules.Year(20), SeasonRules.Year(21)), Is.EqualTo((1, 2)));
            Assert.That(SeasonRules.Next(Season.Winter), Is.EqualTo(Season.Spring));
        }

        [Test]
        public void TheTownTellsTheDayBefore_AndTheFirstDayIsNotANewSeason()
        {
            Assert.That(SeasonRules.Begins(1), Is.False, "the game's first day is no change");
            Assert.That(SeasonRules.Begins(6) && SeasonRules.Begins(16) && SeasonRules.Begins(21), Is.True);
            Assert.That(SeasonRules.Begins(7), Is.False);
            Assert.That(SeasonRules.Eve(5) && SeasonRules.Eve(15), Is.True);
            Assert.That(SeasonRules.Of(5 + 1), Is.Not.EqualTo(SeasonRules.Of(5)), "an eve is followed by another season");
            foreach (Season s in System.Enum.GetValues(typeof(Season)))
                Assert.That(SeasonRules.Changes(s), Is.Not.Empty, s.ToString());
        }

        [Test]
        public void SpringBreeds_SummerBurns_AutumnTrades_WinterIsDearColdAndHungry()
        {
            Assert.That(SeasonRules.BreedFactor(Season.Spring), Is.GreaterThan(1f));
            Assert.That(SeasonRules.BreedFactor(Season.Winter), Is.LessThan(1f));
            Assert.That(SeasonRules.DisasterFactor(Season.Summer), Is.GreaterThan(1f));
            Assert.That(SeasonRules.DisasterKindWeight(Season.Summer, DisasterKind.Wildfire), Is.GreaterThan(1f));
            Assert.That(SeasonRules.DisasterKindWeight(Season.Winter, DisasterKind.Avalanche), Is.GreaterThan(1f));
            Assert.That(SeasonRules.BeatWeight(Season.Summer, StoryBeat.Disaster), Is.GreaterThan(1f));
            Assert.That(SeasonRules.MerchantInterval(Season.Autumn, 48f), Is.LessThan(48f));
            Assert.That(SeasonRules.MerchantInterval(Season.Spring, 48f), Is.EqualTo(48f));
            Assert.That(SeasonRules.MerchantPriceFactor(Season.Autumn), Is.GreaterThan(1f));
            Assert.That(SeasonRules.MealFactor(Season.Winter), Is.GreaterThan(1f));
            Assert.That(SeasonRules.WalkFactor(Season.Winter), Is.LessThan(1f));
            Assert.That(SeasonRules.CampRestFactor(Season.Winter), Is.LessThan(1f));
            Assert.That(SeasonRules.RaidFactor(Season.Winter), Is.GreaterThan(1f));
            Assert.That(SeasonRules.BeatWeight(Season.Winter, StoryBeat.TownRaid) * SeasonRules.BeatWeight(Season.Winter, StoryBeat.Horde), Is.GreaterThan(1f));
            // Everything else stays as it was.
            foreach (var s in new[] { Season.Spring, Season.Summer, Season.Autumn })
                Assert.That((SeasonRules.MealFactor(s), SeasonRules.WalkFactor(s), SeasonRules.CampRestFactor(s), SeasonRules.RaidFactor(s)),
                    Is.EqualTo((1f, 1f, 1f, 1f)), s.ToString());
        }

        [Test]
        public void TheHarvestFestival_IsOneDayOfAutumn()
        {
            int festivals = 0;
            for (int day = 1; day <= SeasonRules.DaysPerYear; day++)
                if (SeasonRules.IsFestival(day)) { festivals++; Assert.That(SeasonRules.Of(day), Is.EqualTo(Season.Autumn)); }
            Assert.That(festivals, Is.EqualTo(1));
            Assert.That(SeasonRules.IsFestival(10 + SeasonRules.FestivalDay), Is.True);
            Assert.That(SeasonRules.FestivalStressRelief, Is.GreaterThan(1f));
        }

        [Test]
        public void Tints_StayWithinTheLight_AndDifferBySeason()
        {
            foreach (Season s in System.Enum.GetValues(typeof(Season)))
            {
                var c = SeasonRules.Tint(s);
                Assert.That(Mathf.Max(c.r, c.g, c.b), Is.LessThanOrEqualTo(1f), s.ToString());
                Assert.That(Mathf.Min(c.r, c.g, c.b), Is.GreaterThan(0.7f), s + ": a tint, not a dark");
            }
            Assert.That(SeasonRules.Tint(Season.Winter).b, Is.GreaterThan(SeasonRules.Tint(Season.Winter).r), "winter is cold blue");
            Assert.That(SeasonRules.Tint(Season.Autumn).r, Is.GreaterThan(SeasonRules.Tint(Season.Autumn).b), "autumn is amber");
        }
    }
}
