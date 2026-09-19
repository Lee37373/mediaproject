using GuildMaster.Adventurers.Prompts;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    public class PartyTests
    {
        static PartyPrompt.ResponseDto Dto(string accept, string say = "좋아, 같이 가자!") => new() { accept = accept, say = say };

        [Test]
        public void Interpret_YesAndNo()
        {
            Assert.That(PartyPrompt.TryInterpret(Dto("yes"), 0.9f, "적당", 0f, "돈", out var accept, out var say), Is.True);
            Assert.That(accept, Is.True);
            Assert.That(say, Is.EqualTo("좋아, 같이 가자!"));
            Assert.That(PartyPrompt.TryInterpret(Dto("no", "혼자가 편해."), 0.9f, "적당", 0f, "돈", out accept, out _), Is.True);
            Assert.That(accept, Is.False);
        }

        [Test]
        public void Interpret_RejectsMalformed()
        {
            Assert.That(PartyPrompt.TryInterpret(Dto("maybe"), 0.9f, "적당", 0f, "돈", out _, out _), Is.False);
            Assert.That(PartyPrompt.TryInterpret(null, 0.9f, "적당", 0f, "돈", out _, out _), Is.False);
        }

        [Test]
        public void Interpret_OverridesUnsafeYes()
        {
            // Low HP.
            PartyPrompt.TryInterpret(Dto("yes"), 0.2f, "적당", 0f, "돈", out var accept, out _);
            Assert.That(accept, Is.False);
            // Cautious personality on a reckless quest.
            PartyPrompt.TryInterpret(Dto("yes"), 0.9f, "무모", 0f, "안전", out accept, out _);
            Assert.That(accept, Is.False);
            // Neutral personality may join a reckless quest because someone already took it.
            PartyPrompt.TryInterpret(Dto("yes"), 0.9f, "무모", 0f, "돈", out accept, out _);
            Assert.That(accept, Is.True);
        }

        [Test]
        public void RuleChance_FollowsSpec()
        {
            Assert.That(PartyPrompt.RuleAcceptChance("동료", 1f), Is.EqualTo(0.8f));
            Assert.That(PartyPrompt.RuleAcceptChance("돈", 0.5f), Is.EqualTo(0.2f));
            Assert.That(PartyPrompt.RuleAcceptChance("돈", 1f), Is.EqualTo(0.55f));
            Assert.That(PartyPrompt.RuleAcceptChance("모험", 1.5f), Is.EqualTo(0.55f));
        }
    }
}
