using GuildMaster.Data;
using GuildMaster.Ecosystem;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>
    /// The guild is warned before it hunts a species down to nothing (P5-1). The rule reads the
    /// guild's own observations, so what it says is what the guild believes.
    /// </summary>
    public class EcologyWarningTests
    {
        static MonsterDefinition Species(int initialPopulation = 4)
        {
            var m = ScriptableObject.CreateInstance<MonsterDefinition>();
            m.name = m.id = "test";
            m.displayName = "슬라임";
            m.initialPopulation = initialPopulation;
            return m;
        }

        [Test]
        public void WithNoKnownNest_TheGuildHasNoOpinion()
        {
            Assert.That(EcologyWarning.Status(Species(), observed: 0, knownNests: 0),
                        Is.EqualTo(EcologyStatus.Healthy));
        }

        [Test]
        public void AHandfulLeftIsEndangered()
        {
            var s = Species();
            Assert.That(EcologyWarning.Status(s, EcologyWarning.EndangeredAt, 1), Is.EqualTo(EcologyStatus.Endangered));
            Assert.That(EcologyWarning.Status(s, 0, 1), Is.EqualTo(EcologyStatus.Endangered));
        }

        [Test]
        public void HalfOfWhatTheNestsShouldHoldIsThinning()
        {
            var s = Species(initialPopulation: 8);
            // Two known nests would naturally hold about 16; eight is half.
            Assert.That(EcologyWarning.Status(s, 8, 2), Is.EqualTo(EcologyStatus.Thinning));
            Assert.That(EcologyWarning.Status(s, 14, 2), Is.EqualTo(EcologyStatus.Healthy));
        }

        [Test]
        public void TheWarningNamesTheSpecies_AndSaysNothingWhenHealthy()
        {
            var s = Species();
            Assert.That(EcologyWarning.Advice(s, EcologyStatus.Endangered), Does.Contain("슬라임"));
            Assert.That(EcologyWarning.Advice(s, EcologyStatus.Endangered), Does.Contain("멸종 위기"));
            Assert.That(EcologyWarning.Advice(s, EcologyStatus.Healthy), Is.Empty);
            Assert.That(EcologyWarning.Label(EcologyStatus.Healthy), Is.Empty);
        }
    }
}
