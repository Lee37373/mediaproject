using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Guide;
using GuildMaster.Incidents;
using GuildMaster.Social;
using GuildMaster.Welfare;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Feelings and the relationship graph (F-10), event cards (F-13) and the "다음 할 일" guide (F-19),
    /// user request 2026-10-11.
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

        // ---------------- F-13 event cards ----------------

        static CardMember M(string id, int level = 3, float fatigue = 10f, int gear = 0, bool free = true, bool canGear = true)
            => new() { Id = id, Name = id, Level = level, Fatigue = fatigue, GearTier = gear, Free = free, CanGearUp = canGear };

        static CardContext Ctx(params CardMember[] members) => new()
        {
            Members = members.ToList(), Gold = 500, Day = 3, HasResidents = true, HasVisitorRoom = true,
        };

        static System.Func<float> Fixed(float v) => () => v;

        [Test]
        public void Cards_ComeAtTheirHours_AtMostTwiceADay_NeverInARaid()
        {
            Assert.That(EventCards.ShouldDraw(2, 10, 0, false, false, 0.1f), Is.True);
            Assert.That(EventCards.ShouldDraw(2, 11, 0, false, false, 0.1f), Is.False, "not a card hour");
            Assert.That(EventCards.ShouldDraw(2, 16, EventCards.MaxPerDay, false, false, 0.1f), Is.False);
            Assert.That(EventCards.ShouldDraw(2, 16, 0, true, false, 0.1f), Is.False, "one is waiting");
            Assert.That(EventCards.ShouldDraw(2, 16, 0, false, true, 0.1f), Is.False, "raid");
            Assert.That(EventCards.ShouldDraw(2, 16, 0, false, false, 0.9f), Is.False, "the roll");
            Assert.That(EventCards.ShouldDraw(1, 10, 0, false, false, 0.1f), Is.False, "quiet first morning");
            Assert.That(EventCards.ShouldDraw(1, 16, 0, false, false, 0.1f), Is.True);
        }

        [Test]
        public void EveryCard_HasTwoOrThreeChoices_AndOneDefault()
        {
            Assert.That(EventCards.All.Count, Is.GreaterThanOrEqualTo(8));
            Assert.That(EventCards.All.Select(c => c.Id).Distinct().Count(), Is.EqualTo(EventCards.All.Count));
            foreach (var card in EventCards.All)
            {
                Assert.That(card.Choices.Count, Is.InRange(2, 3), card.Id);
                Assert.That(card.Choices.Count(c => c.Default), Is.EqualTo(1), card.Id);
                Assert.That(card.DefaultChoice.Cost, Is.EqualTo(0), card.Id + ": nobody pays for an unanswered card");
                foreach (var c in card.Choices)
                {
                    Assert.That(c.Success?.Result, Is.Not.Null.And.Not.Empty, card.Id + "/" + c.Label);
                    if (c.Failure != null) Assert.That(c.Failure.Result, Is.Not.Empty, card.Id + "/" + c.Label);
                }
            }
        }

        [Test]
        public void ADuel_NeedsTwoWhoDoNotGetAlong()
        {
            var duel = EventCards.All.First(c => c.Id == "duel");
            var ctx = Ctx(M("a"), M("b"), M("c"));
            ctx.Bond = (x, y) => (x, y) is ("a", "b") or ("b", "a") ? -30 : 10;
            Assert.That(EventCards.TryCast(duel, ctx, Fixed(0f), out var a, out var b), Is.True);
            Assert.That(new[] { a.Value.Id, b.Value.Id }, Is.EquivalentTo(new[] { "a", "b" }));
            ctx.Bond = (_, _) => 0;
            Assert.That(EventCards.TryCast(duel, ctx, Fixed(0f), out _, out _), Is.False, "nobody is at odds");
            Assert.That(EventCards.DuelChance(5, 3), Is.GreaterThan(EventCards.DuelChance(3, 3)));
            Assert.That(EventCards.DuelChance(1, 20), Is.EqualTo(0.2f));
        }

        [Test]
        public void TheShadyMerchant_PicksWhoCanUseBetterGear()
        {
            var shady = EventCards.All.First(c => c.Id == "shady");
            var ctx = Ctx(M("rich", gear: 2), M("poor", gear: 0), M("maxed", gear: 0, canGear: false));
            Assert.That(EventCards.TryCast(shady, ctx, Fixed(0f), out var a, out _), Is.True);
            Assert.That(a.Value.Id, Is.EqualTo("poor"));
            ctx.Gold = 10;
            Assert.That(EventCards.TryCast(shady, ctx, Fixed(0f), out _, out _), Is.False, "too poor to be offered");
        }

        [Test]
        public void Cards_NeedTheirTown_AndAreNotRepeatedSoon()
        {
            var plague = EventCards.All.First(c => c.Id == "plague");
            var ctx = Ctx(M("a"));
            ctx.HasResidents = false;
            Assert.That(EventCards.TryCast(plague, ctx, Fixed(0f), out _, out _), Is.False);
            ctx.HasResidents = true;
            ctx.Recent = EventCards.All.Where(c => c.Id != "noble").Select(c => c.Id).ToList();
            var drawn = EventCards.Draw(ctx, Fixed(0.3f));
            Assert.That(drawn, Is.Not.Null);
            Assert.That(drawn.Def.Id, Is.EqualTo("noble"));
            Assert.That(drawn.Narration, Does.Not.Contain("{"));
        }

        [Test]
        public void Choices_SucceedByTheirOdds()
        {
            var thief = EventCards.All.First(c => c.Id == "thief");
            var chase = thief.Choices[0];
            Assert.That(EventCards.Resolve(chase, M("a"), null, 0.1f).success, Is.True);
            var (ok, fx) = EventCards.Resolve(chase, M("a"), null, 0.95f);
            Assert.That(ok, Is.False);
            Assert.That(fx.Gold, Is.LessThan(0), "the thief got away with it");
            Assert.That(EventCards.Resolve(thief.DefaultChoice, M("a"), null, 0.99f).success, Is.True, "no odds: always the one result");
            Assert.That(EventCards.Hint(chase, M("a"), null), Does.Contain("%"));
            Assert.That(EventCards.CanAfford(EventCards.All.First(c => c.Id == "festival").Choices[0], 10), Is.False);
        }

        [Test]
        public void CardText_FillsNamesAndParticles()
        {
            Assert.That(EventCards.Format("{A이} {B을} 꺾었다", "카엘", "미라"), Is.EqualTo("카엘이 미라를 꺾었다"));
            Assert.That(EventCards.Format("{A와} {B은}", "토비", "한스"), Is.EqualTo("토비와 한스는"));
            Assert.That(EventCards.Format("{A}의 생일", "엘리", null), Is.EqualTo("엘리의 생일"));
        }

        // ---------------- F-19 guide ----------------

        [Test]
        public void Guide_StepsInOrder()
        {
            var done = new HashSet<GuideStep> { GuideStep.PostQuest };
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
            Assert.That(hints[0].Action, Is.EqualTo(HintAction.EventCard));
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
