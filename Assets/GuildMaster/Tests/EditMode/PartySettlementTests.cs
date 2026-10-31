using System.Linq;
using GuildMaster.Adventurers;
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
        public void TheFallensShares_GoToTheLiving()
        {
            // P4-69: a member who died while the split was being worked out used to be paid anyway.
            var shares = new[] { 50, 31, 21 };
            PartySettlement.GiveTheFallensShares(new[] { true, false, true }, shares);
            Assert.That(shares, Is.EqualTo(new[] { 66, 0, 36 }), "31 shared out, the odd one to the first");
            Assert.That(shares.Sum(), Is.EqualTo(102), "nothing lost");

            var all = new[] { 10, 20 };
            PartySettlement.GiveTheFallensShares(new[] { true, true }, all);
            Assert.That(all, Is.EqualTo(new[] { 10, 20 }), "nobody died: unchanged");

            var none = new[] { 10, 20 };
            PartySettlement.GiveTheFallensShares(new[] { false, false }, none);
            Assert.That(none, Is.EqualTo(new[] { 0, 0 }), "nobody left to pay");
        }
    }
}
