using GuildMaster.Disasters;
using GuildMaster.Storytelling;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// The storyteller's rules (P4-111a, G5 — user decisions 2026-10-30): tension that fades, the pace of the big events and
    /// their breather, the guild near its end, the draw, the sizes and its own dice.
    /// </summary>
    public class StorytellerTests
    {
        [Test]
        public void Tension_HalvesInItsHalfLife_AndStaysWithinItsBounds()
        {
            Assert.That(StorytellerRules.Decay(40f, StorytellerRules.TensionHalfLifeHours), Is.EqualTo(20f).Within(0.01f));
            Assert.That(StorytellerRules.Decay(40f, 0f), Is.EqualTo(40f));
            Assert.That(StorytellerRules.Add(90f, StorytellerRules.MemberDied), Is.EqualTo(StorytellerRules.MaxTension));
            Assert.That(StorytellerRules.Add(0f, -5f), Is.EqualTo(0f));
            // A death weighs more than anything else the guild goes through.
            foreach (float other in new[] { StorytellerRules.NearDeathRetreat, StorytellerRules.BuildingDestroyed, StorytellerRules.GateBroken,
                         StorytellerRules.RaidSighted, StorytellerRules.DragonSighted, StorytellerRules.Plundered, StorytellerRules.DisasterSighted })
                Assert.That(StorytellerRules.MemberDied, Is.GreaterThan(other));
        }

        [Test]
        public void Gaps_KeepTheBreather_AndShortenWithTheDays_NoFurtherThanHalf()
        {
            Assert.That(StorytellerRules.Gap(1, 0f), Is.EqualTo(StorytellerRules.RelaxHours + StorytellerRules.BuildUpMin));
            Assert.That(StorytellerRules.Gap(1, 1f), Is.EqualTo(StorytellerRules.RelaxHours + StorytellerRules.BuildUpMax));
            Assert.That(StorytellerRules.Gap(11, 0.5f), Is.LessThan(StorytellerRules.Gap(1, 0.5f)));
            Assert.That(StorytellerRules.Escalation(200), Is.EqualTo(StorytellerRules.MinEscalation));
            Assert.That(StorytellerRules.Gap(200, 0f), Is.GreaterThanOrEqualTo(StorytellerRules.RelaxHours), "never without the breather");
            // The first comes on day 3 (P5-1: the first raid on days 3 to 6).
            Assert.That(StorytellerRules.FirstBeatHour(0f), Is.GreaterThanOrEqualTo(48f));
            Assert.That(StorytellerRules.FirstBeatHour(1f), Is.LessThan(96f));
        }

        [Test]
        public void NearTheEnd_FewLeft_OrManyLostLately()
        {
            Assert.That(StorytellerRules.NearTheEnd(4, 0), Is.False, "the starting guild");
            Assert.That(StorytellerRules.NearTheEnd(2, 0), Is.True, "two left");
            Assert.That(StorytellerRules.NearTheEnd(6, 2), Is.False, "two of eight");
            Assert.That(StorytellerRules.NearTheEnd(6, 3), Is.True, "three of nine");
            Assert.That(StorytellerRules.NearTheEnd(20, 3), Is.False, "three of a big guild");
            Assert.That(StorytellerRules.NearTheEnd(20, 8), Is.False, "eight of twenty-eight");
            Assert.That(StorytellerRules.NearTheEnd(20, 9), Is.True, "nine of twenty-nine");
        }

        [Test]
        public void TheWilds_AreQuieterInTheBreather_AndQuietestInRecovery()
        {
            Assert.That(StorytellerRules.EcoFactor(StoryPhase.Calm), Is.EqualTo(1f));
            Assert.That(StorytellerRules.EcoFactor(StoryPhase.Relax), Is.LessThan(1f));
            Assert.That(StorytellerRules.EcoFactor(StoryPhase.Recovery), Is.LessThan(StorytellerRules.EcoFactor(StoryPhase.Relax)));
            Assert.That(StorytellerRules.EcoFactor(StoryPhase.Relax), Is.LessThan(StorytellerRules.HoldOffBelow), "a homeless pack waits in the breather");
        }

        [Test]
        public void TheDraw_WaitsForEachKindsDay_AndFrownsOnTheSameTwice()
        {
            Assert.That(StorytellerRules.Weight(StoryBeat.Horde, 3, null), Is.EqualTo(0f), "hordes from day 4");
            Assert.That(StorytellerRules.Weight(StoryBeat.Horde, 4, null), Is.GreaterThan(0f));
            Assert.That(StorytellerRules.Weight(StoryBeat.Disaster, 2, null), Is.GreaterThan(0f));
            Assert.That(StorytellerRules.Weight(StoryBeat.TownRaid, 5, StoryBeat.TownRaid),
                Is.EqualTo(StorytellerRules.BaseWeight(StoryBeat.TownRaid) * StorytellerRules.RepeatFactor).Within(1e-5f));
            Assert.That(StorytellerRules.Weight(StoryBeat.TownRaid, 5, null, 2f), Is.EqualTo(2f * StorytellerRules.BaseWeight(StoryBeat.TownRaid)));
        }

        [Test]
        public void Sizes_GrowWithTheGuild_WithinTheirBounds()
        {
            Assert.That(StorytellerRules.RaidSize(0f), Is.EqualTo(StorytellerRules.RaidMin));
            Assert.That(StorytellerRules.RaidSize(10000f), Is.EqualTo(StorytellerRules.RaidMax));
            Assert.That(StorytellerRules.HordeSize(0f), Is.EqualTo(StorytellerRules.HordeMinSize));
            Assert.That(StorytellerRules.HordeSize(10000f), Is.EqualTo(StorytellerRules.HordeMaxSize));
            // Six members of level 5 with first-tier gear at the starting scale (power about 5 each).
            Assert.That(StorytellerRules.HordeSize(30f), Is.InRange(10, 12));
            Assert.That(StorytellerRules.RaidSize(30f), Is.InRange(5, 7));
        }

        [Test]
        public void ItsDice_AreItsOwn_TheSameForTheSameHour()
        {
            Assert.That(StorytellerRules.Stream(7, 50, 2).Value, Is.EqualTo(StorytellerRules.Stream(7, 50, 2).Value));
            Assert.That(StorytellerRules.Stream(7, 50, 2).Value, Is.Not.EqualTo(StorytellerRules.Stream(7, 51, 2).Value));
            Assert.That(StorytellerRules.Stream(7, 50, 2).Value, Is.Not.EqualTo(StorytellerRules.Stream(7, 50, 3).Value));
        }

        [Test]
        public void DisasterMonsters_SkillsName_TheirDisasters()
        {
            Assert.That(DisasterRules.KindOfSkill("ember"), Is.EqualTo(DisasterKind.Wildfire));
            Assert.That(DisasterRules.KindOfSkill("toxic_spit"), Is.EqualTo(DisasterKind.PoisonFog));
            Assert.That(DisasterRules.KindOfSkill("frost_roar"), Is.EqualTo(DisasterKind.Avalanche));
            Assert.That(DisasterRules.KindOfSkill("sand_whirl"), Is.EqualTo(DisasterKind.Sandstorm));
            Assert.That(DisasterRules.KindOfSkill("quake_stomp"), Is.EqualTo(DisasterKind.Quake));
            Assert.That(DisasterRules.KindOfSkill("lord_toxic_spit"), Is.Null, "a lord's strike is not a disaster");
            Assert.That(DisasterRules.KindOfSkill("fireball"), Is.Null);
        }
    }
}
