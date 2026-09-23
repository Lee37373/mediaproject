using GuildMaster.Core;
using GuildMaster.Notifications;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>The notification list (F-1): posting, merging repeats, reading, limits, and the wording.</summary>
    public class NotificationTests
    {
        NotificationCenter _center;
        float _hour;
        int _posted;
        int _merged;
        int _changed;

        [SetUp]
        public void SetUp()
        {
            EventBus.Clear();
            _hour = 9f;
            _center = new NotificationCenter(() => _hour, () => $"1일 {(int)_hour:00}:00");
            _posted = _merged = _changed = 0;
            EventBus.Subscribe<NotificationPostedEvent>(e => { if (e.Merged) _merged++; else _posted++; });
            EventBus.Subscribe<NotificationsChangedEvent>(_ => _changed++);
        }

        [TearDown]
        public void TearDown() => EventBus.Clear();

        [Test]
        public void Post_AddsAnUnreadEntry_WithTimeAndPublishesAnEvent()
        {
            var n = _center.Post(NotificationKind.Info, "제목", "본문");
            Assert.That(_center.Entries, Has.Count.EqualTo(1));
            Assert.That(n.Read, Is.False);
            Assert.That(n.Count, Is.EqualTo(1));
            Assert.That(n.Hour, Is.EqualTo(9f));
            Assert.That(n.TimeLabel, Is.EqualTo("1일 09:00"));
            Assert.That(_center.Unread, Is.EqualTo(1));
            Assert.That(_posted, Is.EqualTo(1));
        }

        [Test]
        public void Post_WithoutClockDelegates_StillWorks()
        {
            var bare = new NotificationCenter();
            var n = bare.Post(NotificationKind.Info, "a", "b");
            Assert.That(n.TimeLabel, Is.EqualTo(""));
            Assert.That(n.Hour, Is.EqualTo(0f));
        }

        [Test]
        public void Post_SameKeyWhileUnread_MergesIntoOneEntry()
        {
            var first = _center.Post(NotificationKind.Success, "레벨 업", "A가 Lv.2", "levelup:A");
            _hour = 11f;
            var again = _center.Post(NotificationKind.Success, "레벨 업", "A가 Lv.3", "levelup:A");

            Assert.That(again, Is.SameAs(first));
            Assert.That(_center.Entries, Has.Count.EqualTo(1));
            Assert.That(again.Count, Is.EqualTo(2));
            Assert.That(again.Text, Is.EqualTo("A가 Lv.3"), "latest wording");
            Assert.That(again.Hour, Is.EqualTo(11f), "latest time");
            Assert.That(_posted, Is.EqualTo(1));
            Assert.That(_merged, Is.EqualTo(1));
        }

        [Test]
        public void Post_MergeKeepsTheMoreUrgentKind_AndMovesTheEntryToTheEnd()
        {
            var a = _center.Post(NotificationKind.Info, "a", "1", "k");
            var b = _center.Post(NotificationKind.Info, "b", "2", "other");
            _center.Post(NotificationKind.Danger, "a", "3", "k");

            Assert.That(a.Kind, Is.EqualTo(NotificationKind.Danger));
            Assert.That(_center.Entries[0], Is.SameAs(b));
            Assert.That(_center.Entries[1], Is.SameAs(a), "merged entry is the latest news");
        }

        [Test]
        public void Post_SameKeyAfterReading_StartsAFreshEntry()
        {
            var first = _center.Post(NotificationKind.Info, "a", "1", "k");
            _center.MarkRead(first.Id);
            var second = _center.Post(NotificationKind.Info, "a", "2", "k");

            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(_center.Entries, Has.Count.EqualTo(2));
            Assert.That(_center.Unread, Is.EqualTo(1));
        }

        [Test]
        public void Post_WithoutKey_NeverMerges()
        {
            _center.Post(NotificationKind.Info, "a", "1");
            _center.Post(NotificationKind.Info, "a", "1");
            Assert.That(_center.Entries, Has.Count.EqualTo(2));
        }

        [Test]
        public void Post_MergeKeepsTheLink_UnlessANewOneComes()
        {
            var n = _center.Post(NotificationKind.Info, "a", "1", "k", NotificationLink.Roster());
            _center.Post(NotificationKind.Info, "a", "2", "k");
            Assert.That(n.Link.Target, Is.EqualTo(NotificationTarget.Roster));
            _center.Post(NotificationKind.Info, "a", "3", "k", NotificationLink.Negotiation("Q_1"));
            Assert.That(n.Link.Target, Is.EqualTo(NotificationTarget.Negotiation));
            Assert.That(n.Link.Id, Is.EqualTo("Q_1"));
        }

        [Test]
        public void PostOnce_AnnouncesEachKeyOnlyOnce_EvenAfterReadingOrClearing()
        {
            var first = _center.PostOnce("negotiable:Q_1", NotificationKind.Quest, "협상 가능", "text");
            Assert.That(first, Is.Not.Null);
            Assert.That(_center.PostOnce("negotiable:Q_1", NotificationKind.Quest, "협상 가능", "text"), Is.Null);

            _center.MarkRead(first.Id);
            Assert.That(_center.PostOnce("negotiable:Q_1", NotificationKind.Quest, "협상 가능", "text"), Is.Null);
            _center.Clear();
            Assert.That(_center.PostOnce("negotiable:Q_1", NotificationKind.Quest, "협상 가능", "text"), Is.Null);

            Assert.That(_center.PostOnce("negotiable:Q_2", NotificationKind.Quest, "협상 가능", "text"), Is.Not.Null, "another subject");
        }

        [Test]
        public void PostOnce_NeedsAKey()
        {
            Assert.Throws<System.ArgumentException>(() => _center.PostOnce(null, NotificationKind.Info, "a", "b"));
            Assert.Throws<System.ArgumentException>(() => _center.PostOnce("", NotificationKind.Info, "a", "b"));
        }

        [Test]
        public void MarkRead_LowersUnread_AndOnlyPublishesWhenSomethingChanged()
        {
            var a = _center.Post(NotificationKind.Info, "a", "1");
            _center.Post(NotificationKind.Info, "b", "2");
            _center.MarkRead(a.Id);
            Assert.That(_center.Unread, Is.EqualTo(1));
            Assert.That(_changed, Is.EqualTo(1));

            _center.MarkRead(a.Id);
            _center.MarkRead(999);
            Assert.That(_changed, Is.EqualTo(1), "no change, no event");
        }

        [Test]
        public void MarkAllRead_ClearsTheBadge()
        {
            _center.Post(NotificationKind.Info, "a", "1");
            _center.Post(NotificationKind.Info, "b", "2");
            _center.MarkAllRead();
            Assert.That(_center.Unread, Is.EqualTo(0));
            Assert.That(_center.Entries, Has.Count.EqualTo(2), "still listed");
            int before = _changed;
            _center.MarkAllRead();
            Assert.That(_changed, Is.EqualTo(before));
        }

        [Test]
        public void Resolve_MarksEveryUnreadEntryOfTheKeyRead()
        {
            var a = _center.Post(NotificationKind.Quest, "협상 가능", "1", "negotiable:Q_1");
            var b = _center.Post(NotificationKind.Quest, "협상 가능", "2", "negotiable:Q_2");
            _center.Resolve("negotiable:Q_1");
            Assert.That(a.Read, Is.True);
            Assert.That(b.Read, Is.False);
            _center.Resolve(null);
            _center.Resolve("nothing");
            Assert.That(_center.Unread, Is.EqualTo(1));
        }

        [Test]
        public void Clear_EmptiesTheList()
        {
            _center.Post(NotificationKind.Info, "a", "1");
            _center.Clear();
            Assert.That(_center.Entries, Is.Empty);
            Assert.That(_center.Unread, Is.EqualTo(0));
            int before = _changed;
            _center.Clear();
            Assert.That(_changed, Is.EqualTo(before), "already empty");
        }

        [Test]
        public void List_KeepsOnlyTheNewestEntries()
        {
            for (int i = 0; i < NotificationCenter.MaxEntries + 5; i++)
                _center.Post(NotificationKind.Info, "n" + i, "text");

            Assert.That(_center.Entries, Has.Count.EqualTo(NotificationCenter.MaxEntries));
            Assert.That(_center.Entries[0].Title, Is.EqualTo("n5"), "the five oldest were dropped");
            Assert.That(_center.Entries[NotificationCenter.MaxEntries - 1].Title, Is.EqualTo("n" + (NotificationCenter.MaxEntries + 4)));
            Assert.That(_changed, Is.GreaterThan(0), "trimming is announced");
        }

        [Test]
        public void Toast_FlagIsKept_AndAMergeCanTurnItOn()
        {
            var quiet = _center.Post(NotificationKind.Info, "a", "1", "k", default, toast: false);
            Assert.That(quiet.Toast, Is.False);
            _center.Post(NotificationKind.Info, "a", "2", "k", default, toast: true);
            Assert.That(quiet.Toast, Is.True);
        }

        [Test]
        public void Ids_AreUniqueAndIncreasing()
        {
            var a = _center.Post(NotificationKind.Info, "a", "1");
            var b = _center.Post(NotificationKind.Info, "b", "2");
            Assert.That(b.Id, Is.GreaterThan(a.Id));
        }

        [Test]
        public void Links_CarryTheirTarget()
        {
            Assert.That(NotificationLink.None.IsNone, Is.True);
            Assert.That(NotificationLink.QuestBoard().Target, Is.EqualTo(NotificationTarget.QuestBoard));
            Assert.That(NotificationLink.Negotiation("Q_7").Id, Is.EqualTo("Q_7"));
            Assert.That(NotificationLink.Adventurer("adv_1").Target, Is.EqualTo(NotificationTarget.Adventurer));
            var loc = NotificationLink.Location(new Vector2(3f, 4f));
            Assert.That(loc.Target, Is.EqualTo(NotificationTarget.Location));
            Assert.That(loc.Position, Is.EqualTo(new Vector2(3f, 4f)));
            Assert.That(loc.IsNone, Is.False);
        }

        [Test]
        public void Texts_NameTheirSubject()
        {
            Assert.That(NotificationTexts.Negotiable("슬라임 토벌", 7), Does.Contain("슬라임 토벌").And.Contain("7시간"));
            Assert.That(NotificationTexts.Negotiable("x", -3), Does.Contain("0시간"), "never negative");
            Assert.That(NotificationTexts.Died("노아", "고블린"), Does.Contain("노아").And.Contain("고블린"));
            Assert.That(NotificationTexts.Died("노아", null), Does.Contain("노아").And.Not.Contain("에게"));
            Assert.That(NotificationTexts.Crisis("미라", 18), Does.Contain("18%"));
            Assert.That(NotificationTexts.LevelUp("토르", 4), Does.Contain("Lv.4"));
            Assert.That(NotificationTexts.Visitor("라일", "전사", 3), Does.Contain("라일").And.Contain("전사").And.Contain("Lv.3"));
            Assert.That(NotificationTexts.RaidSighted("북"), Does.Contain("북쪽"));
            Assert.That(NotificationTexts.RaidEnded(5, true), Does.Contain("물러갔"));
            Assert.That(NotificationTexts.RaidEnded(5, false), Does.Contain("막아냈"));
            Assert.That(NotificationTexts.BuildingDestroyed("대장간"), Does.Contain("대장간"));
            Assert.That(NotificationTexts.MerchantArrived(0), Does.Contain("1시간"), "at least one hour");
            Assert.That(NotificationTexts.QuestDone("q"), Does.Contain("달성"));
            Assert.That(NotificationTexts.QuestFailed("q"), Does.Contain("실패"));
        }
    }
}
