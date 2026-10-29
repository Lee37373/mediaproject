using System.Collections.Generic;
using GuildMaster.Recruitment;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Recruit affinity is scored by kind of line from a fixed table (user request 2026-09-30). These tests
    /// pin the rules and the pacing: how many good lines it takes to win an easy, a normal and a hard visitor.
    /// </summary>
    public class RecruitScoringTests
    {
        const RecruitReaction V = RecruitReaction.ValueMatch, T = RecruitReaction.TraitMatch, A = RecruitReaction.Answered, F = RecruitReaction.Friendly;

        /// <summary>Plays a talk the way RecruitmentSession scores it; returns the lines needed to auto-join (-1 = never).</summary>
        static int LinesToJoin(int start, IList<RecruitReaction> lines, string mbti = "ISFJ", string text = "그렇군요, 잘 알겠어요.")
        {
            int affinity = start, streak = 0;
            RecruitReaction? last = null;
            for (int i = 0; i < lines.Count; i++)
            {
                streak = last == lines[i] ? streak + 1 : 0;
                affinity += RecruitScoring.Score(lines[i], streak, mbti, text);
                last = lines[i];
                if (affinity >= RecruitmentSession.AutoJoinAffinity) return i + 1;
            }
            return -1;
        }

        static readonly RecruitReaction[] GoodTalk = { V, T, V, T, V, A, V, T, V, T, V, A };

        [Test]
        public void Table_GoodThingsUp_BadThingsDown()
        {
            Assert.That(RecruitScoring.Base(V), Is.GreaterThan(RecruitScoring.Base(T)));
            Assert.That(RecruitScoring.Base(T), Is.GreaterThan(RecruitScoring.Base(A)));
            Assert.That(RecruitScoring.Base(A), Is.GreaterThan(RecruitScoring.Base(F)));
            Assert.That(RecruitScoring.Base(F), Is.GreaterThan(0), "ordinary polite talk is mildly positive");
            Assert.That(RecruitScoring.Base(RecruitReaction.LowEffort), Is.LessThan(0));
            Assert.That(RecruitScoring.Base(RecruitReaction.Clash), Is.LessThan(RecruitScoring.Base(RecruitReaction.LowEffort)));
            Assert.That(RecruitScoring.Base(RecruitReaction.Rude), Is.LessThan(RecruitScoring.Base(RecruitReaction.Clash)));
        }

        [Test]
        public void Codes_RoundTrip_UnknownIsFriendly()
        {
            foreach (RecruitReaction r in System.Enum.GetValues(typeof(RecruitReaction)))
                Assert.That(RecruitScoring.Parse(RecruitScoring.Code(r)), Is.EqualTo(r));
            Assert.That(RecruitScoring.Parse("???"), Is.EqualTo(F));
            Assert.That(RecruitScoring.Parse(null), Is.EqualTo(F));
        }

        [Test]
        public void SamePitchAgain_IsWorthLessEachTime()
        {
            Assert.That(RecruitScoring.Diminish(8, 0), Is.EqualTo(8));
            Assert.That(RecruitScoring.Diminish(8, 1), Is.EqualTo(4));
            Assert.That(RecruitScoring.Diminish(8, 2), Is.EqualTo(0));
            Assert.That(RecruitScoring.Diminish(-5, 3), Is.EqualTo(-5), "bad lines always count");
            // Only ever talking money never gets there.
            Assert.That(LinesToJoin(40, new[] { V, V, V, V, V, V, V, V, V, V }), Is.EqualTo(-1));
            // Nor does endless small talk.
            Assert.That(LinesToJoin(40, new[] { F, F, F, F, F, F, F, F, F, F }), Is.EqualTo(-1));
        }

        [Test]
        public void Temperament_AddsALittleForTheRightKindOfLine()
        {
            Assert.That(RecruitScoring.TemperamentBonus(V, "INFP", "정말 대단한 실력이네요."), Is.EqualTo(RecruitScoring.TemperamentPoints), "F likes praise");
            Assert.That(RecruitScoring.TemperamentBonus(V, "INTJ", "보수는 한 건에 120골드예요."), Is.EqualTo(RecruitScoring.TemperamentPoints), "T likes concrete terms");
            Assert.That(RecruitScoring.TemperamentBonus(V, "ESTP", new string('가', 45)), Is.EqualTo(RecruitScoring.TemperamentPoints), "E likes a real conversation");
            Assert.That(RecruitScoring.TemperamentBonus(V, "ISTJ", "좋아요."), Is.Zero);
            Assert.That(RecruitScoring.TemperamentBonus(RecruitReaction.Clash, "INFP", "정말 대단하네요."), Is.Zero, "no bonus on a bad line");
        }

        [Test]
        public void Pacing_EasyNormalHard()
        {
            // Targets agreed 2026-09-30: easy about 3-4 good lines, normal 5-6, hard 7-9 (no temperament bonus).
            Assert.That(LinesToJoin(55, GoodTalk), Is.InRange(3, 4), "easy (e.g. loyal)");
            Assert.That(LinesToJoin(RecruitmentSession.StartAffinity, GoodTalk), Is.InRange(5, 6), "normal");
            Assert.That(LinesToJoin(RecruitmentSession.HardestStart, GoodTalk), Is.InRange(7, 9), "hard (arrogant, greedy)");
        }

        [Test]
        public void Feedback_ShowsDirectionNotNumbers()
        {
            StringAssert.StartsWith("▲", RecruitScoring.Feedback(V, 8, false));
            StringAssert.StartsWith("▼", RecruitScoring.Feedback(RecruitReaction.Clash, -5, false));
            StringAssert.StartsWith("―", RecruitScoring.Feedback(V, 0, false), "a repeated pitch falls flat");
            StringAssert.Contains("조르", RecruitScoring.Feedback(F, -4, true));
            Assert.That(RecruitScoring.Feedback(V, 8, false), Does.Not.Match(@"\d"));
        }

        [Test]
        public void RuleBased_ClassifiesTheLine()
        {
            Assert.That(RuleBasedRecruiter.Classify("보상은 넉넉하게 드릴게요.", "돈"), Is.EqualTo(V));
            Assert.That(RuleBasedRecruiter.Classify("혼자 자유롭게 다녀도 돼요.", "명예", new[] { "loner" }), Is.EqualTo(T));
            Assert.That(RuleBasedRecruiter.Classify("위험한 일이 많으니 각오해요.", "안전"), Is.EqualTo(RecruitReaction.Clash));
            Assert.That(RuleBasedRecruiter.Classify("ㅇㅇ", "돈"), Is.EqualTo(RecruitReaction.LowEffort));
            Assert.That(RuleBasedRecruiter.Classify("넌 한심해 보이는데.", "돈"), Is.EqualTo(RecruitReaction.Rude));
            Assert.That(RuleBasedRecruiter.Classify("우리 길드는 광장 옆에 있어요.", "돈"), Is.EqualTo(F));
        }

        [Test]
        public void Reconcile_RulesOverruleTheModelWhereTheyAreSure()
        {
            // live test 2026-10-01: kanana called "ㅇㅇ" a trait match
            Assert.That(RecruitScoring.Reconcile(T, RecruitReaction.LowEffort), Is.EqualTo(RecruitReaction.LowEffort));
            Assert.That(RecruitScoring.Reconcile(V, RecruitReaction.Rude), Is.EqualTo(RecruitReaction.Rude));
            Assert.That(RecruitScoring.Reconcile(A, RecruitReaction.Clash), Is.EqualTo(RecruitReaction.Clash));
            // naming the value is at least a value match, unless the model heard it as negative
            Assert.That(RecruitScoring.Reconcile(A, V), Is.EqualTo(V));
            Assert.That(RecruitScoring.Reconcile(RecruitReaction.Clash, V), Is.EqualTo(RecruitReaction.Clash));
            // otherwise the model decides (paraphrases)
            Assert.That(RecruitScoring.Reconcile(V, F), Is.EqualTo(V));
            Assert.That(RecruitScoring.Reconcile(RecruitReaction.Clash, F), Is.EqualTo(RecruitReaction.Clash));
        }
    }
}
