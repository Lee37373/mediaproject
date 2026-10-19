using System.Collections.Generic;
using System.Linq;
using GuildMaster.Town;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Pushing the town walls out (P4-86, user decision 2026-10-19): 23 → 31 → 39 → 47 cells, the old ring becomes
    /// floor, the gates move with the walls, the roads lead on from the new gates, and nothing walks through the walls.
    /// </summary>
    public class TownExpansionTests
    {
        static MapData Map(int seed = 42) => MapGenerator.Generate(new MapGenSettings
        {
            Width = 128, Height = 128, Seed = seed, TownHalfSize = 11, NestCount = 8, MinNestDistanceFromTown = 20,
        });

        static int Along(Vector2Int v, Vector2Int dir) => v.x * dir.x + v.y * dir.y;

        static List<Vector2Int> GateCells(MapData map, int g)
        {
            var dir = TownWalls.GateDirections[g];
            var side = new Vector2Int(dir.y, dir.x);
            return new List<Vector2Int> { map.Gates[g] - side, map.Gates[g], map.Gates[g] + side };
        }

        [Test]
        public void TheGeneratorLaysTheTown_GatesNorthSouthWestEast()
        {
            var map = Map();
            var c = map.TownCenter;
            Assert.That(map.TownHalfSize, Is.EqualTo(11));
            Assert.That(map.BaseTownHalfSize, Is.EqualTo(11));
            Assert.That(map.TownRect, Is.EqualTo(TownWalls.RectFor(c, 11)));
            Assert.That(map.Gates, Is.EqualTo(new[] { c + Vector2Int.up * 11, c + Vector2Int.down * 11, c + Vector2Int.left * 11, c + Vector2Int.right * 11 }));
            foreach (var g in map.Gates)
                Assert.That(map[g + (g - c) / 11], Is.EqualTo(TileType.Road), "a road leads out of every gate");
        }

        [Test]
        public void GrowingMovesTheRingAndTheGates_AndTheOldWallBecomesFloor()
        {
            var map = Map();
            var c = map.TownCenter;
            var oldRect = map.TownRect;
            var area = TownWalls.Grow(map, 15);

            Assert.That(map.TownHalfSize, Is.EqualTo(15));
            Assert.That(map.BaseTownHalfSize, Is.EqualTo(11));
            var r = map.TownRect;
            Assert.That(r, Is.EqualTo(TownWalls.RectFor(c, 15)));
            Assert.That(area.Contains(new Vector2Int(r.xMin, r.yMin)) && area.Contains(new Vector2Int(r.xMax - 1, r.yMax - 1)), Is.True);
            for (int g = 0; g < 4; g++)
            {
                Assert.That(map.Gates[g], Is.EqualTo(c + TownWalls.GateDirections[g] * 15));
                foreach (var cell in GateCells(map, g)) Assert.That(map[cell], Is.EqualTo(TileType.Gate));
            }
            var gateCells = Enumerable.Range(0, 4).SelectMany(g => GateCells(map, g)).ToHashSet();
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                var cell = new Vector2Int(x, y);
                bool border = x == r.xMin || y == r.yMin || x == r.xMax - 1 || y == r.yMax - 1;
                var t = map[cell];
                if (border) Assert.That(t, Is.EqualTo(gateCells.Contains(cell) ? TileType.Gate : TileType.Wall), $"ring at {cell}");
                else Assert.That(t, Is.EqualTo(TileType.TownFloor).Or.EqualTo(TileType.Road), $"inside at {cell}");
                Assert.That(map.HasTree(x, y), Is.False);
            }
            // The plaza roads run on to the new gates, over where the old ones stood.
            for (int k = -14; k <= 14; k++)
            {
                Assert.That(map[c.x + k, c.y], Is.EqualTo(TileType.Road));
                Assert.That(map[c.x, c.y + k], Is.EqualTo(TileType.Road));
            }
            Assert.That(map[oldRect.xMin, oldRect.yMin], Is.EqualTo(TileType.TownFloor), "the old corner is floor now");
        }

        [Test]
        public void EveryStep_TheRoadOutOfEachGateJoinsTheOldOne_AndTheGroundRoundIsOpen()
        {
            var map = Map(20260920);
            var c = map.TownCenter;
            foreach (int half in new[] { 15, 19, 23 })
            {
                TownWalls.Grow(map, half);
                for (int g = 0; g < 4; g++)
                {
                    var dir = TownWalls.GateDirections[g];
                    // Walk the road (4-neighbours) from just outside the gate: it reaches well past the clearing.
                    var start = map.Gates[g] + dir;
                    Assert.That(map[start], Is.EqualTo(TileType.Road), $"half {half} gate {g}: a road at the gate");
                    var seen = new HashSet<Vector2Int> { start };
                    var queue = new Queue<Vector2Int>(seen);
                    int farthest = 0;
                    while (queue.Count > 0)
                    {
                        var p = queue.Dequeue();
                        int along = Along(p - c, dir);
                        farthest = Mathf.Max(farthest, along);
                        foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                        {
                            var n = p + d;
                            if (!map.InBounds(n) || map[n] != TileType.Road || Along(n - c, dir) <= half || !seen.Add(n)) continue;
                            queue.Enqueue(n);
                        }
                    }
                    Assert.That(farthest, Is.GreaterThan(half + TownWalls.ClearRound + 15), $"half {half} gate {g}: the road goes on out");
                }
                var r = map.TownRect;
                for (int y = r.yMin - 10; y < r.yMax + 10; y++)
                for (int x = r.xMin - 10; x < r.xMax + 10; x++)
                {
                    if (!map.InBounds(x, y) || r.Contains(new Vector2Int(x, y)) || !TownWalls.InClearing(x, y, c, half)) continue;
                    Assert.That(map[x, y], Is.EqualTo(TileType.Grass).Or.EqualTo(TileType.Road), $"half {half}: cleared at ({x},{y})");
                    Assert.That(map.HasTree(x, y), Is.False);
                }
            }
        }

        [Test]
        public void AfterGrowing_TheGuildWalksInThroughTheNewGates_AndMonstersStillCannot()
        {
            var map = Map();
            TownWalls.Grow(map, 15);
            TownWalls.Grow(map, 19);
            var grid = new GridMap(map);
            var path = new Pathfinder(grid);
            for (int g = 0; g < 4; g++) grid.SetGateClosed(GateCells(map, g), true);

            var inside = map.TownCenter;
            var outside = map.Gates[3] + Vector2Int.right * 12;
            Assert.That(grid.IsWalkable(outside), Is.True);
            var cells = new List<Vector2Int>();
            Assert.That(path.FindPath(outside, inside, cells), Is.True, "the guild walks in");
            Assert.That(cells.Any(c => map[c] == TileType.Gate), Is.True, "through a gate");
            Assert.That(path.FindPath(outside, inside, cells, passesGates: false), Is.False, "a monster does not");
            // Where the first gates stood is plain floor now.
            Assert.That(grid.IsWalkable(map.TownCenter + Vector2Int.right * 11 + Vector2Int.up), Is.True);
        }

        [Test]
        public void GrowingAFreshMapAgain_LaysTheSameTiles()
        {
            var a = Map(7);
            var b = Map(7);
            TownWalls.Grow(a, 15);
            TownWalls.Grow(a, 19);
            TownWalls.Grow(b, 15);
            TownWalls.Grow(b, 19);
            Assert.That(a.Tiles, Is.EqualTo(b.Tiles));
            Assert.That(a.Trees, Is.EqualTo(b.Trees));
            Assert.That(a.Gates, Is.EqualTo(b.Gates));
        }

        [Test]
        public void Rules_ThreeStepsOfFourCells_CostsRise_SightCoversTheCorners()
        {
            Assert.That(TownExpansionRules.HalfSizeAt(11, 0), Is.EqualTo(11));
            Assert.That(TownExpansionRules.HalfSizeAt(11, 3), Is.EqualTo(23));
            Assert.That(TownExpansionRules.HalfSizeAt(11, 9), Is.EqualTo(23), "no step past the last");
            Assert.That(new[] { 0, 1, 2, 3 }.Select(s => TownExpansionRules.Width(TownExpansionRules.HalfSizeAt(11, s))), Is.EqualTo(new[] { 23, 31, 39, 47 }));
            int last = 0;
            for (int s = 0; s < TownExpansionRules.MaxStage; s++)
            {
                var cost = TownExpansionRules.CostToGrow(s);
                Assert.That(cost.HasValue, Is.True);
                Assert.That(cost.Value.Gold, Is.GreaterThan(last));
                Assert.That(cost.Value.Items, Is.Not.Empty);
                last = cost.Value.Gold;
            }
            Assert.That(TownExpansionRules.CostToGrow(TownExpansionRules.MaxStage).HasValue, Is.False);
            for (int growth = 0; growth <= 12; growth += 4)
            {
                float corner = (11 + growth) * Mathf.Sqrt(2f);
                float sight = TownExpansionRules.SightRadius(18f, growth);
                Assert.That(sight - corner, Is.EqualTo(18f - 11f * Mathf.Sqrt(2f)).Within(0.01f), "as far past the corners as at the start");
                Assert.That(sight, Is.GreaterThanOrEqualTo(11 + growth + 7), "and at least seven cells past the middle of each wall");
            }
        }

        [Test]
        public void ADenTooCloseToTheNewWalls_CrowdsThem()
        {
            var c = new Vector2Int(100, 100);
            Assert.That(TownExpansionRules.Crowds(c + new Vector2Int(19, 0), c, 15), Is.True, "four past the wall");
            Assert.That(TownExpansionRules.Crowds(c + new Vector2Int(20, 3), c, 15), Is.False, "five past it");
            Assert.That(TownExpansionRules.Crowds(c + new Vector2Int(18, -18), c, 15), Is.True, "off the corner");
        }

        [Test]
        public void ATowerOnTheNewWall_OrPluggingANewGate_IsInTheWay()
        {
            var map = Map();
            TownWalls.Grow(map, 15);
            var c = map.TownCenter;
            Assert.That(ConstructionRules.InTheWayOfWalls(map, map.Gates, new RectInt(c.x + 6, c.y + 14, 2, 2)), Is.True, "on the north wall");
            Assert.That(ConstructionRules.InTheWayOfWalls(map, map.Gates, new RectInt(c.x - 1, c.y + 16, 2, 2)), Is.True, "in front of the north gate");
            Assert.That(ConstructionRules.InTheWayOfWalls(map, map.Gates, new RectInt(c.x + 6, c.y + 11, 2, 2)), Is.False, "where the old wall was");
            Assert.That(ConstructionRules.InTheWayOfWalls(map, map.Gates, new RectInt(c.x + 6, c.y + 18, 2, 2)), Is.False, "out in the field");
        }
    }
}
