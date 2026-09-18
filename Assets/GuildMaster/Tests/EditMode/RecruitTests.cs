using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
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
            Assert.That(RecruitmentSession.Judge(50, RecruitDecision.Join, 1), Is.EqualTo(RecruitOutcome.Joined));
            Assert.That(RecruitmentSession.Judge(49, RecruitDecision.Join, 1), Is.EqualTo(RecruitOutcome.Ongoing), "join needs affinity 50");
            Assert.That(RecruitmentSession.Judge(85, RecruitDecision.Undecided, 1), Is.EqualTo(RecruitOutcome.Joined), "85 joins regardless");
            Assert.That(RecruitmentSession.Judge(34, RecruitDecision.Refuse, 1), Is.EqualTo(RecruitOutcome.Left));
            Assert.That(RecruitmentSession.Judge(40, RecruitDecision.Refuse, 1), Is.EqualTo(RecruitOutcome.Ongoing), "refuse above 35 keeps talking");
            Assert.That(RecruitmentSession.Judge(60, RecruitDecision.Undecided, RecruitmentSession.MaxPlayerTurns), Is.EqualTo(RecruitOutcome.Joined));
            Assert.That(RecruitmentSession.Judge(59, RecruitDecision.Undecided, RecruitmentSession.MaxPlayerTurns), Is.EqualTo(RecruitOutcome.Left));
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
        public void Prompt_KeepsOnlyRecentTurns_AndGrammarHasDecisions()
        {
            var turns = new List<RecruitTurn>();
            for (int i = 0; i < 20; i++) turns.Add(new RecruitTurn { FromPlayer = i % 2 == 0, Text = "말" + i });
            var user = RecruitPrompt.BuildUser(turns, "리아", false);
            Assert.That(user.Contains("말7"), Is.False, "old turns dropped");
            Assert.That(user.Contains("말8"), Is.True);
            Assert.That(user.TrimEnd().EndsWith("리아의 대답:"), Is.True);
            StringAssert.Contains("undecided", RecruitPrompt.Grammar);
            StringAssert.Contains("affinity_delta", RecruitPrompt.Grammar);
            StringAssert.DoesNotContain("undecided", RecruitPrompt.ProposalGrammar);
            StringAssert.Contains("refuse", RecruitPrompt.ProposalGrammar);
            Assert.That(RecruitPrompt.Grammar.IndexOf("decision"), Is.LessThan(RecruitPrompt.Grammar.IndexOf("reply")), "decision comes first");
            Assert.That(RecruitPrompt.ParseDecision("join"), Is.EqualTo(RecruitDecision.Join));
            Assert.That(RecruitPrompt.ParseDecision("??"), Is.EqualTo(RecruitDecision.Undecided));
        }
    }
}
