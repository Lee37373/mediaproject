using GuildMaster.Adventurers;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    public class PartyTests
    {
        [Test]
        public void RuleChance_FollowsSpec()
        {
            Assert.That(PartyService.RuleAcceptChance("동료", 1f), Is.EqualTo(0.8f));
            Assert.That(PartyService.RuleAcceptChance("돈", 0.5f), Is.EqualTo(0.2f));
            Assert.That(PartyService.RuleAcceptChance("돈", 1f), Is.EqualTo(0.55f));
            Assert.That(PartyService.RuleAcceptChance("모험", 1.5f), Is.EqualTo(0.55f));
        }
    }
}
