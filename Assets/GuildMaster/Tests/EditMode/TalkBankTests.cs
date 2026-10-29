using System.Linq;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Social;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>The written town-talk database (user decision 2026-10-18): choosing scripts, speech levels, filling names.</summary>
    public class TalkBankTests
    {
        const string Json = @"{""scripts"":[
 {""id"":""generic"",""kind"":""Chat"",""outcome"":""Warm"",""a"":""MV"",""b"":""MV"",""weight"":1,""lines"":[
   {""s"":""A"",""w"":""안녕하세요, {B} 씨!"",""f"":""안녕하십니까, {B} 님."",""c"":""안녕하세요."",""h"":""{B아}, 안녕.""},
   {""s"":""B"",""w"":""반가워요!"",""f"":""반갑습니다."",""c"":""네."",""h"":""어.""}]},
 {""id"":""money"",""kind"":""Chat"",""outcome"":""Warm"",""a"":""M"",""b"":""M"",""valueB"":""돈"",""weight"":1,""lines"":[
   {""s"":""A"",""w"":""보수 받았어요?"",""f"":""보수는 받으셨습니까?"",""c"":""보수 받았어요?"",""h"":""보수 받았어?""},
   {""s"":""B"",""w"":""그럼요!"",""f"":""받았습니다."",""c"":""네."",""h"":""당연하지.""}]},
 {""id"":""hans"",""kind"":""Chat"",""outcome"":""Warm"",""a"":""R"",""b"":""M"",""resA"":""RES_hans"",""weight"":1,""lines"":[
   {""s"":""A"",""t"":""칼 이리 줘 봐.""},
   {""s"":""B"",""w"":""여기요!"",""f"":""여기 있습니다."",""c"":""여기요."",""h"":""자.""}]},
 {""id"":""gossip"",""kind"":""Gossip"",""outcome"":""Neutral"",""a"":""MV"",""b"":""MV"",""weight"":1,""lines"":[
   {""s"":""A"",""w"":""{rumor}더라고요."",""f"":""{rumor}고 합니다."",""c"":""{rumor}더라고요."",""h"":""{rumor}더라.""},
   {""s"":""B"",""w"":""진짜요?"",""f"":""그렇습니까?"",""c"":""그래요?"",""h"":""진짜?""}]}
]}";

        static TalkSide Member(string name, NpcVoice.Register reg = NpcVoice.Register.Warm, string values = "동료")
            => new() { Kind = TalkerKind.Member, Name = name, Register = reg, Values = values };

        static TalkSide Hans() => new() { Kind = TalkerKind.Resident, Name = "한스", ResidentId = "RES_hans" };

        [Test]
        public void ScriptsFitWhoIsTalking()
        {
            var bank = TalkBank.FromJson(Json);
            Assert.That(bank.Count, Is.EqualTo(4));
            var money = bank.Scripts.First(s => s.id == "money");
            Assert.That(TalkBank.Weight(money, Member("카엘"), Member("리안", values: "돈")), Is.GreaterThan(0f));
            Assert.That(TalkBank.Weight(money, Member("카엘"), Member("리안", values: "안전")), Is.EqualTo(0f), "written for someone who cares about money");
            var hans = bank.Scripts.First(s => s.id == "hans");
            Assert.That(TalkBank.Weight(hans, Hans(), Member("리안")), Is.GreaterThan(0f));
            var generic = bank.Scripts.First(s => s.id == "generic");
            Assert.That(TalkBank.Weight(generic, Hans(), Member("리안")), Is.EqualTo(0f), "residents only speak their own lines");
            Assert.That(TalkBank.Weight(money, Member("카엘"), Member("리안", values: "돈")),
                        Is.GreaterThan(TalkBank.Weight(generic, Member("카엘"), Member("리안", values: "돈"))), "written-for-them comes up more");
        }

        [Test]
        public void Pick_AvoidsRecentScripts_UnlessNothingElseFits()
        {
            var bank = TalkBank.FromJson(Json);
            var a = Member("카엘");
            var b = Member("리안", values: "돈");
            var picked = bank.Pick(TownTalkKind.Chat, TalkOutcome.Warm, a, b, () => 0f, new[] { "generic" });
            Assert.That(picked.id, Is.EqualTo("money"));
            picked = bank.Pick(TownTalkKind.Chat, TalkOutcome.Warm, a, b, () => 0f, new[] { "generic", "money" });
            Assert.That(picked, Is.Not.Null, "all recent: one is used anyway");
            Assert.That(bank.Pick(TownTalkKind.Chat, TalkOutcome.Tense, a, b, () => 0f), Is.Null, "nothing written for that");
        }

        [Test]
        public void EachSpeakerUsesTheirOwnSpeechLevel_AndNamesGetTheirParticles()
        {
            var bank = TalkBank.FromJson(Json);
            var generic = bank.Scripts.First(s => s.id == "generic");
            var lines = TalkBank.Render(generic, Member("카엘", NpcVoice.Register.Haughty), Member("리안", NpcVoice.Register.Formal), null);
            Assert.That(lines[0].text, Is.EqualTo("리안아, 안녕."));
            Assert.That(lines[1].text, Is.EqualTo("반갑습니다."));
            lines = TalkBank.Render(generic, Member("카엘", NpcVoice.Register.Warm), Member("미라"), null);
            Assert.That(lines[0].text, Is.EqualTo("안녕하세요, 미라 씨!"));
            var hans = TalkBank.Render(bank.Scripts.First(s => s.id == "hans"), Hans(), Member("리안", NpcVoice.Register.Curt), null);
            Assert.That(hans[0].text, Is.EqualTo("칼 이리 줘 봐."));
            Assert.That(hans[1].text, Is.EqualTo("여기요."));
            Assert.That(TalkBank.Fill("{A이} {B을} 불렀다", "카엘", "미라", null), Is.EqualTo("카엘이 미라를 불렀다"));
        }

        [Test]
        public void RumorsReadNaturally()
        {
            var bank = TalkBank.FromJson(Json);
            var gossip = bank.Scripts.First(s => s.id == "gossip");
            var lines = TalkBank.Render(gossip, Member("카엘"), Member("리안"), "리안이 늑대 사냥 의뢰를 해냈다.");
            Assert.That(lines[0].text, Is.EqualTo("리안이 늑대 사냥 의뢰를 해냈다더라고요."));
        }

        [Test]
        public void OutcomesFollowFeelings()
        {
            var friends = new TalkContext { KindA = TalkerKind.Member, KindB = TalkerKind.Member, FeelAB = 60, FeelBA = 60 };
            var rivals = new TalkContext { KindA = TalkerKind.Member, KindB = TalkerKind.Member, FeelAB = -60, FeelBA = -60 };
            Assert.That(TownTalkRules.RollOutcome(TownTalkKind.Chat, friends, 0.5f), Is.EqualTo(TalkOutcome.Warm));
            Assert.That(TownTalkRules.RollOutcome(TownTalkKind.Chat, rivals, 0.5f), Is.Not.EqualTo(TalkOutcome.Warm));
            Assert.That(TownTalkRules.RollOutcome(TownTalkKind.Quarrel, rivals, 0.5f), Is.EqualTo(TalkOutcome.Fight));
            Assert.That(TownTalkRules.RollOutcome(TownTalkKind.MakeUp, friends, 0.5f), Is.EqualTo(TalkOutcome.Reconciled));
            var keen = new TalkContext { KindA = TalkerKind.Member, KindB = TalkerKind.Visitor, VisitorAffinity = TownTalkRules.InviteJoinAffinity + 10 };
            Assert.That(TownTalkRules.RollOutcome(TownTalkKind.Invite, keen, 0.99f), Is.EqualTo(TalkOutcome.Joined));
            var grumpy = new TalkContext { KindA = TalkerKind.Resident, KindB = TalkerKind.Visitor, ResidentOpinionA = -50 };
            Assert.That(TownTalkRules.RollOutcome(TownTalkKind.Welcome, grumpy, 0.95f), Is.EqualTo(TalkOutcome.Tense));
            Assert.That(TownTalkRules.ShouldSwap(TownTalkKind.Welcome, TalkerKind.Visitor, TalkerKind.Member), Is.True);
            Assert.That(TownTalkRules.ShouldSwap(TownTalkKind.Trade, TalkerKind.Member, TalkerKind.Merchant), Is.True);
        }

        /// <summary>The shipped database: loads, and every line a member or visitor speaks exists in all four speech levels.</summary>
        [Test]
        public void ShippedDatabase_IsComplete()
        {
            var bank = TalkBank.LoadResources();
            Assert.That(bank.Count, Is.GreaterThanOrEqualTo(100));
            foreach (var s in bank.Scripts)
            {
                Assert.That(s.lines.Length, Is.EqualTo(4), s.id);
                for (int i = 0; i < s.lines.Length; i++)
                {
                    var line = s.lines[i];
                    Assert.That(line.s, Is.EqualTo(i % 2 == 0 ? "A" : "B"), s.id);
                    string side = line.s == "A" ? s.a : s.b;
                    if (side == "R" || side == "X") Assert.That(line.t, Is.Not.Empty, s.id);
                    else Assert.That(new[] { line.w, line.f, line.c, line.h }.All(t => !string.IsNullOrEmpty(t)), Is.True, s.id + " line " + i);
                }
            }
            // Every kind of talk has something for two members or a member and a visitor.
            var m = Member("카엘");
            var v = new TalkSide { Kind = TalkerKind.Visitor, Name = "로웬", Values = "모험" };
            Assert.That(bank.Pick(TownTalkKind.Chat, TalkOutcome.Warm, m, m, () => 0.3f), Is.Not.Null);
            Assert.That(bank.Pick(TownTalkKind.Quarrel, TalkOutcome.Fight, m, m, () => 0.3f), Is.Not.Null);
            Assert.That(bank.Pick(TownTalkKind.Welcome, TalkOutcome.Warm, m, v, () => 0.3f), Is.Not.Null);
            Assert.That(bank.Pick(TownTalkKind.Invite, TalkOutcome.Joined, m, v, () => 0.3f), Is.Not.Null);
        }

        /// <summary>
        /// Residents round 2 (2026-10-29): every two residents have something to say to each other in every mood (either
        /// way round), and every resident meets a visitor in every mood, so the old fixed lines no longer come up.
        /// </summary>
        [Test]
        public void ShippedDatabase_EveryResidentTalksInEveryMood()
        {
            var bank = TalkBank.LoadResources();
            var ids = ResidentProfile.Defaults().Select(r => r.Id).ToList();
            TalkSide R(string id) => new() { Kind = TalkerKind.Resident, ResidentId = id, Name = id };
            var v = new TalkSide { Kind = TalkerKind.Visitor, Name = "로웬", Values = "모험", Register = NpcVoice.Register.Curt };
            var moods = new[] { TalkOutcome.Warm, TalkOutcome.Neutral, TalkOutcome.Tense };
            for (int i = 0; i < ids.Count; i++)
            {
                for (int j = i + 1; j < ids.Count; j++)
                    foreach (var o in moods)
                        Assert.That(bank.Pick(TownTalkKind.Chat, o, R(ids[i]), R(ids[j]), () => 0.5f)
                                    ?? bank.Pick(TownTalkKind.Chat, o, R(ids[j]), R(ids[i]), () => 0.5f), Is.Not.Null, $"{ids[i]}-{ids[j]} {o}");
                foreach (var o in moods)
                {
                    Assert.That(bank.Pick(TownTalkKind.Chat, o, R(ids[i]), v, () => 0.5f), Is.Not.Null, $"{ids[i]} chats with a visitor, {o}");
                    Assert.That(bank.Pick(TownTalkKind.Welcome, o, R(ids[i]), v, () => 0.5f), Is.Not.Null, $"{ids[i]} welcomes a visitor, {o}");
                }
                foreach (var o in new[] { TalkOutcome.Joined, TalkOutcome.Neutral, TalkOutcome.Declined })
                    Assert.That(bank.Pick(TownTalkKind.Invite, o, R(ids[i]), v, () => 0.5f), Is.Not.Null, $"{ids[i]} invites a visitor, {o}");
            }
            Assert.That(TownTalkService.NeutralStandsIn(TownTalkKind.Gossip), Is.True);
            Assert.That(TownTalkService.NeutralStandsIn(TownTalkKind.Invite), Is.False, "a refusal is not an ordinary chat");
        }
    }
}
