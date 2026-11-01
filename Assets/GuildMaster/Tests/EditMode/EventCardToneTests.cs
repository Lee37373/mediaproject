using GuildMaster.Incidents;
using GuildMaster.Social;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Event card results read as success / failure, or good / bad for a sure choice (2026-11-01).</summary>
    public class EventCardToneTests
    {
        [Test]
        public void RolledChoices_SucceedOrFail()
        {
            var c = new CardChoice { Chance = 0.5f, Success = new CardEffect { Gold = 50 }, Failure = new CardEffect { StressA = 5 } };
            Assert.That(EventCards.ToneOf(c, true), Is.EqualTo(CardTone.Success));
            Assert.That(EventCards.ToneOf(c, false), Is.EqualTo(CardTone.Failure));
            Assert.That(EventCards.Badge(CardTone.Success), Does.Contain("성공"));
            Assert.That(EventCards.Badge(CardTone.Failure), Does.Contain("실패"));
        }

        [Test]
        public void SureChoices_ReadByWhatTheyDid()
        {
            var gift = new CardChoice { Success = new CardEffect { StressA = -15, EmotionA = Emotion.Happy } };
            var ignore = new CardChoice { Success = new CardEffect { Opinion = -5, ResidentOpinion = -10, RumorTone = -1 } };
            var nothing = new CardChoice { Success = new CardEffect { Result = "상인은 떠났습니다." } };
            Assert.That(EventCards.ToneOf(gift, true), Is.EqualTo(CardTone.Good));
            Assert.That(EventCards.ToneOf(ignore, true), Is.EqualTo(CardTone.Bad));
            Assert.That(EventCards.ToneOf(nothing, true), Is.EqualTo(CardTone.Neutral));
            Assert.That(EventCards.Badge(CardTone.Neutral), Is.Empty);
            var trade = new CardChoice { Success = new CardEffect { Gold = 150, StressAll = 8, Opinion = -2 } };
            Assert.That(EventCards.ToneOf(trade, true), Is.EqualTo(CardTone.Neutral), "money for the guild's nerves: mixed");
        }

        [Test]
        public void EveryCardChoice_HasATone()
        {
            // The real cards: rolled ones split success/failure; sure ones are not all "neutral" by accident.
            int good = 0, bad = 0;
            foreach (var def in EventCards.All)
                foreach (var c in def.Choices)
                {
                    if (c.Failure != null) continue;
                    var t = EventCards.ToneOf(c, true);
                    if (t == CardTone.Good) good++;
                    if (t == CardTone.Bad) bad++;
                }
            Assert.That(good, Is.GreaterThan(0));
            Assert.That(bad, Is.GreaterThan(0));
        }
    }
}
