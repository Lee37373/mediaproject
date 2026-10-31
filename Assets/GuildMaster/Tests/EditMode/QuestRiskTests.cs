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
        public void ABigHuntAlone_IsADangerStepHarder_AndPutDownWhenNobodyComes()
        {
            // P4-100 (user decision 2026-10-21): Lv3–5 members took five Lv3 skulls on alone as 낮음 and died.
            Quests.Quest Hunt(int count) => new("Q_" + count, new Quests.QuestDraft { Type = Quests.QuestType.Hunt, Count = count, RewardGold = 40, MaxParty = 2 }, 0f);
            var five = Hunt(QuestRisk.SoloRaiseCount);
            var four = Hunt(QuestRisk.SoloRaiseCount - 1);
            float lv3 = 3 * 0.75f, lv7 = 7 * 0.75f;
            Assert.That(QuestRisk.Label(QuestRisk.Margin(lv3, 1, five)), Is.EqualTo(QuestRisk.Reckless), "a Lv3 alone on five: a step up (보통)");
            Assert.That(QuestRisk.Label(QuestRisk.Margin(lv3, 2, five)), Is.EqualTo(QuestRisk.Easy), "two of them: as posted (낮음)");
            Assert.That(QuestRisk.Label(QuestRisk.Margin(lv3, 1, four)), Is.EqualTo(QuestRisk.Easy), "four alone: as posted");
            Assert.That(QuestRisk.TooFewFor(five, lv3, 1), Is.True, "alone, it puts the five down");
            Assert.That(QuestRisk.TooFewFor(five, lv3, 2), Is.False);
            Assert.That(QuestRisk.TooFewFor(five, lv7, 1), Is.False, "a Lv7 goes alone");
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

        static Quests.Quest QuestOf(DangerLevel danger, MonsterDefinition species = null, int maxParty = 3)
            => new("Q_1", new Quests.QuestDraft { Type = Quests.QuestType.Hunt, Species = species, Count = 1, RewardGold = 100, MaxParty = maxParty }, 0f) { Danger = danger };

        [Test]
        public void ARecommendedLevel_IsTheDangersSoloLevel_OrAFullPartysForALord()
        {
            Assert.That(QuestRisk.RecommendedLevel(QuestOf(DangerLevel.Low)), Is.EqualTo(1));
            Assert.That(QuestRisk.RecommendedLevel(QuestOf(DangerLevel.Medium)), Is.EqualTo(5));
            Assert.That(QuestRisk.RecommendedLevel(QuestOf(DangerLevel.High)), Is.EqualTo(9));
            var lord = UnityEngine.ScriptableObject.CreateInstance<MonsterDefinition>();
            lord.isRegionLord = true;
            try
            {
                // 90 for eight: 90 / (1 + 0.6 × 7) = 17.3.
                Assert.That(QuestRisk.RecommendedLevel(QuestOf(DangerLevel.High, lord, Monsters.RegionLords.QuestParty)), Is.EqualTo(18));
            }
            finally { UnityEngine.Object.DestroyImmediate(lord); }
        }

        [Test]
        public void ALordsQuest_IsPartyWork_WeighedByTheFullGroup_AndTooFewGiveUp()
        {
            // P4-90 (user decision 2026-10-19): weighed as one alone, the lord's requirement (90) was beyond anyone.
            var lord = UnityEngine.ScriptableObject.CreateInstance<MonsterDefinition>();
            lord.isRegionLord = true;
            try
            {
                var q = QuestOf(DangerLevel.High, lord, Monsters.RegionLords.QuestParty);
                Assert.That((QuestRisk.IsLordWork(q), QuestRisk.IsPartyWork(q)), Is.EqualTo((true, true)));
                int level = QuestRisk.RecommendedLevel(q);
                Assert.That(QuestRisk.TooFewFor(q, level, 1), Is.True, "one alone does not set out");
                Assert.That(QuestRisk.TooFewFor(q, level, 4), Is.True, "nor one party of its recommended level");
                Assert.That(QuestRisk.TooFewFor(q, level, Monsters.RegionLords.QuestParty), Is.False, "two parties of it do");
                Assert.That(QuestRisk.Label(QuestRisk.Margin(level, Monsters.RegionLords.QuestParty, q)), Is.EqualTo(QuestRisk.Fair));
            }
            finally { UnityEngine.Object.DestroyImmediate(lord); }
            var den = QuestOf(DangerLevel.High);
            Assert.That((QuestRisk.IsLordWork(den), QuestRisk.IsPartyWork(den)), Is.EqualTo((false, false)), "an ordinary hunt keeps its own rules");
        }

        [Test]
        public void ALordsGathering_WaitsUntilThePartyIsFair_ButGoesOnARiskyOneWhenTimeIsUp()
        {
            // P4-97 (user decision 2026-10-21): the first taker gathers the rest as they come home — when does it set out?
            var lord = UnityEngine.ScriptableObject.CreateInstance<MonsterDefinition>();
            lord.isRegionLord = true;
            try
            {
                var q = QuestOf(DangerLevel.High, lord, Monsters.RegionLords.QuestParty);
                Assert.That(QuestRisk.IsBossWork(q), Is.True);
                // A power at which eight are 적당 and seven 위험.
                float power = QuestRisk.Requirement(q) / (1f + 0.6f * 7) * 1.01f;
                Assert.That(QuestRisk.Label(QuestRisk.Margin(power, 7, q)), Is.EqualTo(QuestRisk.Risky));
                Assert.That(QuestRisk.ReadyToSetOut(q, power, 7), Is.False, "nobody walks into a lord on 위험 while more could still come");
                Assert.That(QuestRisk.ReadyToSetOut(q, power, 8), Is.True);
                Assert.That(QuestRisk.TooFewFor(q, power, 7), Is.False, "when time is up the seven go");
                Assert.That(QuestRisk.TooFewFor(q, power, 5), Is.True, "five give it up");
                Assert.That(QuestRisk.ReadyToSetOut(q, power * 10f, 1), Is.True, "one strong enough alone does not wait");
                Assert.That(QuestRisk.ReadyToSetOut(q, 0.01f, q.MaxParty), Is.True, "a full one stops gathering (TooFewFor decides the rest)");
            }
            finally { UnityEngine.Object.DestroyImmediate(lord); }
            Assert.That(QuestRisk.IsBossWork(QuestOf(DangerLevel.High)), Is.False);
        }

        [Test]
        public void FarUnderItsLevel_ARiskyQuestIsTooGreen_ButAFairOneIsNot()
        {
            // P4-88 (user decision 2026-10-18): new members followed their party to far 위험 quests and died.
            var medium = QuestOf(DangerLevel.Medium);
            var rookie = new AdventurerData { level = 2 };
            Assert.That(QuestRisk.TooGreen(rookie, medium, QuestRisk.Risky), Is.True);
            Assert.That(QuestRisk.TooGreen(rookie, medium, QuestRisk.Reckless), Is.True);
            Assert.That(QuestRisk.TooGreen(rookie, medium, QuestRisk.Fair), Is.False, "safe enough with this party: no holding back");
            Assert.That(QuestRisk.TooGreen(new AdventurerData { level = 3 }, medium, QuestRisk.Risky), Is.False, "within three levels");
            Assert.That(QuestRisk.TooGreen(new AdventurerData { level = 2, gearTier = 1 }, medium, QuestRisk.Risky), Is.False, "gear counts 1.5 levels");
            Assert.That(QuestRisk.GreenDareChance(-0.5f), Is.GreaterThan(QuestRisk.GreenDareChance(0f)), "the bold dare more often");
            Assert.That(QuestRisk.GreenDareChance(1f), Is.LessThan(0.2f));
        }
    }
}
