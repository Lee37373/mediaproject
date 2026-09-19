using System.Linq;
using GuildMaster.Adventurers;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>P4-29: the priest class — how often it appears, what it carries, where it stands.</summary>
    public class PriestTests
    {
        [Test]
        public void RollClass_PriestIsRare_OthersShareTheRest()
        {
            Assert.That(AdventurerData.RollClass(0.1f), Is.EqualTo(AdventurerClass.Priest));
            Assert.That(AdventurerData.RollClass(0.16f), Is.EqualTo(AdventurerClass.Warrior));
            Assert.That(AdventurerData.RollClass(0.6f), Is.EqualTo(AdventurerClass.Ranger));
            Assert.That(AdventurerData.RollClass(0.999f), Is.EqualTo(AdventurerClass.Mage));

            int priests = 0, n = 10000;
            for (int i = 0; i < n; i++) if (AdventurerData.RollClass((i + 0.5f) / n) == AdventurerClass.Priest) priests++;
            Assert.That(priests / (float)n, Is.EqualTo(AdventurerData.PriestChance).Within(0.001f));
        }

        [Test]
        public void Priest_HealsFromTheBackLine_WithItsOwnSkin()
        {
            var skills = Adventurer.ClassSkills(AdventurerClass.Priest);
            Assert.That(skills, Is.EquivalentTo(new[] { "massheal", "heal" }));
            Assert.That(skills.All(id => Units.SkillFactory.Create(id).IsSupport), Is.True, "heals are support skills");
            Assert.That(PartyTactics.RoleOf(AdventurerClass.Priest), Is.EqualTo(PartyRole.Back));
            Assert.That(Adventurer.SkinsFor(AdventurerClass.Priest), Is.EqualTo(new[] { "frog" }));
            Assert.That(Adventurer.SkinsFor(AdventurerClass.Ranger), Does.Not.Contain("frog"), "the frog is the priest's now");

            var priest = AdventurerStats.Base(AdventurerClass.Priest);
            var ranger = AdventurerStats.Base(AdventurerClass.Ranger);
            Assert.That(priest.IsRanged, Is.True, "weak ranged light");
            Assert.That(priest.Attack, Is.LessThan(ranger.Attack));
            Assert.That(AdventurerData.ClassLabel(AdventurerClass.Priest), Is.EqualTo("사제"));
        }
    }
}
