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

        // ---------------- Forests (P4-48) ----------------

        /// <summary>Open grass 30x10 with a forest band at x = 10..14 across the whole height.</summary>
        static WorldService ForestBand()
        {
            var map = new MapData(30, 10);
            for (int i = 0; i < map.Tiles.Length; i++) map.Tiles[i] = TileType.Grass;
            for (int y = 0; y < 10; y++)
            for (int x = 10; x <= 14; x++) map[x, y] = TileType.Forest;
            return new WorldService(map);
        }

        static Vector2 At(int x, int y) => new(x + 0.5f, y + 0.5f);

        [Test]
        public void FromOutside_OnlyTheForestEdgeIsSeen_NotInsideOrBeyond()
        {
            var w = ForestBand();
            Assert.That(w.CanSee(At(5, 5), At(10, 5), 20f), Is.True, "the first forest cell");
            Assert.That(w.CanSee(At(5, 5), At(12, 5), 20f), Is.False, "deeper in");
            Assert.That(w.CanSee(At(5, 5), At(20, 5), 20f), Is.False, "the grass beyond");
            Assert.That(w.CanSee(At(5, 5), At(9, 5), 20f), Is.True, "grass on our side");
        }

        [Test]
        public void Inside_TheForestDoesNotBlock_ButSightIsAThird()
        {
            var w = ForestBand();
            Assert.That(ForestSight.Radius(w, At(12, 5), 9f), Is.EqualTo(3f).Within(1e-4f));
            Assert.That(ForestSight.Radius(w, At(5, 5), 9f), Is.EqualTo(9f));
            Assert.That(w.CanSee(At(12, 5), At(14, 5), 9f), Is.True, "2 cells away, within 3");
            Assert.That(w.CanSee(At(12, 5), At(16, 5), 9f), Is.False, "4 cells away: past a third of 9");
            Assert.That(w.CanSee(At(14, 5), At(16, 5), 9f), Is.True, "looking out from the edge");
        }

        [Test]
        public void ElevatedEyes_IgnoreForests()
        {
            var w = ForestBand();
            Assert.That(ForestSight.ForestBlocks(w, At(5, 5), elevated: true), Is.False);
            Assert.That(ForestSight.Radius(w, At(12, 5), 9f, elevated: true), Is.EqualTo(9f));
            Assert.That(w.Raycast(At(5, 5), At(20, 5), out _, out _), Is.False, "shots and plain rays are not forest-blocked");
        }
    }
}
