using GuildMaster.Core;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Saved moments and timers go to the hundredth (P5-4), but what is still ahead stays ahead (P4-80):
    /// a cooldown a few milliseconds from ready was saved as ready and fired on load, in a paused game.
    /// </summary>
    public class SaveTimeTests
    {
        [Test]
        public void Hundredths_RoundsToTheHundredth()
        {
            Assert.That(SaveTime.Hundredths(1.234f), Is.EqualTo(1.23f).Within(1e-6f));
            Assert.That(SaveTime.Hundredths(-2.346f), Is.EqualTo(-2.35f).Within(1e-6f));
            Assert.That(SaveTime.Hundredths(0f), Is.EqualTo(0f));
        }

        [Test]
        public void SomethingStillAhead_IsNeverSavedAsNow()
        {
            Assert.That(SaveTime.Hundredths(0.003f), Is.GreaterThan(0f), "a timer 3 ms from firing has not fired");
            Assert.That(SaveTime.Rel(Time.time + 0.003f), Is.GreaterThan(0f), "a cooldown 3 ms from ready is not ready");
            Assert.That(SaveTime.Hundredths(-0.003f), Is.LessThanOrEqualTo(0f), "one just past stays past");
        }

        [Test]
        public void ASavedValue_ComesBackTheSame()
        {
            foreach (float v in new[] { 0.003f, 0.01f, 0.47f, -0.21f, 12.5f })
            {
                float saved = SaveTime.Hundredths(v);
                Assert.That(SaveTime.Hundredths(saved), Is.EqualTo(saved), $"{v}: save → load → save");
                Assert.That(SaveTime.Rel(SaveTime.Abs(saved)), Is.EqualTo(saved).Within(1e-6f), $"{v} as a moment");
            }
        }
    }
}
