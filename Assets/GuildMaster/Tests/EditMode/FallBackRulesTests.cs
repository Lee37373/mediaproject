using GuildMaster.Adventurers;
using GuildMaster.Units;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>A badly hurt member of a fighting party (P4-96): when it rejoins, when the party pulls out, where it stands.</summary>
    public class FallBackRulesTests
    {
        [Test]
        public void Rejoin_AMarginOverItsRetreatLine_NoHigherThanAPriestHeals()
        {
            Assert.That(FallBackRules.RejoinAt(0.35f), Is.EqualTo(HealSkill.HealsBelow).Within(1e-5f), "the usual member: healed back up, it goes in");
            Assert.That(FallBackRules.RejoinAt(0.25f), Is.EqualTo(0.5f).Within(1e-5f), "the brave goes back sooner");
            Assert.That(FallBackRules.RejoinAt(0.5f), Is.EqualTo(HealSkill.HealsBelow).Within(1e-5f), "a coward is not left waiting for a heal that never comes");
            foreach (float line in new[] { 0.1f, 0.25f, 0.35f, 0.5f, 0.7f })
                Assert.That(FallBackRules.RejoinAt(line), Is.GreaterThanOrEqualTo(line + 0.1f - 1e-5f), $"a margin over {line}");
        }

        [Test]
        public void ThePartyPullsOut_OnlyWhenTheFightIsLost()
        {
            Assert.That(FallBackRules.PartyPullsOut(3, 1, canHeal: true), Is.False, "one of three falls back, the rest fight on");
            Assert.That(FallBackRules.PartyPullsOut(8, 3, true), Is.False);
            Assert.That(FallBackRules.PartyPullsOut(8, 4, true), Is.True, "half of them out of it");
            Assert.That(FallBackRules.PartyPullsOut(2, 1, true), Is.True, "a pair is half gone");
            Assert.That(FallBackRules.PartyPullsOut(8, 1, canHeal: false), Is.True, "nobody to heal it");
        }

        [Test]
        public void TheSpot_IsBehindTheHealer_AwayFromTheFoe_AndClearOfIt()
        {
            var spot = FallBackRules.Spot(new Vector2(3f, 0f), healer: Vector2.zero, partyCentre: new Vector2(2f, 0f), threat: new Vector2(5f, 0f));
            Assert.That(Vector2.Distance(spot, new Vector2(-FallBackRules.BehindHealer, 0f)), Is.LessThan(1e-4f), "behind the healer, the healer between it and the foe");

            var close = FallBackRules.Spot(Vector2.zero, healer: Vector2.zero, partyCentre: Vector2.zero, threat: new Vector2(1f, 0f));
            Assert.That(Vector2.Distance(close, new Vector2(1f, 0f)), Is.EqualTo(FallBackRules.KeepClear).Within(1e-4f), "never within reach of the foe");
            Assert.That(close.x, Is.LessThan(0f), "on the far side");

            var noHealer = FallBackRules.Spot(Vector2.zero, healer: null, partyCentre: new Vector2(0f, 10f), threat: new Vector2(0f, 20f));
            Assert.That(noHealer.y, Is.LessThan(10f), "behind the party when it is the healer itself");

            Assert.That(FallBackRules.Spot(Vector2.zero, new Vector2(4f, 4f), Vector2.zero, threat: null), Is.EqualTo(new Vector2(4f, 4f)), "nothing to keep from: by the healer");
            Assert.That(FallBackRules.BehindHealer, Is.LessThan(6f), "inside the heal's reach");
        }
    }
}
