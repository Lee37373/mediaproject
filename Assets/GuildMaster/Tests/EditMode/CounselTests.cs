using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Welfare;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Counselling by conversation (user request 2026-10-01): the relief table, reading a line, the worry, when a talk ends.</summary>
    public class CounselTests
    {
        const CounselReaction E = CounselReaction.Empathy, L = CounselReaction.Listen, S = CounselReaction.Solution, P = CounselReaction.Praise;

        [Test]
        public void Table_KindWordsHelp_HarshWordsHurt()
        {
            Assert.That(CounselScoring.Base(S), Is.GreaterThan(CounselScoring.Base(E)));
            Assert.That(CounselScoring.Base(E), Is.GreaterThan(CounselScoring.Base(L)));
            Assert.That(CounselScoring.Base(CounselReaction.Small), Is.GreaterThan(0));
            Assert.That(CounselScoring.Base(CounselReaction.LowEffort), Is.LessThan(0));
            Assert.That(CounselScoring.Base(CounselReaction.Dismiss), Is.LessThan(CounselScoring.Base(CounselReaction.LowEffort)));
            Assert.That(CounselScoring.Base(CounselReaction.Rude), Is.LessThan(CounselScoring.Base(CounselReaction.Dismiss)));
            for (int i = 0; i < CounselScoring.Codes.Length; i++)
                Assert.That(CounselScoring.Parse(CounselScoring.Codes[i]), Is.EqualTo((CounselReaction)i));
            Assert.That(CounselScoring.Parse("???"), Is.EqualTo(CounselReaction.Small));
        }

        [Test]
        public void SameComfortAgain_IsWorthLess_AndHurtAlwaysCounts()
        {
            Assert.That(CounselScoring.Score(E, 0, "ISTJ", false, false), Is.EqualTo(CounselScoring.EmpathyPoints));
            Assert.That(CounselScoring.Score(E, 1, "ISTJ", false, false), Is.EqualTo(CounselScoring.EmpathyPoints / 2));
            Assert.That(CounselScoring.Score(E, 2, "ISTJ", false, false), Is.EqualTo(0));
            Assert.That(CounselScoring.Score(CounselReaction.Dismiss, 3, "ISTJ", false, false), Is.EqualTo(CounselScoring.DismissPoints));
        }

        [Test]
        public void TheRealWorry_TemperamentAndAnAskedForTalk_AddUp()
        {
            int plain = CounselScoring.Score(E, 0, "ISTJ", false, false);
            Assert.That(CounselScoring.Score(E, 0, "ISTJ", true, false), Is.EqualTo(plain + CounselScoring.TouchedBonus));
            Assert.That(CounselScoring.Score(E, 0, "ISFJ", false, false), Is.EqualTo(plain + CounselScoring.TemperamentPoints), "F takes comfort better");
            Assert.That(CounselScoring.Score(S, 0, "ENTP", false, false), Is.EqualTo(CounselScoring.SolutionPoints + CounselScoring.TemperamentPoints), "T likes concrete help");
            Assert.That(CounselScoring.Score(E, 0, "ISTJ", false, true), Is.GreaterThan(plain), "asked-for talk");
            Assert.That(CounselScoring.Score(CounselReaction.Rude, 0, "ISFJ", true, true), Is.EqualTo(CounselScoring.RudePoints), "no bonus on a hurtful line");
        }

        [Test]
        public void Caps_KeepOneTalkInBounds()
        {
            Assert.That(CounselScoring.Cap(10, CounselScoring.MaxRelief - 4, 0), Is.EqualTo(4));
            Assert.That(CounselScoring.Cap(10, CounselScoring.MaxRelief, 0), Is.EqualTo(0));
            Assert.That(CounselScoring.Cap(-12, 0, CounselScoring.MaxHarm - 5), Is.EqualTo(-5));
            Assert.That(CounselScoring.Cap(-3, 0, 0), Is.EqualTo(-3));
        }

        /// <summary>Pacing: a caring talk settles a member in about 4-5 lines; empty talk never does.</summary>
        [Test]
        public void Pacing_CaringTalkSettlesInFourOrFiveLines()
        {
            int Lines(CounselReaction[] talk, bool touchedFromThird)
            {
                int relief = 0, harm = 0, streak = 0;
                CounselReaction? last = null;
                for (int i = 0; i < talk.Length; i++)
                {
                    streak = last == talk[i] ? streak + 1 : 0;
                    last = talk[i];
                    int d = CounselScoring.Cap(CounselScoring.Score(talk[i], streak, "ISTJ", touchedFromThird && i >= 2, false), relief, harm);
                    if (d > 0) relief += d; else harm -= d;
                    if (CounselSession.Judge(relief, harm, 0, i + 1, 70f) == CounselOutcome.Relieved) return i + 1;
                }
                return -1;
            }
            Assert.That(Lines(new[] { L, E, E, S, P, L, E, S }, true), Is.InRange(4, 5), "ask, sympathise, get to it, help");
            Assert.That(Lines(new[] { E, E, E, E, E, E, E, E }, false), Is.EqualTo(-1), "the same comfort over and over");
            Assert.That(Lines(new[] { CounselReaction.Small, CounselReaction.Small, CounselReaction.Small, CounselReaction.Small }, false), Is.EqualTo(-1));
        }

        [Test]
        public void Judge_WalkoutReliefAndTheEnd()
        {
            Assert.That(CounselSession.Judge(0, 0, CounselScoring.WalkoutNegatives, 3, 70f), Is.EqualTo(CounselOutcome.Walkout));
            Assert.That(CounselSession.Judge(0, CounselScoring.MaxHarm, 1, 2, 70f), Is.EqualTo(CounselOutcome.Walkout));
            Assert.That(CounselSession.Judge(CounselScoring.ReliefGoal, 0, 0, 4, 40f), Is.EqualTo(CounselOutcome.Relieved));
            Assert.That(CounselSession.Judge(6, 0, 0, 2, 10f), Is.EqualTo(CounselOutcome.Relieved), "nothing left to get off their chest");
            Assert.That(CounselSession.Judge(5, 0, 0, CounselScoring.MaxTurns, 60f), Is.EqualTo(CounselOutcome.Ended));
            Assert.That(CounselSession.Judge(5, 0, 0, 2, 60f), Is.EqualTo(CounselOutcome.Ongoing));
        }

        [Test]
        public void RuleBased_ReadsTheLine()
        {
            Assert.That(CounselScoring.Classify("많이 힘들었지? 고생했어."), Is.EqualTo(E));
            Assert.That(CounselScoring.Classify("무슨 일 있었어?"), Is.EqualTo(L));
            Assert.That(CounselScoring.Classify("며칠 휴가 줄 테니 푹 쉬어."), Is.EqualTo(S));
            Assert.That(CounselScoring.Classify("네 덕분에 다들 살았어."), Is.EqualTo(P));
            Assert.That(CounselScoring.Classify("그 정도로 징징대지 마."), Is.EqualTo(CounselReaction.Dismiss));
            Assert.That(CounselScoring.Classify("한심하네."), Is.EqualTo(CounselReaction.Rude));
            Assert.That(CounselScoring.Classify("ㅇㅇ"), Is.EqualTo(CounselReaction.LowEffort));
            Assert.That(CounselScoring.Classify("오늘 날씨가 좋네요."), Is.EqualTo(CounselReaction.Small));
        }

        [Test]
        public void Reconcile_RulesWinWhereTheyAreSure()
        {
            Assert.That(CounselScoring.Reconcile(E, CounselReaction.LowEffort), Is.EqualTo(CounselReaction.LowEffort));
            Assert.That(CounselScoring.Reconcile(S, CounselReaction.Dismiss), Is.EqualTo(CounselReaction.Dismiss));
            Assert.That(CounselScoring.Reconcile(CounselReaction.Small, S), Is.EqualTo(S));
            Assert.That(CounselScoring.Reconcile(P, CounselReaction.Small), Is.EqualTo(P), "paraphrases are the model's call");
        }

        [Test]
        public void Feedback_ShowsDirectionNotNumbers()
        {
            StringAssert.StartsWith("▲", CounselScoring.Feedback(E, 7, false));
            StringAssert.Contains("진짜 고민", CounselScoring.Feedback(E, 11, true));
            StringAssert.StartsWith("―", CounselScoring.Feedback(E, 0, false));
            StringAssert.StartsWith("▼", CounselScoring.Feedback(CounselReaction.Dismiss, -7, false));
            Assert.That(CounselScoring.Feedback(S, 8, true), Does.Not.Match(@"\d"));
        }

        static CounselInput State(float fatigue = 20f, int grudges = 0, float hp = 1f, int gold = 200, int debt = 0,
                                  string values = "명예", string rival = null, int rivalFeeling = 0, params string[] memories)
            => new()
            {
                Stress = 70f, Fatigue = fatigue, HpRatio = hp, Gold = gold, Debt = debt, Grudges = grudges, Values = values,
                RecentMemories = new List<string>(memories), RivalName = rival, RivalFeeling = rivalFeeling,
            };

        [Test]
        public void Concern_FollowsWhatHappenedMostRecently()
        {
            var grief = CounselConcerns.Detect(State(memories: new[] { "레벨 3이 되었다", "동료 리아가 고블린에게 목숨을 잃었다" }));
            Assert.That(grief.Kind, Is.EqualTo(CounselConcernKind.Grief));
            Assert.That(grief.Subject, Is.EqualTo("리아"));
            Assert.That(grief.Touches("리아 일은 정말 안타까워"), Is.True);

            Assert.That(CounselConcerns.Detect(State(grudges: 1)).Kind, Is.EqualTo(CounselConcernKind.Neglect));
            Assert.That(CounselConcerns.Detect(State(hp: 0.3f)).Kind, Is.EqualTo(CounselConcernKind.Fear));
            Assert.That(CounselConcerns.Detect(State(memories: "'늑대 토벌' 의뢰를 포기했다")).Kind, Is.EqualTo(CounselConcernKind.Failure));
            var conflict = CounselConcerns.Detect(State(rival: "카엘", rivalFeeling: -30));
            Assert.That(conflict.Kind, Is.EqualTo(CounselConcernKind.Conflict));
            Assert.That(conflict.Touches("카엘이랑 무슨 일 있었어?"), Is.True);
            Assert.That(CounselConcerns.Detect(State(fatigue: 75f)).Kind, Is.EqualTo(CounselConcernKind.Overwork));
            Assert.That(CounselConcerns.Detect(State(debt: 20)).Kind, Is.EqualTo(CounselConcernKind.Money));
            Assert.That(CounselConcerns.Detect(State()).Kind, Is.EqualTo(CounselConcernKind.Vague));
        }

        [Test]
        public void Lines_EveryConcernAndOutcomeHasWords()
        {
            var rng = new GameRandom(5);
            foreach (CounselConcernKind k in System.Enum.GetValues(typeof(CounselConcernKind)))
            {
                var c = new CounselConcern { Kind = k, Label = "x", Detail = "x", Subject = "리아", Keywords = new string[0] };
                Assert.That(CounselLines.Opening(c, true, rng), Is.Not.Empty);
                Assert.That(CounselLines.Opening(c, false, rng), Is.Not.Empty);
                Assert.That(CounselLines.Confession(c), Is.Not.Empty);
                foreach (CounselReaction r in System.Enum.GetValues(typeof(CounselReaction)))
                    Assert.That(CounselLines.Reply(r, c, true, true, rng), Is.Not.Empty);
            }
            foreach (CounselOutcome o in System.Enum.GetValues(typeof(CounselOutcome)))
                Assert.That(CounselLines.Closing(o, rng), Is.Not.Empty);
        }

        [Test]
        public void Grammar_AsksForReplyReactionAndTouched()
        {
            StringAssert.Contains("\"reaction", CounselPrompt.Grammar);
            StringAssert.Contains("touched", CounselPrompt.Grammar);
            StringAssert.Contains("dismiss", CounselPrompt.Grammar);
        }
    }
}
