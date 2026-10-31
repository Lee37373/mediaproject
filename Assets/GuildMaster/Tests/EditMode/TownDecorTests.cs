using GuildMaster.Town;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>P4-114: where the town's decorations stand.</summary>
    public class TownDecorTests
    {
        static MapData Town(int half)
        {
            var map = new MapData(64, 64);
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                map[x, y] = TileType.Grass;
            TownWalls.Lay(map, new Vector2Int(32, 32), half);
            TownWalls.LayPlazaRoads(map);
            return map;
        }

        [Test]
        public void Decorations_StayOnTheGrassInside_ClearOfTheWallsStreetsAndPlaza_WithLampsBesideTheStreets()
        {
            var map = Town(11);
            var c = map.TownCenter;
            int lamps = 0, others = 0;
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                var prop = TownDecorRules.At(map, x, y);
                if (prop == TownProp.None) continue;
                Assert.That(map[x, y], Is.EqualTo(TileType.TownFloor), $"({x},{y}) on the town's own ground");
                var r = map.TownRect;
                Assert.That(x >= r.xMin + TownDecorRules.WallGap && x < r.xMax - TownDecorRules.WallGap
                            && y >= r.yMin + TownDecorRules.WallGap && y < r.yMax - TownDecorRules.WallGap, Is.True, $"({x},{y}) clear of the walls");
                int dx = x - c.x, dy = y - c.y;
                if (prop == TownProp.Lamp)
                {
                    lamps++;
                    bool besideStreet = Mathf.Abs(dx) == 1 && map[c.x, y] == TileType.Road || Mathf.Abs(dy) == 1 && map[x, c.y] == TileType.Road;
                    Assert.That(besideStreet, Is.True, $"lamp ({x},{y}) beside a street");
                    Assert.That((Mathf.Abs(dx) == 1 ? dy : dx) % TownDecorRules.LampEvery, Is.EqualTo(0), "lamps every few cells");
                }
                else
                {
                    others++;
                    Assert.That(Mathf.Abs(dx) > 1 && Mathf.Abs(dy) > 1, Is.True, $"{prop} ({x},{y}) off the streets");
                    Assert.That(dx * dx + dy * dy, Is.GreaterThan(TownDecorRules.PlazaClear2), $"{prop} ({x},{y}) off the plaza");
                }
                Assert.That(TownDecorRules.At(map, x, y), Is.EqualTo(prop), "the same every time");
            }
            Assert.That(lamps, Is.EqualTo(8), "two each way along both streets in a town of 23");
            Assert.That(others, Is.GreaterThan(5), "some things on the grass");
        }

        [Test]
        public void ABiggerTown_HasMoreLamps()
        {
            int Lamps(MapData map)
            {
                int n = 0;
                for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                    if (TownDecorRules.At(map, x, y) == TownProp.Lamp) n++;
                return n;
            }
            Assert.That(Lamps(Town(15)), Is.GreaterThan(Lamps(Town(11))));
        }
    }
}
