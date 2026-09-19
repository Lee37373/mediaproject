using GuildMaster.LLM;
using GuildMaster.Quests;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    public class DialogueTextTests
    {
        [Test]
        public void CleanSentence_KeepsShortFinishedLine()
        {
            Assert.That(LlmJson.CleanSentence("\"좀 쉬어야겠어...\"", 40, 80, true), Is.EqualTo("좀 쉬어야겠어..."));
        }

        [Test]
        public void CleanSentence_TrimsCutLineToLastSentence()
        {
            // 80 chars = grammar cap reached: the model was cut off mid-sentence.
            string cut = "오늘은 쉬자. 내일은 고블린을 잡으러 가야지 " + new string('가', 80);
            cut = cut.Substring(0, 80);
            Assert.That(LlmJson.CleanSentence(cut, 40, 80, true), Is.EqualTo("오늘은 쉬자."));
        }

        [Test]
        public void CleanSentence_UsesKoreanEndingBeforeSpace()
        {
            string s = "피곤하지만 가야겠다 그리고 고블린 둥지를 끝까지 찾아서 모두 없애버리고 돌아와야";
            Assert.That(LlmJson.CleanSentence(s, 40, 80, true), Is.EqualTo("피곤하지만 가야겠다"));
        }

        [Test]
        public void CleanSentence_DropsUnfinishedWithoutBoundary_OrEllipsizes()
        {
            string s = new string('가', 80);
            Assert.That(LlmJson.CleanSentence(s, 40, 80, true), Is.EqualTo(""));
            Assert.That(LlmJson.CleanSentence(s, 40, 80, false), Does.EndWith("…"));
            Assert.That(LlmJson.CleanSentence(s, 40, 80, false).Length, Is.LessThanOrEqualTo(40));
        }

        [Test]
        public void EscortPay_IsProportionalToHours()
        {
            Assert.That(QuestBoard.EscortPay(60, 30, 2f, 6), Is.EqualTo((20, 10)));
            Assert.That(QuestBoard.EscortPay(40, 20, 4f, 4), Is.EqualTo((40, 20)));
            Assert.That(QuestBoard.EscortPay(40, 20, 9f, 4), Is.EqualTo((40, 20)));
            Assert.That(QuestBoard.EscortPay(40, 20, 0f, 4), Is.EqualTo((0, 0)));
        }
    }
}
