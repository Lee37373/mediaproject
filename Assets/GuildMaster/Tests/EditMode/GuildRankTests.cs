using System.Linq;
using GuildMaster.Data;
using GuildMaster.Guild;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// The guild's ranks (P4-106, user decisions 2026-10-27): F to S, three conditions each (the first the tutorial), climbed
    /// one step at a time as far as the conditions go, S asking for the four region lords; what each rank opens.
    /// </summary>
    public class GuildRankTests
    {
        static GuildStanding Standing(int fame = 0, int quests = 0, int recruits = 0, int upgrades = 0, int nests = 0, int zones = 0,
            int species = 0, int raids = 0, int facilities = 0, int lords = 0, int lv5 = 0, int lv10 = 0)
            => new()
            {
                Fame = fame, QuestsDone = quests, Recruits = recruits, Upgrades = upgrades, NestsCleared = nests, ZonesKnown = zones,
                SpeciesKnown = species, RaidsHeld = raids, Facilities = facilities, LordsSlain = lords,
                MembersAtLevel = level => level <= 5 ? lv5 : level <= 10 ? lv10 : 0,
            };

        [Test]
        public void EveryRank_AsksThreeThings_TheFirstIsTheTutorial_AndSAsksForTheFourLords()
        {
            Assert.That(GuildRankRules.GoalsFor(GuildRank.F), Is.Empty);
            foreach (var r in new[] { GuildRank.E, GuildRank.D, GuildRank.C, GuildRank.B, GuildRank.A })
                Assert.That(GuildRankRules.GoalsFor(r).Count, Is.EqualTo(3), r.ToString());
            Assert.That(GuildRankRules.GoalsFor(GuildRank.E).Select(g => g.Kind),
                Is.EquivalentTo(new[] { GoalKind.QuestsDone, GoalKind.Recruits, GoalKind.Upgrades }), "the tutorial");
            var s = GuildRankRules.GoalsFor(GuildRank.S);
            Assert.That(s.Any(g => g.Kind == GoalKind.LordsSlain && g.Count == GuildRankRules.RegionLordCount), Is.True, "S is the victory");
            // Fame thresholds rise rank by rank.
            int last = 0;
            foreach (var r in new[] { GuildRank.D, GuildRank.C, GuildRank.B, GuildRank.A, GuildRank.S })
            {
                int fame = GuildRankRules.GoalsFor(r).First(g => g.Kind == GoalKind.Fame).Count;
                Assert.That(fame, Is.GreaterThan(last), r.ToString());
                last = fame;
            }
        }

        [Test]
        public void TheGuild_ClimbsOneStepAtATime_AsFarAsTheConditionsGo()
        {
            Assert.That(GuildRankRules.Climb(GuildRank.F, Standing()), Is.EqualTo(GuildRank.F));
            Assert.That(GuildRankRules.Climb(GuildRank.F, Standing(quests: 1, recruits: 1)), Is.EqualTo(GuildRank.F), "the tutorial half done");
            Assert.That(GuildRankRules.Climb(GuildRank.F, Standing(quests: 1, recruits: 1, upgrades: 1)), Is.EqualTo(GuildRank.E));
            // D's conditions met too: two steps at once.
            Assert.That(GuildRankRules.Climb(GuildRank.F, Standing(fame: 60, quests: 1, recruits: 1, upgrades: 1, nests: 5, lv5: 3)), Is.EqualTo(GuildRank.D));
            // C's met but not D's: it stops at E.
            Assert.That(GuildRankRules.Climb(GuildRank.E, Standing(fame: 200, zones: 8, raids: 1)), Is.EqualTo(GuildRank.E));
            // The four lords without the fame: A stays A; with it, S.
            Assert.That(GuildRankRules.Climb(GuildRank.A, Standing(fame: 900, lords: 4)), Is.EqualTo(GuildRank.A));
            Assert.That(GuildRankRules.Climb(GuildRank.A, Standing(fame: 1000, lords: 4)), Is.EqualTo(GuildRank.S));
            Assert.That(GuildRankRules.Climb(GuildRank.S, Standing(fame: 5000, lords: 4)), Is.EqualTo(GuildRank.S), "nothing above S");
        }

        [Test]
        public void Progress_ReadsEachCount_AndMembersByLevel()
        {
            var s = Standing(fame: 42, nests: 3, lv5: 2, lv10: 1);
            Assert.That(GuildRankRules.Progress(new Goal(GoalKind.Fame, 60), s), Is.EqualTo(42));
            Assert.That(GuildRankRules.Progress(new Goal(GoalKind.NestsCleared, 5), s), Is.EqualTo(3));
            Assert.That(GuildRankRules.Progress(new Goal(GoalKind.MembersAtLevel, 3, 5), s), Is.EqualTo(2));
            Assert.That(GuildRankRules.Progress(new Goal(GoalKind.MembersAtLevel, 4, 10), s), Is.EqualTo(1));
            Assert.That(GuildRankRules.Met(new Goal(GoalKind.Fame, 42), s), Is.True);
            Assert.That(new Goal(GoalKind.MembersAtLevel, 3, 5).Label, Is.EqualTo("Lv5 이상 길드원 3명"));
            Assert.That(new Goal(GoalKind.QuestsDone, 1).Label, Is.EqualTo("첫 의뢰 완료"));
        }

        [Test]
        public void Fame_ComesByDanger_AndTheRanksOpenOnlyNewThings()
        {
            Assert.That((GuildRankRules.QuestFame(DangerLevel.Low), GuildRankRules.QuestFame(DangerLevel.Medium), GuildRankRules.QuestFame(DangerLevel.High)),
                Is.EqualTo((GuildRankRules.QuestLow, GuildRankRules.QuestMedium, GuildRankRules.QuestHigh)));
            Assert.That(GuildRankRules.MemberDied, Is.LessThan(0));
            Assert.That(GuildRankRules.QuestFailed, Is.LessThan(0));
            int lastBonus = 0;
            float lastInterval = 1f;
            foreach (GuildRank r in System.Enum.GetValues(typeof(GuildRank)))
            {
                Assert.That(GuildRankRules.VisitorBonus(r), Is.GreaterThanOrEqualTo(lastBonus), r.ToString());
                Assert.That(GuildRankRules.VisitInterval(r), Is.LessThanOrEqualTo(lastInterval), r.ToString());
                lastBonus = GuildRankRules.VisitorBonus(r);
                lastInterval = GuildRankRules.VisitInterval(r);
            }
            Assert.That((GuildRankRules.VisitorBonus(GuildRank.F), GuildRankRules.VisitInterval(GuildRank.F)), Is.EqualTo((0, 1f)), "nothing changes at the start");
            Assert.That(GuildRankRules.Grant(GuildRank.E), Is.GreaterThan(0));
            Assert.That(GuildRankRules.Rewards(GuildRank.D), Does.Contain("하사금").And.Contain("방문자 레벨 +1"));
            Assert.That(GuildRankRules.Rewards(GuildRank.S), Is.EqualTo("승리"));
        }
    }
}
