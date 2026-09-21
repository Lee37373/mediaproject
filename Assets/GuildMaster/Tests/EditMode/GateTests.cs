using System.Collections.Generic;
using System.Linq;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Town gates (P4-39, user request 2026-09-21): the guild walks through, monsters have to break
    /// a gate to get in, and the walls themselves never give.
    /// </summary>
    public class GateTests
    {
        static (MapData map, GridMap grid, Pathfinder path) Town()
        {
            var map = MapGenerator.Generate(new MapGenSettings
            {
                Width = 96, Height = 96, Seed = 42, TownHalfSize = 11, NestCount = 6, MinNestDistanceFromTown = 20,
            });
            var grid = new GridMap(map);
            return (map, grid, new Pathfinder(grid));
        }

        static List<Vector2Int> GateCells(MapData map, Vector2Int gate)
        {
            var d = gate - map.TownCenter;
            var side = Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? new Vector2Int(0, 1) : new Vector2Int(1, 0);
            return new List<Vector2Int> { gate - side, gate, gate + side };
        }

        [Test]
        public void ShutGatesKeepMonstersOut_ButNotTheGuild()
        {
            var (map, grid, path) = Town();
            foreach (var g in map.Gates) grid.SetGateClosed(GateCells(map, g), true);
            grid.TownSealed = true;

            var inside = map.TownCenter;
            var outside = map.Gates[0] + (map.Gates[0] - map.TownCenter) / 11 * 4; // down the road from a gate
            Assert.That(grid.IsWalkable(outside), Is.True);
            var cells = new List<Vector2Int>();

            Assert.That(path.FindPath(outside, inside, cells), Is.True, "the guild walks in");
            Assert.That(path.FindPath(outside, inside, cells, passesGates: false), Is.False, "a monster does not");
            grid.TownSealed = false; // the shortcut aside, the search itself finds no way through the walls
            Assert.That(path.FindPath(outside, inside, cells, passesGates: false), Is.False, "walls cannot be walked through");
            Assert.That(path.FindPath(inside, outside, cells, passesGates: false), Is.False, "nor out");

            // One gate broken: that is the way in.
            var broken = map.Gates[2];
            grid.SetGateClosed(GateCells(map, broken), false);
            Assert.That(path.FindPath(outside, inside, cells, passesGates: false), Is.True);
            Assert.That(cells.Any(c => GateCells(map, broken).Contains(c)), Is.True, "through the broken gate");
            foreach (var g in map.Gates.Where(g => g != broken))
                Assert.That(cells.Any(c => GateCells(map, g).Contains(c)), Is.False, "never through a standing one");
        }

        [Test]
        public void GateCellsStayWalkableForEveryoneElse()
        {
            var (map, grid, _) = Town();
            var gate = map.Gates[0];
            int version = grid.Version;
            grid.SetGateClosed(GateCells(map, gate), true);
            Assert.That(grid.Version, Is.GreaterThan(version), "paths in flight get rechecked");
            Assert.That(grid.IsWalkable(gate), Is.True);
            Assert.That(grid.IsWalkable(gate, passesGates: true), Is.True);
            Assert.That(grid.IsWalkable(gate, passesGates: false), Is.False);
            Assert.That(grid.IsClosedGate(gate), Is.True);
            grid.SetGateClosed(GateCells(map, gate), false);
            Assert.That(grid.IsWalkable(gate, passesGates: false), Is.True);
        }
    }
}
