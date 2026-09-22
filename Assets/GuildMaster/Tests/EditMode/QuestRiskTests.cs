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
            Assert.That(QuestRisk.Label(QuestRisk.Margin(5f, 2, DangerLevel.High)), Is.EqualTo(QuestRisk.Risky)); // 8 / 9
            Assert.That(QuestRisk.Label(QuestRisk.Margin(1f, 1, DangerLevel.High)), Is.EqualTo(QuestRisk.Reckless));
        }

        [Test]
        public void Advice_GivesSoloLevelAndPartySize()
        {
            Assert.That(QuestRisk.SoloLevel(DangerLevel.High, 0), Is.EqualTo(9)); // P4-58: levels run 1..15 across the map
            Assert.That(QuestRisk.SoloLevel(DangerLevel.High, 2), Is.EqualTo(6), "gear counts 1.5 levels per tier");
            Assert.That(QuestRisk.PartyNeeded(5f, DangerLevel.High), Is.EqualTo(3));
            Assert.That(QuestRisk.PartyNeeded(1f, DangerLevel.High), Is.EqualTo(0), "no party of four is enough");
            Assert.That(QuestRisk.PartyNeeded(1f, DangerLevel.Low), Is.EqualTo(1));
        }

        [Test]
        public void HuntDanger_FollowsTheLevel_LargeHuntsAreOneStepHarder_UpToHigh()
        {
            // P4-58 (user decision: "strength is the level"): danger comes from the level monsters are
            // born at there, not from the species.
            Assert.That(Quests.QuestBoard.HuntDanger(1, 7), Is.EqualTo(DangerLevel.Low));
            Assert.That(Quests.QuestBoard.HuntDanger(1, 8), Is.EqualTo(DangerLevel.Medium));
            Assert.That(Quests.QuestBoard.HuntDanger(Monsters.MonsterLevel.MediumDangerLevel, 1), Is.EqualTo(DangerLevel.Medium));
            Assert.That(Quests.QuestBoard.HuntDanger(Monsters.MonsterLevel.HighDangerLevel, 1), Is.EqualTo(DangerLevel.High));
            Assert.That(Quests.QuestBoard.HuntDanger(15, 10), Is.EqualTo(DangerLevel.High), "capped at high");
        }
    }
}
