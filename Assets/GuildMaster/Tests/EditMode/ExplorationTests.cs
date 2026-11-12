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

        /// <summary>
        /// Open grass with a ring of mountain whose rim has been seen and whose middle (grass, as it
        /// happens) never has: the inside of an outcrop the band has only walked round (P4-80).
        /// </summary>
        static MapData OutcropMap()
        {
            var map = new MapData(60, 40);
            for (int i = 0; i < map.Tiles.Length; i++) map.Tiles[i] = TileType.Grass;
            for (int y = 14; y <= 26; y++)
            for (int x = 26; x <= 36; x++)
                if (x == 26 || x == 36 || y == 14 || y == 26) map[x, y] = TileType.Mountain;
            return map;
        }

        static bool InOutcrop(int x, int y) => x > 26 && x < 36 && y > 14 && y < 26;

        [Test]
        public void FogWalledInBySeenRock_IsNoPlaceForALeg()
        {
            var world = new WorldService(OutcropMap());
            System.Func<int, int, bool> unseen = InOutcrop;
            var from = new Vector2(12.5f, 20.5f);
            var goal = new Vector2(55.5f, 20.5f);
            var straightEnd = GridMap.WorldToCell(from + Vector2.right * ExplorationRoute.LegLength);
            Assume.That(InOutcrop(straightEnd.x, straightEnd.y), "the straight leg would end inside the outcrop");

            var leg = ExplorationRoute.NextLeg(world, unseen, from, goal, RouteChoice.Normal);
            var cell = GridMap.WorldToCell(leg);
            Assert.IsFalse(InOutcrop(cell.x, cell.y), "the leg does not end in fog the seen rock walls in");
            var path = new List<Vector2Int>();
            Assert.IsTrue(world.Pathfinder.FindPath(GridMap.WorldToCell(from), cell, path, true, null, RouteChoice.Normal, unseen),
                "and the band can walk it");
        }

        [Test]
        public void OpensOut_IsFogThatTouchesSeenGround_OrTooBigToTell()
        {
            var grid = new GridMap(OutcropMap());
            Assert.IsFalse(ExplorationRoute.OpensOut(grid, InOutcrop, new Vector2Int(30, 20)), "walled in by seen rock");
            // A gap in what was seen of the rim: the fog runs out onto the grass beyond.
            bool Gap(int x, int y) => InOutcrop(x, y) || x == 36 && y == 20;
            Assert.IsTrue(ExplorationRoute.OpensOut(grid, Gap, new Vector2Int(30, 20)), "the unseen rim cell may be a way in");
            Assert.IsTrue(ExplorationRoute.OpensOut(grid, (x, y) => true, new Vector2Int(5, 5)), "a whole map of fog is too big to tell");
            Assert.IsTrue(ExplorationRoute.OpensOut(grid, InOutcrop, new Vector2Int(5, 5)), "seen grass is open");
            Assert.IsFalse(ExplorationRoute.OpensOut(grid, InOutcrop, new Vector2Int(26, 20)), "seen rock is not");
        }

        // ---------------- Scouting from where the band stands (P4-121) ----------------

        /// <summary>Open grass, 100 × 60, with a walled town on its west side.</summary>
        static WorldService OpenCountry()
        {
            var map = new MapData(100, 60);
            for (int i = 0; i < map.Tiles.Length; i++) map.Tiles[i] = TileType.Grass;
            map.TownRect = new RectInt(4, 22, 16, 16);
            map.TownCenter = new Vector2Int(12, 30);
            return new WorldService(map);
        }

        static ExplorationRoute.LegView View(WorldService world, float reach = 200f)
            => new() { Town = GridMap.CellToWorld(world.Map.TownCenter), Reach = reach };

        [Test]
        public void AScoutingLeg_HeadsForCountryNobodyHasSeen()
        {
            var world = OpenCountry();
            var from = new Vector2(50.5f, 30.5f);
            bool Unseen(int x, int y) => y >= 42; // the north is still dark
            var leg = ExplorationRoute.ScoutLeg(world, Unseen, from, RouteChoice.Normal, View(world));
            Assert.That(Vector2.Distance(from, leg), Is.EqualTo(ExplorationRoute.LegLength).Within(1.5f), "one leg from where it stands");
            Assert.That(leg.y, Is.GreaterThan(from.y + 10f), "towards the dark");
        }

        [Test]
        public void AScoutingLeg_GoesOnTheWayTheLastOneWent()
        {
            // Everything round is as well known as everything else: no turning back and forth for nothing
            // (P4-121 — a spot drawn round the town each time took the band from one side to the other).
            var world = OpenCountry();
            var from = new Vector2(50.5f, 30.5f);
            var view = View(world);
            view.Heading = Vector2.down;
            var leg = ExplorationRoute.ScoutLeg(world, null, from, RouteChoice.Normal, view);
            Assert.That(leg.y, Is.LessThan(from.y - 10f), "on the way it was going");
        }

        [Test]
        public void AScoutingLeg_KeepsClearOfWhatTheBandCannotTake()
        {
            var world = OpenCountry();
            var from = new Vector2(50.5f, 30.5f);
            bool Unseen(int x, int y) => x >= 60; // the dark lies east, and so does the monster
            var view = View(world);
            view.Heading = Vector2.right;
            var monster = from + new Vector2(10f, 0f);
            view.Avoid.Add((monster, 9f));
            var leg = ExplorationRoute.ScoutLeg(world, Unseen, from, RouteChoice.Normal, view);
            Assert.That(Vector2.Distance(leg, monster), Is.GreaterThanOrEqualTo(9f), "not into its notice");
            Assert.That(ExplorationRoute.ClosestPass(from, leg, monster), Is.GreaterThan(5f), "nor past under its nose");

            // Walking the goal's way too: the leg towards a den goes round the monster in between.
            var den = from + new Vector2(40f, 0f);
            var toDen = ExplorationRoute.NextLeg(world, Unseen, from, den, RouteChoice.Normal, false, view);
            Assert.That(ExplorationRoute.ClosestPass(from, toDen, monster), Is.GreaterThan(
                ExplorationRoute.ClosestPass(from, from + Vector2.right * ExplorationRoute.LegLength, monster)), "round it, not through it");
            Assert.That(Vector2.Distance(toDen, den), Is.LessThan(Vector2.Distance(from, den)), "still on its way");
        }

        [Test]
        public void AWornBand_LeansBackTowardsTown_AndAFreshOneStaysWithinItsReach()
        {
            var world = OpenCountry();
            var from = new Vector2(60.5f, 30.5f);
            var town = GridMap.CellToWorld(world.Map.TownCenter);
            var fresh = View(world);
            fresh.Heading = Vector2.right;
            var outward = ExplorationRoute.ScoutLeg(world, null, from, RouteChoice.Normal, fresh);
            Assert.That(Vector2.Distance(outward, town), Is.GreaterThan(Vector2.Distance(from, town)), "fresh, it goes on out");

            var worn = View(world);
            worn.Heading = Vector2.right;
            worn.Strain = 1f;
            var homeward = ExplorationRoute.ScoutLeg(world, null, from, RouteChoice.Normal, worn);
            Assert.That(Vector2.Distance(homeward, town), Is.LessThan(Vector2.Distance(from, town) - 8f), "worn, it heads back");

            var short_ = View(world, reach: 50f);
            short_.Heading = Vector2.right;
            var held = ExplorationRoute.ScoutLeg(world, null, from, RouteChoice.Normal, short_);
            Assert.That(Vector2.Distance(held, town), Is.LessThan(Vector2.Distance(outward, town)), "no further out than it can take on");
        }

        [Test]
        public void AScoutingLeg_GoesRoundTheTown_NotAcrossIt()
        {
            // Just north of the walls with the dark on the far side: a leg across the town is the shortest look at it.
            var world = OpenCountry();
            var from = new Vector2(12.5f, 41.5f); // north of the town
            bool Unseen(int x, int y) => y < 22; // the south is dark
            var leg = ExplorationRoute.ScoutLeg(world, Unseen, from, RouteChoice.Normal, View(world));
            for (int i = 1; i <= 9; i++)
                Assert.IsFalse(world.Grid.IsInTown(GridMap.WorldToCell(Vector2.Lerp(from, leg, i / 9f))), "the leg does not cross the town");
        }

        [Test]
        public void WithoutFog_ExplorationKnowsEverything()
        {
            Assert.That(ExplorationRoute.KnowledgeOf(null), Is.Null, "no fog: plan with the whole map");
            Assert.That(ExplorationRoute.Unseen(null, new Vector2Int(3, 3)), Is.False);
        }
    }
}
