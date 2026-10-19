using System.Linq;
using GuildMaster.Adventurers;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    public class BanterTests
    {
        /// <summary>P4-91: the fixed lines are all the talk there is, so there are enough of them, each a line and an answer.</summary>
        [Test]
        public void Lines_AreComplete_AndPlentiful()
        {
            foreach (var (a, b) in BanterLines.Town.Concat(BanterLines.Camp))
            {
                Assert.That(a, Is.Not.Empty);
                Assert.That(b, Is.Not.Empty);
            }
            Assert.That(BanterLines.Town.Length, Is.GreaterThanOrEqualTo(15));
            Assert.That(BanterLines.Camp.Length, Is.GreaterThanOrEqualTo(12));
            Assert.That(BanterLines.Town.Select(p => p.a).Distinct().Count(), Is.EqualTo(BanterLines.Town.Length), "no line twice");
        }

        /// <summary>Talk by a campfire is not town small talk (P4-66).</summary>
        [Test]
        public void TalkByTheFire_IsItsOwn()
        {
            Assert.That(BanterLines.Camp.Any(p => p.a.Contains("불") || p.b.Contains("불")), Is.True);
            Assert.That(BanterLines.Camp.Intersect(BanterLines.Town), Is.Empty);
        }
    }
}
