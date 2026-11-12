using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Audio;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>The candidate hit sounds to compare in play (2026-11-12).</summary>
    public class HitSoundSetTests
    {
        [Test]
        public void ACandidateSet_StandsInForEveryBlow_AndNothingElse()
        {
            Assert.That(HitSoundSets.IsHitKey(SoundRules.HitKey(true, null)), Is.True);
            foreach (AdventurerClass c in System.Enum.GetValues(typeof(AdventurerClass)))
                Assert.That(HitSoundSets.IsHitKey(SoundRules.HitKey(false, c)), Is.True, c.ToString());
            Assert.That(HitSoundSets.IsHitKey(SoundKeys.MonsterDie), Is.False);
            Assert.That(HitSoundSets.IsHitKey(SoundKeys.Click), Is.False);
            Assert.That(HitSoundSets.IsHitKey(SoundKeys.ForgePerfect), Is.False);
        }

        [Test]
        public void EverySet_HasItsOwnName_AndEmptyMeansTheShippedSounds()
        {
            Assert.That(HitSoundSets.All, Does.Contain(""));
            Assert.That(HitSoundSets.All.Select(HitSoundSets.Label).Distinct().Count(), Is.EqualTo(HitSoundSets.All.Length));
            Assert.That(HitSoundSets.Label(""), Is.EqualTo("지금 쓰는 소리"));
        }
    }
}
