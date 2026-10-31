using System.Collections.Generic;
using System.Linq;
using GuildMaster.Town;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>P4-115: the guild hall people walk into.</summary>
    public class GuildHallLayoutTests
    {
        [Test]
        public void EveryPlace_IsInsideOnOpenFloor_AndReachedFromTheEntrance()
        {
            Assert.That(GuildHallLayout.Blocks(GuildHallLayout.Entrance), Is.False, "the way in is open");
            Assert.That(GuildHallLayout.Blocks(GuildHallLayout.Lobby), Is.False);
            // Walk the open floor from the entrance.
            var seen = new HashSet<Vector2Int> { GuildHallLayout.Entrance };
            var open = new Queue<Vector2Int>(seen);
            while (open.Count > 0)
            {
                var c = open.Dequeue();
                foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                {
                    var n = c + d;
                    if (n.x < 0 || n.y < 0 || n.x >= GuildHallLayout.Width || n.y >= GuildHallLayout.Height) continue;
                    if (GuildHallLayout.Blocks(n) || !seen.Add(n)) continue;
                    open.Enqueue(n);
                }
            }
            Assert.That(seen.Contains(GuildHallLayout.Lobby), Is.True);
            foreach (var s in GuildHallLayout.Spots)
            {
                Assert.That(GuildHallLayout.IsInside(s.Cell) && !GuildHallLayout.Blocks(s.Cell), Is.True, $"{s.Use} {s.Cell} on the floor");
                Assert.That(seen.Contains(s.Cell), Is.True, $"{s.Use} {s.Cell} reached from the door");
            }
            Assert.That(GuildHallLayout.Spots.Select(s => s.Cell).Distinct().Count(), Is.EqualTo(GuildHallLayout.Spots.Count), "one place a cell");
            // Nothing of the outside escapes: the walls close the floor in.
            Assert.That(seen.All(c => GuildHallLayout.IsInside(c) || c == GuildHallLayout.Entrance), Is.True);
        }

        [Test]
        public void ThereArePlacesForEverythingMembersComeFor()
        {
            int Count(HallUse use) => GuildHallLayout.Spots.Count(s => s.Serves(use));
            Assert.That(Count(HallUse.Table), Is.EqualTo(4), "four round the big table");
            Assert.That(Count(HallUse.Seat), Is.GreaterThanOrEqualTo(8), "the table's chairs count as seats too");
            Assert.That(Count(HallUse.Board), Is.EqualTo(2));
            Assert.That(Count(HallUse.Counter), Is.EqualTo(2));
            Assert.That(Count(HallUse.Loiter), Is.EqualTo(GuildHallLayout.Spots.Count), "any place will do to hang about");
            var footprint = GuildHallLayout.FootprintAt(new Vector2Int(256, 256));
            Assert.That(footprint, Is.EqualTo(new RectInt(246, 258, 9, 8)), "the starting town's north-west quarter");
        }
    }
}
