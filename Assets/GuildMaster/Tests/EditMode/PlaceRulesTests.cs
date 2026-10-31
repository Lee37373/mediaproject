using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Places;
using GuildMaster.World;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// When a member goes to a place worth going to and what it brings back (P4-105b): a soak when stressed, tired or low, at
    /// once when very stressed; a prayer when low or valuing safety or honour, not again too soon; gathering first for those
    /// who would rather, otherwise on a day off the hunt; only safe ground close enough to town.
    /// </summary>
    public class PlaceRulesTests
    {
        [Test]
        public void ASoak_IsWantedWhenStressedTiredOrLow_AndNeededWhenVeryStressed()
        {
            Assert.That(PlaceRules.WantsSpring(10f, 10f, 60f), Is.False, "fine");
            Assert.That(PlaceRules.WantsSpring(PlaceRules.SpringStressFrom, 10f, 60f), Is.True, "stressed");
            Assert.That(PlaceRules.WantsSpring(10f, PlaceRules.SpringFatigueFrom, 60f), Is.True, "tired");
            Assert.That(PlaceRules.WantsSpring(10f, 10f, PlaceRules.SpringMoodBelow - 1f), Is.True, "low");
            Assert.That(PlaceRules.NeedsSpring(PlaceRules.UrgentSpringStress - 1f), Is.False);
            Assert.That(PlaceRules.NeedsSpring(PlaceRules.UrgentSpringStress), Is.True);
        }

        [Test]
        public void APrayer_IsWantedWhenLowOrByItsValues_AndNotAgainTooSoon()
        {
            Assert.That(PlaceRules.WantsShrine(60f, "돈", 100f, 0f), Is.False, "fine and valuing money");
            Assert.That(PlaceRules.WantsShrine(PlaceRules.ShrineMoodBelow - 1f, "돈", 100f, 0f), Is.True, "low");
            Assert.That(PlaceRules.WantsShrine(60f, "안전", 100f, 0f), Is.True, "safety");
            Assert.That(PlaceRules.WantsShrine(60f, "명예", 100f, 0f), Is.True, "honour");
            Assert.That(PlaceRules.WantsShrine(10f, "안전", 100f, 130f), Is.False, "prayed too lately");
        }

        [Test]
        public void Gathering_ComesFirstForNewcomersTheCarefulAndTheDiligent_AndBringsItsYield()
        {
            Assert.That(PlaceRules.GathersFirst(false, "돈", new List<string> { "brave" }), Is.False);
            Assert.That(PlaceRules.GathersFirst(true, "돈", null), Is.True, "a newcomer");
            Assert.That(PlaceRules.GathersFirst(false, "안전", null), Is.True, "safety");
            Assert.That(PlaceRules.GathersFirst(false, "돈", new List<string> { "diligent" }), Is.True, "diligent");
            // Against a hunt's 1 in the roam layer: first above it, otherwise below.
            Assert.That(PlaceRules.GatherFirstScore, Is.GreaterThan(1f));
            Assert.That(PlaceRules.GatherScore, Is.LessThan(1f));
            Assert.That(PlaceRules.SpringScore, Is.GreaterThan(1f));
            Assert.That(PlaceRules.ShrineScore, Is.GreaterThan(1f));
            Assert.That(PlaceRules.UrgentSpringScore, Is.LessThan(0.8f), "after rest, a potion and gear");

            Assert.That((PlaceRules.GatherCount(PlaceKind.OreVein, 0f), PlaceRules.GatherCount(PlaceKind.OreVein, 0.999f)),
                Is.EqualTo((PlaceRules.OreMin, PlaceRules.OreMax)));
            Assert.That((PlaceRules.GatherCount(PlaceKind.HerbPatch, 0f), PlaceRules.GatherCount(PlaceKind.HerbPatch, 0.999f)),
                Is.EqualTo((PlaceRules.HerbMin, PlaceRules.HerbMax)));
            Assert.That((PlaceRules.GatherItem(PlaceKind.OreVein), PlaceRules.GatherItem(PlaceKind.HerbPatch)), Is.EqualTo(("iron_ore", "herb")));
            Assert.That((PlaceRules.Hours(PlaceKind.HotSpring), PlaceRules.Hours(PlaceKind.Shrine), PlaceRules.Hours(PlaceKind.HerbPatch)),
                Is.EqualTo((PlaceRules.SpringHours, PlaceRules.ShrineHours, PlaceRules.GatherHours)));
        }

        [Test]
        public void OnlySafeGround_CloseEnoughToTown()
        {
            Assert.That(PlaceRules.SafeFor(5, 3f, 100f), Is.True, "born two above its power");
            Assert.That(PlaceRules.SafeFor(6, 3f, 100f), Is.False, "three above");
            Assert.That(PlaceRules.SafeFor(1, 10f, PlaceRules.MaxDistanceFromTown + 1f), Is.False, "too far out");
            Assert.That(PlaceRules.SafeFor(1, 10f, 50f, reach: 40f), Is.False, "beyond a newcomer's own ground");
        }

        [Test]
        public void TheRoamLayer_TakesAWantedSoakOverAHunt_AndAHuntOverAnOrdinaryGathering()
        {
            ThinkOption O(AdventurerAction action, float score) => new() { Layer = ThinkLayer.Roam, Action = action, Score = score };
            var soak = new List<ThinkOption> { O(AdventurerAction.Hunt, 1f), O(AdventurerAction.Visit, PlaceRules.SpringScore) };
            Assert.That(ThinkTree.Pick(soak, 0, () => 0f), Is.EqualTo(1));
            var gather = new List<ThinkOption> { O(AdventurerAction.Hunt, 1f), O(AdventurerAction.Visit, PlaceRules.GatherScore) };
            Assert.That(ThinkTree.Pick(gather, 0, () => 0f), Is.EqualTo(0));
            var dayOff = new List<ThinkOption> { O(AdventurerAction.Visit, PlaceRules.GatherScore) };
            Assert.That(ThinkTree.Pick(dayOff, 0, () => 0f), Is.EqualTo(0), "staying off the hunt, it gathers");
        }
    }
}
