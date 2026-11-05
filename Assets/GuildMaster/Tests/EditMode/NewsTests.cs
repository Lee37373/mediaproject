using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.Reports;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// The town newspaper (P4-130, F-9): which stories make the paper (three whatever their score, up to five newsworthy
    /// ones, two of a kind at most), one story per topic, no close call for someone who died, grouped kinds as one story,
    /// fillers for a thin day, the wording, and the desk's day: made when the ledger closes the day, out at six.
    /// </summary>
    public class NewsTests
    {
        static NewsArticle Story(NewsKind kind, float score, string topic = null, params string[] people)
        {
            var a = new NewsArticle { kind = kind, score = score, headline = kind + " " + score, body = "", topic = topic };
            return a.About(people);
        }

        [Test]
        public void Compose_PrintsTheBest_ThreeAlways_FiveAtMost_AndTwoOfAKind()
        {
            var noted = new List<NewsArticle>
            {
                Story(NewsKind.Merchant, 18), Story(NewsKind.LevelUp, 28), Story(NewsKind.Death, 95, null, "a"),
                Story(NewsKind.Death, 95, null, "b"), Story(NewsKind.Death, 95, null, "c"), Story(NewsKind.Raid, 62),
            };
            var p = NewsRules.Compose(3, noted, null);
            Assert.That(p.day, Is.EqualTo(3));
            Assert.That(p.articles.Select(a => a.kind), Is.EqualTo(new[] { NewsKind.Death, NewsKind.Death, NewsKind.Raid }),
                "two deaths of three, then the raid; the small news stays out past three");
            Assert.That(p.Lead.kind, Is.EqualTo(NewsKind.Death));

            var thin = NewsRules.Compose(3, new[] { Story(NewsKind.Merchant, 18), Story(NewsKind.Discovery, 22) }, null);
            Assert.That(thin.articles.Count, Is.EqualTo(2), "small news makes a thin day's paper");
            Assert.That(thin.Lead.kind, Is.EqualTo(NewsKind.Discovery));

            var busy = Enumerable.Range(0, 12).Select(i => Story((NewsKind)i, 50 + i)).ToList();
            Assert.That(NewsRules.Compose(3, busy, null).articles.Count, Is.EqualTo(NewsRules.MaxArticles));
        }

        [Test]
        public void Compose_TellsATopicOnce_TheBetterStory_AndDropsTheCloseCallsOfTheDead()
        {
            var noted = new List<NewsArticle>
            {
                Story(NewsKind.BigFight, 58, "dragon", "a"),
                Story(NewsKind.DragonSlain, 105, "dragon", "b"),
                Story(NewsKind.LowHp, 40, "close:c", "c"),
                Story(NewsKind.Death, 95, "close:c", "c"),
                Story(NewsKind.Wipe, 60, "wipe:x", "c", "d"),
                Story(NewsKind.LastStand, 75, "close:e", "e"),
            };
            var p = NewsRules.Compose(1, noted, null);
            Assert.That(p.articles.Select(a => a.kind), Is.EqualTo(new[] { NewsKind.DragonSlain, NewsKind.Death, NewsKind.LastStand }));
            Assert.That(p.Lead.people, Is.EquivalentTo(new[] { "a", "b" }), "the fight's people join the slaying's story");
        }

        [Test]
        public void Compose_GroupsLevelUpsAndQuests_AndFillsAQuietDay()
        {
            var noted = new List<NewsArticle> { NewsRules.LevelUp("토르", 3).About("t"), NewsRules.LevelUp("미라", 5).About("m"), NewsRules.QuestDone("고블린 토벌") };
            var p = NewsRules.Compose(2, noted, null);
            var ups = p.articles.Single(a => a.kind == NewsKind.LevelUp);
            Assert.That(ups.body, Is.EqualTo("토르(Lv.3), 미라(Lv.5)가 더 강해졌다."));
            Assert.That(ups.people, Is.EqualTo(new[] { "t", "m" }));
            Assert.That(ups.score, Is.GreaterThan(NewsRules.Score(NewsKind.LevelUp)), "two of them are better news than one");
            Assert.That(p.articles.Single(a => a.kind == NewsKind.QuestDone).headline, Is.EqualTo("'고블린 토벌' 의뢰 달성"), "one stays as written");

            var report = new DailyReport(2, 0f, 100);
            report.Add(GoldCategory.Tax, 60);
            report.GoldEnd = 160;
            var gold = NewsRules.Compose(2, null, report);
            Assert.That(gold.articles.Select(a => a.kind), Is.EqualTo(new[] { NewsKind.Gold }), "a day of nothing but money");
            var quiet = NewsRules.Compose(2, null, new DailyReport(2, 0f, 100));
            Assert.That(quiet.articles.Select(a => a.kind), Is.EqualTo(new[] { NewsKind.Quiet }));
        }

        [Test]
        public void Words_TakeTheRightParticles_AndTheSameWordingForTheSameStory()
        {
            Assert.That(NewsRules.Death("토르", "고블린").body, Is.EqualTo("토르가 고블린과 싸우다 목숨을 잃었다. 길드는 슬픔에 잠겼다."));
            Assert.That(NewsRules.Death("레온", null).body, Is.EqualTo("레온이 목숨을 잃었다. 길드는 슬픔에 잠겼다."));
            Assert.That(NewsRules.Friends("레온", "미라").headline, Is.EqualTo("레온과 미라, 단짝이 되다"));
            Assert.That(NewsRules.BigFoeSlain(new[] { "토르", "미라" }, "고블린 대장", "우두머리와의 싸움").body,
                Is.EqualTo("우두머리와의 싸움에서 토르와 미라가 고블린 대장을 쓰러뜨렸다."));
            Assert.That(NewsRules.NemesisDeed("고블린", "토르 잡이 그릭", "토르", true, true).body,
                Is.EqualTo("토르를 쓰러뜨린 고블린이 '토르 잡이 그릭'이라 불리게 되었다."));
            Assert.That(NewsRules.RankUp("B등급", 0).headline, Is.EqualTo("길드, B등급에 오르다"));
            Assert.That(NewsRules.Death("토르", null).headline, Is.EqualTo(NewsRules.Death("토르", "늑대").headline), "chosen by the story, not by chance");
            foreach (NewsKind k in System.Enum.GetValues(typeof(NewsKind)))
                Assert.That(NewsRules.Tag(k), Is.Not.Empty, k.ToString());
        }

        [Test]
        public void Desk_MakesThePaperWhenTheDayCloses_AndHandsItOverAtSix()
        {
            EventBus.Clear();
            var treasury = new GuildTreasury(500, 0);
            var ledger = new DailyLedger(treasury, 1, 8f);
            var desk = new NewsDesk(null, null);
            var delivered = new List<NewsPaper>();
            EventBus.Subscribe<NewsPaperDeliveredEvent>(e => delivered.Add(e.Paper));
            try
            {
                EventBus.Publish(new Town.RaidSightedEvent { RaidId = 4, Direction = "북" });
                EventBus.Publish(new Town.RaidEndedEvent { RaidId = 4, Killed = 9, Sighted = true });
                EventBus.Publish(new Town.RaidEndedEvent { RaidId = 5, Killed = 3, Sighted = false });
                EventBus.Publish(new Seasons.FestivalEvent { Day = 1 });
                Assert.That(desk.Desk.Count, Is.EqualTo(2), "a raid nobody saw is no news");

                EventBus.Publish(new HourPassedEvent { Day = 2, Hour = 0 });
                Assert.That(desk.Latest?.day, Is.EqualTo(1), "made when the day closed");
                Assert.That(desk.Desk, Is.Empty);
                Assert.That(desk.Latest.Lead.kind, Is.EqualTo(NewsKind.Raid));
                Assert.That(desk.Latest.Lead.body, Does.Contain("쓰러뜨린 적 9"));
                Assert.That(desk.Latest.articles.Count, Is.EqualTo(2));

                EventBus.Publish(new HourPassedEvent { Day = 2, Hour = 5 });
                Assert.That(delivered, Is.Empty, "not before six");
                EventBus.Publish(new HourPassedEvent { Day = 2, Hour = NewsRules.DeliveryHour });
                EventBus.Publish(new HourPassedEvent { Day = 2, Hour = NewsRules.DeliveryHour + 1 });
                Assert.That(delivered.Select(p => p.day), Is.EqualTo(new[] { 1 }), "once");

                // Saved and put back as it was.
                desk.Note(NewsRules.Festival());
                string json = desk.Capture();
                var other = new NewsDesk(null, null);
                other.Restore(json, null);
                Assert.That(other.Capture(), Is.EqualTo(json));
                Assert.That(other.Latest.delivered, Is.True);
                other.Dispose();
            }
            finally
            {
                desk.Dispose();
                ledger.Dispose();
                EventBus.Clear();
            }
        }
    }
}
