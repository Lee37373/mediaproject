using GuildMaster.Adventurers;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Feelings in a fight (P4-131, F-12 — user decisions 2026-11-08: friends and foes, rivals, mentors; small numbers; party
    /// making looks at the whole party): who is what to whom, what it does to the attack, the retreat and the EXP, how a race
    /// is decided, how a member fits a party, and the lines' particles.
    /// </summary>
    public class BondTests
    {
        [Test]
        public void Kinds_FriendsBothWays_AFoeEitherWay_RivalsAwkwardAndLevelled_AMentorWellAboveAndLiked()
        {
            Assert.That(BondRules.Kinds(40, 40, 5, 5), Is.EquivalentTo(new[] { BondKind.Friend }));
            Assert.That(BondRules.Kinds(60, 39, 5, 5), Is.Empty, "a friend both ways, or not one");
            Assert.That(BondRules.Kinds(10, -40, 5, 5), Is.EquivalentTo(new[] { BondKind.Foe }), "one hostile is enough for foes");
            Assert.That(BondRules.Kinds(-20, -15, 7, 4), Is.EquivalentTo(new[] { BondKind.Rival }));
            Assert.That(BondRules.Kinds(-20, -15, 8, 4), Is.Empty, "too far apart to race");
            Assert.That(BondRules.Kinds(-20, -45, 5, 5), Is.EquivalentTo(new[] { BondKind.Foe }), "hostile is past rivalry");
            // A newcomer who likes its senior, liked back, five levels down: its mentor; and the senior's pupil.
            Assert.That(BondRules.Kinds(20, 15, 2, 7), Is.EquivalentTo(new[] { BondKind.Mentor }));
            Assert.That(BondRules.Kinds(15, 20, 7, 2), Is.EquivalentTo(new[] { BondKind.Pupil }));
            Assert.That(BondRules.Kinds(20, 15, 3, 7), Is.Empty, "four levels is no mentor yet");
            Assert.That(BondRules.Kinds(50, 50, 2, 9), Is.EquivalentTo(new[] { BondKind.Friend, BondKind.Mentor }), "both at once");
        }

        [Test]
        public void Attack_AFriendBesideLifts_AFoeInTheWayDrags_SmallNumbers()
        {
            Assert.That(BondRules.AttackShare(true, false), Is.EqualTo(0.10f).Within(1e-6f));
            Assert.That(BondRules.AttackShare(false, true), Is.EqualTo(-0.10f).Within(1e-6f));
            Assert.That(BondRules.AttackShare(true, true), Is.EqualTo(0f).Within(1e-6f));
            Assert.That(BondRules.AttackShare(false, false), Is.EqualTo(0f));
            Assert.That(BondRules.MentorExp, Is.EqualTo(1.15f));
        }

        [Test]
        public void Behind_InARace_HoldsOnLonger_ButNeverPastTheFloor_NorLaterThanItWould()
        {
            Assert.That(BondRules.RetreatWhenBehind(0.35f), Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(BondRules.RetreatWhenBehind(0.2f), Is.EqualTo(0.15f).Within(1e-6f), "floor");
            Assert.That(BondRules.RetreatWhenBehind(0.1f), Is.EqualTo(0.1f).Within(1e-6f), "already braver than the floor: unchanged");
        }

        [Test]
        public void ARace_IsWonByMoreKills_ADrawOrOneKillDoesNotCount()
        {
            Assert.That(BondRules.ContestResult(1, 0), Is.EqualTo(0), "one kill is no race");
            Assert.That(BondRules.ContestResult(2, 1), Is.EqualTo(1));
            Assert.That(BondRules.ContestResult(1, 3), Is.EqualTo(-1));
            Assert.That(BondRules.ContestResult(2, 2), Is.EqualTo(0));
        }

        [Test]
        public void PartyFit_LeavesOutAFoeOfAnyone_AndPutsTheirFriendsFirst()
        {
            Assert.That(BondRules.PartyFit(new (int, int)[0]), Is.EqualTo(0f), "nobody yet");
            Assert.That(BondRules.PartyFit(new[] { (50, 30), (10, -40) }), Is.Null, "a foe of one of them");
            Assert.That(BondRules.PartyFit(new[] { (50, 30), (10, 10) }), Is.EqualTo(25f), "the average both ways");
            Assert.That(BondRules.PartyFit(new[] { (50, 30) }), Is.GreaterThan(BondRules.PartyFit(new[] { (0, 0) })));
        }

        [Test]
        public void Lines_FillTheName_WithItsParticle()
        {
            Assert.That(BondRules.FriendLine("그렌", 1), Does.StartWith("그렌이랑 "));
            Assert.That(BondRules.FriendLine("제나", 1), Does.StartWith("제나랑 "));
            Assert.That(BondRules.FoeLine("레오", 1), Does.Contain("레오랑"));
            var (first, answer) = BondRules.Quarrel("벨라", 0);
            Assert.That(first, Does.Contain("벨라"));
            Assert.That(answer, Is.Not.Empty);
            Assert.That(BondRules.AheadLine("미라", -7), Does.Contain("미라"), "any pick lands on a line");
            Assert.That(BondRules.Effect(BondKind.Friend), Does.Contain("+10%"));
            Assert.That(BondRules.Effect(BondKind.Mentor), Does.Contain("+15%"));
        }
    }
}
