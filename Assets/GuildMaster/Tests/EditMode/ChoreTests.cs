using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Economy;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Residents' odd jobs (일손 돕기, user decision 2026-10-29): small income from minigames, once a day each.</summary>
    public class ChoreTests
    {
        [Test]
        public void Pay_AndOpinion_GrowWithHowWellItWent()
        {
            Assert.That(ChoreRules.Pay(0f), Is.EqualTo(0));
            Assert.That(ChoreRules.Pay(1f), Is.EqualTo(ChoreRules.MaxPay));
            Assert.That(ChoreRules.Pay(0.5f), Is.GreaterThan(0).And.LessThan(ChoreRules.MaxPay));
            Assert.That(ChoreRules.Pay(0.5f) % 5, Is.EqualTo(0));
            Assert.That(ChoreRules.Opinion(1f), Is.GreaterThan(ChoreRules.Opinion(0.5f)));
            Assert.That(ChoreRules.Opinion(0.1f), Is.EqualTo(0));
        }

        [Test]
        public void OncePerDayEach_AndOnlyWhileResidentsAreUp()
        {
            var treasury = new GuildTreasury(0, 0, 0f, 0);
            var service = new ChoreService(treasury);
            Assert.That(service.CanDo(ChoreKind.Forge, 10f, 10f).Ok, Is.True);
            Assert.That(service.CanDo(ChoreKind.Forge, 23f, 23f).Ok, Is.False, "asleep at night");
            service.Begin(ChoreKind.Forge, 10f);
            Assert.That(service.CanDo(ChoreKind.Forge, 12f, 12f).Ok, Is.False, "done today");
            Assert.That(service.CanDo(ChoreKind.Herbs, 12f, 12f).Ok, Is.True, "another resident");
            Assert.That(service.CanDo(ChoreKind.Forge, 34f, 10f).Ok, Is.True, "next day");
            var (pay, _) = service.Complete(ChoreKind.Forge, 1f);
            Assert.That(treasury.Gold, Is.EqualTo(pay));
            Assert.That(GoldCategories(), Is.True);
        }

        static bool GoldCategories() => Reports.GoldCategories.Classify(GoldReason.SideJob, 10) == Reports.GoldCategory.SideJob;

        [Test]
        public void Forge_HitsTheMiddleForFullMarks()
        {
            Assert.That(ForgeGame.Quality(0.5f, 0.5f, 0.2f), Is.EqualTo(1f));
            Assert.That(ForgeGame.Quality(0.58f, 0.5f, 0.2f), Is.EqualTo(0.6f));
            Assert.That(ForgeGame.Quality(0.9f, 0.5f, 0.2f), Is.EqualTo(0f));
            var game = new ForgeGame(new GameRandom(1));
            float speed = game.Speed;
            for (int i = 0; i < ForgeGame.Strikes; i++)
            {
                game.Set(0.5f, 0.5f, game.ZoneWidth);
                Assert.That(game.Strike(), Is.EqualTo(1f));
            }
            Assert.That(game.Over, Is.True);
            Assert.That(game.Score, Is.EqualTo(1f).Within(0.001f));
            Assert.That(game.Speed, Is.GreaterThan(speed), "faster with every strike");
            game.Tick(0.5f);
            Assert.That(game.Needle, Is.InRange(0f, 1f));
        }

        [Test]
        public void Herbs_PairsMatchAndMissesCost()
        {
            var game = new HerbGame(new[] { 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5 });
            Assert.That(game.Pick(0), Is.EqualTo(HerbGame.Flip.First));
            Assert.That(game.Pick(0), Is.EqualTo(HerbGame.Flip.Ignored), "same card twice");
            Assert.That(game.Pick(2), Is.EqualTo(HerbGame.Flip.Miss));
            Assert.That(game.IsFaceUp(2), Is.True);
            Assert.That(game.Pick(0), Is.EqualTo(HerbGame.Flip.First), "the next pick turns the miss back over");
            Assert.That(game.IsFaceUp(2), Is.False);
            Assert.That(game.Pick(1), Is.EqualTo(HerbGame.Flip.Match));
            for (int i = 2; i < 12; i += 2) { game.Pick(i); game.Pick(i + 1); }
            Assert.That(game.Over, Is.True);
            Assert.That(game.Score, Is.EqualTo(1f), "one miss is free");

            var sloppy = new HerbGame(new[] { 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5 });
            for (int m = 0; m < 10; m++) { sloppy.Pick(0); sloppy.Pick(2); }
            for (int i = 0; i < 12; i += 2) { sloppy.Pick(i); sloppy.Pick(i + 1); }
            Assert.That(sloppy.Score, Is.LessThan(1f));
            var slow = new HerbGame(new GameRandom(2));
            slow.Tick(HerbGame.TimeLimit + 1f);
            Assert.That(slow.Over, Is.True);
            Assert.That(slow.Score, Is.EqualTo(0f));
        }

        [Test]
        public void Dough_OnTheBeatScores_MashingDoesNot()
        {
            var notes = new List<float> { 1f, 2f, 3f, 4f };
            var game = new DoughGame(notes);
            foreach (var t in notes)
            {
                game.Tick(t - game.Time);
                Assert.That(game.Press(), Is.EqualTo(1f));
            }
            game.Tick(1f);
            Assert.That(game.Over, Is.True);
            Assert.That(game.Score, Is.EqualTo(1f));

            var masher = new DoughGame(new List<float> { 1f, 2f, 3f, 4f });
            for (int i = 0; i < 100; i++) { masher.Tick(0.05f); masher.Press(); }
            Assert.That(masher.Strays, Is.GreaterThan(0));
            Assert.That(masher.Score, Is.LessThan(0.5f), "mashing does not pay");
            Assert.That(new DoughGame(new GameRandom(3)).Notes.Count, Is.InRange(4, DoughGame.Beats));
        }
    }
}
