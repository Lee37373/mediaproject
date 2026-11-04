using System.Collections.Generic;
using System.Linq;
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
            Assert.That(candidates, Is.EqualTo(new[] { "charge", "slam", "ironwall", "execute", "warcry" }), "in class order");
            Assert.That(SkillCatalog.Candidates(AdventurerClass.Warrior, known, 4), Is.EqualTo(new[] { "charge", "slam" }), "a low level opens the basic grade only");
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
            var pick = SkillCatalog.RuleChoice(AdventurerClass.Mage, mage, SkillCatalog.Candidates(AdventurerClass.Mage, mage), 4);
            Assert.That(pick.learn, Is.Null, "slam fits a mage no better than blink: nothing changes");

            var odd = new List<string> { "fireball", "slam", "charge" };
            pick = SkillCatalog.RuleChoice(AdventurerClass.Mage, odd, SkillCatalog.Candidates(AdventurerClass.Mage, odd), 4);
            Assert.That(pick, Is.EqualTo(("warp", "charge")), "a better fit replaces the worst one");

            var room = new List<string> { "fireball", "blink" };
            Assert.That(SkillCatalog.RuleChoice(AdventurerClass.Mage, room, SkillCatalog.Candidates(AdventurerClass.Mage, room), 4),
                        Is.EqualTo(("warp", (string)null)), "below the cap nothing has to go");
        }

        [Test]
        public void Grades_OpenWithTheLevel_AndAHigherGradePushesOutALowerOne()
        {
            // P4-126 (user decision 2026-11-04): adept at Lv8, master at Lv15.
            Assert.That(SkillCatalog.RequiredLevel("taunt"), Is.EqualTo(1));
            Assert.That(SkillCatalog.RequiredLevel("execute"), Is.EqualTo(SkillCatalog.AdeptLevel));
            Assert.That(SkillCatalog.RequiredLevel("meteor"), Is.EqualTo(SkillCatalog.MasterLevel));
            foreach (AdventurerClass cls in System.Enum.GetValues(typeof(AdventurerClass)))
            {
                var pool = SkillCatalog.LearnPool(cls);
                Assert.That(pool.Count(id => SkillCatalog.Grade(id) == 1), Is.EqualTo(2), $"{cls}: two adept skills");
                Assert.That(pool.Count(id => SkillCatalog.Grade(id) == 2), Is.EqualTo(1), $"{cls}: one master skill");
            }

            var warrior = new List<string> { "taunt", "whirlwind", "charge" };
            Assert.That(SkillCatalog.RuleChoice(AdventurerClass.Warrior, warrior, SkillCatalog.Candidates(AdventurerClass.Warrior, warrior, 7), 7),
                        Is.EqualTo(("slam", (string)null)).Or.EqualTo(((string)null, (string)null)), "Lv7: the basic grade only");
            var (learn, forget) = SkillCatalog.RuleChoice(AdventurerClass.Warrior, warrior, SkillCatalog.Candidates(AdventurerClass.Warrior, warrior, 8), 8);
            Assert.That(SkillCatalog.Grade(learn), Is.EqualTo(1), "Lv8 learns an adept skill");
            Assert.That(SkillCatalog.Grade(forget), Is.EqualTo(0), "and gives up a basic one");
            (learn, _) = SkillCatalog.RuleChoice(AdventurerClass.Warrior, warrior, SkillCatalog.Candidates(AdventurerClass.Warrior, warrior, 15), 15);
            Assert.That(learn, Is.EqualTo("warcry"), "Lv15 goes for the master skill first");
        }

        [Test]
        public void Tastes_DifferByPerson_ButStayTheSame_AndNeverFlipFlop()
        {
            // Two of a class need not carry the same three, and one person's choice never changes on its own.
            var picks = new HashSet<string>();
            for (int i = 0; i < 40; i++)
            {
                string person = "NPC_" + i;
                Assert.That(SkillCatalog.Taste(person, "taunt"), Is.EqualTo(SkillCatalog.Taste(person, "taunt")), "fixed per person");
                var known = new List<string>();
                for (int level = 2; level <= 20; level++)
                {
                    var (learn, forget) = SkillCatalog.RuleChoice(AdventurerClass.Warrior, known, SkillCatalog.Candidates(AdventurerClass.Warrior, known, level), level, person);
                    if (learn == null) continue;
                    Assert.That(forget, Is.Not.EqualTo(learn));
                    if (forget != null) known.Remove(forget);
                    known.Add(learn);
                    // Whatever was given up is not taken back next level.
                    var again = SkillCatalog.RuleChoice(AdventurerClass.Warrior, known, SkillCatalog.Candidates(AdventurerClass.Warrior, known, level), level, person);
                    Assert.That(again.learn == null || again.learn != forget, Is.True, $"{person} took back {forget} at Lv{level}");
                }
                Assert.That(known.Count, Is.EqualTo(SkillCatalog.MaxSkills));
                Assert.That(known, Does.Contain("warcry"), $"{person} ends with the master skill");
                picks.Add(string.Join(",", known.OrderBy(x => x)));
            }
            Assert.That(picks.Count, Is.GreaterThan(1), "not every Lv20 warrior carries the same three");
        }

        [Test]
        public void Boons_RaiseAndSoak_AndTheStrongerOfTwoStays()
        {
            var b = new Boons();
            Assert.That(b.Any, Is.False);
            b.Brace(0.5f, 10f);
            Assert.That(b.Soak(10f), Is.EqualTo(5f), "a guard of half halves the blow");
            b.Ward(3f, 10f);
            Assert.That(b.Soak(10f), Is.EqualTo(2f), "the shield takes 3 of the 5 left");
            Assert.That(b.Shield, Is.EqualTo(0f), "and is spent");
            b.Empower(0.25f, 10f);
            b.Empower(0.1f, 10f);
            Assert.That(b.Attack, Is.EqualTo(0.25f), "the stronger share stays");
            var saved = b.Capture();
            var c = new Boons();
            c.Restore(saved);
            Assert.That(c.Attack, Is.EqualTo(0.25f));
            Assert.That(c.Guard, Is.EqualTo(0.5f));
            b.Clear();
            Assert.That(b.Any, Is.False);
            Assert.That(b.Capture(), Is.Null, "nothing to save");
        }

        [Test]
        public void EveryNewSkill_HasANameAndADescription()
        {
            foreach (var id in new[] { "ironwall", "execute", "warcry", "poison_arrow", "snare", "volley", "holy_shield", "smite", "bless",
                                       "chain_lightning", "frost_ring", "meteor", "poison_sting", "drain", "spores", "tongue", "frenzy",
                                       "slime_burst", "blizzard", "pounce" })
            {
                var s = SkillFactory.Create(id);
                Assert.That(s, Is.Not.Null, id);
                Assert.That(s.Name, Is.Not.Empty, id);
                Assert.That(s.Description, Is.Not.Empty, id);
                Assert.That(s.Cooldown, Is.GreaterThan(0f), id);
            }
        }
    }
}
