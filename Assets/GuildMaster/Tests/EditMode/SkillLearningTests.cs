using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
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
        public void Interpret_TakesOnlyWhatWasOffered()
        {
            var candidates = new List<string> { "charge", "slam" };
            var known = new List<string> { "taunt", "whirlwind" };

            Assert.That(SkillPrompt.Interpret(new SkillPrompt.ResponseDto { learn = "charge" }, candidates, known),
                        Is.EqualTo(("charge", (string)null)), "under the cap, nothing is given up");
            Assert.That(SkillPrompt.Interpret(new SkillPrompt.ResponseDto { learn = "fireball" }, candidates, known).learn,
                        Is.Null, "a skill that was not offered is refused");
            Assert.That(SkillPrompt.Interpret(new SkillPrompt.ResponseDto { learn = "" }, candidates, known).learn, Is.Null);
            Assert.That(SkillPrompt.Interpret(null, candidates, known).learn, Is.Null);
        }

        [Test]
        public void Interpret_AtTheCap_NeedsAValidSkillToGiveUp()
        {
            var candidates = new List<string> { "slam" };
            var full = new List<string> { "taunt", "whirlwind", "charge" };

            Assert.That(SkillPrompt.Interpret(new SkillPrompt.ResponseDto { learn = "slam", forget = "charge" }, candidates, full),
                        Is.EqualTo(("slam", "charge")));
            Assert.That(SkillPrompt.Interpret(new SkillPrompt.ResponseDto { learn = "slam", forget = "" }, candidates, full).learn,
                        Is.Null, "at the cap something has to go");
            Assert.That(SkillPrompt.Interpret(new SkillPrompt.ResponseDto { learn = "slam", forget = "fireball" }, candidates, full).learn,
                        Is.Null, "cannot give up a skill it does not have");
        }
    }
}
