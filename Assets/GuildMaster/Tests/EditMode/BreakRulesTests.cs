using GuildMaster.Adventurers;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Mental breaks and inspirations (P4-104d): how low a member must be to break and how badly, which break fits where it stands
    /// and what it is like, what a spree and a fistfight cost, and which inspiration comes.
    /// </summary>
    public class BreakRulesTests
    {
        static BreakContext Out(bool party = true) => new() { OnQuest = true, WithParty = party, AnyoneNear = true };
        static BreakContext InTown(int gold = 0, bool rival = false, string values = "모험", params string[] traits)
            => new() { InTownFree = true, Gold = gold, RivalInTown = rival, AnyoneNear = true, Values = values, TraitIds = traits };

        [Test]
        public void TheLowerTheMood_TheWorseTheBreak()
        {
            Assert.That(BreakRules.SeverityAt(30f), Is.EqualTo(BreakSeverity.None));
            Assert.That(BreakRules.SeverityAt(20f), Is.EqualTo(BreakSeverity.Minor));
            Assert.That(BreakRules.SeverityAt(10f), Is.EqualTo(BreakSeverity.Major));
            Assert.That(BreakRules.SeverityAt(2f), Is.EqualTo(BreakSeverity.Extreme));
            Assert.That(BreakRules.ChanceOf(BreakSeverity.None), Is.Zero);
            Assert.That(BreakRules.ChanceOf(BreakSeverity.Extreme), Is.GreaterThan(BreakRules.ChanceOf(BreakSeverity.Minor)));
        }

        [Test]
        public void OutOnWork_AMinorBreakTrailsTheParty_AMajorDeserts_TheWorstGoesBerserk()
        {
            Assert.That(BreakRules.Choose(BreakSeverity.Minor, Out(), 0.5f), Is.EqualTo(BreakKind.Sabotage));
            Assert.That(BreakRules.Choose(BreakSeverity.Major, Out(), 0.5f), Is.EqualTo(BreakKind.Desert));
            Assert.That(BreakRules.Choose(BreakSeverity.Extreme, Out(), 0.99f), Is.EqualTo(BreakKind.Berserk));
        }

        [Test]
        public void InTown_AMinorBreakWandersOrSquanders_TheMoneyMindedSquanderMore()
        {
            Assert.That(BreakRules.Choose(BreakSeverity.Minor, InTown(gold: 5), 0f), Is.EqualTo(BreakKind.Wander), "nothing to squander");
            Assert.That(BreakRules.Choose(BreakSeverity.Minor, InTown(gold: 100), 0.5f), Is.EqualTo(BreakKind.Wander));
            Assert.That(BreakRules.Choose(BreakSeverity.Minor, InTown(gold: 100, values: "돈"), 0.5f), Is.EqualTo(BreakKind.Spree));
            Assert.That(BreakRules.Choose(BreakSeverity.Minor, InTown(gold: 100), 0.2f), Is.EqualTo(BreakKind.Spree));
        }

        [Test]
        public void InTown_AMajorBreakComesToBlows_WithSomeoneItDislikes()
        {
            Assert.That(BreakRules.Choose(BreakSeverity.Major, InTown(rival: true), 0.5f), Is.EqualTo(BreakKind.Brawl));
            Assert.That(BreakRules.Choose(BreakSeverity.Major, InTown(rival: false), 0.5f), Is.EqualTo(BreakKind.Wander), "nobody to fight: it wanders");
        }

        [Test]
        public void AtTheWorst_TheBoldGoBerserk_TheTimidQuit()
        {
            Assert.That(BreakRules.Choose(BreakSeverity.Extreme, InTown(values: "명예"), 0.6f), Is.EqualTo(BreakKind.Berserk));
            Assert.That(BreakRules.Choose(BreakSeverity.Extreme, InTown(values: "안전"), 0.6f), Is.EqualTo(BreakKind.Quit));
            Assert.That(BreakRules.Choose(BreakSeverity.Extreme, InTown(values: "동료", traits: "coward"), 0.3f), Is.EqualTo(BreakKind.Quit));
            var alone = new BreakContext { InTownFree = true, AnyoneNear = false };
            Assert.That(BreakRules.Choose(BreakSeverity.Extreme, alone, 0f), Is.EqualTo(BreakKind.Quit), "nobody to go for");
            var busy = new BreakContext();
            Assert.That(BreakRules.Choose(BreakSeverity.Minor, busy, 0f), Is.EqualTo(BreakKind.None), "not free, not out: nothing fits");
        }

        [Test]
        public void ASpreeThrowsAwayAShare_AFistfightNeverWounds()
        {
            Assert.That(BreakRules.SpreeCost(100, 0f), Is.EqualTo(40));
            Assert.That(BreakRules.SpreeCost(100, 1f), Is.EqualTo(70));
            Assert.That(BreakRules.SpreeCost(1, 0f), Is.EqualTo(1), "at least something");
            Assert.That(BreakRules.SpreeCost(0, 0.5f), Is.Zero);

            Assert.That(BreakRules.BrawlDamage(10f, 100f, 100f), Is.EqualTo(10f), "far from the floor: the blow lands");
            Assert.That(BreakRules.BrawlDamage(10f, 35f, 100f), Is.EqualTo(5f).Within(1e-4f), "down to the floor and no further");
            Assert.That(BreakRules.BrawlDamage(10f, 30f, 100f), Is.Zero);
        }

        [Test]
        public void Inspiration_FightingOutThere_LearningAtHome_QuickLearnersLearn()
        {
            Assert.That(BreakRules.ChooseInspiration(true, null, 0.5f), Is.EqualTo(InspirationKind.Combat));
            Assert.That(BreakRules.ChooseInspiration(false, null, 0.4f), Is.EqualTo(InspirationKind.Training));
            Assert.That(BreakRules.ChooseInspiration(true, new[] { "quick_learner" }, 0.6f), Is.EqualTo(InspirationKind.Training));
        }
    }
}
