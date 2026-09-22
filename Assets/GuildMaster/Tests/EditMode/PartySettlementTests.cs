using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Splitting a party's pool at the guild (P4-50).</summary>
    public class PartySettlementTests
    {
        [Test]
        public void Distribute_AlwaysAddsUpToThePool()
        {
            var shares = PartySettlement.Distribute(100, new[] { 1f, 1f, 1f });
            Assert.That(shares.Sum(), Is.EqualTo(100));
            Assert.That(shares.Max() - shares.Min(), Is.LessThanOrEqualTo(1), "33/33/34");

            shares = PartySettlement.Distribute(97, new[] { 3f, 1f });
            Assert.That(shares.Sum(), Is.EqualTo(97));
            Assert.That(shares[0], Is.GreaterThan(shares[1]));

            Assert.That(PartySettlement.Distribute(50, new[] { 0f, 0f }), Is.EqualTo(new[] { 25, 25 }), "no weights: equal");
        }

        [Test]
        public void RuleSplit_HalfEqual_HalfByContribution()
        {
            // All the fighting done by the first: 75% to it, 25% to the other (half of the pool equal).
            var shares = PartySettlement.RuleSplit(200, new[] { 10f, 0f });
            Assert.That(shares, Is.EqualTo(new[] { 150, 50 }));
            // A healer with no kills is not left out.
            Assert.That(PartySettlement.RuleSplit(300, new[] { 100f, 0f, 0f })[2], Is.EqualTo(50));
        }

        [Test]
        public void Contribution_CountsDamageTakenLikeDamageDealt()
        {
            // The front-liner who soaked the blows is worth as much as the one who dealt them (user request).
            var tank = new OutingRecord { Damage = 20f, Taken = 180f };
            var striker = new OutingRecord { Damage = 200f };
            Assert.That(tank.Weight, Is.EqualTo(striker.Weight));
            var shares = PartySettlement.RuleSplit(300, new[] { tank.Weight, striker.Weight, 0f });
            Assert.That(shares, Is.EqualTo(new[] { 125, 125, 50 }));
        }

        [Test]
        public void UnfairShare_UnderSeventyPercent_MakesLeavingLikely_FeelingsShiftIt()
        {
            // 3 members, 300G: equal is 100, 70 is the line.
            Assert.That(PartySettlement.LeaveChanceOverShare(70, 300, 3, 0), Is.EqualTo(0f), "exactly 70% is fair enough");
            Assert.That(PartySettlement.LeaveChanceOverShare(69, 300, 3, 0), Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(PartySettlement.LeaveChanceOverShare(30, 300, 3, -40), Is.EqualTo(0.8f).Within(1e-4f), "disliking the leader");
            Assert.That(PartySettlement.LeaveChanceOverShare(30, 300, 3, 60), Is.EqualTo(0.3f).Within(1e-4f), "liking the leader");
            Assert.That(PartySettlement.LeaveChanceOverShare(0, 0, 3, 0), Is.EqualTo(0f), "nothing to split");
        }

        [Test]
        public void BadBloodWithAMate_Hostile_Always_Uneasy_Sometimes()
        {
            Assert.That(PartySettlement.LeaveChanceOverMates(-45), Is.EqualTo(1f));
            Assert.That(PartySettlement.LeaveChanceOverMates(-20), Is.EqualTo(PartySettlement.UneasyLeaveChance));
            Assert.That(PartySettlement.LeaveChanceOverMates(0), Is.EqualTo(0f));
        }

        [Test]
        public void LeaderAnswer_IsScaledToThePool_AndRejectedWhenEmpty()
        {
            var dto = new SplitPrompt.ResponseDto { m1 = 60, m2 = 30, m3 = 30, say = "고생했어." };
            Assert.That(SplitPrompt.TryInterpret(dto, 3, 240, out var shares, out var say), Is.True);
            Assert.That(shares, Is.EqualTo(new[] { 120, 60, 60 }), "the model's arithmetic is not trusted");
            Assert.That(say, Is.EqualTo("고생했어."));

            Assert.That(SplitPrompt.TryInterpret(new SplitPrompt.ResponseDto(), 2, 100, out _, out _), Is.False, "all zero → rule split");
            Assert.That(SplitPrompt.Grammar(3), Does.Contain("m3").And.Not.Contain("m4"));
        }
    }
}
