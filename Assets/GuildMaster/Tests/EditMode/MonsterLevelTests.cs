using GuildMaster.Monsters;
using GuildMaster.Units;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Monster levels and EXP (user decision 2026-09-22).</summary>
    public class MonsterLevelTests
    {
        [Test]
        public void FartherOutIsBornHigher()
        {
            Assert.That(MonsterLevel.BirthLevel(0f), Is.EqualTo(1), "the ring by the town");
            Assert.That(MonsterLevel.BirthLevel(1f), Is.EqualTo(1 + MonsterLevel.EdgeBirthBonus), "the edge");
            Assert.That(MonsterLevel.BirthLevel(0.5f), Is.GreaterThan(MonsterLevel.BirthLevel(0.1f)));
        }

        [Test]
        public void HigherLevels_AreTougher_WorthMore_AndSmarterEveryFewLevels()
        {
            Assert.That(MonsterLevel.HpScale(1), Is.EqualTo(1f));
            Assert.That(MonsterLevel.HpScale(5), Is.GreaterThan(MonsterLevel.HpScale(2)));
            Assert.That(MonsterLevel.AttackScale(5), Is.GreaterThan(1f));
            Assert.That(MonsterLevel.ExpWorth(7, 1), Is.EqualTo(7), "level 1 is the species' own value");
            Assert.That(MonsterLevel.ExpWorth(7, 5), Is.GreaterThan(MonsterLevel.ExpWorth(7, 1)));
            Assert.That(MonsterLevel.TierAt(CombatTier.Novice, 1), Is.EqualTo(CombatTier.Novice));
            Assert.That(MonsterLevel.TierAt(CombatTier.Novice, 1 + MonsterLevel.LevelsPerTier), Is.EqualTo(CombatTier.Skilled));
            Assert.That(MonsterLevel.TierAt(CombatTier.Veteran, MonsterLevel.MaxLevel), Is.EqualTo(CombatTier.Heroic), "capped");
        }

        [Test]
        public void KillsLevelThemUp_GuildMembersTeachMore()
        {
            // A goblin (7 EXP) killing three of its own level-1 kind nearly makes level 2.
            Assert.That(3 * MonsterLevel.ExpWorth(7, 1), Is.LessThan(MonsterLevel.ExpToNext(1)));
            Assert.That(6 * MonsterLevel.ExpWorth(7, 1), Is.GreaterThanOrEqualTo(MonsterLevel.ExpToNext(1)));
            Assert.That(MonsterLevel.ExpForAdventurer(5), Is.GreaterThan(MonsterLevel.ExpForAdventurer(1)));
            Assert.That(MonsterLevel.ExpToNext(10), Is.GreaterThan(MonsterLevel.ExpToNext(1)));
        }
    }
}
