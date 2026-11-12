using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Quests;
using GuildMaster.Social;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>The residents' requests and the town's word on the guild (P4-134 F-14): favour steps, thanks, what they ask, the visitors' word.</summary>
    public class ResidentRequestTests
    {
        [Test]
        public void Favours_ComeInStepsOfTenPercent_At25_50_75()
        {
            Assert.That(new[] { -30, 0, 24, 25, 49, 50, 74, 75, 100 }.Select(ResidentRequestRules.Tier), Is.EqualTo(new[] { 0, 0, 0, 1, 1, 2, 2, 3, 3 }));
            Assert.That(ResidentRequestRules.Share(0), Is.EqualTo(0f));
            Assert.That(ResidentRequestRules.Share(2), Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(ResidentRequestRules.Share(9), Is.EqualTo(0.3f).Within(1e-5f), "three steps at most");
            Assert.That((ResidentRequestRules.NextTierAt(10), ResidentRequestRules.NextTierAt(60), ResidentRequestRules.NextTierAt(80)), Is.EqualTo((25, 75, -1)));
            Assert.That(ResidentRequestRules.ChildBrag(3), Is.EqualTo(6));
            // A cut rounds up (the bakery does not lose its copper), never below nothing.
            Assert.That(ResidentRequestRules.Cut(160, 0.1f), Is.EqualTo(144));
            Assert.That(ResidentRequestRules.Cut(5, 0.3f), Is.EqualTo(4));
            Assert.That(ResidentRequestRules.Cut(0, 0.3f), Is.EqualTo(0));
            Assert.That(ResidentRequestRules.Cut(60, 0f), Is.EqualTo(60));
            // Every trade says what its favour does.
            foreach (ResidentJob job in System.Enum.GetValues(typeof(ResidentJob)))
                Assert.That(ResidentRequestRules.FavourText(job, 1), Is.Not.Empty, job.ToString());
            Assert.That(ResidentRequestRules.FavourText(ResidentJob.Baker, 2), Does.Contain("20%"));
        }

        [Test]
        public void TheTownsWord_StartsAtNothing_AndMovesVisitorsBothWays()
        {
            // The six start at 15 on average: a new game's visitors hear nothing either way.
            int start = ResidentRequestRules.TownOpinion(ResidentProfile.Defaults().Select(p => p.GuildOpinion));
            Assert.That(start, Is.EqualTo(15));
            Assert.That(ResidentRequestRules.VisitorWord(start), Is.EqualTo(0));
            Assert.That(new[] { 25, 50, 75, -24, -25, -50 }.Select(ResidentRequestRules.VisitorWord), Is.EqualTo(new[] { 3, 6, 10, 0, -5, -10 }));
            Assert.That(ResidentRequestRules.TownOpinion(new int[0]), Is.EqualTo(0));
            // Only the child starts at a favour step (40): its bragging is the one favour of a new game.
            var stepped = ResidentProfile.Defaults().Where(p => ResidentRequestRules.Tier(p.GuildOpinion) > 0).Select(p => p.Job).ToArray();
            Assert.That(stepped, Is.EqualTo(new[] { ResidentJob.Child }));
        }

        [Test]
        public void WhatTheyAsk_AndHowTheyThankTheGuild()
        {
            Assert.That(ResidentRequestRules.KindsOf(ResidentJob.Child), Does.Contain(RequestKind.Story));
            Assert.That(ResidentRequestRules.KindsOf(ResidentJob.Baker), Does.Contain(RequestKind.Delivery));
            Assert.That(ResidentRequestRules.KindsOf(ResidentJob.Chief), Does.Contain(RequestKind.Delivery));
            Assert.That(ResidentRequestRules.KindsOf(ResidentJob.Guard), Is.EqualTo(new[] { RequestKind.Goods }));
            Assert.That(ResidentRequestRules.TradeItem(ResidentJob.Blacksmith), Is.EqualTo(ErrandRules.Ore));
            Assert.That(ResidentRequestRules.TradeItem(ResidentJob.Herbalist), Is.EqualTo(ErrandRules.Herb));
            Assert.That(ResidentRequestRules.TradeItem(ResidentJob.Guard), Is.Null, "the others ask a monster's part");
            var rng = new GameRandom(3);
            for (int i = 0; i < 50; i++)
            {
                Assert.That(ResidentRequestRules.WantedCount(ResidentJob.Blacksmith, ErrandRules.Ore, rng), Is.InRange(3, 5));
                Assert.That(ResidentRequestRules.WantedCount(ResidentJob.Herbalist, ErrandRules.Herb, rng), Is.InRange(3, 6));
                Assert.That(ResidentRequestRules.WantedCount(ResidentJob.Guard, "slime_jelly", rng), Is.InRange(2, 4));
            }
            Assert.That(ResidentRequestRules.WantedCount(ResidentJob.Child, "slime_jelly", rng), Is.EqualTo(ErrandRules.MinCount), "a parts errand needs two at least");

            // The thanks: one thing each, by trade.
            var db = DefaultContent.Build();
            db.BuildIndex();
            foreach (ResidentJob job in System.Enum.GetValues(typeof(ResidentJob)))
                foreach (float roll in new[] { 0f, 0.1f, 0.3f, 0.99f })
                {
                    var t = ResidentRequestRules.ThanksOf(job, roll);
                    Assert.That(t.IsEmpty, Is.False, $"{job} {roll}");
                    if (!string.IsNullOrEmpty(t.Item.itemId)) Assert.That(db.Item(t.Item.itemId), Is.Not.Null, $"{job} gives a real item: {t.Item.itemId}");
                }
            Assert.That(ResidentRequestRules.ThanksOf(ResidentJob.Chief, 0f).Gold, Is.EqualTo(ResidentRequestRules.ChiefGold));
            Assert.That(ResidentRequestRules.ThanksOf(ResidentJob.Baker, 0f).Food, Is.EqualTo(ResidentRequestRules.BakerFood));
            Assert.That(ResidentRequestRules.ThanksOf(ResidentJob.Blacksmith, 0f).Item.itemId, Is.EqualTo(ErrandRules.Gear(1)));
            Assert.That(ResidentRequestRules.ThanksOf(ResidentJob.Child, 0.01f).Item.itemId, Is.EqualTo("crown_shard"), "rarely a crown shard");
            Assert.That(ResidentRequestRules.ThanksOf(ResidentJob.Child, 0.9f).Item.itemId, Is.EqualTo("stone_shard"));

            // Their words fill in what they ask.
            Assert.That(ResidentRequestRules.AskLine(ResidentJob.Blacksmith, RequestKind.Goods, "철광석", 4, null), Does.Contain("철광석 x4"));
            Assert.That(ResidentRequestRules.AskLine(ResidentJob.Baker, RequestKind.Delivery, null, 0, "해마루 마을"), Does.Contain("해마루 마을"));
            Assert.That(ResidentRequestRules.TaleLine("군주를 쓰러뜨렸다", 3), Does.Contain("군주를 쓰러뜨렸다"));
            Assert.That(ResidentRequestRules.TaleLine(null, 1), Is.Not.Empty);
        }

        [Test]
        public void AskingHours_AreTheDay()
        {
            Assert.That(ResidentRequestRules.AskingHour(5.9f), Is.False);
            Assert.That(ResidentRequestRules.AskingHour(6f), Is.True);
            Assert.That(ResidentRequestRules.AskingHour(17.9f), Is.True);
            Assert.That(ResidentRequestRules.AskingHour(18f), Is.False, "late enough that nobody could answer before they sleep");
        }
    }
}
