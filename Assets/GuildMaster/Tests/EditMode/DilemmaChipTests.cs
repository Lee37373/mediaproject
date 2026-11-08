using System.Linq;
using GuildMaster.Dilemmas;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// The decision card window's chips (P4-135: KMS's event card chips of 2026-11-05, moved onto the decision cards — the
    /// EventCardChipTests that came with them in the merge tested the event cards P4-118 had folded away): before choosing,
    /// which way a choice moves things and nothing more; after, what it actually did, with the numbers.
    /// </summary>
    public class DilemmaChipTests
    {
        [Test]
        public void Directions_ShowWhichWayOnly_ForEveryChoiceOfEveryCard()
        {
            foreach (var d in DilemmaRules.All)
            foreach (var c in d.Choices)
            {
                var chips = DilemmaChips.Directions(c.Hints);
                Assert.That(chips, Is.Not.Empty, d.Id);
                Assert.That(chips.Count, Is.LessThanOrEqualTo(DilemmaChips.MaxChoiceChips), d.Id);
                Assert.That(chips.All(x => !x.Text.Any(char.IsDigit)), Is.True, $"{d.Id}: no amounts before choosing");
            }
        }

        [Test]
        public void Directions_AreColouredByWhatTheyMeanForTheGuild()
        {
            var chips = DilemmaChips.Directions(new[] { DilemmaHint.GoldDown, DilemmaHint.MoodUp, DilemmaHint.Tired, DilemmaHint.Risk, DilemmaHint.TownUp });
            Assert.That(chips.Select(c => (c.Text, c.Sign)), Is.EqualTo(new[]
            {
                ("골드 ▼", -1), ("기분 ▲", 1), ("피로", -1), ("운에 맡김", 0), ("주민 평판 ▲", 1),
            }));
            Assert.That(DilemmaChips.Directions(new DilemmaHint[0]).Single().Text, Is.EqualTo("변화 없음"));
            Assert.That(DilemmaRules.HintText(DilemmaHint.FameUp), Is.EqualTo("명성 ▲"), "the words are the chips'");
        }

        [Test]
        public void Changes_SumWhatMoved_AndGiveTheNumbers()
        {
            var log = new DilemmaChangeLog();
            log.Add("골드", -20);
            log.Add("골드", 100);
            log.Add("제나 스트레스", -10, goodWhenUp: false);
            log.Add("명성", 0.3f); // under half a point: left out
            log.Add("토비 호감", -6);
            Assert.That(log.Chips().Select(c => (c.Text, c.Sign)), Is.EqualTo(new[]
            {
                ("골드 +80", 1), ("제나 스트레스 −10", 1), ("토비 호감 −6", -1),
            }));
        }

        [Test]
        public void Changes_ABondWrittenBothWaysIsOneChange_AndMarksComeOnceAfterTheAmounts()
        {
            var log = new DilemmaChangeLog();
            log.Mark("방문자가 옴", 1);
            log.Bond("제나", "벨라", 10);
            log.Bond("벨라", "제나", 10);
            log.Mark("방문자가 옴", 1);
            log.Mark("나쁜 소문", -1);
            Assert.That(log.Chips().Select(c => c.Text), Is.EqualTo(new[] { "벨라·제나 사이 +10", "방문자가 옴", "나쁜 소문" }));

            for (int i = 0; i < 20; i++) log.Add("물자" + i, 1);
            Assert.That(log.Chips().Count, Is.EqualTo(DilemmaChips.MaxChangeChips), "at most a row of them");
        }
    }
}
