using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Where the model is used (team decision 2026-10-28: recruiting and counselling talks only).</summary>
    public class LlmUseTests
    {
        [Test]
        public void ModelOnlyForRecruitAndCounsel_EverythingElseTakesItsRules()
        {
            Assert.That(LlmUse.Allows(LlmFeature.Recruit), Is.True);
            Assert.That(LlmUse.Allows(LlmFeature.Counsel), Is.True);
            foreach (var f in new[] { LlmFeature.TownTalk, LlmFeature.Decision, LlmFeature.Party, LlmFeature.Split, LlmFeature.Skill,
                                      LlmFeature.Proposal, LlmFeature.Persuasion, LlmFeature.EventCard })
            {
                Assert.That(LlmUse.Allows(f), Is.False, f.ToString());
                Assert.That(LlmUse.Gateway(f, null), Is.Null);
            }
        }

        [Test]
        public void AFeatureCanBeTurnedBackOn()
        {
            try
            {
                LlmUse.Set(LlmFeature.Skill, true);
                Assert.That(LlmUse.Allows(LlmFeature.Skill), Is.True);
            }
            finally { LlmUse.Set(LlmFeature.Skill, false); }
        }
    }
}
