using GuildMaster.Adventurers;
using GuildMaster.Audio;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Sound (P4-9, 2026-10-31): which sound an event makes and how often it may repeat.</summary>
    public class SoundRulesTests
    {
        [Test]
        public void Blows_SoundLikeTheAttacker()
        {
            Assert.That(SoundRules.HitKey(true, null), Is.EqualTo(SoundKeys.HitBlunt), "monsters thump");
            Assert.That(SoundRules.HitKey(false, AdventurerClass.Warrior), Is.EqualTo(SoundKeys.HitSword));
            Assert.That(SoundRules.HitKey(false, AdventurerClass.Ranger), Is.EqualTo(SoundKeys.Arrow));
            Assert.That(SoundRules.HitKey(false, AdventurerClass.Mage), Is.EqualTo(SoundKeys.HitMagic));
            Assert.That(SoundRules.HitKey(false, AdventurerClass.Priest), Is.EqualTo(SoundKeys.HitBlunt));
            Assert.That(SoundRules.HitKey(false, null), Is.EqualTo(SoundKeys.HitSword), "the guild master");
        }

        [Test]
        public void Gold_AndTheClock()
        {
            Assert.That(SoundRules.GoldKey(30), Is.EqualTo(SoundKeys.CoinGain));
            Assert.That(SoundRules.GoldKey(-30), Is.EqualTo(SoundKeys.CoinSpend));
            Assert.That(SoundRules.GoldKey(0), Is.Null);
            Assert.That(SoundRules.HourKey(6), Is.EqualTo(SoundKeys.DayStart));
            Assert.That(SoundRules.HourKey(20), Is.EqualTo(SoundKeys.NightFall));
            Assert.That(SoundRules.HourKey(13), Is.Null);
        }

        [Test]
        public void Throttle_KeepsBusyFightsReadable()
        {
            var t = new SoundThrottle();
            Assert.That(t.Allow(SoundKeys.HitSword, 10f), Is.True);
            Assert.That(t.Allow(SoundKeys.HitSword, 10.02f), Is.False, "too soon after the last blow");
            Assert.That(t.Allow(SoundKeys.Arrow, 10.02f), Is.True, "another sound is free");
            Assert.That(t.Allow(SoundKeys.HitSword, 10.2f), Is.True);
            Assert.That(t.Allow(SoundKeys.Notify, 0f), Is.True);
            Assert.That(t.Allow(SoundKeys.Notify, 0.5f), Is.False);
        }
    }
}
