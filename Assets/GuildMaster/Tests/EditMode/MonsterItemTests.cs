using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Monsters;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>P4-21: who wants which ground item, and what a dead monster leaves behind.</summary>
    public class MonsterItemTests
    {
        GameDatabase _db;

        [SetUp]
        public void SetUp()
        {
            _db = DefaultContent.Build();
            _db.BuildIndex();
        }

        [Test]
        public void Equip_TakesOnlyUpgradesForItsSlot()
        {
            var dagger = _db.Item("rusty_dagger");
            var gear1 = _db.Item("gear_t1");
            var jelly = _db.Item("slime_jelly");
            Assert.That(MonsterEquipment.Wants(MonsterPickupMode.Equip, dagger, null, 0f, 10f), Is.True);
            Assert.That(MonsterEquipment.Wants(MonsterPickupMode.Equip, gear1, dagger, 0f, 10f), Is.True, "guild gear beats a rusty dagger");
            Assert.That(MonsterEquipment.Wants(MonsterPickupMode.Equip, dagger, gear1, 0f, 10f), Is.False, "never a downgrade");
            Assert.That(MonsterEquipment.Wants(MonsterPickupMode.Equip, jelly, null, 0f, 10f), Is.True,
                        "not wearable, but eaten for the tier EXP (2026-09-23)");
            Assert.That(MonsterEquipment.Wants(MonsterPickupMode.None, dagger, null, 0f, 10f), Is.False);
            Assert.That(MonsterEquipment.Wants(MonsterPickupMode.None, jelly, null, 0f, 10f), Is.False,
                        "a species that picks nothing up eats nothing either");
        }

        [Test]
        public void Absorb_SwallowsUntilTheCap_ButNeverTheCrown()
        {
            var jelly = _db.Item("slime_jelly");
            var crown = _db.Item("crown_shard");
            Assert.That(MonsterEquipment.Wants(MonsterPickupMode.Absorb, jelly, null, 0f, 10f), Is.True);
            Assert.That(MonsterEquipment.Wants(MonsterPickupMode.Absorb, jelly, null, 10f, 10f), Is.True,
                        "full of jelly, but the leavings still teach it something (2026-09-23)");
            Assert.That(MonsterEquipment.Wants(MonsterPickupMode.Absorb, _db.Item("gear_t1"), null, 10f, 10f), Is.False,
                        "gear it cannot wear and has no room to swallow");
            // Unique items are worn by any picker, slimes included, and only once.
            Assert.That(MonsterEquipment.Wants(MonsterPickupMode.Absorb, crown, null, 10f, 10f), Is.True);
            Assert.That(MonsterEquipment.Wants(MonsterPickupMode.Equip, crown, crown, 0f, 10f), Is.False);

            var eq = new MonsterEquipment();
            float gained = eq.Absorb(_db.Item("royal_jelly"), 12f) + eq.Absorb(_db.Item("royal_jelly"), 12f);
            Assert.That(eq.AbsorbedHp, Is.EqualTo(12f).Within(0.001f), "capped");
            Assert.That(gained, Is.EqualTo(12f).Within(0.001f));
        }

        [Test]
        public void Drops_AlwaysIncludeWornGear_PlusTheRolledLootTable()
        {
            var goblin = _db.Monster("goblin");
            var dagger = _db.Item("rusty_dagger");
            var drops = MonsterEquipment.BuildDrops(goblin, new[] { dagger }, new GameRandom(1));
            Assert.That(drops.Any(s => s.itemId == "rusty_dagger"), Is.True);
            // Worn gear alone (no killer): no loot table roll.
            var bare = MonsterEquipment.BuildDrops(null, new[] { dagger }, new GameRandom(1));
            Assert.That(bare.Count, Is.EqualTo(1));
            // The dagger moved from the loot table to starting gear, so scarcity still finds it.
            Assert.That(goblin.CanDrop(dagger), Is.True);
            Assert.That(goblin.loot.Any(l => l.item == dagger), Is.False);
        }

        /// <summary>
        /// 2026-09-23 (user decision): eating what a fight leaves behind buys smarts, not strength.
        /// The guild pays for an uncleared battlefield by meeting a sharper pack there later.
        /// </summary>
        [Test]
        public void EatingTheLeavings_BuysTierSteps_UpToACap()
        {
            var jelly = _db.Item("slime_jelly");     // value 4
            var ore = _db.Item("iron_ore");          // value 15
            var gear = _db.Item("gear_t1");

            Assert.That(MonsterEquipment.TierExpFor(ore), Is.GreaterThan(MonsterEquipment.TierExpFor(jelly)),
                        "the better the leavings, the more they teach");
            Assert.That(MonsterEquipment.TierExpFor(gear), Is.Zero, "worn gear is carried, not picked over");
            Assert.That(MonsterEquipment.TierExpFor(null), Is.Zero);

            Assert.That(MonsterLevel.ScavengedTierSteps(0), Is.Zero);
            Assert.That(MonsterLevel.ScavengedTierSteps(MonsterLevel.TierExpPerStep - 1), Is.Zero);
            Assert.That(MonsterLevel.ScavengedTierSteps(MonsterLevel.TierExpPerStep), Is.EqualTo(1));
            Assert.That(MonsterLevel.ScavengedTierSteps(MonsterLevel.TierExpPerStep * 99),
                        Is.EqualTo(MonsterLevel.MaxScavengedTierSteps), "scavenging sharpens a pack, it does not remake it");
        }

        [Test]
        public void ScavengedSteps_RaiseTheTier_ButNeverPastHeroic()
        {
            // A level-1 slime is on instinct; two steps of leavings make it 숙련.
            Assert.That(MonsterLevel.TierAt(Units.CombatTier.Instinct, 1, 0), Is.EqualTo(Units.CombatTier.Instinct));
            Assert.That(MonsterLevel.TierAt(Units.CombatTier.Instinct, 1, MonsterLevel.TierExpPerStep),
                        Is.EqualTo(Units.CombatTier.Novice));
            Assert.That(MonsterLevel.TierAt(Units.CombatTier.Instinct, 1, MonsterLevel.TierExpPerStep * 2),
                        Is.EqualTo(Units.CombatTier.Skilled));
            // The level's own steps and the scavenged ones add up, and stop at the top.
            Assert.That(MonsterLevel.TierAt(Units.CombatTier.Veteran, MonsterLevel.MaxLevel, MonsterLevel.TierExpPerStep * 2),
                        Is.EqualTo(Units.CombatTier.Heroic));
            // The old two-argument rule is unchanged for everything that never scavenged.
            Assert.That(MonsterLevel.TierAt(Units.CombatTier.Novice, 8),
                        Is.EqualTo(MonsterLevel.TierAt(Units.CombatTier.Novice, 8, 0)));
        }
    }
}
