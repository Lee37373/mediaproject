using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Guide;
using GuildMaster.Social;
using GuildMaster.Welfare;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Feelings and the relationship graph (F-10) and the "다음 할 일" guide (F-19), user request 2026-10-11. The event cards
    /// (F-13) are decision cards since P4-118: see DilemmaTests.
    /// </summary>
    public class LivelyGuildTests
    {
        // ---------------- F-10 feelings ----------------

        [Test]
        public void Feelings_FadeAfterTheirHours_AndTheStrongestShows()
        {
            var book = new EmotionBook();
            book.Feel("a", Emotion.Excited, 0f, 5f, "의뢰 성공");
            Assert.That(book.Current("a", 1f, 0f, 0f, false, out var why), Is.EqualTo(Emotion.Excited));
            Assert.That(why, Is.EqualTo("의뢰 성공"));
            book.Feel("a", Emotion.Sad, 2f, 24f, "친구를 잃음");
            Assert.That(book.Current("a", 3f, 0f, 0f, false, out _), Is.EqualTo(Emotion.Sad), "grief outweighs a success");
            Assert.That(book.Current("a", 30f, 0f, 0f, false, out _), Is.EqualTo(Emotion.None), "both have faded");
        }

        [Test]
        public void StressAndTiredness_ShowWhenNothingStrongerHappened()
        {
            var book = new EmotionBook();
            Assert.That(book.Current("a", 0f, StressRules.StrainedAt, 0f, false, out _), Is.EqualTo(Emotion.Strained));
            Assert.That(book.Current("a", 0f, 10f, EmotionRules.SleepyFatigue, false, out _), Is.EqualTo(Emotion.Sleepy));
            Assert.That(book.Current("a", 0f, 10f, 0f, true, out _), Is.EqualTo(Emotion.Sleepy), "asleep");
            Assert.That(book.Current("a", 0f, 10f, 0f, false, out _), Is.EqualTo(Emotion.None));
            book.Feel("a", Emotion.Happy, 0f, 3f, "대화");
            Assert.That(book.Current("a", 1f, 10f, 90f, false, out _), Is.EqualTo(Emotion.Happy), "happy beats sleepy");
            book.Feel("a", Emotion.Angry, 0f, 8f, "다툼");
            Assert.That(book.Current("a", 1f, StressRules.StrainedAt, 0f, false, out _), Is.EqualTo(Emotion.Angry), "anger beats strain");
        }

        [Test]
        public void AFight_MakesBothAngry_MakingUpCalmsIt()
        {
            Assert.That(EmotionRules.AfterTalk(TownTalkKind.Quarrel, TalkOutcome.Fight, 0).kind, Is.EqualTo(Emotion.Angry));
            Assert.That(EmotionRules.AfterTalk(TownTalkKind.Quarrel, TalkOutcome.Fight, 1).kind, Is.EqualTo(Emotion.Angry));
            Assert.That(EmotionRules.AfterTalk(TownTalkKind.Comfort, TalkOutcome.Warm, 1).kind, Is.EqualTo(Emotion.Happy));
            Assert.That(EmotionRules.AfterTalk(TownTalkKind.Chat, TalkOutcome.Neutral, 0).hours, Is.EqualTo(0f));
            var book = new EmotionBook();
            book.Feel("a", Emotion.Angry, 0f, 8f, "다툼");
            book.Calm("a", Emotion.Angry);
            Assert.That(book.Current("a", 1f, 0f, 0f, false, out _), Is.EqualTo(Emotion.None));
        }

        [Test]
        public void Grief_DependsOnHowCloseTheyWere()
        {
            Assert.That(EmotionRules.GriefFor(-20), Is.EqualTo(0f));
            Assert.That(EmotionRules.GriefFor(StressRules.FriendRelation), Is.EqualTo(EmotionRules.GriefHours));
            Assert.That(EmotionRules.GriefFor(EmotionRules.CloseFriend), Is.EqualTo(EmotionRules.DeepGriefHours));
        }

        [Test]
        public void RecentRelationshipChanges_AreRemembered_ForAWhile()
        {
            var book = new EmotionBook();
            book.MarkChanged("b", "a", 10f, -5);
            Assert.That(book.ChangedRecently("a", "b", 12f, EmotionRules.RecentChangeHours, out int sign), Is.True, "either order");
            Assert.That(sign, Is.EqualTo(-1));
            Assert.That(book.ChangedRecently("a", "b", 20f, EmotionRules.RecentChangeHours, out _), Is.False);
            book.MarkChanged("a", "c", 10f, 3);
            book.Forget("a");
            Assert.That(book.ChangedRecently("a", "c", 11f, 6f, out _), Is.False);
        }

        [Test]
        public void Graph_AveragesWhatExists_AndNeverSeeds()
        {
            var a = new AdventurerData { id = "a", relations = new List<RelationEntry> { new() { id = "b", value = 40 } } };
            Assert.That(RelationGraph.Peek(a, "b"), Is.EqualTo(40));
            Assert.That(RelationGraph.Peek(a, "c"), Is.Null);
            Assert.That(a.relations.Count, Is.EqualTo(1), "reading adds nothing");
            Assert.That(RelationGraph.Bond(40, 10), Is.EqualTo(25));
            Assert.That(RelationGraph.Bond(-30, null), Is.EqualTo(-30));
            Assert.That(RelationGraph.Bond(null, null), Is.EqualTo(0));
            Assert.That(RelationGraph.Shown(4), Is.False);
            Assert.That(RelationGraph.Thickness(100), Is.GreaterThan(RelationGraph.Thickness(10)));
            var resident = ResidentProfile.Defaults()[0];
            Assert.That(RelationGraph.Peek(resident, "a"), Is.Null);
            resident.AddFeeling("a", 12);
            Assert.That(RelationGraph.Peek(resident, "a"), Is.EqualTo(12));
        }

        // ---------------- F-19 guide ----------------

        [Test]
        public void Guide_StepsInOrder()
        {
            var done = new HashSet<GuideStep> { GuideStep.Board };
            Assert.That(GuideRules.Next(done.Contains), Is.EqualTo(GuideStep.TalkVisitor));
            foreach (var s in GuideRules.Order) done.Add(s);
            Assert.That(GuideRules.Next(done.Contains), Is.Null);
            foreach (var s in GuideRules.Order)
            {
                Assert.That(GuideRules.Title(s), Is.Not.Empty);
                Assert.That(GuideRules.HowTo(s), Is.Not.Empty);
            }
        }

        [Test]
        public void Guide_PointsAtWhatNeedsTheGuildMaster_MostUrgentFirst()
        {
            var s = new GuideSnapshot { Gold = 500, OpenQuests = 0, IdleMembers = 2, PendingCard = "결투 신청" };
            s.Members.Add(new GuideSnapshot.Member { Id = "m1", Name = "카엘", Stress = 70f });
            s.Members.Add(new GuideSnapshot.Member { Id = "m2", Name = "미라", Stress = 10f });
            s.Visitors.Add(new GuideSnapshot.Visitor { Id = "v1", Name = "로웬", Affinity = 60 });
            s.Visitors.Add(new GuideSnapshot.Visitor { Id = "v2", Name = "베일", Affinity = -1 });
            var hints = GuideRules.Hints(s);
            Assert.That(hints.Count, Is.EqualTo(GuideRules.MaxHints));
            Assert.That(hints[0].Action, Is.EqualTo(HintAction.Decision));
            Assert.That(hints.Any(h => h.Action == HintAction.Counsel && h.TargetId == "m1"), Is.True);
            Assert.That(hints.Any(h => h.TargetId == "m2"), Is.False, "calm members need nothing");
            Assert.That(hints.Any(h => h.Action == HintAction.Visitor && h.TargetId == "v1"), Is.True);
            Assert.That(hints.Any(h => h.TargetId == "v2"), Is.False, "never talked to: no sign of interest");
            Assert.That(hints.Any(h => h.Action == HintAction.QuestBoard), Is.True);
            Assert.That(hints.Select(h => h.Urgency), Is.Ordered.Descending);
        }

        [Test]
        public void Guide_ADemand_ComesBeforeMereStress_AndAFullRosterNeedsNoVisitors()
        {
            var s = new GuideSnapshot { Gold = 20, OpenQuests = 3, RosterFull = true };
            s.Members.Add(new GuideSnapshot.Member { Id = "m1", Name = "카엘", Stress = 65f, Demand = CareDemand.Leave });
            s.Visitors.Add(new GuideSnapshot.Visitor { Id = "v1", Name = "로웬", Affinity = 70 });
            var hints = GuideRules.Hints(s);
            Assert.That(hints[0].Text, Does.Contain("휴가"));
            Assert.That(hints.Any(h => h.Action == HintAction.Visitor), Is.False);
            Assert.That(hints.Last().Text, Does.Contain("20G"), "low on gold");
        }
    }
}
