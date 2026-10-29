using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.Welfare;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Counsel choices and written answers (user decision 2026-10-28: choices plus free typing).</summary>
    public class CounselChoiceTests
    {
        static CounselBank Shipped() => CounselBank.LoadResources();

        /// <summary>A member whose worry is of this kind (through the real concern check).</summary>
        static CounselConcern Worry(CounselConcernKind kind)
        {
            var s = new CounselInput { HpRatio = 1f, Gold = 500, Values = "명예", RecentMemories = new List<string>() };
            switch (kind)
            {
                case CounselConcernKind.Grief: s.RecentMemories = new List<string> { "동료 카엘이 늑대 사냥 중 목숨을 잃었다" }; break;
                case CounselConcernKind.Neglect: s.Grudges = 1; break;
                case CounselConcernKind.Fear: s.HpRatio = 0.3f; break;
                case CounselConcernKind.Failure: s.RecentMemories = new List<string> { "늑대 사냥 의뢰를 포기했다" }; break;
                case CounselConcernKind.Conflict: s.RivalName = "카엘"; s.RivalFeeling = -50; break;
                case CounselConcernKind.Overwork: s.Fatigue = 80f; break;
                case CounselConcernKind.Money: s.Debt = 10; break;
            }
            var c = CounselConcerns.Detect(s);
            Assert.That(c.Kind, Is.EqualTo(kind), "test setup");
            return c;
        }

        static IEnumerable<CounselConcernKind> AllKinds => System.Enum.GetValues(typeof(CounselConcernKind)).Cast<CounselConcernKind>();

        [Test]
        public void ShippedLines_HaveEveryAnswerInEverySpeechLevel()
        {
            var b = Shipped();
            Assert.That(b.ChoiceTotal, Is.GreaterThanOrEqualTo(40));
            var keys = new List<string> { "opening.closed", "listen.known", "empathy.known", "wrong.guess", "solution.miss", "praise", "small", "dismiss" };
            foreach (CounselOutcome o in new[] { CounselOutcome.Relieved, CounselOutcome.Walkout, CounselOutcome.Ended }) keys.Add("closing." + o);
            foreach (var k in AllKinds) { keys.Add("opening." + k); keys.Add("confess." + k); keys.Add("touched." + k); keys.Add("solved." + k); }
            foreach (var key in keys)
                foreach (NpcVoice.Register r in System.Enum.GetValues(typeof(NpcVoice.Register)))
                    Assert.That(b.Line(key, r, null), Is.Not.Null.And.Not.Empty, key + " " + r);
        }

        /// <summary>A chosen line means what the game's word rules read in it, so typing it gives the same result.</summary>
        [Test]
        public void ShippedChoices_ReadTheWayTheGameReadsWords()
        {
            var worries = AllKinds.ToDictionary(k => k, k => Worry(k));
            foreach (var c in Shipped().AllChoices)
            {
                string text = c.Template.Replace("{subject}", "카엘");
                var (reaction, _) = CounselBank.ReactionFor(c, null);
                Assert.That(CounselScoring.Classify(text), Is.EqualTo(reaction), c.Id);
                if (c.Topic != null)
                    Assert.That(worries[c.Topic.Value].Touches(text), Is.True, c.Id + " goes to its worry");
                else
                    foreach (var w in worries.Values)
                        Assert.That(w.Touches(text), Is.False, $"{c.Id} is not about {w.Kind}");
            }
        }

        [Test]
        public void Offer_GuessesBeforeTheWorryIsKnown_HelpsAfter()
        {
            var b = Shipped();
            var rng = new GameRandom(5);
            foreach (var kind in AllKinds)
            {
                var worry = Worry(kind);
                for (int i = 0; i < 6; i++)
                {
                    var before = b.Offer(worry, false, new HashSet<string>(), rng);
                    Assert.That(before.Count, Is.EqualTo(CounselBank.ChoiceCount), kind.ToString());
                    Assert.That(before.Any(c => c.Kind == CounselChoiceKind.Solve), Is.False, "no help offered for an unknown worry");
                    Assert.That(before.Count(c => c.Kind == CounselChoiceKind.Touch), Is.EqualTo(2));
                    if (kind != CounselConcernKind.Grief)
                        Assert.That(before.Any(c => c.Topic == CounselConcernKind.Grief), Is.False, "no guessing at a death");
                    Assert.That(before.All(c => !c.Text.Contains("{")), Is.True);

                    var after = b.Offer(worry, true, new HashSet<string>(), rng);
                    Assert.That(after.Count, Is.EqualTo(CounselBank.ChoiceCount), kind.ToString());
                    Assert.That(after.Any(c => c.Kind == CounselChoiceKind.Solve && c.Topic == kind), Is.True, kind.ToString());
                    Assert.That(after.All(c => !c.Text.Contains("{")), Is.True);
                }
            }
        }

        [Test]
        public void Respond_FollowsTheWorry()
        {
            var b = Shipped();
            var rng = new GameRandom(2);
            var worry = Worry(CounselConcernKind.Overwork);
            var reg = NpcVoice.Register.Warm;
            var right = b.AllChoices.First(c => c.Kind == CounselChoiceKind.Touch && c.Topic == CounselConcernKind.Overwork);
            var hit = b.Respond(right, worry, false, reg, rng);
            Assert.That(hit.touched, Is.True);
            Assert.That(hit.reaction, Is.EqualTo(CounselReaction.Empathy));

            var wrong = b.AllChoices.First(c => c.Kind == CounselChoiceKind.Touch && c.Topic == CounselConcernKind.Money);
            var miss = b.Respond(wrong, worry, false, reg, rng);
            Assert.That(miss.touched, Is.False);
            Assert.That(miss.text, Does.Contain(b.Line("confess.Overwork", reg, null)), "a wrong guess still gets the real worry out");
            Assert.That(b.Respond(wrong, worry, true, reg, rng).text, Does.Not.Contain(b.Line("confess.Overwork", reg, null)), "said once is enough");

            var help = b.AllChoices.First(c => c.Kind == CounselChoiceKind.Solve && c.Topic == CounselConcernKind.Overwork);
            Assert.That(b.Respond(help, worry, true, reg, rng).touched, Is.True);
            var otherHelp = b.AllChoices.First(c => c.Kind == CounselChoiceKind.Solve && c.Topic == CounselConcernKind.Money);
            var r = b.Respond(otherHelp, worry, true, reg, rng);
            Assert.That(r.reaction, Is.EqualTo(CounselReaction.Solution));
            Assert.That(r.touched, Is.False);

            var tough = b.AllChoices.First(c => c.Kind == CounselChoiceKind.Tough);
            Assert.That(b.Respond(tough, worry, true, reg, rng).reaction, Is.EqualTo(CounselReaction.Dismiss));

            var grief = Worry(CounselConcernKind.Grief);
            string opening = b.Opening(grief, true, NpcVoice.Register.Haughty, rng);
            Assert.That(opening, Does.Contain("카엘"));
            Assert.That(opening, Does.Not.Contain("{"));
            Assert.That(b.Opening(grief, false, NpcVoice.Register.Formal, null), Is.Not.EqualTo(b.Opening(grief, false, NpcVoice.Register.Warm, null)));
        }
    }
}
