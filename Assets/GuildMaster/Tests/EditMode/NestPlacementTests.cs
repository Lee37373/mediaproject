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
    /// Nests are handed out by terrain (P4-58, user decision 2026-09-22): a species lives only in its
    /// regions; how strong its young are is the site's distance (the birth level), not the species.
    /// </summary>
    public class NestPlacementTests
    {
        static MonsterDefinition Species(string id, DangerLevel danger, params Region[] regions)
        {
            var m = ScriptableObject.CreateInstance<MonsterDefinition>();
            m.name = m.id = id;
            m.displayName = id;
            m.danger = danger;
            m.regions.AddRange(regions);
            return m;
        }

        static List<MonsterDefinition> Roster() => new()
        {
            Species("grass", DangerLevel.Low, Region.Grassland),
            Species("sand", DangerLevel.Low, Region.Desert),
            Species("both", DangerLevel.Medium, Region.Grassland, Region.Desert),
        };

        /// <summary>Alternating grassland and desert sites, ordered by distance from town.</summary>
        static List<NestSite> Sites(int count)
        {
            var sites = new List<NestSite>();
            for (int i = 0; i < count; i++)
                sites.Add(new NestSite
                {
                    Cell = new Vector2Int(i, 0), Biome = Biome.Plains, DistanceFromTown = 20 + i * 5,
                    Region = i % 2 == 0 ? Region.Grassland : Region.Desert,
                });
            return sites;
        }

        [Test]
        public void Fit_IsZeroOutsideTheHabitat_AndPrefersItsBiomes()
        {
            var s = Species("grass", DangerLevel.Low, Region.Grassland);
            Assert.That(EcosystemManager.Fit(s, Region.Desert, Biome.Plains), Is.Zero);
            Assert.That(EcosystemManager.Fit(s, Region.Grassland, Biome.Plains), Is.GreaterThan(0f));
            s.biomes.Add(Biome.Forest);
            Assert.That(EcosystemManager.Fit(s, Region.Grassland, Biome.Forest),
                        Is.GreaterThan(EcosystemManager.Fit(s, Region.Grassland, Biome.Plains)));
            // A woodland kind lives in woods anywhere.
            s.habitatBiomes.Add(Biome.Forest);
            Assert.That(EcosystemManager.Fit(s, Region.Desert, Biome.Forest), Is.GreaterThan(0f));
        }

        [Test]
        public void EverySiteGoesToAKindThatLivesThere_AndNobodyIsLeftOut()
        {
            var species = Roster();
            for (int seed = 0; seed < 8; seed++)
            {
                var sites = Sites(12);
                var result = EcosystemManager.AssignSpecies(species, sites, new GameRandom(seed));
                for (int i = 0; i < sites.Count; i++)
                    Assert.That(result[i].LivesAt(sites[i].Region, sites[i].Biome), Is.True, $"{result[i].id} on {sites[i].Region}");
                foreach (var s in species)
                    Assert.That(result, Does.Contain(s), $"{s.id} was left out of the world");
            }
        }

        [Test]
        public void GuaranteedNestsAreTheNearestOfTheHabitat()
        {
            // The early economy lives off the slimes by the walls (minNests at the nearest sites).
            var species = Roster();
            species[0].minNests = 2;
            var sites = Sites(12);
            var result = EcosystemManager.AssignSpecies(species, sites, new GameRandom(3));
            Assert.That(result[0].id, Is.EqualTo("grass"), "the nearest grassland site");
            Assert.That(result[2].id, Is.EqualTo("grass"), "the next grassland site");
        }

        /// <summary>
        /// The real roster on real maps: every kind lives only on its own terrain, and every kind
        /// is somewhere on the map.
        /// </summary>
        [Test]
        public void OnGeneratedMaps_EachKindLivesOnItsOwnTerrain()
        {
            var db = DefaultContent.Build();
            var balance = ScriptableObject.CreateInstance<Session.GameBalance>();
            var seen = new HashSet<string>();
            for (int seed = 1; seed <= 4; seed++)
            {
                var map = MapGenerator.Generate(new MapGenSettings
                {
                    Width = balance.mapWidth,
                    Height = balance.mapHeight,
                    Seed = seed,
                    TownHalfSize = balance.townHalfSize,
                    NestCount = balance.nestCount,
                    MinNestDistanceFromTown = balance.minNestDistanceFromTown,
                    InnerHalfSize = balance.innerHalfSize,
                    InnerNestCount = balance.innerNestCount, Landforms = balance.landforms,
                });
                var assignment = EcosystemManager.AssignSpecies(db.monsters, map.Nests, new GameRandom(seed));
                for (int i = 0; i < assignment.Count; i++)
                {
                    var site = map.Nests[i];
                    Assert.That(assignment[i].LivesAt(site.Region, site.Biome), Is.True,
                                $"seed {seed}: {assignment[i].id} on {site.Region}/{site.Biome}");
                    seen.Add(assignment[i].id);
                }
            }
            Object.DestroyImmediate(balance);
            // Region lords (P4-83) and the dragon (P4-93) are never dealt a den: their lairs are set apart (RegionLords.PickLairs, Dragon.PickLair).
            foreach (var s in db.monsters.Where(m => !m.isRegionLord && !m.isDragon)) Assert.That(seen, Does.Contain(s.id), $"{s.id} never got a nest");
        }

        [Test]
        public void On512Map_HalfTheNestsStayOnTheOld256Map()
        {
            // P4-81 (user decision 2026-10-16): 64 nests in all; spread evenly only 16 would be near the town.
            var balance = ScriptableObject.CreateInstance<Session.GameBalance>();
            try
            {
                Assert.That(balance.mapWidth, Is.EqualTo(512));
                var map = MapGenerator.Generate(new MapGenSettings
                {
                    Width = balance.mapWidth, Height = balance.mapHeight, Seed = 7, TownHalfSize = balance.townHalfSize,
                    NestCount = balance.nestCount, MinNestDistanceFromTown = balance.minNestDistanceFromTown,
                    InnerHalfSize = balance.innerHalfSize, InnerNestCount = balance.innerNestCount, Landforms = balance.landforms,
                });
                var c = map.TownCenter;
                int inner = map.Nests.Count(n => Mathf.Abs(n.Cell.x - c.x) <= balance.innerHalfSize && Mathf.Abs(n.Cell.y - c.y) <= balance.innerHalfSize);
                Assert.That(map.Nests.Count, Is.EqualTo(balance.nestCount));
                Assert.That(inner, Is.EqualTo(balance.innerNestCount));
                Assert.That(map.Nests.Max(n => (n.Cell - c).magnitude), Is.GreaterThan(Ecosystem.EcosystemManager.InnerEdge + 60f),
                            "the outer land has nests far out (bosses and towns go there next)");
            }
            finally
            {
                Object.DestroyImmediate(balance);
            }
        }

        [Test]
        public void EveryRegionHasAKindLivingInIt()
        {
            var db = DefaultContent.Build();
            foreach (Region r in System.Enum.GetValues(typeof(Region)))
                Assert.That(db.monsters.Any(m => m.LivesAt(r, Biome.Plains)), Is.True, $"nobody lives in {r}");
        }
    }
}
