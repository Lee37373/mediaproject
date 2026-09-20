using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Nests are handed out by how far the site is from town (P5-1, user decision 2026-09-20):
    /// weak species ring the walls, dangerous ones live out at the edges.
    /// </summary>
    public class NestPlacementTests
    {
        static MonsterDefinition Species(string id, float band, DangerLevel danger, params Biome[] biomes)
        {
            var m = ScriptableObject.CreateInstance<MonsterDefinition>();
            m.name = m.id = id;
            m.displayName = id;
            m.distanceBand = band;
            m.danger = danger;
            m.biomes.AddRange(biomes.Length > 0 ? biomes : new[] { Biome.Plains });
            return m;
        }

        static List<MonsterDefinition> Roster() => new()
        {
            Species("weak", 0.05f, DangerLevel.Low),
            Species("mid", 0.4f, DangerLevel.Low),
            Species("strong", 0.8f, DangerLevel.Medium),
            Species("boss", 1f, DangerLevel.High),
        };

        static List<NestSite> Sites(int count)
        {
            var sites = new List<NestSite>();
            for (int i = 0; i < count; i++)
                sites.Add(new NestSite { Cell = new Vector2Int(i, 0), Biome = Biome.Plains, DistanceFromTown = 20 + i * 5 });
            return sites;
        }

        [Test]
        public void Fit_PeaksAtTheSpeciesOwnBand()
        {
            var s = Species("mid", 0.4f, DangerLevel.Low);
            float onBand = EcosystemManager.Fit(s, 0.4f, Biome.Plains);
            float nearBand = EcosystemManager.Fit(s, 0.55f, Biome.Plains);
            float farOff = EcosystemManager.Fit(s, 1f, Biome.Plains);
            Assert.That(onBand, Is.GreaterThan(nearBand));
            Assert.That(nearBand, Is.GreaterThan(farOff));
        }

        [Test]
        public void Fit_PrefersAMatchingBiomeAtTheSameDistance()
        {
            var s = Species("mid", 0.4f, DangerLevel.Low, Biome.Forest);
            Assert.That(EcosystemManager.Fit(s, 0.4f, Biome.Forest),
                        Is.GreaterThan(EcosystemManager.Fit(s, 0.4f, Biome.Plains)));
        }

        [Test]
        public void EverySpeciesGetsAtLeastOneNest()
        {
            var species = Roster();
            var result = EcosystemManager.AssignSpecies(species, Sites(12), new GameRandom(1234));
            foreach (var s in species)
                Assert.That(result, Does.Contain(s), $"{s.id} was left out of the world");
        }

        /// <summary>Mean distance from town of the sites a species ended up on (0 sites = 0).</summary>
        static float Average(List<MonsterDefinition> assignment, List<NestSite> sites, string speciesId)
        {
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < assignment.Count; i++)
            {
                if (assignment[i] == null || assignment[i].id != speciesId) continue;
                sum += sites[i].DistanceFromTown;
                n++;
            }
            return n == 0 ? 0f : sum / n;
        }

        [Test]
        public void StrongSpeciesEndUpFurtherFromTownThanWeakOnes()
        {
            var species = Roster();
            var sites = Sites(14);
            // Average over seeds: any single map may be lopsided, the rule must hold in general.
            float weakSum = 0f, strongSum = 0f;
            int seeds = 12;
            for (int seed = 0; seed < seeds; seed++)
            {
                var result = EcosystemManager.AssignSpecies(species, sites, new GameRandom(seed));
                weakSum += Average(result, sites, "weak");
                strongSum += Average(result, sites, "strong");
            }
            Assert.That(strongSum / seeds, Is.GreaterThan(weakSum / seeds),
                        "strong species should settle further out than weak ones");
        }

        [Test]
        public void TheNearestSiteGoesToTheWeakestSpecies()
        {
            var species = Roster();
            var sites = Sites(14);
            int nearest = 0; // Sites() is ordered by distance
            int weakWins = 0;
            for (int seed = 0; seed < 12; seed++)
            {
                var result = EcosystemManager.AssignSpecies(species, sites, new GameRandom(seed));
                if (result[nearest].id == "weak") weakWins++;
            }
            Assert.That(weakWins, Is.GreaterThanOrEqualTo(10), "the ring around the walls should be the weak species'");
        }

        /// <summary>
        /// The real roster on real maps: the walk out of town must get harder, species by species.
        /// This is the rule the user asked for, so it is checked end to end, not just on the formula.
        /// </summary>
        [Test]
        public void OnGeneratedMaps_TheRosterRingsTheTownByDanger()
        {
            var db = DefaultContent.Build();
            var order = new[] { "slime", "stone_slime", "goblin", "skull", "goblin_warrior", "slime_king" };
            var totals = new Dictionary<string, float>();
            var counts = new Dictionary<string, int>();
            foreach (var id in order) { totals[id] = 0f; counts[id] = 0; }

            var balance = ScriptableObject.CreateInstance<Session.GameBalance>();
            for (int seed = 1; seed <= 8; seed++)
            {
                var map = MapGenerator.Generate(new MapGenSettings
                {
                    Width = balance.mapWidth,
                    Height = balance.mapHeight,
                    Seed = seed,
                    TownHalfSize = balance.townHalfSize,
                    NestCount = balance.nestCount,
                    MinNestDistanceFromTown = balance.minNestDistanceFromTown,
                });

                var assignment = EcosystemManager.AssignSpecies(db.monsters, map.Nests, new GameRandom(seed));
                for (int i = 0; i < assignment.Count; i++)
                {
                    var id = assignment[i].id;
                    if (!totals.ContainsKey(id)) continue;
                    totals[id] += map.Nests[i].DistanceFromTown;
                    counts[id]++;
                }
            }

            float Mean(string id) => counts[id] == 0 ? float.MaxValue : totals[id] / counts[id];
            for (int i = 1; i < order.Length; i++)
                Assert.That(Mean(order[i]), Is.GreaterThan(Mean(order[i - 1])),
                            $"{order[i]} should live further out than {order[i - 1]} " +
                            $"({Mean(order[i]):0} vs {Mean(order[i - 1]):0} cells)");
        }

        [Test]
        public void FewerSitesThanSpeciesStillFillsFromTheTownOutwards()
        {
            var result = EcosystemManager.AssignSpecies(Roster(), Sites(2), new GameRandom(7));
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.Select(r => r.id), Does.Contain("weak"));
        }
    }
}
