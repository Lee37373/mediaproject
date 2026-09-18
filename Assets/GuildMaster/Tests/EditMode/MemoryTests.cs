using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    public class MemoryTests
    {
        [Test]
        public void Add_MergesSameKeyOnSameDay_ButNotAcrossDays()
        {
            var list = new List<MemoryEntry>();
            AdventurerMemory.Add(list, 10f, 2, "오크를 쓰러뜨렸다 ({n}마리)", "kill:orc");
            AdventurerMemory.Add(list, 12f, 1, "다른 일", null);
            AdventurerMemory.Add(list, 14f, 2, "오크를 쓰러뜨렸다 ({n}마리)", "kill:orc");
            Assert.That(list.Count, Is.EqualTo(2));
            Assert.That(list[1].Text, Is.EqualTo("오크를 쓰러뜨렸다 (2마리)"), "merged entry moves to the end");

            AdventurerMemory.Add(list, 30f, 2, "오크를 쓰러뜨렸다 ({n}마리)", "kill:orc"); // day 2
            Assert.That(list.Count, Is.EqualTo(3));
            Assert.That(list[2].Text, Is.EqualTo("오크를 쓰러뜨렸다 (1마리)"));
        }

        [Test]
        public void Add_OverCapacity_DropsLeastImportantOldest()
        {
            var list = new List<MemoryEntry>();
            AdventurerMemory.Add(list, 0f, 3, "죽을 뻔했다");
            for (int i = 1; i <= AdventurerMemory.Capacity + 5; i++)
                AdventurerMemory.Add(list, i, 1, "일상 " + i);
            Assert.That(list.Count, Is.EqualTo(AdventurerMemory.Capacity));
            Assert.That(list.Any(m => m.text == "죽을 뻔했다"), Is.True, "important memory survives");
            Assert.That(list.Any(m => m.text == "일상 1"), Is.False, "oldest routine memory dropped");
            Assert.That(list.Last().text, Is.EqualTo("일상 " + (AdventurerMemory.Capacity + 5)));
        }

        [Test]
        public void SelectForPrompt_KeepsImportantAndRecent_InChronologicalOrder()
        {
            var list = new List<MemoryEntry>();
            AdventurerMemory.Add(list, 1f, 3, "동료가 쓰러졌다");
            for (int i = 2; i <= 12; i++) AdventurerMemory.Add(list, i, 1, "일상 " + i);
            AdventurerMemory.Add(list, 13f, 2, "레벨 2가 되었다");

            var picked = AdventurerMemory.SelectForPrompt(list);
            Assert.That(picked.Count, Is.EqualTo(AdventurerMemory.PromptCount));
            Assert.That(picked.First().text, Is.EqualTo("동료가 쓰러졌다"), "old but important is kept, first in time order");
            Assert.That(picked.Last().text, Is.EqualTo("레벨 2가 되었다"));
            Assert.That(picked.Select(m => m.hour), Is.Ordered);
            Assert.That(picked.Distinct().Count(), Is.EqualTo(picked.Count));
        }

        [Test]
        public void Format_ShowsDayAndHour()
        {
            var m = new MemoryEntry { hour = 24f + 14.5f, importance = 1, text = "길드에 합류했다" };
            Assert.That(m.Format(), Is.EqualTo("[2일 14시] 길드에 합류했다"));
        }

        [TestCase("오크", "을", "를", ExpectedResult = "를")]
        [TestCase("고블린", "을", "를", ExpectedResult = "을")]
        [TestCase("카이", "이", "가", ExpectedResult = "가")]
        [TestCase("라일", "이", "가", ExpectedResult = "이")]
        public string Josa_PicksParticleByFinalConsonant(string word, string a, string b)
            => MemoryRecorder.Josa(word, a, b);
    }
}
