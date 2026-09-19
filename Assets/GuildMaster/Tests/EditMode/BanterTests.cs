using GuildMaster.Adventurers.Prompts;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    public class BanterTests
    {
        [Test]
        public void Interpret_AcceptsTwoLines()
        {
            var dto = new BanterPrompt.ResponseDto { a = "오늘 날씨 좋다.", b = "그러게, 사냥 가기 딱이야." };
            Assert.That(BanterPrompt.TryInterpret(dto, out var a, out var b), Is.True);
            Assert.That(a, Is.EqualTo("오늘 날씨 좋다."));
            Assert.That(b, Is.EqualTo("그러게, 사냥 가기 딱이야."));
        }

        [Test]
        public void Interpret_RejectsMissingOrCutLine()
        {
            Assert.That(BanterPrompt.TryInterpret(new BanterPrompt.ResponseDto { a = "안녕.", b = "" }, out _, out _), Is.False);
            Assert.That(BanterPrompt.TryInterpret(null, out _, out _), Is.False);
            // B was cut by the grammar cap with no sentence end → dropped → fixed pair instead.
            var cut = new string('가', BanterPrompt.LineMaxChars);
            Assert.That(BanterPrompt.TryInterpret(new BanterPrompt.ResponseDto { a = "안녕.", b = cut }, out _, out _), Is.False);
        }

        [Test]
        public void StripOwnName_RemovesLeadingSelfAddress()
        {
            Assert.That(BanterPrompt.StripOwnName("로웬, 오늘도 몸이 튼튼해라.", "로웬"), Is.EqualTo("오늘도 몸이 튼튼해라."));
            Assert.That(BanterPrompt.StripOwnName("로웬아, 힘내자.", "로웬"), Is.EqualTo("힘내자."));
            // Addressing the other person or mentioning oneself mid-line stays.
            Assert.That(BanterPrompt.StripOwnName("레오야, 힘내자.", "로웬"), Is.EqualTo("레오야, 힘내자."));
            Assert.That(BanterPrompt.StripOwnName("로웬은 괜찮아.", "로웬"), Is.EqualTo("로웬은 괜찮아."));
        }

        [Test]
        public void FixedPairs_AreComplete()
        {
            foreach (var (a, b) in BanterPrompt.FixedPairs)
            {
                Assert.That(a, Is.Not.Empty);
                Assert.That(b, Is.Not.Empty);
            }
        }
    }
}
