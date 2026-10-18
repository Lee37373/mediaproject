using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Incidents;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using GuildMaster.Units;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>P4-85: sudden incidents — the rumour's circle, the rare variant's numbers, its reward and spoils.</summary>
    public class IncidentTests
    {
        [Test]
        public void Rumour_PutsTheCircleOffTheSpot_ButAlwaysAroundWhatRoamsThere()
        {
            var rng = new GameRandom(7);
            float offset = IncidentRules.RumourOffset(IncidentRules.RareRoam);
            Assert.That(offset + IncidentRules.RareRoam, Is.LessThan(IncidentRules.RumourRadius), "anything within its roam stays inside the circle");
            var spot = new Vector2(100f, 50f);
            float farthest = 0f;
            for (int i = 0; i < 500; i++)
            {
                var centre = IncidentRules.RumourCentre(spot, offset, rng);
                float d = Vector2.Distance(centre, spot);
                Assert.That(d, Is.LessThanOrEqualTo(offset + 1e-3f));
                farthest = Mathf.Max(farthest, d);
            }
            Assert.That(farthest, Is.GreaterThan(offset * 0.8f), "it really is rough");
            Assert.That(IncidentRules.RumourDelayMin, Is.GreaterThan(0f), "the news takes a while to come");
            Assert.That(IncidentRules.RumourOffset(100f), Is.GreaterThan(0f), "never exactly the spot");
        }

        [Test]
        public void RareVariant_IsAboveTheLand_NamedAndCapped()
        {
            Assert.That(RareMonster.Level(10), Is.EqualTo(10 + RareMonster.LevelBonus));
            Assert.That(RareMonster.Level(MonsterLevel.MaxLevel), Is.EqualTo(MonsterLevel.MaxLevel), "the ordinary cap");
            Assert.That(RareMonster.Level(0), Is.EqualTo(1 + RareMonster.LevelBonus));
            Assert.That(RareMonster.Name(0, "고블린"), Is.EqualTo("황금 고블린"));
            Assert.That(RareMonster.Name(99, "고블린"), Does.EndWith(" 고블린"), "an unknown variant still gets a name");
            Assert.That(RareMonster.SpeciesTier(CombatTier.Instinct), Is.EqualTo(CombatTier.Novice));
            Assert.That(RareMonster.SpeciesTier(CombatTier.Heroic), Is.EqualTo(CombatTier.Heroic));
            Assert.That(RareMonster.HpMultiplier, Is.GreaterThan(MonsterBoss.HpMultiplier), "tougher than a pack boss");
        }

        [Test]
        public void RareHunt_PaysMoreThanADen_AndAsksForAFullPartyOfItsLevel()
        {
            float distance = 100f;
            int den = QuestPricing.HuntReward(DangerLevel.High, QuestPricing.HuntKills, distance);
            Assert.That(IncidentRules.RareReward(distance), Is.GreaterThanOrEqualTo(den * 2), "a big reward");
            Assert.That(IncidentRules.RareReward(200f), Is.GreaterThan(IncidentRules.RareReward(100f)), "the walk pays");

            int land = 10, level = RareMonster.Level(land);
            float need = IncidentRules.RareRequirement(land);
            Assert.That(QuestRisk.Label(QuestRisk.MarginAgainst(level, IncidentRules.RareParty, need)), Is.EqualTo(QuestRisk.Fair).Or.EqualTo(QuestRisk.Easy),
                        "four of its level can take it");
            Assert.That(QuestRisk.Label(QuestRisk.MarginAgainst(level, 1, need)), Is.EqualTo(QuestRisk.Reckless), "one alone is reckless");
        }

        [Test]
        public void RareHunt_IsPartyWork_TooFewAreTooFew_ButAnOrdinaryHuntIsNot()
        {
            var draft = new QuestDraft { Type = QuestType.Hunt, Count = 1, Point = Vector2.zero, HasPoint = true, RewardGold = 100, MaxParty = IncidentRules.RareParty };
            int land = 10, level = RareMonster.Level(land);
            var rare = new Quest("Q_1", draft, 0f)
            {
                Incident = new Incident { Kind = IncidentKind.RareMonster, Requirement = IncidentRules.RareRequirement(land) },
                Danger = DangerLevel.High,
            };
            Assert.That(QuestRisk.IsPartyWork(rare), Is.True);
            Assert.That(QuestRisk.TooFewFor(rare, level, 1), Is.True, "one alone does not set out");
            Assert.That(QuestRisk.TooFewFor(rare, level, IncidentRules.RareParty), Is.False, "a full party of its level does");
            Assert.That(QuestRisk.RecommendedLevel(rare), Is.InRange(level - 2, level), "per member of a full party");

            var den = new Quest("Q_2", draft, 0f) { Danger = DangerLevel.High };
            Assert.That(QuestRisk.IsPartyWork(den), Is.False);
            Assert.That(QuestRisk.TooFewFor(den, 1, 1), Is.False, "ordinary quests keep their own rules");
        }

        [Test]
        public void Horde_IsBiggerThanADen_AsksForMostOfIt_AndAPartyAboveItsLevel()
        {
            var db = DefaultContent.Build();
            foreach (var s in db.monsters)
                if (s.canRaid && !s.isRegionLord) Assert.That(IncidentRules.HordeMin, Is.GreaterThanOrEqualTo(s.raidThreshold), $"{s.id}: a settled horde is a raiding den");
            Assert.That(IncidentRules.HordeKills(10), Is.EqualTo(7));
            Assert.That(IncidentRules.HordeKills(16), Is.EqualTo(12));
            Assert.That(IncidentRules.RumouredCount(12), Is.EqualTo(10));
            Assert.That(IncidentRules.RumouredCount(13), Is.EqualTo(15));
            Assert.That(IncidentRules.HordeDanger(3, 12), Is.EqualTo(DangerLevel.Medium), "a big head count is a step worse than its level");

            int level = 6, size = 12;
            float need = IncidentRules.HordeRequirement(level, size);
            Assert.That(QuestRisk.Label(QuestRisk.MarginAgainst(level, IncidentRules.HordeParty, need)), Is.EqualTo(QuestRisk.Risky).Or.EqualTo(QuestRisk.Reckless),
                        "four of its own level against twelve is no fair fight");
            Assert.That(QuestRisk.Label(QuestRisk.MarginAgainst(level * 1.3f, IncidentRules.HordeParty, need)), Is.EqualTo(QuestRisk.Fair), "four well above it is");
            Assert.That(IncidentRules.HordeReward(level, size, 100f), Is.GreaterThan(QuestPricing.HuntReward(DangerLevel.High, size, 100f)), "urgent, like a defence");
        }

        [Test]
        public void Rescue_TravellerHoldsOutTheHours_TheBlowsLast_AndTheRewardIsDoubled()
        {
            // One blow of 10 a second (a defenceless target, criticals on average) for an hour of ten seconds.
            float one = IncidentRules.HoldOutHp(new[] { (10f, 1f) }, 1f, 10f);
            Assert.That(one, Is.EqualTo(10f * (1f + Combat.CritChance * (Combat.CritMultiplier - 1f)) * 10f).Within(0.01f));
            Assert.That(IncidentRules.HoldOutHp(new[] { (10f, 1f), (10f, 1f) }, 2f, 10f), Is.EqualTo(one * 4f).Within(0.01f), "twice the blows, twice the hours");
            Assert.That(IncidentRules.RescueReward(DangerLevel.Medium, 3, 60f), Is.GreaterThan(QuestPricing.HuntReward(DangerLevel.Medium, 3, 60f)));
            Assert.That(IncidentRules.TravellerName(new GameRandom(3)), Does.Contain(" "));
            var db = DefaultContent.Build();
            db.BuildIndex();
            foreach (var s in IncidentRules.Cargo) Assert.That(db.Item(s.itemId), Is.Not.Null, s.itemId);
        }

        [Test]
        public void RareVariant_DropsItsTrophy_AndGearByLevel()
        {
            var loot = new List<ItemStack>();
            RareMonster.AddSpoils(RareMonster.TopGearLevel - 1, loot);
            Assert.That(loot.Exists(s => s.itemId == RareMonster.TrophyId), Is.True);
            Assert.That(loot.Exists(s => s.itemId == "gear_t2"), Is.True);
            loot.Clear();
            RareMonster.AddSpoils(RareMonster.TopGearLevel, loot);
            Assert.That(loot.Exists(s => s.itemId == "gear_t3"), Is.True);

            var db = DefaultContent.Build();
            db.BuildIndex();
            Assert.That(db.Item(RareMonster.TrophyId), Is.Not.Null, "the trophy is a real item");
            Assert.That(db.Item(RareMonster.TrophyId).equipSlot, Is.EqualTo(EquipSlot.None), "no monster wears it");
        }
    }
}
