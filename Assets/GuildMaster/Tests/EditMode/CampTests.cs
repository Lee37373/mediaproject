using GuildMaster.Adventurers;
using GuildMaster.Units;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Rules of camping out in the wild (P4-66, user decisions 2026-09-23).</summary>
    public class CampTests
    {
        [Test]
        public void OnlyFarFromTown_AndAloneOnlyFurtherStill()
        {
            Assert.That(CampRules.FarOut(20f, alone: false), Is.False, "near town they just walk home");
            Assert.That(CampRules.FarOut(35f, alone: false), Is.True);
            Assert.That(CampRules.FarOut(35f, alone: true), Is.False, "alone, a fire nobody watches is worse than the walk");
            Assert.That(CampRules.FarOut(55f, alone: true), Is.True);
        }

        [Test]
        public void HurtOrWornOut_NeedsRest_AloneOnlyWhenWorse()
        {
            Assert.That(CampRules.NeedsRest(0.9f, 10f, alone: false), Is.False);
            Assert.That(CampRules.NeedsRest(0.55f, 10f, alone: false), Is.True, "hurt");
            Assert.That(CampRules.NeedsRest(0.9f, 75f, alone: false), Is.True, "worn out");
            Assert.That(CampRules.NeedsRest(0.55f, 75f, alone: true), Is.False, "alone it pushes on");
            Assert.That(CampRules.NeedsRest(0.45f, 10f, alone: true), Is.True);
        }

        [Test]
        public void NightCamp_FollowsTheLeadersNerve()
        {
            float brave = CampRules.NightCampChance(RouteChoice.Bold, alone: false);
            float normal = CampRules.NightCampChance(RouteChoice.Normal, alone: false);
            float coward = CampRules.NightCampChance(RouteChoice.Careful, alone: false);
            Assert.That(brave, Is.LessThan(normal));
            Assert.That(normal, Is.LessThan(coward));
            Assert.That(brave, Is.GreaterThanOrEqualTo(0.3f));
            Assert.That(coward, Is.LessThanOrEqualTo(0.95f));
            Assert.That(CampRules.NightCampChance(RouteChoice.Normal, alone: true), Is.EqualTo(normal * 0.5f).Within(1e-5f));
        }

        [Test]
        public void TheLight_ShowsFurtherAtNight_ButNotPastTheChaseLimit()
        {
            Assert.That(CampRules.LightRadius(true), Is.GreaterThan(CampRules.LightRadius(false)));
            // A drawn monster that could not keep the camper as a target would never arrive.
            Assert.That(CampRules.LightRadius(true), Is.LessThan(14f));
        }

        [Test]
        public void TheCampEnds_WhenEveryoneIsMended_AndANightCampAtDawn()
        {
            Assert.That(CampRules.Done(CampReason.Recover, 0.6f, 10f, night: false, 2f), Is.False, "still hurt");
            Assert.That(CampRules.Done(CampReason.Recover, 0.9f, 40f, night: false, 2f), Is.False, "still tired");
            Assert.That(CampRules.Done(CampReason.Recover, 0.9f, 10f, night: true, 2f), Is.True, "mended: no need to wait for the day");
            Assert.That(CampRules.Done(CampReason.Night, 0.9f, 10f, night: true, 2f), Is.False, "a night camp waits for the morning");
            Assert.That(CampRules.Done(CampReason.Night, 0.9f, 10f, night: false, 9f), Is.True);
            Assert.That(CampRules.Done(CampReason.Recover, 0.2f, 90f, night: true, CampRules.MaxHours), Is.True, "not forever");
        }

        [Test]
        public void TheWatch_GoesRound()
        {
            Assert.That(CampRules.WatchIndex(0f, 3), Is.EqualTo(0));
            Assert.That(CampRules.WatchIndex(CampRules.WatchHours + 0.1f, 3), Is.EqualTo(1));
            Assert.That(CampRules.WatchIndex(CampRules.WatchHours * 3 + 0.1f, 3), Is.EqualTo(0));
            Assert.That(CampRules.WatchIndex(1f, 0), Is.EqualTo(-1));
        }

        [Test]
        public void SleepMendsFasterThanTheWatch_AndSlowerThanTown()
        {
            Assert.That(CampRules.SleepHealPerHour, Is.GreaterThan(CampRules.WatchHealPerHour));
            Assert.That(CampRules.SleepHealPerHour, Is.LessThan(0.25f), "the inn is still the best bed");
            Assert.That(CampRules.SleepFatiguePerHour, Is.GreaterThan(CampRules.WatchFatiguePerHour));
        }
    }
}
