using System.Linq;
using System.Text.RegularExpressions;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Social;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>The town talk's world words, other wordings and seasonal scripts (2026-11-02, more variety).</summary>
    public class TalkWorldTests
    {
        static TalkSide Member(string name, NpcVoice.Register reg = NpcVoice.Register.Warm)
            => new() { Kind = TalkerKind.Member, Name = name, Register = reg, Values = "동료" };

        [Test]
        public void FillWorld_PutsTheWordsAndTheirParticles()
        {
            var w = new TalkWorld { Season = "가을", Monster = "고블린", Loot = "뼛조각", Place = "온천" };
            Assert.AreEqual("고블린이 늘었어요", TalkBank.FillWorld("{monster이} 늘었어요", w));
            Assert.AreEqual("슬라임이 늘었어요", TalkBank.FillWorld("{monster이} 늘었어요", new TalkWorld { Monster = "슬라임" }));
            Assert.AreEqual("늑대가 왔어", TalkBank.FillWorld("{monster이} 왔어", new TalkWorld { Monster = "늑대" }));
            Assert.AreEqual("뼛조각을 팔자", TalkBank.FillWorld("{loot을} 팔자", w));
            Assert.AreEqual("가을엔 온천 쪽", TalkBank.FillWorld("{season}엔 {place} 쪽", w));
            Assert.AreEqual("숲으로", TalkBank.FillWorld("{place으로}", new TalkWorld()), "nothing known: the defaults");
        }

        [Test]
        public void Alternates_AreAllUsedAndSpokenInTheSpeakersLevel()
        {
            var line = new TalkLineDto
            {
                s = "A", w = "하나", f = "하나입니다", c = "하나.", h = "하나야",
                alt = new[] { new TalkAltDto { w = "둘", f = "둘입니다", c = "둘.", h = "둘이야" } },
            };
            Assert.AreEqual("하나", TalkBank.LineFor(line, Member("카엘"), () => 0.1f));
            Assert.AreEqual("둘", TalkBank.LineFor(line, Member("카엘"), () => 0.9f));
            Assert.AreEqual("둘입니다", TalkBank.LineFor(line, Member("카엘", NpcVoice.Register.Formal), () => 0.9f));
            Assert.AreEqual("하나", TalkBank.LineFor(line, Member("카엘"), null), "no roll: the first wording");
        }

        [Test]
        public void SeasonalScripts_OnlyInTheirSeasonAndTime()
        {
            var s = new TalkScriptDto { season = "Winter", time = "night" };
            Assert.IsTrue(TalkBank.WhenFits(s, "Winter", true));
            Assert.IsFalse(TalkBank.WhenFits(s, "Summer", true));
            Assert.IsFalse(TalkBank.WhenFits(s, "Winter", false));
            Assert.IsTrue(TalkBank.WhenFits(s, null, null), "no clock (tests, the old callers): anything goes");
            Assert.IsTrue(TalkBank.WhenFits(new TalkScriptDto(), "Summer", false));
        }

        /// <summary>The shipped database: every other wording a member speaks has all four levels, and every blank is one the game fills.</summary>
        [Test]
        public void ShippedDatabase_AlternatesAndBlanksAreComplete()
        {
            var bank = TalkBank.LoadResources();
            var blank = new Regex(@"\{[^}]*\}");
            var known = new Regex(@"^\{(?:(?:A|B)(?:이|을|와|은|아)?|rumor|(?:season|monster|loot|place)(?:이|을|와|은|으로)?)\}$");
            int alts = 0;
            foreach (var s in bank.Scripts)
                foreach (var line in s.lines)
                {
                    string side = line.s == "A" ? s.a : s.b;
                    bool resident = side == "R" || side == "X";
                    foreach (var text in new[] { line.t, line.w, line.f, line.c, line.h }.Where(t => !string.IsNullOrEmpty(t)))
                        foreach (Match m in blank.Matches(text))
                            Assert.That(known.IsMatch(m.Value), Is.True, $"{s.id}: {m.Value}");
                    if (line.alt == null) continue;
                    foreach (var a in line.alt)
                    {
                        alts++;
                        if (resident) Assert.That(a.t, Is.Not.Empty, s.id);
                        else Assert.That(new[] { a.w, a.f, a.c, a.h }.All(t => !string.IsNullOrEmpty(t)), Is.True, s.id);
                    }
                }
            Assert.That(alts, Is.GreaterThan(0));
            Assert.That(bank.Scripts.Any(x => !string.IsNullOrEmpty(x.season)), Is.True, "some scripts are seasonal");
        }
    }
}
