using GuildMaster.Adventurers;
using GuildMaster.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>People drawn with the CharacterMaker rig (2026-10-30): how each is dressed, and effect colours.</summary>
    public class HumanLookTests
    {
        [Test]
        public void SamePerson_SameLook()
        {
            var a = HumanLook.Adventurer("ADV_7", AdventurerClass.Warrior, Color.white, Color.black);
            var b = HumanLook.Adventurer("ADV_7", AdventurerClass.Warrior, Color.white, Color.black);
            Assert.That(a.Hair, Is.EqualTo(b.Hair));
            Assert.That(a.Chest, Is.EqualTo(b.Chest));
            Assert.That(a.Weapon, Is.EqualTo(b.Weapon));
            Assert.That(a.Shield, Is.EqualTo(b.Shield));
            Assert.That(HumanLook.Plain("x").Hair, Is.EqualTo(HumanLook.Plain("x").Hair));
        }

        [Test]
        public void ClassesCarryTheirGear()
        {
            for (int i = 0; i < 20; i++)
            {
                string id = "ADV_" + i;
                var w = HumanLook.Adventurer(id, AdventurerClass.Warrior, Color.white, Color.white);
                Assert.That(new[] { "Sword", "Axe", "Blunt" }, Does.Contain(w.Weapon));
                Assert.That(HumanLook.Adventurer(id, AdventurerClass.Ranger, Color.white, Color.white).Weapon, Is.EqualTo("Bow"));
                Assert.That(HumanLook.Adventurer(id, AdventurerClass.Mage, Color.white, Color.white).Weapon, Is.EqualTo("Staff"));
                var p = HumanLook.Adventurer(id, AdventurerClass.Priest, Color.white, Color.white);
                Assert.That(p.Weapon, Is.EqualTo("Wand"));
                Assert.That(p.Shield, Is.EqualTo(HumanLook.None));
                Assert.That(w.Runs, Is.True, "adventurers run");
            }
            Assert.That(HumanLook.Plain("resident").Runs, Is.False, "townsfolk walk");
            Assert.That(HumanLook.Plain("resident").Weapon, Is.Null);
        }

        [Test]
        public void BornColoursAreKept()
        {
            var skin = new Color(0.6f, 0.4f, 0.3f, 1f);
            var hair = new Color(0.9f, 0.1f, 0.1f, 1f);
            var look = HumanLook.Adventurer("ADV_1", AdventurerClass.Mage, skin, hair);
            Assert.That(look.Skin, Is.EqualTo(skin));
            Assert.That(look.HairColor, Is.EqualTo(hair));
        }

        [Test]
        public void SkillColours_PickThePacksNearestVariant()
        {
            Assert.That(CasualVfx.ColorName(new Color(0.9f, 0.2f, 0.2f)), Is.EqualTo("Red"));
            Assert.That(CasualVfx.ColorName(new Color(1f, 0.85f, 0.3f)), Is.EqualTo("Yellow"));
            Assert.That(CasualVfx.ColorName(new Color(0.3f, 0.85f, 0.4f)), Is.EqualTo("Green"));
            Assert.That(CasualVfx.ColorName(new Color(0.3f, 0.5f, 1f)), Is.EqualTo("Blue"));
            Assert.That(CasualVfx.ColorName(new Color(0.7f, 0.3f, 0.9f)), Is.EqualTo("Purple"));
            Assert.That(CasualVfx.ColorName(new Color(0.9f, 0.9f, 0.9f)), Is.EqualTo("White"));
        }
    }
}
