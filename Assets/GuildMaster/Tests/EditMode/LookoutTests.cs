using GuildMaster.Ecosystem;
using GuildMaster.Town;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>The watchtower lookout and bell (2026-11-02) and the ×1.5 wilds (2026-11-02).</summary>
    public class LookoutTests
    {
        [Test]
        public void FlareAim_StaysWithinRange()
        {
            var tower = new Vector2(10f, 10f);
            Assert.AreEqual(new Vector2(20f, 10f), LookoutRules.FlareAim(tower, new Vector2(20f, 10f)));
            var far = LookoutRules.FlareAim(tower, new Vector2(110f, 10f));
            Assert.AreEqual(LookoutRules.FlareRange, Vector2.Distance(tower, far), 1e-3f);
            Assert.AreEqual(10f, far.y, 1e-3f);
        }

        [Test]
        public void Bell_CallsOnlyMembersAtTheirOwnBusinessNearby()
        {
            Assert.IsTrue(LookoutRules.Recalls(true, false, true, false, 30f));
            Assert.IsFalse(LookoutRules.Recalls(true, true, true, false, 30f), "a member on a quest keeps to it");
            Assert.IsFalse(LookoutRules.Recalls(true, false, false, false, 30f), "a member resting or fighting is left be");
            Assert.IsFalse(LookoutRules.Recalls(true, false, true, true, 5f), "already in town");
            Assert.IsFalse(LookoutRules.Recalls(true, false, true, false, LookoutRules.BellRadius + 1f), "out of earshot");
            Assert.IsFalse(LookoutRules.Recalls(false, false, true, false, 30f), "a visitor");
        }

        [Test]
        public void Lookout_SeesFurtherThanTheTowerAlone()
        {
            Assert.Greater(LookoutRules.SightRadius, 14f);
            Assert.Less(LookoutRules.BellHoldHours, LookoutRules.BellCooldownHours);
        }

        [Test]
        public void Left_RoundsUp()
        {
            Assert.AreEqual("3시간", LookoutRules.Left(2.1f, "시간"));
            Assert.AreEqual("0초", LookoutRules.Left(-1f, "초"));
        }

        [Test]
        public void Abundance_GrowsDensAndLairsKeepTheirSize()
        {
            Assert.AreEqual(9, MonsterAbundance.Scaled(6));
            Assert.AreEqual(1, MonsterAbundance.Scaled(0));
            Assert.AreEqual(0.075f, MonsterAbundance.ScaledGrowth(0.05f), 1e-5f);
            for (int cap = 1; cap <= 30; cap++)
                Assert.AreEqual(cap, MonsterAbundance.Unscaled(MonsterAbundance.Scaled(cap)), $"lair of size {cap}");
        }
    }
}
