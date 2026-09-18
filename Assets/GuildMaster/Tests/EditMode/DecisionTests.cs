using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.LLM;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    public class DecisionTests
    {
        static readonly string[] Ids = { "Q_1", "Q_2" };

        static DecisionPrompt.ResponseDto Dto(string action, string questId = "", string reason = "이유", string say = "가자!")
            => new() { action = action, quest_id = questId, reason = reason, say = say };

        [Test]
        public void Interpret_AcceptsValidQuest()
        {
            Assert.That(LlmDecisionMaker.TryInterpret(Dto("accept_quest", "Q_2"), 0.9f, Ids, true, out var d, out _), Is.True);
            Assert.That(d.Action, Is.EqualTo(AdventurerAction.AcceptQuest));
            Assert.That(d.QuestId, Is.EqualTo("Q_2"));
            Assert.That(d.FromLLM, Is.True);
            Assert.That(d.Say, Is.EqualTo("가자!"));
        }

        [Test]
        public void Interpret_RejectsUnknownOrMissingQuestId()
        {
            Assert.That(LlmDecisionMaker.TryInterpret(Dto("accept_quest", "Q_9"), 0.9f, Ids, true, out _, out var r1), Is.False);
            Assert.That(r1, Is.EqualTo("quest_id"));
            Assert.That(LlmDecisionMaker.TryInterpret(Dto("accept_quest", ""), 0.9f, Ids, true, out _, out var r2), Is.False);
            Assert.That(r2, Is.EqualTo("quest_id"));
        }

        [Test]
        public void Interpret_RejectsAcceptWhenBadlyHurt()
        {
            Assert.That(LlmDecisionMaker.TryInterpret(Dto("accept_quest", "Q_1"), 0.2f, Ids, true, out _, out var reason), Is.False);
            Assert.That(reason, Is.EqualTo("unsafe"));
            // Resting while hurt is fine.
            Assert.That(LlmDecisionMaker.TryInterpret(Dto("rest"), 0.2f, Ids, true, out var d, out _), Is.True);
            Assert.That(d.Action, Is.EqualTo(AdventurerAction.Rest));
            Assert.That(d.QuestId, Is.Null);
        }

        [Test]
        public void Interpret_RejectsShopWhenNotUseful()
        {
            Assert.That(LlmDecisionMaker.TryInterpret(Dto("shop"), 1f, Ids, false, out _, out var reason), Is.False);
            Assert.That(reason, Is.EqualTo("shop"));
            Assert.That(LlmDecisionMaker.TryInterpret(Dto("shop"), 1f, Ids, true, out var d, out _), Is.True);
            Assert.That(d.Action, Is.EqualTo(AdventurerAction.Shop));
        }

        [Test]
        public void Reckless_DependsOnPersonality()
        {
            Assert.That(LlmDecisionMaker.RecklessAllowed("위험", 1f, "안전", false), Is.True, "not reckless");
            Assert.That(LlmDecisionMaker.RecklessAllowed("무모", 1f, "명예", true), Is.False, "coward never");
            Assert.That(LlmDecisionMaker.RecklessAllowed("무모", 0f, "안전", true), Is.False, "safety-minded never");
            Assert.That(LlmDecisionMaker.RecklessAllowed("무모", -0.5f, "돈", false), Is.True, "brave trait");
            Assert.That(LlmDecisionMaker.RecklessAllowed("무모", 0f, "모험", false), Is.True, "adventure values");
            Assert.That(LlmDecisionMaker.RecklessAllowed("무모", 0f, "돈", false), Is.False, "ordinary alone");
            Assert.That(LlmDecisionMaker.RecklessAllowed("무모", 0f, "돈", true), Is.True, "ordinary joining a party");
            Assert.That(DecisionPrompt.MoneyLabel(10), Is.EqualTo("부족"));
            Assert.That(DecisionPrompt.MoneyLabel(80), Is.EqualTo("보통"));
            Assert.That(DecisionPrompt.MoneyLabel(200), Is.EqualTo("넉넉"));
        }

        [Test]
        public void Interpret_RejectsUnknownActionAndNull()
        {
            Assert.That(LlmDecisionMaker.TryInterpret(Dto("dance"), 1f, Ids, true, out _, out var r1), Is.False);
            Assert.That(r1, Is.EqualTo("action"));
            Assert.That(LlmDecisionMaker.TryInterpret(null, 1f, Ids, true, out _, out var r2), Is.False);
            Assert.That(r2, Is.EqualTo("parse"));
        }

        [Test]
        public void Grammar_AllowsOnlyGivenQuestIds()
        {
            var g = DecisionPrompt.BuildGrammar(Ids);
            StringAssert.Contains("Q_1", g);
            StringAssert.Contains("Q_2", g);
            StringAssert.DoesNotContain("Q_3", g);
            StringAssert.Contains("accept_quest", g);
            StringAssert.StartsWith("root ::=", g);
        }

        [Test]
        public void Context_SerializesSnakeCaseKeys_IncludingClass()
        {
            var ctx = new DecisionPrompt.ContextDto
            {
                npc_id = "NPC_001", name = "리아", @class = "궁수", level = 2, mbti = "INTP", values = "돈",
                traits = new[] { "겁이 많다." },
                status = new DecisionPrompt.StatusDto { hp_percent = 80 },
                available_quests = new[] { new DecisionPrompt.QuestDto { quest_id = "Q_1", members = new string[0] } },
                recent_events = new string[0],
            };
            var json = LlmJson.ToJson(ctx);
            StringAssert.Contains("\"class\":\"궁수\"", json);
            StringAssert.Contains("\"hp_percent\":80", json);
            StringAssert.Contains("\"available_quests\":[{\"quest_id\":\"Q_1\"", json);
        }

        [Test]
        public void Response_ParsesFromModelText()
        {
            const string text = "{\"action\":\"accept_quest\",\"quest_id\":\"Q_1\",\"reason\":\"보상이 좋다\",\"say\":\"내가 맡을게!\"}";
            Assert.That(LlmJson.TryParse<DecisionPrompt.ResponseDto>(text, out var dto), Is.True);
            Assert.That(dto.action, Is.EqualTo("accept_quest"));
            Assert.That(dto.quest_id, Is.EqualTo("Q_1"));
            Assert.That(dto.say, Is.EqualTo("내가 맡을게!"));
        }
    }
}
