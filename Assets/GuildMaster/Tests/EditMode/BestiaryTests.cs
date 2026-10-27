using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Bestiary;
using GuildMaster.Data;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// The bestiary (P4-107, user decisions 2026-10-27): research levels by points (a kind never seen is nowhere), every kind weak
    /// to one class and strong against another, mastery's edge, the lighter look of a studied kind's quests, and a page that
    /// says more the further the research has gone.
    /// </summary>
    public class BestiaryTests
    {
        [Test]
        public void ResearchLevels_GoBySight_ThenByPoints()
        {
            Assert.That(BestiaryRules.Level(500, seen: false), Is.Zero, "never seen: nothing known");
            Assert.That(BestiaryRules.Level(0, true), Is.EqualTo(1), "seen");
            Assert.That(BestiaryRules.Level(BestiaryRules.Level2At - 1, true), Is.EqualTo(1));
            Assert.That(BestiaryRules.Level(BestiaryRules.Level2At, true), Is.EqualTo(2));
            Assert.That(BestiaryRules.Level(BestiaryRules.Level3At, true), Is.EqualTo(3));
            Assert.That((BestiaryRules.NextAt(1), BestiaryRules.NextAt(2), BestiaryRules.NextAt(3)),
                Is.EqualTo((BestiaryRules.Level2At, BestiaryRules.Level3At, 0)));
            Assert.That(BestiaryRules.BossKill, Is.GreaterThan(BestiaryRules.Kill), "a boss teaches more");
        }

        [Test]
        public void EveryKind_IsWeakToOneClass_AndStrongAgainstAnother()
        {
            var db = DefaultContent.Build();
            db.BuildIndex();
            foreach (var s in db.monsters)
            {
                Assert.That(BestiaryRules.TryMatchup(s.id, out var weak, out var strong), Is.True, s.id);
                Assert.That(weak, Is.Not.EqualTo(strong), s.id);
                Assert.That(BestiaryRules.ClassFactor(s.id, weak), Is.EqualTo(BestiaryRules.Weak), s.id);
                Assert.That(BestiaryRules.ClassFactor(s.id, strong), Is.EqualTo(BestiaryRules.Strong), s.id);
                var neither = System.Enum.GetValues(typeof(AdventurerClass)).Cast<AdventurerClass>().First(c => c != weak && c != strong);
                Assert.That(BestiaryRules.ClassFactor(s.id, neither), Is.EqualTo(1f), s.id);
            }
            Assert.That(BestiaryRules.SpeciesWithMatchups.All(id => db.Monster(id) != null), Is.True, "no matchup for a kind that does not exist");
            Assert.That(BestiaryRules.ClassFactor("no_such_kind", AdventurerClass.Mage), Is.EqualTo(1f));
            // Every class is somebody's weakness.
            foreach (AdventurerClass c in System.Enum.GetValues(typeof(AdventurerClass)))
                Assert.That(db.monsters.Any(s => BestiaryRules.TryMatchup(s.id, out var w, out _) && w == c), Is.True, c.ToString());
        }

        [Test]
        public void Mastery_AndAStudiedKind_WeighLess()
        {
            Assert.That((BestiaryRules.MasteryDamage, BestiaryRules.MasteryTaken), Is.EqualTo((1.1f, 0.95f)), "user decision ①");
            Assert.That(BestiaryRules.RiskFactor(0), Is.EqualTo(1f));
            Assert.That(BestiaryRules.RiskFactor(1), Is.EqualTo(1f));
            Assert.That(BestiaryRules.RiskFactor(2), Is.LessThan(1f), "user decision ②: from level 2");
            Assert.That(BestiaryRules.RiskFactor(3), Is.LessThan(BestiaryRules.RiskFactor(2)));
        }

        [Test]
        public void ThePage_SaysMore_TheFurtherTheResearch()
        {
            var db = DefaultContent.Build();
            db.BuildIndex();
            var skull = db.Monster("skull");
            Assert.That(BestiaryText.Page(skull, 0), Is.Empty);
            var one = BestiaryText.Page(skull, 1);
            var two = BestiaryText.Page(skull, 2);
            var three = BestiaryText.Page(skull, 3);
            Assert.That(one.Count, Is.LessThan(two.Count));
            Assert.That(two.Count, Is.LessThan(three.Count));
            Assert.That(string.Join("\n", one), Does.Not.Contain("약점"), "level 1 does not tell its weakness");
            Assert.That(string.Join("\n", two), Does.Contain("약점 사제").And.Contain("강함 궁수"));
            Assert.That(string.Join("\n", three), Does.Contain("숙련"));
            Assert.That(BestiaryText.Matchup(skull), Is.EqualTo("약점 사제 · 강함 궁수"));
            Assert.That((BestiaryRules.Breeding(0.3f), BestiaryRules.Breeding(0.1f), BestiaryRules.Breeding(0.01f)), Is.EqualTo(("빠름", "보통", "느림")));
        }
    }
}
