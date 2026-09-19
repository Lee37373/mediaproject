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
            Assert.That(MonsterEquipment.Wants(MonsterPickupMode.Equip, jelly, null, 0f, 10f), Is.False, "materials are not wearable");
            Assert.That(MonsterEquipment.Wants(MonsterPickupMode.None, dagger, null, 0f, 10f), Is.False);
        }

        [Test]
        public void Absorb_SwallowsUntilTheCap_ButNeverTheCrown()
        {
            var jelly = _db.Item("slime_jelly");
            var crown = _db.Item("crown_shard");
            Assert.That(MonsterEquipment.Wants(MonsterPickupMode.Absorb, jelly, null, 0f, 10f), Is.True);
            Assert.That(MonsterEquipment.Wants(MonsterPickupMode.Absorb, jelly, null, 10f, 10f), Is.False, "at the cap");
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
    }
}
