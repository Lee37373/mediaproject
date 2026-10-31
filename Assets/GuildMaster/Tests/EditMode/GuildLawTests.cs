using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Guild;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// The guild's rules (P4-112, H2 — user decisions 2026-10-31): ten rules, five from the start and one more at each rank E to
    /// A; every rule starts as the game played before and that way moves nobody; how a member takes them goes by its traits and
    /// value, within caps; and the numbers each way sets.
    /// </summary>
    public class GuildLawTests
    {
        [Test]
        public void TenRules_FiveFromTheStart_OneMoreAtEachRankFromEToA()
        {
            Assert.That(GuildLawRules.Count, Is.EqualTo(10));
            Assert.That(GuildLawRules.All.Select((d, i) => (int)d.Law == i).All(x => x), Is.True, "indexed by their enum");
            Assert.That(GuildLawRules.All.Count(d => GuildLawRules.IsOpen(d.Law, GuildRank.F)), Is.EqualTo(5));
            foreach (var r in new[] { GuildRank.E, GuildRank.D, GuildRank.C, GuildRank.B, GuildRank.A })
            {
                Assert.That(GuildLawRules.OpenedAt(r).Count(), Is.EqualTo(1), r.ToString());
                Assert.That(GuildRankRules.Rewards(r), Does.Contain($"길드 규칙 '{GuildLawRules.OpenedAt(r).First().Name}' 열림"));
            }
            Assert.That(GuildLawRules.OpenedAt(GuildRank.F), Is.Empty);
            Assert.That(GuildLawRules.All.All(d => GuildLawRules.IsOpen(d.Law, GuildRank.A)), Is.True);
            Assert.That(GuildLawRules.IsOpen(GuildLaw.Training, GuildRank.F), Is.False);
        }

        [Test]
        public void EveryRule_StartsAsTheGamePlayedBefore_AndThatWayMovesNobody()
        {
            foreach (var d in GuildLawRules.All)
            {
                var o = d.Options[d.Default];
                Assert.That(o.Everyone, Is.Zero, d.Name);
                Assert.That(o.Feelings, Is.Empty, d.Name);
                Assert.That(d.Options.Length, Is.InRange(2, 3), d.Name);
                // A change either way has something to say for those it moves.
                foreach (var option in d.Options)
                    Assert.That(option.Pleased.Length + option.Displeased.Length, Is.GreaterThan(0), $"{d.Name} {option.Label}");
            }
            // The numbers at the defaults are the old ones.
            Assert.That(GuildLawRules.SplitEqualShare(GuildLawRules.Def(GuildLaw.Split).Default), Is.EqualTo(0.5f));
            Assert.That(GuildLawRules.RestBelowHp(0), Is.EqualTo(0.6f));
            Assert.That(GuildLawRules.RestAboveFatigue(0), Is.EqualTo(70f));
            Assert.That(GuildLawRules.ShopFactor(GuildLawRules.Def(GuildLaw.Shop).Default), Is.EqualTo(1f));
            Assert.That(GuildLawRules.Def(GuildLaw.NightMarch).Default, Is.EqualTo(GuildLawRules.NightFree));
            var defaults = GuildLawRules.All.Select(d => d.Default).ToArray();
            var m = GuildLawRules.Mood(defaults, new[] { "greedy", "brave", "lazy" }, "돈", newcomer: true);
            Assert.That(m.Liked, Is.Zero);
            Assert.That(m.Disliked, Is.Zero);
        }

        [Test]
        public void HowAMemberTakesARule_GoesByItsTraitsAndValue()
        {
            // Loot owed to the guild: the greedy and those after money mind it, the loyal are glad.
            Assert.That(GuildLawRules.Feeling(GuildLaw.Loot, 1, new[] { "greedy" }, "돈", false), Is.EqualTo(-7f));
            Assert.That(GuildLawRules.Feeling(GuildLaw.Loot, 1, new[] { "loyal" }, "명예", false), Is.EqualTo(2f));
            Assert.That(GuildLawRules.Feeling(GuildLaw.Loot, 1, new[] { "sturdy" }, "지식", false), Is.Zero);
            // The guild paying for the keep pleases everyone, the greedy most.
            Assert.That(GuildLawRules.Feeling(GuildLaw.Upkeep, 1, new string[0], "안전", false), Is.EqualTo(2f));
            Assert.That(GuildLawRules.Feeling(GuildLaw.Upkeep, 1, new[] { "greedy" }, "돈", false), Is.EqualTo(6f));
            // The training rule is only the newcomers'.
            Assert.That(GuildLawRules.Feeling(GuildLaw.Training, 1, new[] { "lazy" }, "모험", newcomer: true), Is.EqualTo(-5f));
            Assert.That(GuildLawRules.Feeling(GuildLaw.Training, 1, new[] { "lazy" }, "모험", newcomer: false), Is.Zero);
            Assert.That(GuildLawRules.Who(GuildLaw.Loot, 1), Does.Contain("꺼림: 탐욕스러움").And.Contain("돈 중시").And.Contain("반김: 충성스러움"));
            Assert.That(GuildLawRules.Who(GuildLaw.Upkeep, 1), Does.StartWith("반김: 모두"));
            Assert.That(GuildLawRules.Who(GuildLaw.Training, 1), Does.EndWith("(신입만)"));
        }

        [Test]
        public void AllTheRulesTogether_MoveAMoodNoFurtherThanTheCaps()
        {
            // A greedy, lazy coward after money under every rule that galls it: held at the cap.
            var galling = new int[GuildLawRules.Count];
            galling[(int)GuildLaw.Loot] = 1;
            galling[(int)GuildLaw.Split] = 0;
            galling[(int)GuildLaw.NightMarch] = GuildLawRules.NightPress;
            galling[(int)GuildLaw.Holiday] = 1;
            galling[(int)GuildLaw.Shop] = 2;
            var m = GuildLawRules.Mood(galling, new[] { "greedy", "lazy", "coward" }, "돈", false);
            Assert.That(m.Disliked, Is.EqualTo(GuildLawRules.DislikedCap));
            Assert.That(m.DislikedNames, Does.Contain("전리품 길드 귀속").And.Contain("길드 가게 비싸게"));
            // Some it likes, some it does not: both shown, each within its cap.
            var mixed = GuildLawRules.All.Select(d => d.Default).ToArray();
            mixed[(int)GuildLaw.Upkeep] = 1;
            mixed[(int)GuildLaw.Shop] = 0;
            mixed[(int)GuildLaw.Holiday] = 1;
            mixed[(int)GuildLaw.Loot] = 1;
            var both = GuildLawRules.Mood(mixed, new[] { "greedy", "lazy" }, "돈", false);
            Assert.That(both.Liked, Is.EqualTo(GuildLawRules.LikedCap));
            Assert.That(both.Disliked, Is.EqualTo(-7f));
            Assert.That(GuildLawRules.Names(both.DislikedNames), Is.EqualTo("전리품 길드 귀속"));
            Assert.That(GuildLawRules.Names(both.LikedNames), Does.EndWith("외 2"));
        }

        [Test]
        public void TheSplitRule_SetsHowMuchGoesEvenly()
        {
            var deeds = new[] { 30f, 10f, 0f, 0f };
            Assert.That(PartySettlement.RuleSplit(400, deeds, GuildLawRules.SplitEqualShare(0)), Is.EqualTo(new[] { 100, 100, 100, 100 }));
            Assert.That(PartySettlement.RuleSplit(400, deeds, GuildLawRules.SplitEqualShare(2)), Is.EqualTo(new[] { 300, 100, 0, 0 }));
            Assert.That(PartySettlement.RuleSplit(400, deeds, GuildLawRules.SplitEqualShare(1)), Is.EqualTo(new[] { 200, 100, 50, 50 }));
            // By deeds, with none done: evenly after all.
            Assert.That(PartySettlement.RuleSplit(90, new[] { 0f, 0f, 0f }, 0f), Is.EqualTo(new[] { 30, 30, 30 }));
        }

        [Test]
        public void TheHoliday_IsTheLastDayOfEachSeason()
        {
            Assert.That(new[] { 5, 10, 15, 20, 25 }.All(GuildLawRules.IsHolidayDay), Is.True);
            Assert.That(new[] { 0, 1, 4, 6, 11, 19 }.Any(GuildLawRules.IsHolidayDay), Is.False);
            Assert.That(GuildLawRules.ShopFactor(0), Is.LessThan(1f));
            Assert.That(GuildLawRules.ShopFactor(2), Is.GreaterThan(1f));
            Assert.That(GuildLawRules.RestBelowHp(1), Is.GreaterThan(GuildLawRules.RestBelowHp(0)));
            Assert.That(GuildLawRules.RestAboveFatigue(1), Is.LessThan(GuildLawRules.RestAboveFatigue(0)));
        }
    }
}
