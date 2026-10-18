using System.Collections.Generic;
using System.Linq;
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
            // No more fixed turn limit (user request 2026-09-23): talk is unlimited, so Judge only concludes
            // things on an explicit join/refuse or affinity reaching 85.
            Assert.That(RecruitmentSession.Judge(50, RecruitDecision.Join), Is.EqualTo(RecruitOutcome.Joined));
            Assert.That(RecruitmentSession.Judge(49, RecruitDecision.Join), Is.EqualTo(RecruitOutcome.Ongoing), "join needs affinity 50");
            Assert.That(RecruitmentSession.Judge(85, RecruitDecision.Undecided), Is.EqualTo(RecruitOutcome.Joined), "85 joins regardless");
            Assert.That(RecruitmentSession.Judge(34, RecruitDecision.Refuse), Is.EqualTo(RecruitOutcome.Left));
            Assert.That(RecruitmentSession.Judge(40, RecruitDecision.Refuse), Is.EqualTo(RecruitOutcome.Ongoing), "refuse above 35 keeps talking");
            Assert.That(RecruitmentSession.Judge(60, RecruitDecision.Undecided), Is.EqualTo(RecruitOutcome.Ongoing), "no turn cap: undecided just continues, however long the talk");
        }

        [Test]
        public void InitialAffinity_UsesRecruitDifficulty()
        {
            var db = DefaultContent.Build();
            db.BuildIndex();
            Assert.That(RecruitmentSession.InitialAffinity(null), Is.EqualTo(RecruitmentSession.StartAffinity));
            Assert.That(RecruitmentSession.InitialAffinity(TraitSet.FromIds(new[] { "loyal" }, db)), Is.EqualTo(55));
            Assert.That(RecruitmentSession.InitialAffinity(TraitSet.FromIds(new[] { "arrogant" }, db)), Is.EqualTo(25));
            Assert.That(RecruitmentSession.InitialAffinity(TraitSet.FromIds(new[] { "arrogant", "greedy" }, db)), Is.EqualTo(15));
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
            var topical = RuleBasedRecruiter.Respond("우리는 무리한 의뢰를 받지 않아서 안전합니다.", "명예", 40, false, rng);
            var filler = RuleBasedRecruiter.Respond("우리 길드는 마을 광장 옆에 있어요.", "명예", 40, false, rng);
            Assert.AreEqual(RecruitDecision.Undecided, topical.Decision);
            Assert.AreNotEqual(topical.Text, filler.Text);
            Assert.That(topical.Text, Does.Contain("무리한 의뢰").Or.Contain("돌아올 수 있는"));
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
            StringAssert.Contains("affinity_delta", RecruitPrompt.Grammar);
            StringAssert.DoesNotContain("undecided", RecruitPrompt.ProposalGrammar);
            StringAssert.Contains("refuse", RecruitPrompt.ProposalGrammar);
            Assert.That(RecruitPrompt.Grammar.IndexOf("reply"), Is.LessThan(RecruitPrompt.Grammar.IndexOf("decision")), "chat: the line comes first");
            Assert.That(RecruitPrompt.ProposalGrammar.IndexOf("decision"), Is.LessThan(RecruitPrompt.ProposalGrammar.IndexOf("reply")), "an offer is answered first, then explained");
            Assert.That(RecruitPrompt.ParseDecision("join"), Is.EqualTo(RecruitDecision.Join));
            Assert.That(RecruitPrompt.ParseDecision("??"), Is.EqualTo(RecruitDecision.Undecided));
        }
    }
}
