using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Data;
using GuildMaster.Units;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Damage formula and party sizing (P4-38, user report 2026-09-21: "one member clears a whole
    /// pack alone, so parties are pointless").
    /// </summary>
    public class CombatBalanceTests
    {
        [Test]
        public void ArmourDividesDamageInsteadOfCancellingIt()
        {
            Assert.That(Combat.Mitigate(10f, 0f), Is.EqualTo(10f));
            Assert.That(Combat.Mitigate(10f, Combat.ArmorScale), Is.EqualTo(5f).Within(0.001f), "ArmorScale halves a hit");
            Assert.That(Combat.Mitigate(10f, 30f), Is.LessThan(Combat.Mitigate(10f, 10f)), "more armour still helps");
            Assert.That(Combat.Mitigate(1f, 100f), Is.EqualTo(1f), "nothing hits for less than 1");
        }

        [Test]
        public void ALevelledMemberStillFeelsAGoblin()
        {
            // With subtraction a Lv5 in tier-1 gear took 1.5 from a goblin's 6 — a quarter —
            // and cleared packs alone. Division leaves over half of it.
            var db = DefaultContent.Build();
            var goblin = db.monsters.Find(m => m.id == "goblin");
            var geared = AdventurerStats.Compute(new AdventurerData { cls = AdventurerClass.Warrior, level = 5, gearTier = 1 }, null);
            Assert.That(Combat.Mitigate(goblin.attack, geared.Defense) / goblin.attack, Is.GreaterThan(0.5f));
        }

        [Test]
        public void TheGuildStartsWithOneFullParty()
        {
            var lineup = AdventurerGenerator.StartingLineup;
            Assert.That(lineup.Length, Is.GreaterThanOrEqualTo(4));
            Assert.That(lineup.Distinct().Count(), Is.EqualTo(System.Enum.GetValues(typeof(AdventurerClass)).Length),
                        "a front line, a back line and a healer: one of each class");
            var balance = UnityEngine.ScriptableObject.CreateInstance<Session.GameBalance>();
            Assert.That(balance.startingAdventurers, Is.GreaterThanOrEqualTo(lineup.Length));
            Assert.That(balance.startingAdventurers, Is.LessThan(balance.rosterCapacity), "room left to recruit");
            UnityEngine.Object.DestroyImmediate(balance);
        }
    }
}
