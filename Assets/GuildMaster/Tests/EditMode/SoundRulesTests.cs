using GuildMaster.Adventurers;
using GuildMaster.Audio;
using GuildMaster.Incidents;
using GuildMaster.Seasons;
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

        [Test]
        public void Music_FollowsTheSeason_AndDayOrNight()
        {
            Assert.That(SoundRules.MusicKey(Season.Spring, 6), Is.EqualTo("bgm_spring_day"), "day from the morning bells");
            Assert.That(SoundRules.MusicKey(Season.Summer, 19), Is.EqualTo("bgm_summer_day"));
            Assert.That(SoundRules.MusicKey(Season.Autumn, 20), Is.EqualTo("bgm_autumn_night"), "night from the evening bells");
            Assert.That(SoundRules.MusicKey(Season.Winter, 3), Is.EqualTo("bgm_winter_night"));
        }

        [Test]
        public void Music_FadesOut_Pauses_ThenFadesIn_NeverTwoAtOnce()
        {
            var f = new MusicFader();
            Assert.That(f.Request("a", instant: true), Is.EqualTo("a"));
            Assert.That(f.Level, Is.EqualTo(1f));
            Assert.That(f.Request("b"), Is.Null, "waits for a to fade out");
            f.Tick(MusicFader.FadeOutSeconds / 2);
            Assert.That(f.Current, Is.EqualTo("a"));
            Assert.That(f.Level, Is.EqualTo(0.5f).Within(0.01f));
            f.Tick(MusicFader.FadeOutSeconds / 2);
            Assert.That(f.Current, Is.Null, "silent pause");
            Assert.That(f.Tick(MusicFader.PauseSeconds / 2), Is.Null);
            Assert.That(f.Tick(MusicFader.PauseSeconds / 2 + 0.01f), Is.EqualTo("b"), "b starts after the pause");
            Assert.That(f.Level, Is.EqualTo(0f));
            f.Tick(MusicFader.FadeInSeconds);
            Assert.That(f.Level, Is.EqualTo(1f));
        }

        [Test]
        public void Music_ChangingItsMind_MidFade()
        {
            var f = new MusicFader();
            f.Request("a", instant: true);
            f.Request("b");
            f.Tick(1f);
            Assert.That(f.Request("a"), Is.Null);
            float low = f.Level;
            f.Tick(1f);
            Assert.That(f.Level, Is.GreaterThan(low), "back up, no pause");
            Assert.That(f.Pending, Is.Null);
            Assert.That(f.Request("a"), Is.Null, "already on");
        }

        [Test]
        public void Takes_GroupUnderTheirSound()
        {
            Assert.That(SoundRules.TakeOf("hit_sword"), Is.EqualTo("hit_sword"));
            Assert.That(SoundRules.TakeOf("hit_sword_v3"), Is.EqualTo("hit_sword"));
            Assert.That(SoundRules.TakeOf("bgm_winter_night"), Is.EqualTo("bgm_winter_night"));
            Assert.That(SoundRules.TakeOf("ui_v"), Is.EqualTo("ui_v"), "no number, not a take");
        }

        [Test]
        public void EventCardResults_SoundLikeTheirTone()
        {
            Assert.That(SoundRules.CardKey(CardTone.Success), Is.EqualTo(SoundKeys.CardSuccess));
            Assert.That(SoundRules.CardKey(CardTone.Good), Is.EqualTo(SoundKeys.CardSuccess));
            Assert.That(SoundRules.CardKey(CardTone.Failure), Is.EqualTo(SoundKeys.CardFail));
            Assert.That(SoundRules.CardKey(CardTone.Bad), Is.EqualTo(SoundKeys.CardFail));
            Assert.That(SoundRules.CardKey(CardTone.Neutral), Is.EqualTo(SoundKeys.CardResult));
        }
    }
}
