using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Data;
using GuildMaster.Monsters;
using GuildMaster.Proposals;
using GuildMaster.Quests;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>The four region lords (P4-83): lairs, cleansed land, titles, their content, and how their quest is weighed and priced.</summary>
    public class RegionLordTests
    {
        static NestSite Site(int x, int y, Region r) => new() { Cell = new Vector2Int(x, y), Region = r };

        [Test]
        public void PickLairs_TakesEachOuterRegionsFarthestSiteBeyondTheInnerEdge()
        {
            var sites = new List<NestSite>
            {
                Site(100, 0, Region.Desert),    // inside the inner edge: not a lair
                Site(250, 0, Region.Desert),
                Site(300, 0, Region.Desert),    // the desert's farthest
                Site(0, 290, Region.Snow),
                Site(0, 310, Region.Grassland), // grassland has no lord
            };
            var lairs = RegionLords.PickLairs(sites, Vector2.zero, 180f);
            Assert.That(lairs[Region.Desert], Is.EqualTo(2));
            Assert.That(lairs[Region.Snow], Is.EqualTo(3));
            Assert.That(lairs.ContainsKey(Region.Grassland), Is.False);
            Assert.That(lairs.ContainsKey(Region.Swamp), Is.False, "no site out there, no lord");
        }

        [Test]
        public void Sealed_IsOnlyACleansedRegionsOuterLand()
        {
            var cleansed = new HashSet<Region> { Region.Desert };
            Assert.That(RegionLords.IsSealed(Region.Desert, 200f, cleansed, 180f), Is.True);
            Assert.That(RegionLords.IsSealed(Region.Desert, 150f, cleansed, 180f), Is.False, "the inner hunting grounds stay (user decision)");
            Assert.That(RegionLords.IsSealed(Region.Snow, 250f, cleansed, 180f), Is.False);
            Assert.That(RegionLords.IsSealed(Region.Grassland, 250f, new HashSet<Region> { Region.Grassland }, 180f), Is.False);
            Assert.That(RegionLords.IsSealed(Region.Desert, 250f, null, 180f), Is.False);
        }

        [Test]
        public void Title_TakesTheRightParticle()
        {
            Assert.That(RegionLords.Title("모래 폭군 카르샤"), Is.EqualTo("모래 폭군 카르샤를 쓰러뜨린 자"));
            Assert.That(RegionLords.Title("서리 리치 이스벨"), Is.EqualTo("서리 리치 이스벨을 쓰러뜨린 자"));
        }

        [Test]
        public void Content_HasOneLordPerOuterRegion_LivingNowhereButItsLair()
        {
            var db = DefaultContent.Build();
            db.BuildIndex();
            var lords = db.monsters.Where(m => m.isRegionLord).ToList();
            Assert.That(lords.Select(l => l.lordRegion), Is.EquivalentTo(RegionLords.OuterRegions));
            foreach (var l in lords)
            {
                Assert.That(l.lairEscort, Is.Not.Null, l.id);
                Assert.That(l.lairEscort.LivesAt(l.lordRegion, Biome.Plains) || l.lairEscort.regions.Contains(l.lordRegion), Is.True, $"{l.id}: its guards are of its region");
                foreach (Region r in System.Enum.GetValues(typeof(Region)))
                foreach (Biome b in System.Enum.GetValues(typeof(Biome)))
                    Assert.That(l.LivesAt(r, b), Is.False, $"{l.id} is never dealt a den or sent as newcomers");
                Assert.That(l.skillIds, Has.Count.EqualTo(2), "its region's strike and the call of its guards");
                Assert.That(l.skillIds.All(id => Units.SkillFactory.Create(id) != null), Is.True);
                var trophy = l.loot.First(e => e.item.uniqueEffect == "lord_trophy").item;
                Assert.That(l.loot.First(e => e.item == trophy).chance, Is.EqualTo(1f), "the trophy always drops");
                Assert.That(MonsterEquipment.Wants(MonsterPickupMode.Absorb, trophy, null, 0f, 100f), Is.False, "no monster swallows or wears it");
                Assert.That(MonsterEquipment.Wants(MonsterPickupMode.Equip, trophy, null, 0f, 100f), Is.False);
            }
        }

        [Test]
        public void LordQuest_IsWeighedAgainstTheLord_AndTakesTwoParties()
        {
            var db = DefaultContent.Build();
            db.BuildIndex();
            var lord = db.Monster("lord_desert");
            var q = new Quest("Q_1", new QuestDraft { Type = QuestType.Hunt, Species = lord, Count = 1, RewardGold = 100, MaxParty = 8 }, 0f) { Danger = DangerLevel.High };
            Assert.That(q.MaxParty, Is.EqualTo(RegionLords.QuestParty), "two parties' worth");
            Assert.That(QuestRisk.Requirement(q), Is.EqualTo(RegionLords.RiskRequirement));
            // Power 20 alone reads an ordinary 높음 hunt as easy, but a lord as hopeless.
            Assert.That(QuestRisk.Label(QuestRisk.Margin(20f, 1, DangerLevel.High)), Is.EqualTo(QuestRisk.Easy));
            Assert.That(QuestRisk.Label(QuestRisk.Margin(20f, 1, q)), Is.EqualTo(QuestRisk.Reckless));
            Assert.That(QuestRisk.Label(QuestRisk.Margin(21f, 8, q)), Is.EqualTo(QuestRisk.Fair), "eight around level 18 with gear");
            var plain = new Quest("Q_2", new QuestDraft { Type = QuestType.Hunt, Species = db.Monster("slime"), Count = 3, RewardGold = 10, MaxParty = 8 }, 0f);
            Assert.That(plain.MaxParty, Is.EqualTo(4), "other quests stay at four");
        }

        [Test]
        public void LordReward_IsPricedLikeItsLair()
        {
            Assert.That(QuestPricing.LordReward(2, 300f), Is.EqualTo(QuestPricing.HuntReward(DangerLevel.High, 5, 300f)), "at least a five-kill hunt");
            Assert.That(QuestPricing.LordReward(8, 300f), Is.GreaterThan(QuestPricing.LordReward(5, 300f)), "a fuller lair pays more");
        }

        [Test]
        public void ProposalCard_ForALord_IsOneKill_UpToTwoParties()
        {
            var target = new ProposalTarget
            {
                Key = "nest:1,1", Type = QuestType.Hunt, Danger = DangerLevel.High, Label = "모래 폭군 카르샤", Area = "마을 남쪽 300칸",
                Distance = 300f, Observed = 6, Point = Vector2.zero, IsLord = true,
            };
            var pool = ProposalRules.BuildPool(new[] { target }, 22f, 8);
            Assert.That(pool, Is.Not.Empty);
            foreach (var p in pool)
            {
                Assert.That(p.Count, Is.EqualTo(1));
                Assert.That(p.Party, Is.InRange(1, RegionLords.QuestParty));
                Assert.That(p.Risk, Is.EqualTo(QuestRisk.Label(QuestRisk.MarginAgainst(22f, p.Party, RegionLords.RiskRequirement))));
            }
            Assert.That(ProposalRules.BuildPool(new[] { target }, 5f, 8), Is.Empty, "a weak guild is never offered a lord");
            Assert.That(ProposalTexts.FallbackTitle(pool[0]), Does.StartWith("[지대 군주]"));
        }
    }
}
