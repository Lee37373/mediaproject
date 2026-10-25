using System.Collections.Generic;
using GuildMaster.Adventurers;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// A member's nature and what it lives through (P4-104e): what it values colours its thoughts, close calls leave fears and at
    /// last nerves of steel, small talk moves feelings a little (user decision 2026-10-21) and quarrels come between those who do not
    /// get on, and a social inspiration comes to the sociable at home.
    /// </summary>
    public class NatureTests
    {
        [Test]
        public void WhatItValues_ColoursItsThoughts_AndSteelNervesHalveFear()
        {
            Assert.That(ThoughtRules.TraitScale(ThoughtTone.Glory, null, "명예"), Is.EqualTo(1.5f), "honour seekers love a great kill");
            Assert.That(ThoughtRules.TraitScale(ThoughtTone.Gain, null, "돈"), Is.EqualTo(1.25f));
            Assert.That(ThoughtRules.TraitScale(ThoughtTone.People, null, "동료"), Is.EqualTo(1.25f));
            Assert.That(ThoughtRules.TraitScale(ThoughtTone.Fear, null, "안전"), Is.EqualTo(1.25f));
            Assert.That(ThoughtRules.TraitScale(ThoughtTone.Glory, null, "돈"), Is.EqualTo(1f));
            Assert.That(ThoughtRules.TraitScale(ThoughtTone.Fear, new[] { "coward" }, "안전", new[] { QuirkRules.SteelNerves }),
                Is.EqualTo(1.5f * 1.25f * 0.5f).Within(1e-5f), "all of it together");
        }

        [Test]
        public void CloseCalls_MayLeaveAFear_OncePerKind_AndEnoughOfThemTurnToSteel()
        {
            var quirks = new List<string>();
            Assert.That(QuirkRules.AfterCloseCall(quirks, "skull", 1, QuirkRules.FearChance + 0.01f), Is.Null, "not every time");
            Assert.That(QuirkRules.AfterCloseCall(quirks, "skull", 2, 0f), Is.EqualTo(QuirkRules.Fear("skull")));
            Assert.That(QuirkRules.AfterCloseCall(quirks, "skull", 3, 0f), Is.Null, "one fear of a kind");
            Assert.That(QuirkRules.Fears(quirks, "skull"), Is.True);
            Assert.That(QuirkRules.AfterCloseCall(quirks, "goblin", QuirkRules.SteelNervesAfter, 0f), Is.EqualTo(QuirkRules.SteelNerves));
            Assert.That(quirks, Is.EquivalentTo(new[] { QuirkRules.SteelNerves }), "the fears are gone");
            Assert.That(QuirkRules.AfterCloseCall(quirks, "slime", 9, 0f), Is.Null, "nothing frightens it now");
            Assert.That(QuirkRules.Label(QuirkRules.Fear("skull"), id => "해골"), Is.EqualTo("해골 공포"));
        }

        [Test]
        public void Talk_MovesFeelingsALittle_QuarrelsComeBetweenThoseWhoDoNotGetOn()
        {
            Assert.That(BanterRules.FriendlyChange(false, false), Is.EqualTo(1));
            Assert.That(BanterRules.FriendlyChange(true, false), Is.EqualTo(2), "the like-minded");
            Assert.That(BanterRules.FriendlyChange(false, true), Is.EqualTo(2), "by the fire");
            Assert.That(BanterRules.QuarrelChange(false), Is.EqualTo(-2));
            Assert.That(BanterRules.QuarrelChange(true), Is.EqualTo(-3), "an abrasive one in it");

            float friends = BanterRules.QuarrelChance(40, true, false);
            float strangers = BanterRules.QuarrelChance(0, false, false);
            float sour = BanterRules.QuarrelChance(-20, false, false);
            float enemies = BanterRules.QuarrelChance(-60, false, true);
            Assert.That(friends, Is.LessThan(strangers));
            Assert.That(strangers, Is.LessThan(sour));
            Assert.That(sour, Is.LessThan(enemies));
            Assert.That(enemies, Is.LessThanOrEqualTo(1f));
        }

        [Test]
        public void ASocialInspiration_ComesToTheSociableAtHome()
        {
            Assert.That(BreakRules.ChooseInspiration(false, new[] { "sociable" }, 0.5f), Is.EqualTo(InspirationKind.Social));
            Assert.That(BreakRules.ChooseInspiration(false, null, 0.5f), Is.EqualTo(InspirationKind.Training));
            Assert.That(BreakRules.ChooseInspiration(true, new[] { "sociable" }, 0.5f), Is.EqualTo(InspirationKind.Combat), "out fighting, never");
        }
    }
}
