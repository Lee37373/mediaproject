using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.LLM;
using GuildMaster.Persuasion;
using GuildMaster.Quests;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    public class PersuasionTests
    {
        // ---- when persuasion opens ----

        [Test]
        public void IsOpen_NeedsSixHoursUntaken()
        {
            Assert.That(PersuasionRules.IsOpen(QuestStatus.Open, 0, 0f, 6f), Is.True);
            Assert.That(PersuasionRules.IsOpen(QuestStatus.Open, 0, 2f, 8f), Is.True, "counted from the posting time");
            Assert.That(PersuasionRules.IsOpen(QuestStatus.Open, 0, 0f, 5.9f), Is.False, "not yet six hours");
            Assert.That(PersuasionRules.IsOpen(QuestStatus.Open, 1, 0f, 10f), Is.False, "someone already took it");
            Assert.That(PersuasionRules.IsOpen(QuestStatus.InProgress, 0, 0f, 10f), Is.False);
        }

        [Test]
        public void HoursUntilOpen_CountsDownAndStopsAtZero()
        {
            Assert.That(PersuasionRules.HoursUntilOpen(0f, 2f), Is.EqualTo(4f).Within(0.001f));
            Assert.That(PersuasionRules.HoursUntilOpen(0f, 6f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(PersuasionRules.HoursUntilOpen(0f, 20f), Is.EqualTo(0f).Within(0.001f));
        }

        // ---- starting willingness and the bar for a yes ----

        [Test]
        public void InitialWillingness_FollowsRiskAndPersonality()
        {
            Assert.That(PersuasionRules.InitialWillingness(QuestRisk.Fair, 0f, "돈"), Is.EqualTo(55));
            Assert.That(PersuasionRules.InitialWillingness(QuestRisk.Easy, 0f, "돈"), Is.GreaterThan(PersuasionRules.InitialWillingness(QuestRisk.Fair, 0f, "돈")));
            Assert.That(PersuasionRules.InitialWillingness(QuestRisk.Risky, 0f, "돈"), Is.LessThan(PersuasionRules.InitialWillingness(QuestRisk.Fair, 0f, "돈")));
            Assert.That(PersuasionRules.InitialWillingness(QuestRisk.Reckless, 0f, "돈"), Is.EqualTo(20));
            Assert.That(PersuasionRules.InitialWillingness(QuestRisk.Risky, 1f, "돈"), Is.EqualTo(20), "cowards start lower");
            Assert.That(PersuasionRules.InitialWillingness(QuestRisk.Risky, -0.5f, "돈"), Is.EqualTo(50), "the brave start higher");
            Assert.That(PersuasionRules.InitialWillingness(QuestRisk.Fair, 0f, "명예"), Is.EqualTo(60));
            Assert.That(PersuasionRules.InitialWillingness(QuestRisk.Fair, 0f, "안전"), Is.EqualTo(50));
        }

        [Test]
        public void InitialWillingness_IsClamped()
        {
            Assert.That(PersuasionRules.InitialWillingness(QuestRisk.Reckless, 1f, "안전"), Is.EqualTo(5));
            Assert.That(PersuasionRules.InitialWillingness(QuestRisk.Easy, -1f, "명예"), Is.EqualTo(80));
        }

        [Test]
        public void RecklessBlocked_MatchesTheDecisionGate()
        {
            Assert.That(PersuasionRules.IsRecklessBlocked(QuestRisk.Reckless, 0f, "돈"), Is.True, "neutral: would not go alone");
            Assert.That(PersuasionRules.IsRecklessBlocked(QuestRisk.Reckless, 1f, "명예"), Is.True, "cautious never goes");
            Assert.That(PersuasionRules.IsRecklessBlocked(QuestRisk.Reckless, -0.5f, "돈"), Is.False, "brave goes anyway");
            Assert.That(PersuasionRules.IsRecklessBlocked(QuestRisk.Risky, 1f, "안전"), Is.False, "only reckless quests are gated");
            // The gate itself (moved from the model's decision checks, P4-91).
            Assert.That(PersuasionRules.RecklessAllowed(QuestRisk.Risky, 1f, "안전", false), Is.True, "not reckless");
            Assert.That(PersuasionRules.RecklessAllowed(QuestRisk.Reckless, 1f, "명예", true), Is.False, "coward never");
            Assert.That(PersuasionRules.RecklessAllowed(QuestRisk.Reckless, 0f, "안전", true), Is.False, "safety-minded never");
            Assert.That(PersuasionRules.RecklessAllowed(QuestRisk.Reckless, -0.5f, "돈", false), Is.True, "brave trait");
            Assert.That(PersuasionRules.RecklessAllowed(QuestRisk.Reckless, 0f, "모험", false), Is.True, "adventure values");
            Assert.That(PersuasionRules.RecklessAllowed(QuestRisk.Reckless, 0f, "돈", false), Is.False, "ordinary alone");
            Assert.That(PersuasionRules.RecklessAllowed(QuestRisk.Reckless, 0f, "돈", true), Is.True, "ordinary joining a party");
        }

        [Test]
        public void AcceptThreshold_RisesWithRecklessness()
        {
            Assert.That(PersuasionRules.AcceptThreshold(QuestRisk.Fair, false), Is.EqualTo(50));
            Assert.That(PersuasionRules.AcceptThreshold(QuestRisk.Risky, false), Is.EqualTo(50));
            Assert.That(PersuasionRules.AcceptThreshold(QuestRisk.Reckless, false), Is.EqualTo(65));
            Assert.That(PersuasionRules.AcceptThreshold(QuestRisk.Reckless, true), Is.EqualTo(75));
        }

        // ---- verdict ----

        [Test]
        public void Judge_AcceptsOnlyAtTheThreshold()
        {
            Assert.That(PersuasionRules.Judge(50, PersuadeDecision.Accept, 1, 50, true), Is.EqualTo(PersuadeOutcome.Accepted));
            Assert.That(PersuasionRules.Judge(49, PersuadeDecision.Accept, 1, 50, true), Is.EqualTo(PersuadeOutcome.Ongoing), "a yes below the bar keeps talking");
            Assert.That(PersuasionRules.Judge(60, PersuadeDecision.Undecided, 1, 50, true), Is.EqualTo(PersuadeOutcome.Ongoing));
            Assert.That(PersuasionRules.Judge(80, PersuadeDecision.Undecided, 1, 50, true), Is.EqualTo(PersuadeOutcome.Accepted), "far above the bar is a yes");
        }

        [Test]
        public void Judge_RefusesWhenFarBelowTheThreshold()
        {
            Assert.That(PersuasionRules.Judge(29, PersuadeDecision.Refuse, 1, 50, true), Is.EqualTo(PersuadeOutcome.Refused));
            Assert.That(PersuasionRules.Judge(35, PersuadeDecision.Refuse, 1, 50, true), Is.EqualTo(PersuadeOutcome.Ongoing), "a refusal close to the bar can still be turned");
            Assert.That(PersuasionRules.Judge(10, PersuadeDecision.Undecided, 1, 50, true), Is.EqualTo(PersuadeOutcome.Ongoing), "only an explicit refusal ends it early");
        }

        [Test]
        public void Judge_LastTurnGivesAFinalAnswer()
        {
            int last = PersuasionRules.MaxPlayerTurns;
            Assert.That(PersuasionRules.Judge(60, PersuadeDecision.Undecided, last, 50, true), Is.EqualTo(PersuadeOutcome.Accepted));
            Assert.That(PersuasionRules.Judge(59, PersuadeDecision.Undecided, last, 50, true), Is.EqualTo(PersuadeOutcome.Refused));
        }

        [Test]
        public void Judge_NeverAcceptsWhenAcceptIsForbidden()
        {
            Assert.That(PersuasionRules.Judge(100, PersuadeDecision.Accept, 1, 50, false), Is.EqualTo(PersuadeOutcome.Ongoing));
            Assert.That(PersuasionRules.Judge(100, PersuadeDecision.Accept, PersuasionRules.MaxPlayerTurns, 50, false), Is.EqualTo(PersuadeOutcome.Refused));
        }

        // ---- rule-based replies ----

        [Test]
        public void RuleBased_Score_RewardsRewardCompanyAndSafetyArguments()
        {
            string plain = "부탁드립니다.";
            Assert.That(RuleBasedPersuader.Score("보상으로 골드를 넉넉히 드릴게요.", "돈", QuestRisk.Fair, 1),
                Is.GreaterThan(RuleBasedPersuader.Score(plain, "돈", QuestRisk.Fair, 1)));
            // Company only matters when the quest takes a party.
            Assert.That(RuleBasedPersuader.Score("동료들도 같이 갈 거예요.", "돈", QuestRisk.Fair, 3)
                        - RuleBasedPersuader.Score("동료들도 같이 갈 거예요.", "돈", QuestRisk.Fair, 1), Is.EqualTo(3));
            // Safety only matters when the quest is dangerous.
            Assert.That(RuleBasedPersuader.Score("무리하지 말고 위험하면 후퇴해요.", "돈", QuestRisk.Risky, 1)
                        - RuleBasedPersuader.Score("무리하지 말고 위험하면 후퇴해요.", "돈", QuestRisk.Fair, 1), Is.EqualTo(3));
            Assert.That(RuleBasedPersuader.Score("", "돈", QuestRisk.Fair, 1), Is.LessThan(0));
        }

        [Test]
        public void RuleBased_Proposal_AcceptsWhenWillingEnough_RefusesWhenNot()
        {
            var rng = new GameRandom(1);
            var yes = RuleBasedPersuader.Respond("부탁드립니다.", "돈", QuestRisk.Risky, 1, 70, 50, true, rng);
            Assert.That(yes.Decision, Is.EqualTo(PersuadeDecision.Accept));
            Assert.That(string.IsNullOrEmpty(yes.Text), Is.False);
            var no = RuleBasedPersuader.Respond("부탁드립니다.", "돈", QuestRisk.Risky, 1, 10, 50, true, rng);
            Assert.That(no.Decision, Is.EqualTo(PersuadeDecision.Refuse));
            var unsure = RuleBasedPersuader.Respond("부탁드립니다.", "돈", QuestRisk.Risky, 1, 40, 50, true, rng);
            Assert.That(unsure.Decision, Is.EqualTo(PersuadeDecision.Undecided), "close to the bar: not a refusal yet");
        }

        [Test]
        public void RuleBased_Chat_OnlyRefusesWhenHopeless()
        {
            var rng = new GameRandom(2);
            Assert.That(RuleBasedPersuader.Respond("부탁드립니다.", "돈", QuestRisk.Reckless, 1, 55, 60, false, rng).Decision, Is.EqualTo(PersuadeDecision.Undecided));
            Assert.That(RuleBasedPersuader.Respond("부탁드립니다.", "돈", QuestRisk.Reckless, 1, 5, 75, false, rng).Decision, Is.EqualTo(PersuadeDecision.Refuse));
        }

        // ---- LLM prompt ----

        [Test]
        public void Prompt_SendsRecentTurnsAsChatTurns_WithWillingnessInWords()
        {
            var turns = new List<PersuadeTurn>();
            for (int i = 0; i < 21; i++) turns.Add(new PersuadeTurn { FromPlayer = i % 2 == 0, Text = "말" + i });
            var req = new LlmRequest();
            NpcVoice.SetChat(req, turns.Select(t => (t.FromPlayer, t.Text)), PersuadePrompt.HistoryTurns * 2, PersuadePrompt.BuildAside(40, 50, false));

            Assert.That(req.User, Does.StartWith("말20"));
            Assert.That(req.Messages.Any(m => m.Text.Contains("말8")), Is.False, "old turns dropped");
            Assert.That(req.Messages.Any(m => m.Text == "말9"), Is.True);
            Assert.That(req.Messages[0].FromUser, Is.True);
            StringAssert.DoesNotContain("정식으로", req.User);
            StringAssert.Contains("정식으로", PersuadePrompt.BuildAside(40, 50, true));
            Assert.That(PersuadePrompt.BuildAside(40, 50, true), Does.Not.Match(@"\d"), "willingness and the bar stay out of the prompt");
        }

        [Test]
        public void Grammar_ChatReplyFirst_RequestDecisionFirst()
        {
            StringAssert.Contains("undecided", PersuadePrompt.Grammar);
            StringAssert.Contains("willingness_delta", PersuadePrompt.Grammar);
            StringAssert.DoesNotContain("undecided", PersuadePrompt.ProposalGrammar);
            StringAssert.Contains("refuse", PersuadePrompt.ProposalGrammar);
            Assert.That(PersuadePrompt.Grammar.IndexOf("reply"), Is.LessThan(PersuadePrompt.Grammar.IndexOf("decision")), "chat: the line comes first");
            Assert.That(PersuadePrompt.ProposalGrammar.IndexOf("decision"), Is.LessThan(PersuadePrompt.ProposalGrammar.IndexOf("reply")), "a request is answered first, then explained");
            Assert.That(PersuadePrompt.ParseDecision("accept"), Is.EqualTo(PersuadeDecision.Accept));
            Assert.That(PersuadePrompt.ParseDecision("refuse"), Is.EqualTo(PersuadeDecision.Refuse));
            Assert.That(PersuadePrompt.ParseDecision("??"), Is.EqualTo(PersuadeDecision.Undecided));
        }
    }
}
