using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Quests;
using GuildMaster.Villages;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>
    /// The other villages (P4-84, user decisions 2026-10-18): two per outer region on open ground apart from the dens and
    /// from each other, durability that mends only when left alone, candidates of the land round them, new villages drawn
    /// to cleansed land, and an urgent price on their defence.
    /// </summary>
    public class VillageTests
    {
        static MapData Map512(int seed)
        {
            var balance = ScriptableObject.CreateInstance<Session.GameBalance>();
            try
            {
                return MapGenerator.Generate(new MapGenSettings
                {
                    Width = balance.mapWidth, Height = balance.mapHeight, Seed = seed, TownHalfSize = balance.townHalfSize,
                    NestCount = balance.nestCount, MinNestDistanceFromTown = balance.minNestDistanceFromTown,
                    InnerHalfSize = balance.innerHalfSize, InnerNestCount = balance.innerNestCount, Landforms = balance.landforms,
                });
            }
            finally { Object.DestroyImmediate(balance); }
        }

        [Test]
        public void TwoVillagesPerOuterRegion_OneInnerOneOuter_ApartFromDensAndEachOther_AndTheSameForTheSameSeed()
        {
            foreach (int seed in new[] { 20260920, 31337 })
            {
                var map = Map512(seed);
                var sites = VillagePlacement.PlaceInitial(map, VillagePlacement.StreamFor(seed));
                Assert.That(sites.Count, Is.EqualTo(VillagePlacement.OuterRegions.Length * VillageRules.PerRegion), $"seed {seed}");
                foreach (var region in VillagePlacement.OuterRegions)
                {
                    var here = sites.Where(s => s.Region == region).ToList();
                    Assert.That(here.Count(s => s.Inner), Is.EqualTo(1), $"{region}: one on the inner land");
                    Assert.That(here.Count(s => !s.Inner), Is.EqualTo(1), $"{region}: one beyond it");
                }
                foreach (var s in sites)
                {
                    float d = Vector2.Distance(s.Cell, map.TownCenter);
                    if (s.Inner) Assert.That(d, Is.InRange(VillageRules.InnerMin, VillageRules.InnerMax));
                    else Assert.That(d, Is.GreaterThanOrEqualTo(VillageRules.OuterMin));
                    Assert.That(map.RegionOf(s.Cell.x, s.Cell.y), Is.EqualTo(s.Region));
                    Assert.That(VillagePlacement.Fits(map, s.Cell, sites.Where(o => o.Cell != s.Cell).ToList()), Is.True, $"{s.Name} at {s.Cell}");
                    Assert.That(map.Nests.Min(n => Vector2.Distance(n.Cell, s.Cell)), Is.GreaterThanOrEqualTo(VillageRules.NestClearance));
                    Assert.That(string.IsNullOrEmpty(s.Name), Is.False);
                }
                Assert.That(sites.Select(s => s.Name).Distinct().Count(), Is.EqualTo(sites.Count), "every village has its own name");
                var again = VillagePlacement.PlaceInitial(map, VillagePlacement.StreamFor(seed));
                Assert.That(again.Select(s => s.Cell), Is.EqualTo(sites.Select(s => s.Cell)), "a seeded world gets the same villages");
            }
        }

        [Test]
        public void ANewVillage_GoesWhereTheWeightsSay_AndKeepsItsDistances()
        {
            var map = Map512(20260920);
            var standing = VillagePlacement.PlaceInitial(map, VillagePlacement.StreamFor(20260920));
            var names = standing.Select(s => s.Name).ToList();
            var rng = new GameRandom(5);
            for (int i = 0; i < 5; i++)
            {
                // Only the snow can take it (as if every other weight were nothing).
                Assert.That(VillagePlacement.TryPickNew(map, standing, names, r => r == Region.Snow ? VillageRules.CleansedWeight : 0f, rng, out var site), Is.True);
                Assert.That(site.Region, Is.EqualTo(Region.Snow));
                Assert.That(Vector2.Distance(site.Cell, map.TownCenter), Is.GreaterThanOrEqualTo(VillageRules.InnerMin));
                Assert.That(VillagePlacement.Fits(map, site.Cell, standing), Is.True);
                Assert.That(names, Does.Not.Contain(site.Name));
                standing.Add(site);
                names.Add(site.Name);
            }
        }

        [Test]
        public void ADamagedVillage_MendsOnlyAfterSixQuietHours_AndAFallenOneNever()
        {
            float max = VillageRules.MaxHp;
            Assert.That(VillageRules.Mend(300f, max, 5.9f, 1f), Is.EqualTo(300f), "hit lately: no mending");
            Assert.That(VillageRules.Mend(300f, max, VillageRules.RegenDelayHours, 1f), Is.EqualTo(300f + max * VillageRules.RegenPerHour).Within(0.01f));
            Assert.That(VillageRules.Mend(max - 1f, max, 100f, 1f), Is.EqualTo(max), "never above its durability");
            Assert.That(VillageRules.Mend(0f, max, 100f, 1f), Is.EqualTo(0f), "a fallen village is gone for good");
            Assert.That(VillageRules.Blow(20f), Is.EqualTo(20f * VillageRules.DamageMul).Within(0.001f));
            Assert.That(VillageRules.Blow(0f), Is.GreaterThan(0f), "every blow tells");
        }

        [Test]
        public void Candidates_AreOfTheLandRoundTheVillage_AndTheOldestMovesOnAfterADay()
        {
            Assert.That(VillageRules.CandidateLevel(20, 2, 0), Is.EqualTo(20), "an outer village's people are strong");
            Assert.That(VillageRules.CandidateLevel(3, 6, -2), Is.EqualTo(6), "never weaker than our own town's visitors");
            Assert.That(VillageRules.CandidateLevel(29, 1, 5), Is.EqualTo(Monsters.MonsterLevel.MaxLevel));
            Assert.That(VillageRules.MovesOn(10f, 33f), Is.False);
            Assert.That(VillageRules.MovesOn(10f, 34f), Is.True);
        }

        [Test]
        public void AVillageDefence_PaysMoreThanTheSameHunt()
        {
            int hunt = QuestPricing.HuntReward(DangerLevel.Medium, 4, 120f);
            int defence = QuestPricing.DefenceReward(DangerLevel.Medium, 4, 120f);
            Assert.That(defence, Is.EqualTo(QuestPricing.HuntReward(DangerLevel.Medium, 4, 120f, VillageRules.DefenceRewardFactor)));
            Assert.That(defence, Is.GreaterThan(hunt));
            Assert.That(Quest.ClientName(QuestClient.Village), Is.EqualTo("다른 마을"));
        }
    }
}
