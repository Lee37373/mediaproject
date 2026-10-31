using System.Linq;
using GuildMaster.Town;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>P4-115: the walls' pieces and what stands in the training yard.</summary>
    public class TownLookTests
    {
        static MapData Town(int half)
        {
            var map = new MapData(80, 80);
            for (int y = 0; y < 80; y++)
            for (int x = 0; x < 80; x++)
                map[x, y] = TileType.Grass;
            TownWalls.Lay(map, new Vector2Int(40, 40), half);
            TownWalls.LayPlazaRoads(map);
            return map;
        }

        static void AssertRing(MapData map)
        {
            var r = map.TownRect;
            var c = map.TownCenter;
            int corners = 0, gateTowers = 0;
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                if (map[x, y] != TileType.Wall) continue;
                var piece = TownWallView.PieceAt(map, x, y);
                bool corner = (x == r.xMin || x == r.xMax - 1) && (y == r.yMin || y == r.yMax - 1);
                bool besideGate = Mathf.Abs(x - c.x) == 2 || Mathf.Abs(y - c.y) == 2;
                if (corner) { Assert.That(piece, Is.EqualTo(WallPiece.Corner), $"({x},{y}) a corner"); corners++; continue; }
                if (besideGate) { Assert.That(piece, Is.EqualTo(WallPiece.GateTower), $"({x},{y}) beside a gate"); gateTowers++; continue; }
                var expected = y == r.yMax - 1 ? WallPiece.AlongNorth : y == r.yMin ? WallPiece.AlongSouth
                             : x == r.xMin ? WallPiece.AlongWest : WallPiece.AlongEast;
                Assert.That(piece, Is.EqualTo(expected), $"({x},{y}) a stretch with its battlements outside");
            }
            Assert.That(corners, Is.EqualTo(4));
            Assert.That(gateTowers, Is.EqualTo(8), "a tower each side of each gate");
        }

        [Test]
        public void TheWalls_HaveATowerAtEachCorner_AndEachSideOfEachGate_AndStretchesBetween()
        {
            var map = Town(11);
            AssertRing(map);
            TownWalls.Grow(map, 15);
            AssertRing(map);
        }

        [Test]
        public void TheTrainingYard_GainsDummiesAsItGoesUp_AndEveryoneHasRoomToStrike()
        {
            var yard = new RectInt(0, 0, 7, 6);   // the starting town's yard
            int[] drills = new int[3];
            for (int level = 1; level <= 3; level++)
            {
                var stations = TrainingYardLayout.Of(level);
                drills[level - 1] = stations.Count(s => s.Drills);
                foreach (var s in stations)
                {
                    var at = TrainingYardLayout.WorldOf(yard, s);
                    Assert.That(at.x > yard.xMin + 0.5f && at.x < yard.xMax - 0.5f && at.y > yard.yMin + 0.5f && at.y < yard.yMax - 0.5f,
                                Is.True, $"level {level} {s.Kind} inside the fence");
                }
                // Places beside the dummies and targets, clear of the fence.
                int places = stations.Where(s => s.Drills).Sum(s =>
                {
                    var at = TrainingYardLayout.WorldOf(yard, s);
                    return new[] { -1, 1 }.Count(side => at.x + side * TrainingYardLayout.Beside is var x && x > yard.xMin + 0.55f && x < yard.xMax - 0.55f);
                });
                Assert.That(places, Is.GreaterThanOrEqualTo(drills[level - 1] + 1), $"level {level}: room to strike");
            }
            Assert.That(drills, Is.EqualTo(new[] { 1, 3, 5 }), "one dummy, then two and a target, then three and two targets");
        }
    }
}
