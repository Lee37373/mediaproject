using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>Line of sight used by the fog and by target detection (walls/mountains block, trees do not).</summary>
    public class SightTests
    {
        static WorldService WallAtX5()
        {
            var map = new MapData(12, 12);
            for (int i = 0; i < map.Tiles.Length; i++) map.Tiles[i] = TileType.Grass;
            for (int y = 0; y < 12; y++) map[5, y] = TileType.Wall;
            map[3, 8] = TileType.Forest;
            map.Trees[map.Index(3, 8)] = true;
            return new WorldService(map);
        }

        [Test]
        public void WallBlocksSightBehindIt()
        {
            var w = WallAtX5();
            Assert.That(w.Raycast(new Vector2(2.5f, 2.5f), new Vector2(8.5f, 2.5f), out _, out _), Is.True);
            Assert.That(w.HasLineOfSight(new Vector2(2.5f, 2.5f), new Vector2(8.5f, 2.5f)), Is.False);
        }

        [Test]
        public void WallCellItselfIsVisible()
        {
            var w = WallAtX5();
            Assert.That(w.Raycast(new Vector2(2.5f, 2.5f), new Vector2(5.5f, 2.5f), out _, out _), Is.False);
        }

        [Test]
        public void TreesDoNotBlockSameSideSight()
        {
            var w = WallAtX5();
            Assert.That(w.Raycast(new Vector2(1.5f, 8.5f), new Vector2(4.5f, 8.5f), out _, out int trees), Is.False);
            Assert.That(trees, Is.EqualTo(1));
            Assert.That(w.HasLineOfSight(new Vector2(1.5f, 8.5f), new Vector2(4.5f, 8.5f)), Is.True);
        }
    }
}
