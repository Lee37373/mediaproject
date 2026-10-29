using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.LLM;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    public class NpcVoiceTests
    {
        // ---- character sheet ----

        [Test]
        public void Sheet_DescribesPersonInWords_NotCodes()
        {
            string sheet = NpcVoice.Sheet("ENTP", "돈", new[] { "겁이 많다." });
            StringAssert.DoesNotContain("ENTP", sheet);
            StringAssert.DoesNotContain("MBTI", sheet);
            StringAssert.Contains("말이 많은", sheet, "E is talkative");
            StringAssert.Contains("셈이 빠르다", sheet, "the value is a personality, not a slogan");
            StringAssert.Contains("겁이 많다.", sheet);
            // Values used to be told to show "only when the topic touches them", so every visitor talked in
            // the same neutral voice (user report 2026-09-29). Now they colour the whole attitude.
            StringAssert.DoesNotContain("이야기가 그쪽으로 흐를 때만", sheet);
            StringAssert.Contains("흥정", sheet, "a money-minded person haggles, whatever the topic");
        }

        [Test]
        public void Sheet_TraitsAddHowTheyTalk()
        {
            string sheet = NpcVoice.Sheet("ISTJ", "안전", new[] { "자신이 최고라고 믿으며 남을 깔본다." }, new[] { "arrogant" });
            StringAssert.Contains("자신이 최고라고", sheet);
            StringAssert.Contains("한 수 아래로", sheet, "the trait's speech habit is added");
            Assert.That(NpcVoice.TraitHabit("no_such_trait"), Is.Empty);
        }

        [Test]
        public void ValuesAndTemperament_GiveDifferentTalkers()
        {
            // Different values push different topics; different letters change how long and how warm the line is.
            var agendas = new[] { "돈", "명예", "동료", "모험", "안전", "지식" }.Select(NpcVoice.ValueAgenda).ToList();
            Assert.That(agendas.Distinct().Count(), Is.EqualTo(agendas.Count));
            Assert.That(NpcVoice.Temperament("ENFP"), Is.Not.EqualTo(NpcVoice.Temperament("ISTJ")));
            StringAssert.Contains("두세 문장", NpcVoice.RegisterRule(NpcVoice.Register.Warm, "ENFP"));
            StringAssert.Contains("한 문장", NpcVoice.RegisterRule(NpcVoice.Register.Warm, "INFP"));
        }

        [Test]
        public void RecruitRegister_FollowsTraitsThenLetters()
        {
            Assert.That(NpcVoice.RecruitRegister("ENFP", new[] { "arrogant" }), Is.EqualTo(NpcVoice.Register.Haughty), "arrogance wins");
            Assert.That(NpcVoice.RecruitRegister("ENFP", new[] { "loner" }), Is.EqualTo(NpcVoice.Register.Curt));
            Assert.That(NpcVoice.RecruitRegister("INTP", null), Is.EqualTo(NpcVoice.Register.Curt));
            Assert.That(NpcVoice.RecruitRegister("ESTJ", null), Is.EqualTo(NpcVoice.Register.Formal));
            Assert.That(NpcVoice.RecruitRegister("ESFP", null), Is.EqualTo(NpcVoice.Register.Warm));
            Assert.That(NpcVoice.RecruitRegister(null, null), Is.EqualTo(NpcVoice.Register.Warm));
        }

        [Test]
        public void ExampleReply_DiffersByValueAndRegister()
        {
            Assert.That(NpcVoice.ExampleReply("돈", NpcVoice.Register.Warm), Is.Not.EqualTo(NpcVoice.ExampleReply("지식", NpcVoice.Register.Warm)));
            Assert.That(NpcVoice.ExampleReply("돈", NpcVoice.Register.Warm), Is.Not.EqualTo(NpcVoice.ExampleReply("돈", NpcVoice.Register.Haughty)));
            Assert.That(NpcVoice.ExampleReply("낚시", NpcVoice.Register.Formal), Is.Not.Empty, "unknown values still get a line");
        }

        [Test]
        public void Sheet_Temperament_FollowsTheLetters()
        {
            StringAssert.Contains("말수가 적은", NpcVoice.Temperament("ISFJ"));
            StringAssert.Contains("담백하게", NpcVoice.Temperament("ISTJ"));
            StringAssert.Contains("기분을 살핀다", NpcVoice.Temperament("ISFJ"));
            Assert.That(NpcVoice.Temperament(null), Is.Empty);
            Assert.That(NpcVoice.Temperament("XY"), Is.Empty, "too short to read");
        }

        [Test]
        public void Sheet_UnknownValue_StillReadable()
        {
            StringAssert.Contains("'낚시'", NpcVoice.ValueSentence("낚시"));
            Assert.That(NpcVoice.ValueSentence(""), Is.Empty);
            Assert.That(NpcVoice.ValueSentence(null), Is.Empty);
        }

        // ---- numbers as feelings ----

        [Test]
        public void Condition_UsesWordsAndNoDigits()
        {
            string healthy = NpcVoice.Condition(1f, 0f, 0f, 200);
            StringAssert.Contains("멀쩡", healthy);
            StringAssert.Contains("돈 걱정은 없다", healthy);
            string worn = NpcVoice.Condition(0.3f, 80f, 75f, 10);
            StringAssert.Contains("상처가 깊어", worn);
            StringAssert.Contains("많이 지쳐", worn);
            StringAssert.Contains("신경이 곤두서", worn);
            StringAssert.Contains("빠듯", worn);
            Assert.That(worn, Does.Not.Match(@"\d"));
        }

        [Test]
        public void RecruitMood_BandsMatchTheVerdict()
        {
            // The verdict lets a visitor join from affinity 50 up; below that the mood must not read as a yes.
            StringAssert.Contains("들어갈 수도", NpcVoice.RecruitMood(50));
            StringAssert.DoesNotContain("들어갈", NpcVoice.RecruitMood(49));
            StringAssert.Contains("불쾌", NpcVoice.RecruitMood(10));
            StringAssert.Contains("들어가고 싶다", NpcVoice.RecruitMood(90));
            Assert.That(NpcVoice.RecruitMood(50), Is.Not.EqualTo(NpcVoice.RecruitMood(49)));
        }

        [Test]
        public void PersuadeMood_FollowsTheGapToTheBar()
        {
            StringAssert.Contains("기울었다", NpcVoice.PersuadeMood(50, 50));
            StringAssert.Contains("굳었다", NpcVoice.PersuadeMood(70, 50));
            StringAssert.Contains("망설여진다", NpcVoice.PersuadeMood(40, 50));
            StringAssert.Contains("내키지 않는다", NpcVoice.PersuadeMood(25, 50));
            StringAssert.Contains("전혀 없다", NpcVoice.PersuadeMood(5, 50));
        }

        [Test]
        public void ToneToward_FollowsTheFeeling()
        {
            StringAssert.Contains("다정한 반말", NpcVoice.ToneToward(50));
            StringAssert.Contains("친근한 반말", NpcVoice.ToneToward(20));
            StringAssert.Contains("존댓말", NpcVoice.ToneToward(0));
            StringAssert.Contains("퉁명스럽", NpcVoice.ToneToward(-20));
            StringAssert.Contains("차갑고", NpcVoice.ToneToward(-60));
        }

        // ---- chat turns ----

        static List<(bool, string)> Turns(params (bool, string)[] t) => t.ToList();

        [Test]
        public void SetChat_LastPlayerLineIsTheMessage_EarlierOnesAreHistory()
        {
            var req = new LlmRequest();
            NpcVoice.SetChat(req, Turns((true, "안녕하세요"), (false, "네, 안녕하세요."), (true, "뭐 하세요?")), 12);
            Assert.That(req.User, Is.EqualTo("뭐 하세요?"));
            Assert.That(req.Messages.Count, Is.EqualTo(2));
            Assert.That(req.Messages[0].FromUser, Is.True);
            Assert.That(req.Messages[0].Text, Is.EqualTo("안녕하세요"));
            Assert.That(req.Messages[1].FromUser, Is.False);
        }

        [Test]
        public void SetChat_AsideRidesOnTheNewestMessage_OnlyOnce()
        {
            var req = new LlmRequest();
            NpcVoice.SetChat(req, Turns((true, "하나"), (false, "둘"), (true, "셋")), 12, "(속마음)");
            Assert.That(req.User, Is.EqualTo("셋\n(속마음)"));
            Assert.That(req.Messages.Any(m => m.Text.Contains("속마음")), Is.False, "history stays plain");
        }

        [Test]
        public void SetChat_NpcOpeningLine_GetsAUserTurnInFront()
        {
            var req = new LlmRequest();
            NpcVoice.SetChat(req, Turns((false, "어서 오세요."), (true, "길드에 오실래요?")), 12);
            Assert.That(req.Messages.Count, Is.EqualTo(2));
            Assert.That(req.Messages[0].FromUser, Is.True, "chat turns start with the user");
            Assert.That(req.Messages[0].Text, Is.EqualTo(NpcVoice.OpeningUser));
            Assert.That(req.Messages[1].Text, Is.EqualTo("어서 오세요."));
        }

        [Test]
        public void SetChat_MergesNeighboursOfTheSameSpeaker()
        {
            var req = new LlmRequest();
            NpcVoice.SetChat(req, Turns((true, "하나"), (true, "둘"), (false, "셋"), (false, "넷"), (true, "다섯")), 12);
            Assert.That(req.Messages.Count, Is.EqualTo(2));
            Assert.That(req.Messages[0].Text, Is.EqualTo("하나 둘"));
            Assert.That(req.Messages[1].Text, Is.EqualTo("셋 넷"));
            Assert.That(req.User, Is.EqualTo("다섯"));
        }

        [Test]
        public void SetChat_NeverEndsOnTheNpc_AndHandlesAnEmptyTranscript()
        {
            var req = new LlmRequest();
            NpcVoice.SetChat(req, Turns((true, "안녕"), (false, "네")), 12);
            Assert.That(req.User, Is.Not.Empty, "the model always has a message to answer");
            Assert.That(req.Messages.Last().FromUser, Is.False);

            var empty = new LlmRequest();
            NpcVoice.SetChat(empty, Turns(), 12);
            Assert.That(empty.User, Is.Not.Empty);
            Assert.That(empty.Messages, Is.Empty);
        }

        [Test]
        public void SetChat_KeepsOnlyTheLastLines()
        {
            var turns = new List<(bool, string)>();
            for (int i = 0; i < 21; i++) turns.Add((i % 2 == 0, "말" + i));
            var req = new LlmRequest();
            NpcVoice.SetChat(req, turns, 12);
            Assert.That(req.User, Is.EqualTo("말20"));
            Assert.That(req.Messages.Any(m => m.Text == "말8"), Is.False);
            for (int i = 1; i < req.Messages.Count; i++)
                Assert.That(req.Messages[i].FromUser, Is.Not.EqualTo(req.Messages[i - 1].FromUser));
        }
    }
}
