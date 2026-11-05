using System.Linq;
using GuildMaster.Incidents;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>The event card's direction chips and result chips (2026-11-05, UI round).</summary>
    public class EventCardChipTests
    {
        static EventCardDef Card(string id) => EventCards.All.First(c => c.Id == id);

        [Test]
        public void Directions_ShowWhichWayOnly()
        {
            var take = Card("noble").Choices[0]; // 받는다 (+150G)
            var chips = EventCards.Directions(take, null, null);
            Assert.That(chips.Any(c => c.text == "골드 ▲▲" && c.sign > 0), Is.True, string.Join(", ", chips.Select(c => c.text)));
            Assert.That(chips.Any(c => c.text.StartsWith("스트레스(전원) ▲") && c.sign < 0), Is.True);
            Assert.That(chips.Any(c => c.text == "평판 ▼" && c.sign < 0), Is.True);
            Assert.That(chips.All(c => !c.text.Any(char.IsDigit)), Is.True, "no amounts before choosing");
        }

        [Test]
        public void Directions_ARollSplitsWhatDiffers()
        {
            var haggle = Card("noble").Choices[1]; // 조건을 깎는다, 60%
            var chips = EventCards.Directions(haggle, null, null).Select(c => c.text).ToList();
            Assert.That(chips, Has.Some.StartsWith("성공: 골드"));
            Assert.That(chips, Has.Some.StartsWith("실패:"));
            Assert.That(chips.Count, Is.LessThanOrEqualTo(EventCards.MaxChips));
        }

        [Test]
        public void Directions_NameThePeople()
        {
            var show = Card("toby").Choices[0]; // 훈련을 보여 준다
            var chips = EventCards.Directions(show, "니나", null, id => id == "RES_toby" ? "토비" : "주민").Select(c => c.text).ToList();
            Assert.That(chips, Has.Member("토비 호감 ▲"));
            Assert.That(chips, Has.Member("니나 피로 ▲"));
        }

        [Test]
        public void Directions_ABigStressChangeGetsTwoArrows()
        {
            var gift = Card("birthday").Choices[0];  // 선물을 사 준다, stress −15
            var words = Card("birthday").Choices[1]; // 축하한다는 말만, stress −5
            Assert.That(EventCards.Directions(gift, "미라", null).Select(c => c.text), Has.Member("미라 스트레스 ▼▼"));
            Assert.That(EventCards.Directions(words, "미라", null).Select(c => c.text), Has.Member("미라 스트레스 ▼"));
        }

        [Test]
        public void Changes_GiveTheNumbersAndTheCost()
        {
            var sword = Card("toby").Choices[1]; // 목검을 하나 사 준다, 20G
            var chips = EventCards.Changes(sword, sword.Success, "니나", null, id => "토비").Select(c => c.text).ToList();
            Assert.That(chips, Has.Member("골드 −20"));
            Assert.That(chips, Has.Member("토비 호감 +20"));
            var away = Card("toby").Choices[2];
            Assert.That(EventCards.Changes(away, away.Success, "니나", null, id => "토비").Single().sign, Is.LessThan(0));
        }
    }
}
