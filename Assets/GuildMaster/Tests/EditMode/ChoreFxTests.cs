using GuildMaster.Audio;
using GuildMaster.Economy;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>The odd jobs' sounds and the residents' reactions (2026-11-11, minigame polish).</summary>
    public class ChoreFxTests
    {
        [Test]
        public void TheAnvil_RingsByHowWellTheHammerLanded()
        {
            Assert.That(SoundRules.ChoreHitKey(ChoreKind.Forge, 1f), Is.EqualTo(SoundKeys.ForgePerfect));
            Assert.That(SoundRules.ChoreHitKey(ChoreKind.Forge, 0.6f), Is.EqualTo(SoundKeys.ForgeGood));
            Assert.That(SoundRules.ChoreHitKey(ChoreKind.Forge, 0.2f), Is.EqualTo(SoundKeys.ForgeWeak));
            Assert.That(SoundRules.ChoreHitKey(ChoreKind.Forge, 0f), Is.EqualTo(SoundKeys.ForgeMiss));
        }

        [Test]
        public void TheDough_SlapsOrSquelches()
        {
            Assert.That(SoundRules.ChoreHitKey(ChoreKind.Dough, 1f), Is.EqualTo(SoundKeys.DoughPerfect));
            Assert.That(SoundRules.ChoreHitKey(ChoreKind.Dough, 0.3f), Is.EqualTo(SoundKeys.DoughGood));
            Assert.That(SoundRules.ChoreHitKey(ChoreKind.Dough, -1f), Is.EqualTo(SoundKeys.DoughMiss));
        }

        [Test]
        public void TheResultJingle_FollowsTheGrade()
        {
            Assert.That(SoundRules.ChoreResultKey(0.95f), Is.EqualTo(SoundKeys.ChoreGreat));
            Assert.That(SoundRules.ChoreResultKey(0.6f), Is.EqualTo(SoundKeys.ChoreGreat));
            Assert.That(SoundRules.ChoreResultKey(0.45f), Is.EqualTo(SoundKeys.ChoreOk));
            Assert.That(SoundRules.ChoreResultKey(0.1f), Is.EqualTo(SoundKeys.ChoreBad));
        }

        [Test]
        public void EveryResident_HasSomethingToSay()
        {
            foreach (var k in ChoreRules.All)
            {
                Assert.That(ChoreRules.Greeting(k), Is.Not.Empty);
                foreach (var q in new[] { 1f, 0.6f, 0.2f, 0f, -1f })
                    for (int pick = -3; pick < 6; pick++)
                        Assert.That(ChoreRules.Cheer(k, q, pick), Is.Not.Empty, $"{k} {q} {pick}");
            }
            Assert.That(ChoreRules.Cheer(ChoreKind.Forge, 1f, 0), Is.Not.EqualTo(ChoreRules.Cheer(ChoreKind.Forge, 0f, 0)));
        }
    }
}
