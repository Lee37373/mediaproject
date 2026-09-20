using GuildMaster.Adventurers;
using GuildMaster.Ecosystem;
using GuildMaster.Units;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>
    /// P4-19: walking a pursuer into someone else's territory is not enough on its own — it is
    /// right behind and follows straight back out. These are the rules for cutting that thread.
    /// </summary>
    public class LureSkillTests
    {
        [Test]
        public void BreakAway_NeedsToBeAtTheSpotWithThePursuerOnOurHeels()
        {
            var spot = new Vector2(10f, 0f);
            var atSpot = new Vector2(10.5f, 0f);
            var closeBehind = new Vector2(12f, 0f);

            Assert.That(TerritoryTactics.ShouldBreakAway(atSpot, spot, closeBehind), Is.True);
            Assert.That(TerritoryTactics.ShouldBreakAway(new Vector2(4f, 0f), spot, closeBehind), Is.False,
                        "still on the way: the pursuer has not been led far enough");
            Assert.That(TerritoryTactics.ShouldBreakAway(atSpot, spot, new Vector2(30f, 0f)), Is.False,
                        "nothing worth spending a skill on when the chase is already lost");
        }

        [Test]
        public void BreakAway_UsesTheLurePointNotTheTerritory()
        {
            // The spot is what the guild picked; territory radii are simulation values an
            // adventurer must not read (information hiding, P3-2c).
            var spot = Vector2.zero;
            Assert.That(TerritoryTactics.ShouldBreakAway(Vector2.zero, spot, new Vector2(TerritoryTactics.CloseBehind - 0.1f, 0f)), Is.True);
            Assert.That(TerritoryTactics.ShouldBreakAway(Vector2.zero, spot, new Vector2(TerritoryTactics.CloseBehind + 0.1f, 0f)), Is.False);
            Assert.That(TerritoryTactics.ShouldBreakAway(new Vector2(TerritoryTactics.ArrivedDistance + 0.1f, 0f), spot, Vector2.zero), Is.False);
        }

        [Test]
        public void TheNewSkillsExist_AndAreLearnable()
        {
            Assert.That(SkillFactory.Create("stealth"), Is.InstanceOf<StealthSkill>());
            Assert.That(SkillFactory.Create("warp"), Is.InstanceOf<WarpSkill>());
            // Anyone may learn them (2026-09-20 user decision); the lure itself stays veteran+.
            Assert.That(SkillCatalog.IndexOf(SkillCatalog.LearnPool(AdventurerClass.Ranger), "stealth"),
                        Is.GreaterThanOrEqualTo(0), "the ranger hides");
            Assert.That(SkillCatalog.IndexOf(SkillCatalog.LearnPool(AdventurerClass.Mage), "warp"),
                        Is.GreaterThanOrEqualTo(0), "the mage hops");
        }

        [Test]
        public void StealthAndWarp_HaveCooldownsLongEnoughToBeADecision()
        {
            var stealth = SkillFactory.Create("stealth");
            var warp = SkillFactory.Create("warp");
            Assert.That(stealth.Cooldown, Is.GreaterThan(StealthSkill.Duration),
                        "hiding must not be available again the moment it wears off");
            Assert.That(warp.Cooldown, Is.GreaterThan(5f));
            Assert.That(stealth.Description, Is.Not.Empty, "shown to the player in the skill prompt");
            Assert.That(warp.Description, Is.Not.Empty);
        }
    }
}
