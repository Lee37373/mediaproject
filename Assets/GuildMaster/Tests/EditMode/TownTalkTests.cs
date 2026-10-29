using System.Linq;
using GuildMaster.Social;
using GuildMaster.Welfare;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Town talks between everyone in town (user request 2026-10-01): what a talk is about, what it does, rumors.</summary>
    public class TownTalkTests
    {
        static TalkContext Members(int ab = 0, int ba = 0, float sa = 10f, float sb = 10f, bool fight = false, bool news = false)
            => new() { KindA = TalkerKind.Member, KindB = TalkerKind.Member, FeelAB = ab, FeelBA = ba, StressA = sa, StressB = sb, RecentFight = fight, AHasNews = news };

        [Test]
        public void AFriendComfortsSomeoneStruggling()
        {
            var c = Members(ab: 30, sb: 75f);
            Assert.That(TownTalkRules.WantsComfort(c), Is.True);
            Assert.That(TownTalkRules.ChooseKind(c, 0.9f), Is.EqualTo(TownTalkKind.Comfort));
            Assert.That(TownTalkRules.WantsComfort(Members(ab: 0, sb: 75f)), Is.False, "not close enough");
            Assert.That(TownTalkRules.WantsComfort(Members(ab: 30, sa: 75f, sb: 75f)), Is.False, "struggling themselves");
        }

        [Test]
        public void HostilePairsQuarrel_FriendsHardlyEver_FightsGetMadeUp()
        {
            var hostile = Members(ab: -30, ba: -20);
            Assert.That(TownTalkRules.ChooseKind(hostile, 0.1f), Is.EqualTo(TownTalkKind.Quarrel));
            Assert.That(TownTalkRules.ChooseKind(hostile, 0.9f), Is.EqualTo(TownTalkKind.Chat));
            Assert.That(TownTalkRules.QuarrelChance(Members(ab: 40, ba: 40)), Is.EqualTo(0f));
            Assert.That(TownTalkRules.QuarrelChance(Members(ab: 0, ba: 0, sa: 50f)), Is.GreaterThan(0f), "on edge");
            Assert.That(TownTalkRules.ChooseKind(Members(ab: -20, ba: -20, fight: true), 0.2f), Is.EqualTo(TownTalkKind.MakeUp));
        }

        [Test]
        public void NewsIsPassedOn_AndStrangersTalkToo()
        {
            Assert.That(TownTalkRules.ChooseKind(Members(news: true), 0.2f), Is.EqualTo(TownTalkKind.Gossip));
            var visitor = new TalkContext { KindA = TalkerKind.Member, KindB = TalkerKind.Visitor };
            Assert.That(TownTalkRules.ChooseKind(visitor, 0.5f), Is.EqualTo(TownTalkKind.Welcome));
            var visitors = new TalkContext { KindA = TalkerKind.Visitor, KindB = TalkerKind.Visitor };
            Assert.That(TownTalkRules.ChooseKind(visitors, 0.5f), Is.EqualTo(TownTalkKind.Chat));
            var merchant = new TalkContext { KindA = TalkerKind.Merchant, KindB = TalkerKind.Visitor, AHasNews = true };
            Assert.That(TownTalkRules.ChooseKind(merchant, 0.1f), Is.EqualTo(TownTalkKind.Gossip));
            Assert.That(TownTalkRules.ChooseKind(merchant, 0.9f), Is.EqualTo(TownTalkKind.Trade));
        }

        [Test]
        public void Effects_FollowHowTheTalkWent()
        {
            var comfort = TownTalkRules.Effects(TownTalkKind.Comfort, TalkOutcome.Warm);
            Assert.That(comfort.StressB, Is.LessThan(0f), "the comforted one feels better");
            Assert.That(comfort.FeelBA, Is.GreaterThan(0));
            var fight = TownTalkRules.Effects(TownTalkKind.Quarrel, TalkOutcome.Fight);
            Assert.That(fight.FeelAB, Is.LessThan(0));
            Assert.That(fight.StressA, Is.GreaterThan(0f));
            Assert.That(fight.VisitorAffinity, Is.LessThan(0));
            var makeUp = TownTalkRules.Effects(TownTalkKind.MakeUp, TalkOutcome.Reconciled);
            Assert.That(makeUp.FeelAB, Is.GreaterThan(-fight.FeelAB), "making up heals more than the fight broke");
            Assert.That(TownTalkRules.Effects(TownTalkKind.Welcome, TalkOutcome.Warm).VisitorAffinity, Is.GreaterThan(0));
            Assert.That(TownTalkRules.Effects(TownTalkKind.Trade, TalkOutcome.Warm).VisitorAffinity, Is.EqualTo(0));
        }

        [Test]
        public void Outcomes_ParseOnlyWhatTheKindAllows()
        {
            Assert.That(TownTalkRules.ParseOutcome("fight", TownTalkKind.Quarrel), Is.EqualTo(TalkOutcome.Fight));
            Assert.That(TownTalkRules.ParseOutcome("fight", TownTalkKind.Chat), Is.EqualTo(TalkOutcome.Neutral));
            Assert.That(TownTalkRules.ParseOutcome("reconciled", TownTalkKind.MakeUp), Is.EqualTo(TalkOutcome.Reconciled));
            StringAssert.Contains("fight", TownTalkPrompt.BuildGrammar(TownTalkKind.Quarrel));
            StringAssert.DoesNotContain("fight", TownTalkPrompt.BuildGrammar(TownTalkKind.Chat));
        }

        [Test]
        public void Rumors_SpreadOneTalkAtATime_NeverToTheirFace_AndFade()
        {
            var book = new RumorBook();
            var r = book.Add("아리스가 늑대 토벌을 해냈다", 10f, new[] { "A" }, new[] { "A" }, guildTone: 1, subjectTone: 1);
            Assert.That(book.Pick("A", "B", 11f), Is.SameAs(r));
            Assert.That(book.Pick("B", "C", 11f), Is.Null, "B has not heard it yet");
            Assert.That(book.Learn(r, "B"), Is.True);
            Assert.That(book.Learn(r, "B"), Is.False);
            Assert.That(book.Pick("B", "C", 12f), Is.SameAs(r));
            Assert.That(book.Pick("B", "A", 12f), Is.Null, "A knows it (and it is about A)");
            var aboutC = book.Add("C와 D가 다퉜다", 12f, new[] { "B" }, new[] { "C", "D" });
            Assert.That(book.Pick("B", "C", 13f), Is.SameAs(r), "not about C to C's face");
            Assert.That(book.Pick("B", "E", 13f), Is.SameAs(aboutC), "freshest first");
            Assert.That(book.Pick("B", "E", 12f + RumorBook.LifetimeHours + 1f), Is.Null, "old news");
            Assert.That(book.Add("아리스가 늑대 토벌을 해냈다", 15f, new[] { "Z" }), Is.SameAs(r), "same story, more people know it");
            Assert.That(r.KnownBy.Contains("Z"), Is.True);
            book.Forget("B");
            Assert.That(book.Pick("B", "E", 13f), Is.Null);
        }

        [Test]
        public void Interpret_KeepsWholeLines_InOrder()
        {
            var dto = new TownTalkPrompt.ResponseDto { l1 = "안녕, 오늘 어때?", l2 = "(웃으며) 괜찮아.", l3 = "다행이다.", l4 = "", outcome = "warm" };
            var lines = TownTalkPrompt.Interpret(dto, "아리스", "리아");
            Assert.That(lines.Count, Is.EqualTo(3));
            Assert.That(lines[0].speaker, Is.EqualTo(0));
            Assert.That(lines[1].speaker, Is.EqualTo(1));
            Assert.That(lines[1].text, Is.EqualTo("괜찮아."));
            Assert.That(TownTalkPrompt.Interpret(new TownTalkPrompt.ResponseDto { l1 = "혼자 말", l2 = "" }, "a", "b"), Is.Empty);
            // live test 2026-10-01: the model wrote a script
            Assert.That(TownTalkPrompt.StripSpeakerTag("테오: 요즘 어때?", "테오"), Is.EqualTo("요즘 어때?"));
            Assert.That(TownTalkPrompt.StripSpeakerTag("테오도 왔어?", "테오"), Is.EqualTo("테오도 왔어?"));
            Assert.That(TownTalkPrompt.CutAtSpeakerTag("요즘 잘 돌아가냐? 다리안: 응.", "카엘", "다리안"), Is.EqualTo("요즘 잘 돌아가냐?"));
            Assert.That(TownTalkPrompt.CutAtSpeakerTag("다리안: 응.", "카엘", "다리안"), Is.EqualTo("다리안: 응."), "a tag at the start is StripSpeakerTag's job");
        }

        [Test]
        public void FixedLines_CoverEveryKind()
        {
            var rng = new Core.GameRandom(3);
            foreach (TownTalkKind k in System.Enum.GetValues(typeof(TownTalkKind)))
            {
                var s = TownTalkLines.Pick(k, false, "리아가 목숨을 잃었다", rng);
                Assert.That(s.Lines.Length, Is.GreaterThanOrEqualTo(2), k.ToString());
                Assert.That(TownTalkRules.OutcomesFor(k), Does.Contain(s.Outcome), k.ToString());
            }
            Assert.That(TownTalkLines.Hearsay("리아가 목숨을 잃었다"), Is.EqualTo("리아가 목숨을 잃었대."));
        }

        [Test]
        public void StressThresholds_MatchTheCareRules()
        {
            Assert.That(TownTalkRules.IsStruggling(StressRules.StrainedAt), Is.True);
            Assert.That(TownTalkRules.IsOnEdge(StressRules.TenseAt), Is.True);
        }

        // ---- Residents and recruiting in town (user request 2026-10-07) ----

        [Test]
        public void Invite_OnlyAWarmVisitor_ByAMemberOrAFriendlyResident()
        {
            var warm = new TalkContext { KindA = TalkerKind.Member, KindB = TalkerKind.Visitor, VisitorAffinity = TownTalkRules.InviteMinAffinity };
            Assert.That(TownTalkRules.WantsInvite(warm), Is.True);
            Assert.That(TownTalkRules.ChooseKind(warm, 0.1f), Is.EqualTo(TownTalkKind.Invite));
            Assert.That(TownTalkRules.ChooseKind(warm, 0.9f), Is.EqualTo(TownTalkKind.Welcome));
            var cold = warm;
            cold.VisitorAffinity = TownTalkRules.InviteMinAffinity - 1;
            Assert.That(TownTalkRules.WantsInvite(cold), Is.False);
            var resident = new TalkContext { KindA = TalkerKind.Resident, KindB = TalkerKind.Visitor, VisitorAffinity = 60, ResidentOpinionA = ResidentProfile.GoodOpinion };
            Assert.That(TownTalkRules.WantsInvite(resident), Is.True, "a resident who likes the guild recommends it");
            resident.ResidentOpinionA = 0;
            Assert.That(TownTalkRules.WantsInvite(resident), Is.False);
            Assert.That(TownTalkRules.ChooseKind(resident, 0.9f), Is.EqualTo(TownTalkKind.Welcome), "residents still talk to visitors about the guild");
        }

        [Test]
        public void Invite_NoYesBelowTheBar_AndFixedLinesFollowTheVisitor()
        {
            Assert.That(TownTalkRules.ResolveInvite(TalkOutcome.Joined, TownTalkRules.InviteJoinAffinity - 1), Is.EqualTo(TalkOutcome.Neutral));
            Assert.That(TownTalkRules.ResolveInvite(TalkOutcome.Joined, TownTalkRules.InviteJoinAffinity), Is.EqualTo(TalkOutcome.Joined));
            Assert.That(TownTalkRules.InviteVerdict(80), Is.EqualTo(TalkOutcome.Joined));
            Assert.That(TownTalkRules.InviteVerdict(TownTalkRules.InviteMinAffinity), Is.EqualTo(TalkOutcome.Neutral));
            Assert.That(TownTalkRules.InviteVerdict(20), Is.EqualTo(TalkOutcome.Declined));
            var rng = new Core.GameRandom(1);
            foreach (var o in new[] { TalkOutcome.Joined, TalkOutcome.Neutral, TalkOutcome.Declined })
                Assert.That(TownTalkLines.Pick(TownTalkKind.Invite, TalkerKind.Member, TalkerKind.Visitor, null, null, rng, o).Outcome, Is.EqualTo(o));
            Assert.That(TownTalkRules.ParseOutcome("joined", TownTalkKind.Invite), Is.EqualTo(TalkOutcome.Joined));
            Assert.That(TownTalkRules.ParseOutcome("joined", TownTalkKind.Chat), Is.EqualTo(TalkOutcome.Neutral));
            StringAssert.Contains("declined", TownTalkPrompt.BuildGrammar(TownTalkKind.Invite));
        }

        [Test]
        public void TownTalks_MoveVisitorsEnough_ToJoinInAFewTalks()
        {
            // A visitor who starts at 40: two warm welcomes and good news reach the invite bar, a third the join bar.
            int affinity = 40;
            affinity += TownTalkRules.Effects(TownTalkKind.Welcome, TalkOutcome.Warm).VisitorAffinity;
            Assert.That(affinity, Is.GreaterThanOrEqualTo(TownTalkRules.InviteMinAffinity));
            affinity += TownTalkRules.Effects(TownTalkKind.Gossip, TalkOutcome.Warm).VisitorAffinity + TownTalkRules.RumorAffinity;
            Assert.That(affinity, Is.GreaterThanOrEqualTo(TownTalkRules.InviteJoinAffinity));
        }

        [Test]
        public void Residents_HaveJobsAndOpinions_ThatMoveAndColourWhatTheySay()
        {
            var all = ResidentProfile.Defaults();
            Assert.That(all.Count, Is.InRange(5, 6));
            Assert.That(all.Select(r => r.Id).Distinct().Count(), Is.EqualTo(all.Count));
            foreach (var r in all)
            {
                Assert.That(r.Persona, Is.Not.Empty, r.Name);
                Assert.That(r.JobLabel, Is.Not.Empty, r.Name);
            }
            var p = all[0];
            p.AddOpinion(500);
            Assert.That(p.GuildOpinion, Is.EqualTo(ResidentProfile.Max));
            Assert.That(TownTalkRules.WordOfMouth(p.GuildOpinion), Is.GreaterThan(0));
            Assert.That(TownTalkRules.WordOfMouth(-ResidentProfile.GoodOpinion), Is.LessThan(0));
            Assert.That(TownTalkRules.WordOfMouth(0), Is.EqualTo(0));
            p.AddFeeling("NPC_001", 7);
            Assert.That(p.FeelingToward("NPC_001"), Is.EqualTo(7));
            Assert.That(p.FeelingToward("nobody"), Is.EqualTo(0));
            Assert.That(TownTalkRules.OpinionDelta(TalkOutcome.Warm), Is.GreaterThan(0));
            Assert.That(TownTalkRules.OpinionDelta(TalkOutcome.Fight), Is.LessThan(0));
            var rng = new Core.GameRandom(2);
            foreach (var job in System.Enum.GetValues(typeof(ResidentJob)))
            {
                var r = new ResidentProfile { Job = (ResidentJob)job, GuildOpinion = 0 };
                Assert.That(TownTalkLines.Pick(TownTalkKind.Chat, TalkerKind.Resident, TalkerKind.Member, r, null, rng).Lines.Length, Is.GreaterThanOrEqualTo(2));
                Assert.That(TownTalkLines.Pick(TownTalkKind.Welcome, TalkerKind.Resident, TalkerKind.Visitor, r, null, rng).Lines.Length, Is.GreaterThanOrEqualTo(2));
            }
        }
    }
}
