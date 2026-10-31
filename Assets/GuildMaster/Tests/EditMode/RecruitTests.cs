using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.LLM;
using GuildMaster.Recruitment;
using GuildMaster.Traits;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    public class RecruitTests
    {
        [Test]
        public void Judge_FollowsPlanRules()
        {
            // No formal proposal and no turn limit (user request 2026-09-30): a visitor who says they want in
            // joins from ConversationJoinFloor up, and anyone joins on their own at AutoJoinAffinity.
            int floor = RecruitmentSession.ConversationJoinFloor, auto = RecruitmentSession.AutoJoinAffinity;
            Assert.That(floor, Is.LessThan(50), "joining in the flow of the talk no longer needs 50");
            Assert.That(RecruitmentSession.Judge(floor, RecruitDecision.Join), Is.EqualTo(RecruitOutcome.Joined));
            Assert.That(RecruitmentSession.Judge(floor - 1, RecruitDecision.Join), Is.EqualTo(RecruitOutcome.Ongoing), "still too wary");
            Assert.That(RecruitmentSession.Judge(auto, RecruitDecision.Undecided), Is.EqualTo(RecruitOutcome.Joined), "joins on their own");
            Assert.That(RecruitmentSession.Judge(auto - 1, RecruitDecision.Undecided), Is.EqualTo(RecruitOutcome.Ongoing));
            Assert.That(RecruitmentSession.Judge(34, RecruitDecision.Refuse), Is.EqualTo(RecruitOutcome.Left));
            Assert.That(RecruitmentSession.Judge(40, RecruitDecision.Refuse), Is.EqualTo(RecruitOutcome.Ongoing), "refuse above 35 keeps talking");
        }

        /// <summary>User report 2026-10-18: a visitor the town had warmed up joined after a single, low-effort "안녕".</summary>
        [Test]
        public void Judge_NoJoiningBeforeBeingAskedOrTalkingAWhile_NorOnABadLine()
        {
            int floor = RecruitmentSession.ConversationJoinFloor, auto = RecruitmentSession.AutoJoinAffinity;
            Assert.That(RecruitmentSession.MayJoin(invited: false, playerTurns: 1), Is.False);
            Assert.That(RecruitmentSession.MayJoin(invited: true, playerTurns: 1), Is.True, "asked straight out");
            Assert.That(RecruitmentSession.MayJoin(false, RecruitmentSession.OwnJoinMinTurns), Is.True, "talked a while");
            Assert.That(RecruitmentSession.Judge(63, RecruitDecision.Join, mayJoin: false, lineDelta: 3), Is.EqualTo(RecruitOutcome.Ongoing), "first line, not asked");
            Assert.That(RecruitmentSession.Judge(auto, RecruitDecision.Undecided, mayJoin: false, lineDelta: 3), Is.EqualTo(RecruitOutcome.Ongoing));
            Assert.That(RecruitmentSession.Judge(63, RecruitDecision.Join, mayJoin: true, lineDelta: -2), Is.EqualTo(RecruitOutcome.Ongoing), "not on a low-effort line");
            Assert.That(RecruitmentSession.Judge(floor, RecruitDecision.Join, mayJoin: true, lineDelta: 0), Is.EqualTo(RecruitOutcome.Joined));
            Assert.That(RecruitmentSession.Judge(auto, RecruitDecision.Undecided, mayJoin: true, lineDelta: 1), Is.EqualTo(RecruitOutcome.Joined));
            Assert.That(RecruitmentSession.Judge(20, RecruitDecision.Refuse, mayJoin: false, lineDelta: -12), Is.EqualTo(RecruitOutcome.Left), "leaving needs no guard");
        }

        [Test]
        public void Greeting_PicksUpOnlyAnEarlierTalk_InTheirOwnVoice()
        {
            string first = RecruitmentSession.Greeting(false, false, NpcVoice.Register.Warm);
            string heard = RecruitmentSession.Greeting(false, true, NpcVoice.Register.Warm);
            string again = RecruitmentSession.Greeting(true, true, NpcVoice.Register.Warm);
            Assert.That(first, Does.Not.Contain("아까"));
            Assert.That(heard, Does.Not.Contain("아까"));
            Assert.That(again, Does.Contain("아까"));
            Assert.That(RecruitmentSession.Greeting(false, false, NpcVoice.Register.Haughty), Is.Not.EqualTo(first), "speech level shows");
            Assert.That(RecruitmentSession.Greeting(false, false, NpcVoice.Register.Haughty), Does.Not.EndWith("요?"));
        }

        [Test]
        public void Invitation_IsSaidInTheTalk_AndNaggingNeverPays()
        {
            Assert.That(RuleBasedRecruiter.IsInvitation("우리 길드에 들어와 줄래요?"), Is.True);
            Assert.That(RuleBasedRecruiter.IsInvitation("함께하자, 우리 길드로 와."), Is.True);
            Assert.That(RuleBasedRecruiter.IsInvitation("같이 하실래요?"), Is.True);
            Assert.That(RuleBasedRecruiter.IsInvitation("오늘 날씨 좋네요."), Is.False);
            // Asking again right away can never raise affinity (the old proposal button could be mashed).
            Assert.That(RecruitmentSession.NaggedDelta(10), Is.EqualTo(-RecruitmentSession.NagPenalty));
            Assert.That(RecruitmentSession.NaggedDelta(-3), Is.EqualTo(-3 - RecruitmentSession.NagPenalty));
        }

        [Test]
        public void RuleBased_Invitation_AcceptedOnceWellDisposed()
        {
            var rng = new GameRandom(5);
            var yes = RuleBasedRecruiter.Respond("우리 길드에 들어와 줄래요? 함께하면 좋겠어요.", "모험", 50, false, rng);
            Assert.That(yes.Decision, Is.EqualTo(RecruitDecision.Join));
            var notYet = RuleBasedRecruiter.Respond("우리 길드에 들어와 줄래요? 함께하면 좋겠어요.", "모험", 30, false, rng);
            Assert.That(notYet.Decision, Is.EqualTo(RecruitDecision.Undecided), "not a refusal either, just not yet");
            Assert.That(string.IsNullOrEmpty(notYet.Text), Is.False);
        }

        [Test]
        public void InitialAffinity_UsesRecruitDifficulty()
        {
            var db = DefaultContent.Build();
            db.BuildIndex();
            Assert.That(RecruitmentSession.InitialAffinity(null), Is.EqualTo(RecruitmentSession.StartAffinity));
            Assert.That(RecruitmentSession.InitialAffinity(TraitSet.FromIds(new[] { "loyal" }, db)), Is.EqualTo(55));
            Assert.That(RecruitmentSession.InitialAffinity(TraitSet.FromIds(new[] { "arrogant" }, db)), Is.EqualTo(25));
            Assert.That(RecruitmentSession.InitialAffinity(TraitSet.FromIds(new[] { "arrogant", "greedy" }, db)), Is.EqualTo(RecruitmentSession.HardestStart),
                "the hardest visitor still starts where a good talk can win them over");
        }

        [Test]
        public void AVisitorStrongerThanTheGuild_StartsColder_ButNeverOutOfReach()
        {
            // P4-88 (user decision 2026-10-18): an outer village's Lv17 does not join a Lv5 guild for the asking.
            Assert.That(RecruitmentSession.InitialAffinity(null, 0), Is.EqualTo(RecruitmentSession.StartAffinity));
            Assert.That(RecruitmentSession.InitialAffinity(null, 5), Is.EqualTo(RecruitmentSession.StartAffinity - 5 * RecruitmentSession.AffinityPerLevelAbove));
            Assert.That(RecruitmentSession.InitialAffinity(null, 12), Is.EqualTo(16));
            Assert.That(RecruitmentSession.InitialAffinity(null, 40), Is.EqualTo(RecruitmentSession.StartAffinity - RecruitmentSession.MaxLevelPenalty), "the penalty is capped");
            Assert.That(RecruitmentSession.InitialAffinity(null, -3), Is.EqualTo(RecruitmentSession.StartAffinity), "a weaker visitor is no easier");
            Assert.That(RecruitmentSession.NextRecruitAffinity(-1, null, 3, 12), Is.EqualTo(19), "small talk seeds the same way");
        }

        [Test]
        public void NextRecruitAffinity_SeedsOnFirstContactThenAccumulates()
        {
            // Ambient town small talk (BanterService) shares this same persistent affinity with the formal
            // recruit dialogue (user request 2026-09-23): a visitor who has never talked to the guild
            // master (-1) gets seeded exactly like a freshly opened RecruitmentSession would, then deltas
            // accumulate and clamp to [0,100] the same way SendAsync does.
            var db = DefaultContent.Build();
            db.BuildIndex();
            var loyal = TraitSet.FromIds(new[] { "loyal" }, db);
            Assert.That(RecruitmentSession.NextRecruitAffinity(-1, loyal, 5), Is.EqualTo(RecruitmentSession.InitialAffinity(loyal) + 5));
            Assert.That(RecruitmentSession.NextRecruitAffinity(80, null, 10), Is.EqualTo(90));
            Assert.That(RecruitmentSession.NextRecruitAffinity(98, null, 10), Is.EqualTo(100), "clamped to 100");
            Assert.That(RecruitmentSession.NextRecruitAffinity(2, null, -10), Is.EqualTo(0), "clamped to 0");
        }

        [Test]
        public void RuleBased_Score_RewardsValuesAndPraise_PunishesRudeness()
        {
            int neutral = RuleBasedRecruiter.Score("우리 길드는 마을 광장 옆에 있어요.", "명예");
            int values = RuleBasedRecruiter.Score("우리 길드에서 명성을 쌓고 영웅이 되어 보세요.", "명예");
            int rude = RuleBasedRecruiter.Score("넌 한심해 보이는데 들어와.", "명예");
            Assert.That(values, Is.GreaterThan(neutral));
            Assert.That(rude, Is.LessThan(0));
            Assert.That(RuleBasedRecruiter.Score("", "돈"), Is.LessThan(0));
        }

        [Test]
        public void RuleBased_UndecidedReply_AnswersTheTopicTheMasterRaised()
        {
            // Without this every fallback line was filler ("그렇군요. 다른 건요?") whatever was
            // said, which reads as the visitor not listening (user report 2026-09-20).
            Assert.AreEqual("안전", RuleBasedRecruiter.Topic("무리한 의뢰는 받지 않으니 안전해요."));
            Assert.AreEqual("돈", RuleBasedRecruiter.Topic("보상은 넉넉하게 드리죠."));
            Assert.IsNull(RuleBasedRecruiter.Topic("우리 길드는 마을 광장 옆에 있어요."));

            var rng = new GameRandom(7);
            // The visitor's OWN value: keen.
            var keen = RuleBasedRecruiter.Respond("우리는 무리한 의뢰를 받지 않아서 안전합니다.", "안전", 40, false, rng);
            Assert.That(keen.Text, Does.Contain("무리한 의뢰").Or.Contain("돌아올 수 있는"));
            // Someone else's value: acknowledged, then turned to what this visitor wants — which tells the
            // guild master their values (user request 2026-09-29). It used to answer as if it were their passion.
            var topical = RuleBasedRecruiter.Respond("우리는 무리한 의뢰를 받지 않아서 안전합니다.", "명예", 40, false, rng);
            var filler = RuleBasedRecruiter.Respond("우리 길드는 마을 광장 옆에 있어요.", "명예", 40, false, rng);
            Assert.AreEqual(RecruitDecision.Undecided, topical.Decision);
            Assert.AreNotEqual(topical.Text, filler.Text);
            StringAssert.Contains("이름을 남길", topical.Text);
            Assert.That(topical.Reveals, Is.EqualTo(VisitorReveal.Values));
            Assert.That(filler.Reveals, Is.Null, "filler gives nothing away");
        }

        [Test]
        public void RuleBased_StockLines_FollowTheSpeechLevel()
        {
            var warm = RuleBasedRecruiter.Respond("안녕하세요", "돈", 40, false, new GameRandom(3), null, NpcVoice.Register.Warm);
            var haughty = RuleBasedRecruiter.Respond("안녕하세요", "돈", 40, false, new GameRandom(3), null, NpcVoice.Register.Haughty);
            Assert.That(warm.Text, Is.Not.EqualTo(haughty.Text));
            Assert.That(haughty.Text, Does.Not.Contain("요."), "the arrogant visitor does not speak politely");
        }

        // ---- finding out about a visitor (user request 2026-09-29) ----

        static AdventurerData Visitor() => new()
        {
            name = "오스틴", mbti = "ESFJ", values = "돈", traitIds = new List<string> { "arrogant", "sturdy" },
        };

        // Fixed reveal rules (user report 2026-10-07: "정보가 드러나는 포인트가 애매모호함").

        [Test]
        public void Reveal_Personality_AfterAFewLines_OrWhenAsked()
        {
            var d = Visitor();
            Assert.That(VisitorReveal.BeforeReply(d, "안녕하세요", 1, 30, out _), Is.Empty);
            Assert.That(VisitorReveal.BeforeReply(d, "어떤 성격이세요?", 1, 10, out _).Select(s => s.Key), Does.Contain(VisitorReveal.Personality), "asked: at once");
            Assert.That(VisitorReveal.BeforeReply(d, "날씨 좋네요", VisitorReveal.PersonalityTurns, 10, out _).Select(s => s.Key),
                Does.Contain(VisitorReveal.Personality), "enough talk");
            VisitorReveal.Reveal(d, VisitorReveal.Personality);
            Assert.That(VisitorReveal.BeforeReply(d, "어떤 성격이세요?", 5, 50, out _), Is.Empty, "already known");
        }

        [Test]
        public void Reveal_Values_WhenTouched_OrAskedOnceLikedEnough()
        {
            var d = Visitor();
            // 2026-10-30: asking is a roll that affinity improves, and a visitor only takes a few questions.
            Assert.That(VisitorReveal.AskChance(VisitorReveal.Values, 40), Is.EqualTo(35));
            Assert.That(VisitorReveal.AskChance(VisitorReveal.Values, 60), Is.GreaterThan(VisitorReveal.AskChance(VisitorReveal.Values, 40)));
            Assert.That(VisitorReveal.AskChance(VisitorReveal.Values, 100), Is.EqualTo(VisitorReveal.MaxAskChance));
            var early = VisitorReveal.BeforeReply(d, "뭐가 제일 중요하세요?", 1, 40, out var hint, askRoll: 0.9f);
            Assert.That(early, Is.Empty, "the roll missed: they dodge");
            StringAssert.Contains("35%", hint, "and the window says the odds");
            var asked = VisitorReveal.BeforeReply(d, "뭐가 제일 중요하세요?", 1, 40, out hint, askRoll: 0.1f);
            Assert.That(asked.Select(s => s.Key), Is.EqualTo(new[] { VisitorReveal.Values }));
            Assert.That(hint, Is.Null);
            var pestered = VisitorReveal.BeforeReply(d, "뭐가 제일 중요하세요?", 1, 90, out hint, 0f, mayAsk: false);
            Assert.That(pestered, Is.Empty, "no more questions");
            StringAssert.Contains("캐묻", hint);
            Assert.That(VisitorReveal.QuestionLimit("ENFP"), Is.EqualTo(3));
            Assert.That(VisitorReveal.QuestionLimit("ISTJ"), Is.EqualTo(2));
            Assert.That(VisitorReveal.AskedAbout(d, "뭐가 제일 중요하세요?"), Is.EqualTo(VisitorReveal.Values));
            Assert.That(VisitorReveal.AskedAbout(d, "날씨 좋네요"), Is.Null);
            var touched = VisitorReveal.AfterReply(d, RecruitReaction.ValueMatch, false, null, 30, 38);
            Assert.That(touched.Select(s => s.Key), Is.EqualTo(new[] { VisitorReveal.Values }));
            Assert.That(touched[0].Reason, Is.Not.Empty);
            var clash = VisitorReveal.AfterReply(d, RecruitReaction.Clash, true, null, 30, 25);
            Assert.That(clash.Select(s => s.Key), Is.EqualTo(new[] { VisitorReveal.Values }));
            Assert.That(VisitorReveal.AfterReply(d, RecruitReaction.Friendly, false, null, 30, 32), Is.Empty);
        }

        [Test]
        public void Reveal_Traits_WhenOffered_OpeningUp_OrAsked()
        {
            var d = Visitor();
            var offered = VisitorReveal.AfterReply(d, RecruitReaction.TraitMatch, false, "sturdy", 30, 36);
            Assert.That(offered.Select(s => s.Key), Is.EqualTo(new[] { VisitorReveal.TraitKey("sturdy") }), "the trait the words were for");
            int mark = VisitorReveal.OpenUpAffinity[0];
            var opened = VisitorReveal.AfterReply(d, RecruitReaction.Friendly, false, null, mark - 2, mark);
            Assert.That(opened.Select(s => s.Key), Is.EqualTo(new[] { VisitorReveal.TraitKey("arrogant") }), "opening up: the first hidden one");
            Assert.That(VisitorReveal.AfterReply(d, RecruitReaction.Friendly, false, null, mark, mark + 3), Is.Empty, "only when crossing");
            VisitorReveal.BeforeReply(d, "장점이 뭐예요?", 1, 40, out var hint, askRoll: 0.95f);
            StringAssert.Contains("얼버무림", hint);
            var asked = VisitorReveal.BeforeReply(d, "장점이 뭐예요?", 1, 40, out _, askRoll: 0f);
            Assert.That(asked.Select(s => s.Key), Is.EqualTo(new[] { VisitorReveal.TraitKey("arrogant") }));
            // 56 → 60: no open-up mark (55/70) crossed, so only the pitch itself could show a trait.
            var notTwice = VisitorReveal.AfterReply(d, RecruitReaction.TraitMatch, false, "arrogant", 56, 60, asked);
            Assert.That(notTwice, Is.Empty, "what was already planned is not repeated, and no other trait comes out in its place");
            // 2026-10-29: a pitch for a trait already known does not give away a different one.
            VisitorReveal.Reveal(d, VisitorReveal.TraitKey("sturdy"));
            Assert.That(VisitorReveal.AfterReply(d, RecruitReaction.TraitMatch, false, "sturdy", 56, 60), Is.Empty);
            Assert.That(VisitorReveal.AfterReply(d, RecruitReaction.TraitMatch, false, null, 56, 60).Count, Is.LessThanOrEqualTo(1), "a typed line the model judged may still show one");
        }

        [Test]
        public void Reveal_IsAnnouncedOnce_AndEveryKindSaysHowToFindIt()
        {
            var d = Visitor();
            Assert.That(VisitorReveal.Reveal(d, VisitorReveal.Values), Is.True);
            Assert.That(VisitorReveal.Reveal(d, VisitorReveal.Values), Is.False, "already known");
            Assert.That(VisitorReveal.IsRevealed(d, VisitorReveal.Values), Is.True);
            Assert.That(VisitorReveal.AfterReply(d, RecruitReaction.ValueMatch, false, null, 30, 38), Is.Empty, "nothing new");
            foreach (var kind in new[] { VisitorReveal.Personality, VisitorReveal.Values, "trait" })
                Assert.That(VisitorReveal.HowToFind(kind), Is.Not.Empty);
            foreach (var key in new[] { VisitorReveal.Personality, VisitorReveal.Values, VisitorReveal.TraitKey("arrogant") })
            {
                Assert.That(VisitorReveal.Confession(key, d), Is.Not.Empty);
                Assert.That(VisitorReveal.ForModel(key, d), Is.Not.Empty);
            }
        }

        [Test]
        public void Reveal_Labels_ReadLikeTheProfile()
        {
            var d = Visitor();
            Assert.That(VisitorReveal.Label(VisitorReveal.Values, d), Is.EqualTo("가치관: 돈"));
            Assert.That(VisitorReveal.Label(VisitorReveal.Personality, d), Is.EqualTo("성격: ESFJ"));
            Assert.That(VisitorReveal.Label(VisitorReveal.TraitKey("arrogant"), d, id => id == "arrogant" ? "오만함" : id), Is.EqualTo("특성: 오만함"));
        }

        [Test]
        public void RuleBased_Proposal_JoinsWhenLiked_RefusesWhenDisliked()
        {
            var rng = new GameRandom(1);
            var liked = RuleBasedRecruiter.Respond("함께 모험하며 새로운 곳을 탐험합시다!", "모험", 50, true, rng);
            Assert.That(liked.Decision, Is.EqualTo(RecruitDecision.Join));
            Assert.That(string.IsNullOrEmpty(liked.Text), Is.False);
            var disliked = RuleBasedRecruiter.Respond("바보같은 소리 말고 들어와", "모험", 30, true, rng);
            Assert.That(disliked.Decision, Is.EqualTo(RecruitDecision.Refuse));
        }

        [Test]
        public void Prompt_SendsRecentTurnsAsChatTurns_WithFeelingsInWords()
        {
            var turns = new List<RecruitTurn>();
            for (int i = 0; i < 21; i++) turns.Add(new RecruitTurn { FromPlayer = i % 2 == 0, Text = "말" + i });
            var req = new LlmRequest();
            NpcVoice.SetChat(req, turns.Select(t => (t.FromPlayer, t.Text)), RecruitPrompt.HistoryTurns * 2, RecruitPrompt.BuildAside(55, false));

            Assert.That(req.User, Does.StartWith("말20"), "the newest player line is the message being answered");
            Assert.That(req.Messages.Any(m => m.Text.Contains("말8")), Is.False, "old turns dropped");
            Assert.That(req.Messages.Any(m => m.Text == "말9"), Is.True);
            Assert.That(req.Messages[0].FromUser, Is.True, "chat turns start with the user");
            for (int i = 1; i < req.Messages.Count; i++)
                Assert.That(req.Messages[i].FromUser, Is.Not.EqualTo(req.Messages[i - 1].FromUser), "turns alternate");

            StringAssert.DoesNotContain("정식으로", req.User);
            StringAssert.Contains("정식으로", RecruitPrompt.BuildAside(55, true));
            Assert.That(RecruitPrompt.BuildAside(-1, false), Is.Null, "nothing to add");
            Assert.That(RecruitPrompt.BuildAside(55, true), Does.Not.Match(@"\d"), "feelings are words, not numbers");
        }

        [Test]
        public void Grammar_ChatReplyFirst_OfferDecisionFirst()
        {
            StringAssert.Contains("undecided", RecruitPrompt.Grammar);
            StringAssert.DoesNotContain("reveal", RecruitPrompt.Grammar, "what is revealed is decided by VisitorReveal's rules, not the model");
            StringAssert.Contains("reaction", RecruitPrompt.Grammar);
            StringAssert.DoesNotContain("affinity_delta", RecruitPrompt.Grammar, "the model no longer picks the number (RecruitScoring does)");
            StringAssert.DoesNotContain("undecided", RecruitPrompt.ProposalGrammar);
            StringAssert.Contains("refuse", RecruitPrompt.ProposalGrammar);
            Assert.That(RecruitPrompt.Grammar.IndexOf("reply"), Is.LessThan(RecruitPrompt.Grammar.IndexOf("decision")), "chat: the line comes first");
            Assert.That(RecruitPrompt.ProposalGrammar.IndexOf("decision"), Is.LessThan(RecruitPrompt.ProposalGrammar.IndexOf("reply")), "an offer is answered first, then explained");
            Assert.That(RecruitPrompt.ParseDecision("join"), Is.EqualTo(RecruitDecision.Join));
            Assert.That(RecruitPrompt.ParseDecision("??"), Is.EqualTo(RecruitDecision.Undecided));
        }
    }
}
