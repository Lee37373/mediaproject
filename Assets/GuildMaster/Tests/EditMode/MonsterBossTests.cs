using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Monsters;
using GuildMaster.Units;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>P4-57: pack bosses — every species has one, a step smarter, with a richer drop.</summary>
    public class MonsterBossTests
    {
        [Test]
        public void EverySpecies_HasANamedBoss()
        {
            var db = DefaultContent.Build();
            foreach (var s in db.monsters)
                Assert.That(s.bossName, Is.Not.Empty, s.id);
            Assert.That(MonsterBoss.Name("고블린", null), Is.EqualTo("고블린 우두머리"), "fallback name");
        }

        [Test]
        public void Boss_IsOneTierSmarter_CappedAtHeroic()
        {
            Assert.That(MonsterBoss.SpeciesTier(CombatTier.Instinct, true), Is.EqualTo(CombatTier.Novice));
            Assert.That(MonsterBoss.SpeciesTier(CombatTier.Instinct, false), Is.EqualTo(CombatTier.Instinct));
            Assert.That(MonsterBoss.SpeciesTier(CombatTier.Heroic, true), Is.EqualTo(CombatTier.Heroic));
            Assert.That(MonsterBoss.Chance, Is.GreaterThan(0f).And.LessThan(0.5f), "a low chance per pack (user decision)");
        }

        [Test]
        public void Boss_RollsTheLootTableSeveralTimes()
        {
            var db = DefaultContent.Build();
            db.BuildIndex();
            // The slime king always drops a crown shard (chance 1): one per roll.
            var king = db.Monster("slime_king");
            var one = MonsterEquipment.BuildDrops(king, null, new GameRandom(3));
            var boss = MonsterEquipment.BuildDrops(king, null, new GameRandom(3), null, MonsterBoss.LootRolls);
            Assert.That(one.Count(s => s.itemId == "crown_shard"), Is.EqualTo(1));
            Assert.That(boss.Count(s => s.itemId == "crown_shard"), Is.EqualTo(MonsterBoss.LootRolls));
        }
    }
}
