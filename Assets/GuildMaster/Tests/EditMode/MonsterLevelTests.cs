using GuildMaster.Monsters;
using GuildMaster.Units;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Monster levels and EXP (user decision 2026-09-22).</summary>
    public class MonsterLevelTests
    {
        [Test]
        public void StrengthIsTheLevel_NotTheSpecies()
        {
            // P4-58 (user decision 2026-09-22): species differ a little, the level a lot.
            var db = GuildMaster.Data.DefaultContent.Build();
            float Power(string id, int level)
            {
                var s = GuildMaster.Adventurers.FreeHunt.StatsOf(db.monsters.Find(m => m.id == id), level);
                return s.MaxHp * s.Attack;
            }
            float weakest = float.MaxValue, strongest = 0f;
            foreach (var m in db.monsters)
            {
                float p = Power(m.id, 1);
                weakest = UnityEngine.Mathf.Min(weakest, p);
                strongest = UnityEngine.Mathf.Max(strongest, p);
            }
            Assert.That(strongest / weakest, Is.LessThan(3f), "species stay within a small spread");
            Assert.That(Power("slime", MonsterLevel.BirthLevel(1f)), Is.GreaterThan(Power("slime_king", 1)),
                        "a slime from the edge outclasses a slime king by the walls");
            Assert.That(MonsterLevel.DangerAt(MonsterLevel.BirthLevel(0f)), Is.EqualTo(GuildMaster.Data.DangerLevel.Low));
            Assert.That(MonsterLevel.DangerAt(MonsterLevel.BirthLevel(1f)), Is.EqualTo(GuildMaster.Data.DangerLevel.High));
        }

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
