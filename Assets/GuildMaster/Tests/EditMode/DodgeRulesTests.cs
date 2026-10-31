using GuildMaster.Units;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Reacting to a hostile telegraph (P4-95): quick with a chance that grows with the tier, else after a moment.</summary>
    public class DodgeRulesTests
    {
        [Test]
        public void TheQuickRead_GetsLikelierWithEveryTier_ButNeverCertain()
        {
            var tiers = (CombatTier[])System.Enum.GetValues(typeof(CombatTier));
            for (int i = 1; i < tiers.Length; i++)
                Assert.That(DodgeRules.QuickChance(tiers[i]), Is.GreaterThan(DodgeRules.QuickChance(tiers[i - 1])), $"{tiers[i]} over {tiers[i - 1]}");
            Assert.That(DodgeRules.QuickChance(CombatTier.Heroic), Is.LessThan(1f), "even a hero is sometimes caught");
            Assert.That(DodgeRules.QuickChance(CombatTier.Novice), Is.LessThan(0.5f), "a novice mostly is");
        }

        [Test]
        public void TheRoll_PicksQuickOrSlow_AndTheSpreadPlacesTheSlowOne()
        {
            float heroic = DodgeRules.QuickChance(CombatTier.Heroic);
            Assert.That(DodgeRules.ReactionDelay(CombatTier.Heroic, heroic - 0.01f, 0.9f), Is.EqualTo(DodgeRules.QuickReaction));
            Assert.That(DodgeRules.ReactionDelay(CombatTier.Heroic, heroic + 0.01f, 0f), Is.EqualTo(DodgeRules.SlowReactionMin));
            Assert.That(DodgeRules.ReactionDelay(CombatTier.Novice, 0.5f, 1f), Is.EqualTo(DodgeRules.SlowReactionMax));
            // The same roll that is quick for a veteran is slow for a novice.
            Assert.That(DodgeRules.ReactionDelay(CombatTier.Veteran, 0.5f, 0.5f), Is.EqualTo(DodgeRules.QuickReaction));
            Assert.That(DodgeRules.ReactionDelay(CombatTier.Novice, 0.5f, 0.5f), Is.GreaterThan(DodgeRules.QuickReaction));
            // Slow enough that a short warning catches them: the dragon's tail sweep goes off before the slowest even move.
            Assert.That(DodgeRules.SlowReactionMin, Is.GreaterThan(Monsters.Dragon.TailWindup));
        }
    }
}
