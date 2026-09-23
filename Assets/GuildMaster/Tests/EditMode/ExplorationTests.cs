using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Units;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Walking country nobody has seen (P4-64): a plan may only use what is known, unseen ground is
    /// assumed open, and the band commits one leg at a time instead of one long route.
    /// </summary>
    public class ExplorationTests
    {
        /// <summary>Open grass with a lake across the middle, leaving a gap at the top.</summary>
        static MapData LakeMap()
        {
            var map = new MapData(40, 24);
            for (int i = 0; i < map.Tiles.Length; i++) map.Tiles[i] = TileType.Grass;
            for (int y = 0; y <= 18; y++)
            for (int x = 18; x <= 21; x++)
                map[x, y] = TileType.Water;
            return map;
        }

        static bool CrossesColumn(List<Vector2Int> path, int xMin, int xMax, int yMax)
        {
            foreach (var c in path) if (c.x >= xMin && c.x <= xMax && c.y <= yMax) return true;
            return false;
        }

        [Test]
        public void APlanOverUnseenGround_GoesStraightAt_ThenRoundOnceItIsKnown()
        {
            var map = LakeMap();
            var grid = new GridMap(map);
            var pf = new Pathfinder(grid);
            var from = new Vector2Int(2, 6);
            var to = new Vector2Int(37, 6);

            // Nobody has seen the middle of the map: the lake is not on anyone's plan yet.
            bool Unseen(int x, int y) => x >= 12 && x <= 28;
            var guess = new List<Vector2Int>();
            Assert.IsTrue(pf.FindPath(from, to, guess, true, null, 0f, Unseen));
            Assert.IsTrue(CrossesColumn(guess, 18, 21, 18), "the plan walks into the unseen lake");

            // Seen: the same walk keeps out of the water and goes round the top.
            var known = new List<Vector2Int>();
            Assert.IsTrue(pf.FindPath(from, to, known, true, null, 0f));
            Assert.IsFalse(CrossesColumn(known, 18, 21, 18), "what is known is walked round");
            foreach (var c in known) Assert.IsTrue(grid.IsWalkable(c), "and only over ground it can walk");
        }

        [Test]
        public void AnUnseenGoal_IsStillWorthWalkingTo()
        {
            // The goal cell itself is water, but nobody has seen it: the band sets out all the same
            // and finds out when it gets there (the walk corrects itself, see PathFollower).
            var grid = new GridMap(LakeMap());
            var pf = new Pathfinder(grid);
            var path = new List<Vector2Int>();
            Assert.IsTrue(pf.FindPath(new Vector2Int(2, 6), new Vector2Int(19, 6), path, true, null, 0f, (x, y) => x >= 12));
            Assert.AreEqual(new Vector2Int(19, 6), path[^1]);
        }

        [Test]
        public void ALegIsAtMostOneLegLong_AndTheGoalWhenItIsClose()
        {
            var world = new WorldService(LakeMap());
            var from = new Vector2(2.5f, 6.5f);
            var near = from + new Vector2(6f, 0f);
            Assert.That(ExplorationRoute.NextLeg(world, null, from, near, RouteChoice.Normal), Is.EqualTo(near));

            var far = from + new Vector2(120f, 0f);
            var leg = ExplorationRoute.NextLeg(world, null, from, far, RouteChoice.Normal);
            Assert.That(Vector2.Distance(from, leg), Is.LessThanOrEqualTo(ExplorationRoute.LegLength + 3f));
            Assert.That(Vector2.Distance(leg, far), Is.LessThan(Vector2.Distance(from, far)), "a leg makes progress");
        }

        [Test]
        public void ALegAimsOffTheStraightLine_WhenTheKnownWayIsBlocked()
        {
            // A known stretch of water fills the whole way ahead, with open ground above it: the leg
            // is aimed off the straight line rather than at a shore the band cannot walk along.
            var map = new MapData(40, 30);
            for (int i = 0; i < map.Tiles.Length; i++) map.Tiles[i] = TileType.Grass;
            for (int y = 0; y <= 10; y++)
            for (int x = 14; x <= 34; x++)
                map[x, y] = TileType.Water;
            var world = new WorldService(map);
            var from = new Vector2(10.5f, 6.5f);
            var goal = new Vector2(37.5f, 6.5f);

            var leg = ExplorationRoute.NextLeg(world, null, from, goal, RouteChoice.Normal);
            Assert.IsTrue(world.Grid.IsWalkable(GridMap.WorldToCell(leg)), "the leg ends on walkable ground");
            Assert.That(leg.y, Is.GreaterThan(from.y + 4f), "it turns for the open ground above the water");
            // Less of it is water than the straight line the band gave up (a little shore is fine —
            // the route round it is the pathfinder's job).
            Assert.That(Blocked(world, from, leg), Is.LessThan(Blocked(world, from, from + new Vector2(ExplorationRoute.LegLength, 0f))),
                "the chosen heading crosses less known water than straight ahead");
        }

        /// <summary>Sampled cells of a straight leg that are known to be unwalkable.</summary>
        static int Blocked(WorldService world, Vector2 from, Vector2 to)
        {
            int blocked = 0;
            for (int i = 1; i <= 9; i++)
                if (!world.Grid.IsWalkable(GridMap.WorldToCell(Vector2.Lerp(from, to, i / 9f)))) blocked++;
            return blocked;
        }

        [Test]
        public void WithoutFog_ExplorationKnowsEverything()
        {
            Assert.That(ExplorationRoute.KnowledgeOf(null), Is.Null, "no fog: plan with the whole map");
            Assert.That(ExplorationRoute.Unseen(null, new Vector2Int(3, 3)), Is.False);
        }
    }
}
