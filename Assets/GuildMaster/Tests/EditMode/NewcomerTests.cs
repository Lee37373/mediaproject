using GuildMaster.Adventurers;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Newcomers (P4-99, user decisions 2026-10-21): who is one, how far one hunts on its own, and what the training ground
    /// pays — more as the days go by and the further a member is behind the guild's best.
    /// </summary>
    public class NewcomerTests
    {
        /// <summary>Birth level by distance as the 256-map inner land has it: innermost site 20 cells out, its edge 180 (level 15).</summary>
        static int LevelAt(float distance) => 1 + Mathf.RoundToInt(Mathf.Clamp01((distance - 20f) / 160f) * 14f);

        [Test]
        public void ANewcomer_IsWeakerThanTheNearRingsYoung()
        {
            int near = LevelAt(NewcomerRules.NearRing);
            Assert.That(near, Is.GreaterThan(1), "the near ring breeds above level 1");
            Assert.That(NewcomerRules.IsNewcomer(1f * 0.75f, near), Is.True, "a fresh Lv1 at the weakened base");
            Assert.That(NewcomerRules.IsNewcomer(near, near), Is.False, "as strong as them: no newcomer");
        }

        [Test]
        public void ANewcomer_HuntsWhereTheYoungAreNoStrongerThanIt_NeverPastTheNearRing()
        {
            float weakest = NewcomerRules.Reach(0.75f, 20f, LevelAt);
            Assert.That(weakest, Is.EqualTo(20f + NewcomerRules.InnerMargin), "the weakest just past the innermost dens");
            float stronger = NewcomerRules.Reach(2.25f, 20f, LevelAt);
            Assert.That(stronger, Is.GreaterThan(weakest), "stronger, further");
            Assert.That(LevelAt(stronger), Is.LessThanOrEqualTo(2), "but only where the young are no stronger than it");
            Assert.That(NewcomerRules.Reach(50f, 20f, LevelAt), Is.EqualTo(NewcomerRules.NearRing), "never past the near ring");
            Assert.That(NewcomerRules.Reach(0.75f, 60f, LevelAt), Is.EqualTo(NewcomerRules.NearRing), "a map whose dens start further out: the near ring");
        }

        [Test]
        public void ANewcomersQuests_KeepToTheSameGround_AsItsHunts()
        {
            Assert.That(NewcomerRules.WithinReach(0.75f, 1), Is.True, "level 1 ground is everyone's");
            Assert.That(NewcomerRules.WithinReach(0.75f, 3), Is.False, "a Lv1's skull hunt 52 cells out (young of level 3)");
            Assert.That(NewcomerRules.WithinReach(1.5f, 2), Is.False, "a Lv2 at the weakened base is not up to level 2 young");
            Assert.That(NewcomerRules.WithinReach(2.25f, 2), Is.True);
        }

        [Test]
        public void AQuestBeyondItsGround_IsADare_ByTheRiskAndItsNature()
        {
            // P4-100 (user decision 2026-10-21): not barred — by the danger, the dice and its nature.
            float fair = NewcomerRules.DareChance(QuestRisk.Fair, 0f, "돈");
            Assert.That(fair, Is.EqualTo(NewcomerRules.DareFair));
            Assert.That(NewcomerRules.DareChance(QuestRisk.Easy, 0f, "돈"), Is.GreaterThan(fair), "the easier, the likelier");
            Assert.That(NewcomerRules.DareChance(QuestRisk.Risky, 0f, "돈"), Is.LessThan(fair));
            Assert.That(NewcomerRules.DareChance(QuestRisk.Reckless, -1f, "명예"), Is.Zero, "never when 무모");
            Assert.That(NewcomerRules.DareChance(QuestRisk.Fair, -1f, "돈"), Is.GreaterThan(fair), "the brave dare more");
            Assert.That(NewcomerRules.DareChance(QuestRisk.Fair, 1f, "돈"), Is.LessThan(fair), "the timid less");
            Assert.That(NewcomerRules.DareChance(QuestRisk.Fair, 0f, "안전"), Is.LessThan(fair), "those who value safety less");
            Assert.That(NewcomerRules.DareChance(QuestRisk.Fair, 0f, "명예"), Is.GreaterThan(fair), "those who value honour more");
            Assert.That(NewcomerRules.DareChance(QuestRisk.Easy, -1f, "명예"), Is.EqualTo(NewcomerRules.MaxDare), "within a cap");
        }

        [Test]
        public void Training_PaysMore_AsTheDaysGoBy_AndTheFurtherBehindTheBest()
        {
            Assert.That(NewcomerRules.TrainingExp(0f, 10f, 10), Is.Zero, "a ground that teaches nothing pays nothing");
            int topNow = NewcomerRules.TrainingExp(6f, 0f, 0);
            Assert.That(topNow, Is.EqualTo(6), "the guild's best, on day one: the ground's own rate");
            Assert.That(NewcomerRules.TrainingExp(6f, 10f, 0), Is.GreaterThan(topNow), "later in the game");
            int behind = NewcomerRules.TrainingExp(6f, 0f, 8);
            Assert.That(behind, Is.GreaterThan(topNow * 2), "far behind, it catches up fast");
            Assert.That(NewcomerRules.TrainingExp(6f, 0f, 100), Is.EqualTo(NewcomerRules.TrainingExp(6f, 0f, NewcomerRules.MaxCatchUpLevels)), "within a cap");
        }
    }
}
