using System.Linq;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Places worth going to (P4-105): every kind laid on the game's map, each on its own ground, clear of the town and the deep
    /// woods and of one another, some gathering spots close in, and the same seed laying the same places.
    /// </summary>
    public class PlacePlacementTests
    {
        static MapData Map(int seed)
        {
            var b = ScriptableObject.CreateInstance<Session.GameBalance>();
            try
            {
                var map = MapGenerator.Generate(new MapGenSettings
                {
                    Width = b.mapWidth, Height = b.mapHeight, Seed = seed, TownHalfSize = b.townHalfSize, NestCount = b.nestCount,
                    MinNestDistanceFromTown = b.minNestDistanceFromTown, InnerHalfSize = b.innerHalfSize, InnerNestCount = b.innerNestCount,
                    Landforms = b.landforms,
                });
                map = Underground.Extend(map, seed);
                PlacePlacement.Lay(map, seed);
                return map;
            }
            finally { Object.DestroyImmediate(b); }
        }

        static bool Near(MapData map, Vector2Int c, int r, System.Func<int, bool> match)
        {
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = c.x + dx, y = c.y + dy;
                if (x >= 0 && y >= 0 && x < map.Width && y < map.SurfaceHeight && match(map.Index(x, y))) return true;
            }
            return false;
        }

        [Test]
        public void EveryKind_IsLaid_OnItsGround_ClearOfTheTownAndTheWoods_AndOfOneAnother()
        {
            foreach (int seed in new[] { 20260920, 4242 })
            {
                var map = Map(seed);
                foreach (var spec in PlacePlacement.Specs)
                    Assert.That(map.Places.Count(p => p.Kind == spec.Kind), Is.EqualTo(spec.Count), $"seed {seed}: {spec.Kind}");
                foreach (var p in map.Places)
                {
                    int i = map.Index(p.Cell.x, p.Cell.y);
                    string at = $"seed {seed} {p.Kind} {p.Cell}";
                    Assert.That(map.Tiles[i], Is.EqualTo(TileType.Grass).Or.EqualTo(TileType.Sand), at);
                    Assert.That(map.Deep[i], Is.False, at);
                    Assert.That(map.TownRect.Contains(p.Cell), Is.False, at);
                    Assert.That(map.Nests.All(n => (n.Cell - p.Cell).magnitude >= 10f), Is.True, at + " clear of the dens");
                    Assert.That(map.Places.All(o => o == p || (o.Cell - p.Cell).magnitude >= 20f), Is.True, at + " apart from the others");
                    switch (p.Kind)
                    {
                        case PlaceKind.HotSpring: Assert.That(Near(map, p.Cell, 8, j => map.Tiles[j] == TileType.Water), Is.True, at + " by water"); break;
                        case PlaceKind.OreVein:
                            Assert.That(Near(map, p.Cell, 8, j => map.Tiles[j] == TileType.Mountain) || map.Regions[i] is Region.Desert or Region.Wasteland,
                                Is.True, at + " in rocky ground"); break;
                        case PlaceKind.HerbPatch:
                            Assert.That(map.Regions[i] == Region.Swamp || Near(map, p.Cell, 6, j => map.Tiles[j] == TileType.Forest), Is.True, at + " by woods or in the swamp"); break;
                        case PlaceKind.OldCamp: Assert.That(Near(map, p.Cell, 12, j => map.Tiles[j] == TileType.Road), Is.True, at + " by a road"); break;
                    }
                }
                foreach (var spec in PlacePlacement.Specs.Where(s => s.NearCount > 0))
                    Assert.That(map.Places.Count(p => p.Kind == spec.Kind && (p.Cell - map.TownCenter).magnitude <= spec.NearDistance + 1f),
                        Is.GreaterThanOrEqualTo(spec.NearCount), $"seed {seed}: {spec.Kind} close to town");
            }
        }

        [Test]
        public void TheSameSeed_LaysTheSamePlaces()
        {
            var a = Map(31337).Places.Select(p => (p.Kind, p.Cell)).ToList();
            var b = Map(31337).Places.Select(p => (p.Kind, p.Cell)).ToList();
            Assert.That(b, Is.EqualTo(a));
        }
    }
}
