using System.Linq;
using GuildMaster.Adventurers;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    public class DialogueLogTests
    {
        static AdventurerData Npc(string name) => new AdventurerData { id = name, name = name };

        [Test]
        public void Banter_IsRecordedForBothSpeakers_AndQueryableByPartner()
        {
            var a = Npc("리아");
            var b = Npc("테오");
            var c = Npc("제나");
            DialogueLog.AddBanter(a, b, 10f, "오늘 날씨 좋다.", "그러게.");
            DialogueLog.AddBanter(a, c, 12f, "고블린 봤어?", "응, 북쪽에서.");

            Assert.AreEqual(4, a.dialogue.Count);
            Assert.AreEqual(2, b.dialogue.Count);
            var withTeo = DialogueLog.LastBanterWith(a, "테오", 5);
            Assert.AreEqual(2, withTeo.Count);
            Assert.AreEqual("리아", withTeo[0].speaker);
            Assert.AreEqual("그러게.", withTeo[1].text);
            Assert.IsTrue(b.dialogue.All(l => l.with == "리아"));
        }

        [Test]
        public void Recruit_KeepsSpeakerAndSide_AndCapacityDropsOldest()
        {
            var v = Npc("라일");
            DialogueLog.AddRecruit(v, 5f, true, "안녕하시오.");
            DialogueLog.AddRecruit(v, 5f, false, "안녕하세요.");
            var lines = DialogueLog.Recruit(v).ToList();
            Assert.AreEqual(DialogueLog.MasterName, lines[0].speaker);
            Assert.IsTrue(lines[0].fromPlayer);
            Assert.AreEqual("라일", lines[1].speaker);

            for (int i = 0; i < DialogueLog.Capacity + 5; i++) DialogueLog.AddRecruit(v, 6f, true, "말 " + i);
            Assert.AreEqual(DialogueLog.Capacity, v.dialogue.Count);
            Assert.AreEqual("말 " + (DialogueLog.Capacity + 4), v.dialogue.Last().text);
        }
    }
}
