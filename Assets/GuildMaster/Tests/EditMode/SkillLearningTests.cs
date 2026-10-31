using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Units;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// P4-34: three skills at most, learned on level-up. The model picks; these are the rules that
    /// bound what it may pick and what happens when it answers with something unusable.
    /// </summary>
    public class SkillLearningTests
    {
        [Test]
        public void EveryClassPool_IsLearnableAndBigEnoughToForceAChoice()
        {
            foreach (AdventurerClass cls in System.Enum.GetValues(typeof(AdventurerClass)))
            {
                var pool = SkillCatalog.LearnPool(cls);
                Assert.That(pool.Count, Is.GreaterThan(SkillCatalog.MaxSkills),
                            $"{cls} must be able to outgrow the cap, or nothing is ever given up");
                foreach (var id in pool)
                    Assert.That(SkillFactory.Create(id), Is.Not.Null, $"{cls} pool has unknown skill '{id}'");
                Assert.That(pool, Is.Unique, cls.ToString());
            }
        }

        [Test]
        public void StartingSkills_ComeFromTheClassPool()
        {
            foreach (AdventurerClass cls in System.Enum.GetValues(typeof(AdventurerClass)))
            {
                var pool = SkillCatalog.LearnPool(cls);
                foreach (var id in Adventurer.ClassSkills(cls))
                    Assert.That(SkillCatalog.IndexOf(pool, id), Is.GreaterThanOrEqualTo(0),
                                $"{cls} starts with '{id}', which it could never learn");
            }
        }

        [Test]
        public void Candidates_LeaveOutWhatIsAlreadyKnown()
        {
            var known = new List<string> { "taunt", "whirlwind" };
            var candidates = SkillCatalog.Candidates(AdventurerClass.Warrior, known);

            Assert.That(candidates, Does.Not.Contain("taunt"));
            Assert.That(candidates, Does.Not.Contain("whirlwind"));
            Assert.That(candidates, Is.EqualTo(new[] { "charge", "slam" }), "in class order");
            Assert.That(SkillCatalog.Candidates(AdventurerClass.Warrior, SkillCatalog.LearnPool(AdventurerClass.Warrior)), Is.Empty);
        }

        [Test]
        public void ForgettingOnlyStartsAtTheCap()
        {
            Assert.That(SkillCatalog.MustForget(new List<string> { "taunt", "whirlwind" }), Is.False);
            Assert.That(SkillCatalog.MustForget(new List<string> { "taunt", "whirlwind", "charge" }), Is.True);
        }

        [Test]
        public void RuleForget_DropsTheLeastFittingSkill()
        {
            // Warrior order is taunt, whirlwind, charge, slam.
            Assert.That(SkillCatalog.RuleForget(AdventurerClass.Warrior, new List<string> { "taunt", "slam", "whirlwind" }),
                        Is.EqualTo("slam"), "the last of the class order goes");
            Assert.That(SkillCatalog.RuleForget(AdventurerClass.Warrior, new List<string> { "taunt", "heal", "whirlwind" }),
                        Is.EqualTo("heal"), "something off the pool goes first");
            Assert.That(SkillCatalog.RuleForget(AdventurerClass.Warrior, new List<string>()), Is.Null);
        }

        [Test]
        public void RuleChoice_SwapsOnlyForABetterFit_SoItNeverFlipFlops()
        {
            // P4-73 (long run): a full mage dropped blink for slam, then slam for blink, every level.
            var mage = new List<string> { "fireball", "warp", "blink" };
            var pick = SkillCatalog.RuleChoice(AdventurerClass.Mage, mage, SkillCatalog.Candidates(AdventurerClass.Mage, mage));
            Assert.That(pick.learn, Is.Null, "slam fits a mage no better than blink: nothing changes");

            var odd = new List<string> { "fireball", "slam", "charge" };
            pick = SkillCatalog.RuleChoice(AdventurerClass.Mage, odd, SkillCatalog.Candidates(AdventurerClass.Mage, odd));
            Assert.That(pick, Is.EqualTo(("warp", "charge")), "a better fit replaces the worst one");

            var room = new List<string> { "fireball", "blink" };
            Assert.That(SkillCatalog.RuleChoice(AdventurerClass.Mage, room, SkillCatalog.Candidates(AdventurerClass.Mage, room)),
                        Is.EqualTo(("warp", (string)null)), "below the cap nothing has to go");
        }
    }
}
