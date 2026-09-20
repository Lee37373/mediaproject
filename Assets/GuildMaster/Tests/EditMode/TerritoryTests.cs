using GuildMaster.Ecosystem;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    public class TerritoryTests
    {
        [Test]
        public void Radius_GrowsWithPopulation_ZeroWhenEmpty()
        {
            Assert.AreEqual(0f, Nest.TerritoryRadiusFor(5f, 0, 10));
            Assert.AreEqual(5f * 0.68f, Nest.TerritoryRadiusFor(5f, 1, 10), 1e-4f);
            Assert.AreEqual(5f * 1.4f, Nest.TerritoryRadiusFor(5f, 10, 10), 1e-4f);
            Assert.Less(Nest.TerritoryRadiusFor(5f, 3, 10), Nest.TerritoryRadiusFor(5f, 8, 10));
        }

        [Test]
        public void Outcome_RoutsOnlyLopsidedEncounters()
        {
            Assert.AreEqual(0, TerritoryService.Outcome(100f, 0f));                                   // nobody intrudes
            Assert.AreEqual(0, TerritoryService.Outcome(100f, 150f));                                 // even fight
            Assert.AreEqual(-1, TerritoryService.Outcome(100f, 100f * TerritoryService.RoutRatio));   // residents flee
            Assert.AreEqual(1, TerritoryService.Outcome(100f * TerritoryService.RoutRatio, 100f));    // intruders turn back
        }

        [Test]
        public void AFightAlreadyUnderway_IsNeverCalledOff()
        {
            // A monster lured into a nest is alone against the whole pack, so the plain rule always
            // sends it home; mid-fight it has to stay and take it (user report 2026-09-20).
            float residents = 100f * TerritoryService.RoutRatio;
            Assert.AreEqual(1, TerritoryService.Outcome(residents, 100f, alreadyFighting: false));
            Assert.AreEqual(0, TerritoryService.Outcome(residents, 100f, alreadyFighting: true));
            // Same for the residents' side of a lopsided encounter.
            Assert.AreEqual(-1, TerritoryService.Outcome(100f, residents, alreadyFighting: false));
            Assert.AreEqual(0, TerritoryService.Outcome(100f, residents, alreadyFighting: true));
        }

        [Test]
        public void Lure_WaypointMustBeAheadOfThePursuer_AndWorthTheDetour()
        {
            var self = new Vector2(0f, 0f);
            var home = new Vector2(20f, 0f);
            var waypoint = new Vector2(10f, 3f);
            float detour = TerritoryTactics.Detour(self, waypoint, home);
            Assert.AreEqual(2f * Mathf.Sqrt(109f) - 20f, detour, 1e-3f);
            Assert.IsTrue(TerritoryTactics.IsGoodLure(self, pursuer: new Vector2(-2f, 0f), waypoint, detour));
            // The pursuer would get there first.
            Assert.IsFalse(TerritoryTactics.IsGoodLure(self, pursuer: new Vector2(9f, 2f), waypoint, detour));
            // Too far out of the way.
            Assert.IsFalse(TerritoryTactics.IsGoodLure(self, new Vector2(-2f, 0f), waypoint, TerritoryTactics.MaxDetour + 1f));
        }
    }
}
