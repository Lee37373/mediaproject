using GuildMaster.Town;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Nest-driven raids (P3-6, 2026-09-19): each nest counts on its own.</summary>
    public class RaidTests
    {
        [Test]
        public void DangerLevel_StepsAboveThreshold()
        {
            // Goblin nest: threshold 10, step 4 → levels at 10 / 14 / 18.
            Assert.That(WaveManager.DangerLevel(9, 10, 4), Is.EqualTo(0));
            Assert.That(WaveManager.DangerLevel(10, 10, 4), Is.EqualTo(1));
            Assert.That(WaveManager.DangerLevel(13, 10, 4), Is.EqualTo(1));
            Assert.That(WaveManager.DangerLevel(14, 10, 4), Is.EqualTo(2));
            Assert.That(WaveManager.DangerLevel(18, 10, 4), Is.EqualTo(3));
            Assert.That(WaveManager.DangerLevel(20, 10, 4), Is.EqualTo(3));
        }

        [Test]
        public void DangerLevel_OgreStepOfOne()
        {
            Assert.That(WaveManager.DangerLevel(1, 2, 1), Is.EqualTo(0));
            Assert.That(WaveManager.DangerLevel(2, 2, 1), Is.EqualTo(1));
            Assert.That(WaveManager.DangerLevel(4, 2, 1), Is.EqualTo(3));
        }

        [Test]
        public void PartySize_FractionWithMinimum()
        {
            Assert.That(WaveManager.PartySize(18, 0.7f, 3), Is.EqualTo(13));
            Assert.That(WaveManager.PartySize(10, 0.7f, 3), Is.EqualTo(7));
            // Small nests still send the minimum, but never more than they have.
            Assert.That(WaveManager.PartySize(3, 0.7f, 3), Is.EqualTo(3));
            Assert.That(WaveManager.PartySize(2, 0.7f, 3), Is.EqualTo(2));
        }
    }
}
