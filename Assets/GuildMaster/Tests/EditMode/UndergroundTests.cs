using System.Collections.Generic;
using System.Linq;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>
    /// P4-85 ruins: the underground band above the surface — the surface untouched, the ways down where they belong,
    /// each dungeon one connected piece cut off from the surface by the dark, and routes that step through the portal.
    /// </summary>
    public class UndergroundTests
    {
        static MapData s_surface, s_map;

        /// <summary>One 512 map for the whole fixture (generating one takes a few seconds).</summary>
        static void Build()
        {
            if (s_map != null) return;
            var balance = ScriptableObject.CreateInstance<Session.GameBalance>();
            try
            {
                s_surface = MapGenerator.Generate(new MapGenSettings
                {
                    Width = balance.mapWidth, Height = balance.mapHeight, Seed = 20260920, TownHalfSize = balance.townHalfSize,
                    NestCount = balance.nestCount, MinNestDistanceFromTown = balance.minNestDistanceFromTown,
                    InnerHalfSize = balance.innerHalfSize, InnerNestCount = balance.innerNestCount,
                });
                s_map = Underground.Extend(s_surface, 20260920);
            }
            finally { Object.DestroyImmediate(balance); }
        }

        [Test]
        public void TheSurfaceIsUntouched_AndTheBandLiesAboveItBeyondTheDark()
        {
            Build();
            Assert.That(s_map.SurfaceHeight, Is.EqualTo(s_surface.Height));
            Assert.That(s_map.Height, Is.EqualTo(Underground.TotalHeight(s_surface.Height)));
            for (int i = 0; i < s_surface.Tiles.Length; i++)
                if (s_map.Tiles[i] != s_surface.Tiles[i]) Assert.Fail($"surface cell {i % s_map.Width},{i / s_map.Width} changed");
            Assert.That(s_map.Nests.Select(n => n.Cell), Is.EqualTo(s_surface.Nests.Select(n => n.Cell)));
            Assert.That(s_map.EdgeSpawnCells, Is.EqualTo(s_surface.EdgeSpawnCells), "the map's edges are the surface's");
            for (int y = s_map.SurfaceHeight; y < s_map.SurfaceHeight + Underground.Gap; y++)
            for (int x = 0; x < s_map.Width; x++)
                Assert.That(s_map[x, y], Is.EqualTo(TileType.Void), "nothing but the dark between");
            Assert.That(MapData.IsWalkable(TileType.Void) || MapData.IsWalkable(TileType.RuinWall), Is.False);
            Assert.That(MapData.IsHardCover(TileType.Void) && MapData.IsHardCover(TileType.RuinWall), Is.True, "the dark and the walls block sight");
            Assert.That(Underground.Extend(s_surface, 20260920).Ruins.Select(r => r.Entrance), Is.EqualTo(s_map.Ruins.Select(r => r.Entrance)), "the same seed, the same ruins");
        }

        [Test]
        public void TheWaysDown_AreOnOpenGround_ClearOfTheTownTheDensAndEachOther()
        {
            Build();
            Assert.That(s_map.Ruins.Count, Is.EqualTo(Underground.Count));
            foreach (var r in s_map.Ruins)
            {
                Assert.That(s_map[r.Entrance], Is.EqualTo(TileType.Grass).Or.EqualTo(TileType.Sand), $"#{r.Index}");
                Assert.That(r.Entrance.y, Is.LessThan(s_map.SurfaceHeight));
                Assert.That(Vector2Int.Distance(r.Entrance, s_map.TownCenter), Is.GreaterThanOrEqualTo(Underground.MinTownDistance));
                foreach (var n in s_map.Nests) Assert.That(Vector2Int.Distance(r.Entrance, n.Cell), Is.GreaterThanOrEqualTo(Underground.NestMargin));
                foreach (var o in s_map.Ruins)
                    if (o != r) Assert.That(Vector2Int.Distance(r.Entrance, o.Entrance), Is.GreaterThanOrEqualTo(Underground.Spacing));
                Assert.That(r.Region, Is.EqualTo(s_map.RegionOf(r.Entrance.x, r.Entrance.y)));
            }
            int inner = s_map.Ruins.Count(r => Vector2Int.Distance(r.Entrance, s_map.TownCenter) <= Underground.InnerMax + 1f);
            Assert.That(inner, Is.InRange(2, Underground.Count - 2), "some near, some far");
        }

        [Test]
        public void EachDungeon_IsOnePieceFromTheStepsUpToTheTreasure_InsideItsOwnSlot()
        {
            Build();
            foreach (var r in s_map.Ruins)
            {
                Assert.That(r.Area.yMin, Is.GreaterThanOrEqualTo(s_map.SurfaceHeight + Underground.Gap));
                Assert.That(r.Area.yMax, Is.LessThanOrEqualTo(s_map.Height));
                Assert.That(r.Rooms.Count, Is.EqualTo(Underground.Rooms));
                Assert.That(s_map[r.Inside], Is.EqualTo(TileType.RuinFloor));
                Assert.That(s_map[r.Chest], Is.EqualTo(TileType.RuinFloor));
                Assert.That(s_map.RuinAt(r.Inside), Is.SameAs(r));
                Assert.That(s_map.RuinAt(r.Entrance), Is.Null, "the way down is on the surface");
                // Every floor cell of the slot is reached from the steps up without leaving the slot; its border is wall.
                var seen = new HashSet<Vector2Int> { r.Inside };
                var open = new Queue<Vector2Int>(seen);
                while (open.Count > 0)
                {
                    var c = open.Dequeue();
                    foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                    {
                        var n = c + d;
                        if (!r.Area.Contains(n) || s_map[n] != TileType.RuinFloor || !seen.Add(n)) continue;
                        open.Enqueue(n);
                    }
                }
                Assert.That(seen.Contains(r.Chest), Is.True, $"#{r.Index}: the treasure can be reached");
                for (int y = r.Area.yMin; y < r.Area.yMax; y++)
                for (int x = r.Area.xMin; x < r.Area.xMax; x++)
                {
                    var c = new Vector2Int(x, y);
                    if (s_map[c] == TileType.RuinFloor) Assert.That(seen.Contains(c), Is.True, $"#{r.Index}: no cut-off floor at {c}");
                    bool border = x == r.Area.xMin || y == r.Area.yMin || x == r.Area.xMax - 1 || y == r.Area.yMax - 1;
                    if (border) Assert.That(s_map[c], Is.EqualTo(TileType.RuinWall), $"#{r.Index}: walled in at {c}");
                }
            }
        }

        [Test]
        public void ARouteBetweenTheSurfaceAndADungeon_StepsThroughItsPortalOnce_EitherWay()
        {
            Build();
            var world = new WorldService(s_map);
            var r = s_map.Ruins.OrderBy(o => Vector2Int.Distance(o.Entrance, s_map.TownCenter)).First();
            var path = new List<Vector2Int>();
            Assert.That(world.FindPath(world.TownCenterWorld, GridMap.CellToWorld(r.Chest), path), Is.True, "down");
            var jumps = Jumps(path);
            Assert.That(jumps.Count, Is.EqualTo(1));
            Assert.That(path[jumps[0] - 1], Is.EqualTo(r.Entrance));
            Assert.That(path[jumps[0]], Is.EqualTo(r.Inside));
            Assert.That(path[^1], Is.EqualTo(r.Chest));

            Assert.That(world.FindPath(GridMap.CellToWorld(r.Chest), world.TownCenterWorld, path), Is.True, "up");
            jumps = Jumps(path);
            Assert.That(jumps.Count, Is.EqualTo(1));
            Assert.That(path[jumps[0] - 1], Is.EqualTo(r.Inside));
            Assert.That(path[jumps[0]], Is.EqualTo(r.Entrance));

            Assert.That(world.FindPath(GridMap.CellToWorld(r.Inside), GridMap.CellToWorld(r.Chest), path), Is.True, "within the halls");
            Assert.That(Jumps(path), Is.Empty);
            Assert.That(world.TryGetPortalExit(r.Entrance, out var exit) && exit == r.Inside, Is.True);
            Assert.That(world.OnTheSurface(GridMap.CellToWorld(r.Chest)), Is.EqualTo(GridMap.CellToWorld(r.Entrance)), "down there is as far as its way down");
            Assert.That(world.HasLineOfSight(GridMap.CellToWorld(r.Entrance), GridMap.CellToWorld(r.Inside), 99), Is.False, "nobody sees across the dark");
        }

        static List<int> Jumps(List<Vector2Int> path)
        {
            var jumps = new List<int>();
            for (int i = 1; i < path.Count; i++)
                if ((path[i] - path[i - 1]).sqrMagnitude > 9) jumps.Add(i);
            return jumps;
        }
    }
}
