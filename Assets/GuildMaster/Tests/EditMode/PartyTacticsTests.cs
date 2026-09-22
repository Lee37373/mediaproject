using GuildMaster.Adventurers;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>P4-27: party roles, formation slots and back-line distance.</summary>
    public class PartyTacticsTests
    {
        [Test]
        public void Roles_WarriorsFront_OthersBack()
        {
            Assert.That(PartyTactics.RoleOf(AdventurerClass.Warrior), Is.EqualTo(PartyRole.Front));
            Assert.That(PartyTactics.RoleOf(AdventurerClass.Ranger), Is.EqualTo(PartyRole.Back));
            Assert.That(PartyTactics.RoleOf(AdventurerClass.Mage), Is.EqualTo(PartyRole.Back));
        }

        [Test]
        public void Slots_FrontAhead_BackBehind_SpreadSideways()
        {
            var heading = Vector2.right;
            var front = PartyTactics.SlotOffset(heading, PartyRole.Front, 0, 1);
            var back = PartyTactics.SlotOffset(heading, PartyRole.Back, 0, 1);
            Assert.That(front.x, Is.EqualTo(PartyTactics.SlotDepth).Within(1e-4f), "front-liner ahead of the leader");
            Assert.That(back.x, Is.EqualTo(-PartyTactics.SlotDepth).Within(1e-4f), "back-liner behind");
            Assert.That(front.y, Is.EqualTo(0f).Within(1e-4f), "a single follower stays in line");

            var left = PartyTactics.SlotOffset(heading, PartyRole.Back, 0, 2);
            var right = PartyTactics.SlotOffset(heading, PartyRole.Back, 1, 2);
            Assert.That(Mathf.Abs(left.y - right.y), Is.EqualTo(PartyTactics.SlotSpacing).Within(1e-4f), "two back-liners side by side");
            Assert.That(left.y + right.y, Is.EqualTo(0f).Within(1e-4f), "centred on the leader's line");
        }

        [Test]
        public void BackLineDistance_StaysBehindTheFront_WithinRange()
        {
            Assert.That(PartyTactics.BackLineDistance(1.5f, 5f), Is.EqualTo(3f).Within(1e-4f), "1.5 behind a front-liner in melee");
            Assert.That(PartyTactics.BackLineDistance(3.5f, 5f), Is.EqualTo(4.5f).Within(1e-4f), "capped at 90% of the range");
            Assert.That(PartyTactics.BackLineDistance(4.5f, 5f), Is.Null, "front-liner too far forward to stand behind in range");
        }

        [Test]
        public void FollowerPace_KeepsStepWithTheLeaderInsteadOfRushingAhead()
        {
            // A quick ranger (3.4) behind a leader walking at 3.0, on its slot: no faster than the leader.
            Assert.That(3.4f * PartyTactics.FollowerPace(0f, 0f, 3.0f, 3.4f), Is.EqualTo(3.0f).Within(1e-3f));
            // A little behind the slot: a little faster, to close the gap smoothly.
            float behind = 3.4f * PartyTactics.FollowerPace(1f, 1f, 3.0f, 3.4f);
            Assert.That(behind, Is.GreaterThan(3.0f).And.LessThanOrEqualTo(3.4f * 1.3f));
            // Ahead of it: slower than the leader, never faster.
            Assert.That(3.4f * PartyTactics.FollowerPace(1f, -1f, 3.0f, 3.4f), Is.LessThan(3.0f));
            // Straggling: sprint. Leader standing: normal walk.
            Assert.That(PartyTactics.FollowerPace(8f, 8f, 3.0f, 3.4f), Is.EqualTo(1.3f));
            Assert.That(PartyTactics.FollowerPace(2f, 2f, 0f, 3.4f), Is.EqualTo(1f));
        }
    }
}
