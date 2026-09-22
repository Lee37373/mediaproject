using System.Collections.Generic;
using GuildMaster.Adventurers;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>
    /// P4-33: loot is collected as one planned round instead of re-picking the nearest item over
    /// and over, which made adventurers set off home and turn back.
    /// </summary>
    public class LootRouteTests
    {
        [Test]
        public void Route_StartsWithTheClosest_ThenChainsFromEachPickup()
        {
            // A line heading away from the walker, listed out of order.
            var points = new List<Vector2> { new(9f, 0f), new(1f, 0f), new(5f, 0f), new(3f, 0f) };
            var order = AdventurerLooting.RouteOrder(Vector2.zero, points, 10);

            Assert.That(order, Is.EqualTo(new[] { 1, 3, 2, 0 }), "walks outward, never doubling back");
        }

        [Test]
        public void Route_ChainsFromTheLastPickup_NotFromTheWalker()
        {
            // (1,0) is closest to the start, but (2,0) is closest to it — a plain sort by distance
            // from the start would give the same answer here, so put one behind the walker.
            var points = new List<Vector2> { new(-1.5f, 0f), new(1f, 0f), new(2f, 0f) };
            var order = AdventurerLooting.RouteOrder(Vector2.zero, points, 10);

            Assert.That(order[0], Is.EqualTo(1), "the closest stop comes first");
            Assert.That(order[1], Is.EqualTo(2), "then the one closest to it, not the next-closest to the start");
            Assert.That(order[2], Is.EqualTo(0));
        }

        [Test]
        public void Route_IsCappedAndVisitsEachPointOnce()
        {
            var points = new List<Vector2>();
            for (int i = 0; i < 12; i++) points.Add(new Vector2(i + 1, 0f));

            var order = AdventurerLooting.RouteOrder(Vector2.zero, points, AdventurerLooting.MaxRouteItems);

            Assert.That(order.Count, Is.EqualTo(AdventurerLooting.MaxRouteItems), "nobody tidies up all day");
            Assert.That(order, Is.Unique);
        }

        [Test]
        public void Route_IsEmptyWithNothingToCollect()
        {
            Assert.That(AdventurerLooting.RouteOrder(Vector2.zero, new List<Vector2>(), 5), Is.Empty);
            Assert.That(AdventurerLooting.RouteOrder(Vector2.zero, null, 5), Is.Empty);
            Assert.That(AdventurerLooting.RouteOrder(Vector2.zero, new List<Vector2> { Vector2.one }, 0), Is.Empty);
        }
    }
}
