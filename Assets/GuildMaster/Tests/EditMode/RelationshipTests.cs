using GuildMaster.Adventurers;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    public class RelationshipTests
    {
        [Test]
        public void Seed_FromSociabilityAndValues()
        {
            Assert.That(Relationships.Seed(1f, "돈", "명예"), Is.EqualTo(0));
            Assert.That(Relationships.Seed(1.5f, "돈", "명예"), Is.EqualTo(5));
            Assert.That(Relationships.Seed(0.5f, "돈", "명예"), Is.EqualTo(-5));
            Assert.That(Relationships.Seed(1.5f, "동료", "동료"), Is.EqualTo(10));
        }

        [Test]
        public void Labels_FollowThresholds()
        {
            Assert.That(Relationships.Label(40), Is.EqualTo("친함"));
            Assert.That(Relationships.Label(15), Is.EqualTo("호감"));
            Assert.That(Relationships.Label(0), Is.EqualTo("보통"));
            Assert.That(Relationships.Label(-15), Is.EqualTo("껄끄러움"));
            Assert.That(Relationships.Label(-40), Is.EqualTo("적대"));
            Assert.That(Relationships.Describe(42), Is.EqualTo("친함(+42)"));
            Assert.That(Relationships.Describe(-18), Is.EqualTo("껄끄러움(-18)"));
        }

        [Test]
        public void PartyRuleChance_ShiftsWithFeeling()
        {
            Assert.That(PartyService.RuleAcceptChance("돈", 1f, 40), Is.EqualTo(0.75f).Within(1e-4));
            Assert.That(PartyService.RuleAcceptChance("돈", 1f, -40), Is.EqualTo(0.35f).Within(1e-4));
            Assert.That(PartyService.RuleAcceptChance("동료", 1f, 100), Is.EqualTo(0.95f).Within(1e-4));
            Assert.That(PartyService.RuleAcceptChance("돈", 0.5f, -100), Is.EqualTo(0.05f).Within(1e-4));
        }
    }
}
