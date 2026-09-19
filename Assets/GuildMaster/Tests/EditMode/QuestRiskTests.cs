using GuildMaster.Adventurers;
using GuildMaster.Data;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>P4-11: the quest board warning uses the same risk words as the adventurers.</summary>
    public class QuestRiskTests
    {
        [Test]
        public void Labels_FollowTheMargin()
        {
            Assert.That(QuestRisk.Label(QuestRisk.Margin(2f, 1, DangerLevel.Low)), Is.EqualTo(QuestRisk.Easy));
            Assert.That(QuestRisk.Label(QuestRisk.Margin(1f, 1, DangerLevel.Low)), Is.EqualTo(QuestRisk.Fair));
            Assert.That(QuestRisk.Label(QuestRisk.Margin(3f, 2, DangerLevel.High)), Is.EqualTo(QuestRisk.Risky)); // 4.8 / 6
            Assert.That(QuestRisk.Label(QuestRisk.Margin(1f, 1, DangerLevel.High)), Is.EqualTo(QuestRisk.Reckless));
        }

        [Test]
        public void Advice_GivesSoloLevelAndPartySize()
        {
            Assert.That(QuestRisk.SoloLevel(DangerLevel.High, 0), Is.EqualTo(6));
            Assert.That(QuestRisk.SoloLevel(DangerLevel.High, 2), Is.EqualTo(3), "gear counts 1.5 levels per tier");
            Assert.That(QuestRisk.PartyNeeded(3f, DangerLevel.High), Is.EqualTo(3));
            Assert.That(QuestRisk.PartyNeeded(1f, DangerLevel.High), Is.EqualTo(0), "no party of four is enough");
            Assert.That(QuestRisk.PartyNeeded(1f, DangerLevel.Low), Is.EqualTo(1));
        }
    }
}
