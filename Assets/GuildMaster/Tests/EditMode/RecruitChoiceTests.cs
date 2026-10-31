using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.Recruitment;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Recruit choices and written answers (user decision 2026-10-28: choices plus free typing).</summary>
    public class RecruitChoiceTests
    {
        static RecruitLines Shipped() => RecruitLines.LoadResources();

        static RecruitLines.VisitorInfo Info(string values, NpcVoice.Register reg = NpcVoice.Register.Warm, params string[] traits)
            => new() { Values = values, TraitIds = traits, Mbti = "ISTP", ClassLabel = "궁수", Register = reg, TraitName = id => id };

        static RecruitChoiceState State(int turns, bool first = false, string known = null) => new()
        {
            FirstTalk = first, PlayerTurns = turns, KnownValues = known, HiddenTraitLeft = true,
            KnownTraits = new List<string>(), Used = new HashSet<string>(),
        };

        [Test]
        public void ShippedLines_HaveEveryAnswerInEverySpeechLevel()
        {
            var b = Shipped();
            Assert.That(b.ChoiceTotal, Is.GreaterThanOrEqualTo(40));
            var keys = new List<string>
            {
                "greet", "about", "guild", "ask.open", "ask.dodge", "invite.notyet", "join", "trait.hit", "trait.miss",
                "confess.personality.E", "confess.personality.I", "confess.trait",
                "roll.ok", "roll.great", "roll.no", "roll.offended", "stage.next", "refuse",
            };
            foreach (var v in RecruitLines.AllValues) { keys.Add("keen." + v); keys.Add("meh." + v); keys.Add("confess.values." + v); keys.Add("stage.next." + v); }
            foreach (var k in keys)
                foreach (NpcVoice.Register r in System.Enum.GetValues(typeof(NpcVoice.Register)))
                    Assert.That(b.Line(k, r, null), Is.Not.Null.And.Not.Empty, k + " " + r);
        }

        /// <summary>A chosen line means what the game's word rules would read in it, so typing it gives the same result.</summary>
        [Test]
        public void ShippedChoices_ReadTheWayTheGameReadsWords()
        {
            foreach (var c in Shipped().AllChoices)
            {
                Assert.That(RuleBasedRecruiter.IsInvitation(c.Text), Is.EqualTo(c.Kind == RecruitChoiceKind.Invite), c.Id);
                Assert.That(RuleBasedRecruiter.Topic(c.Text), Is.EqualTo(c.Kind == RecruitChoiceKind.Pitch ? c.Topic : null), c.Id);
                Assert.That(VisitorReveal.AsksValues(c.Text), Is.EqualTo(c.Kind == RecruitChoiceKind.AskValues), c.Id);
                Assert.That(VisitorReveal.AsksPersonality(c.Text), Is.EqualTo(c.Kind == RecruitChoiceKind.AskPersonality), c.Id);
                Assert.That(VisitorReveal.AsksTraits(c.Text), Is.EqualTo(c.Kind == RecruitChoiceKind.AskTraits), c.Id);
                if (c.Kind == RecruitChoiceKind.Trait)
                    Assert.That(RecruitScoring.WantedTrait(c.Text, new[] { c.Topic }), Is.EqualTo(c.Topic), c.Id);
                foreach (var v in RecruitLines.AllValues)
                {
                    var r = RuleBasedRecruiter.Classify(c.Text, v);
                    Assert.That(r != RecruitReaction.Rude && r != RecruitReaction.Clash && r != RecruitReaction.LowEffort, Is.True, $"{c.Id} for {v}: {r}");
                }
            }
        }

        /// <summary>Bannerlord-style (2026-10-29): three arguments to roll for and one line that only finds things out; no invitation.</summary>
        [Test]
        public void Offer_ThreeArgumentsAndAQuestion_NoInvitation()
        {
            var b = Shipped();
            var rng = new GameRandom(7);
            // Meeting them (user report 2026-10-29): answer the greeting, ask, tell about the guild — no pitches yet.
            var first = b.Offer(State(0, first: true), rng);
            Assert.That(first.Count, Is.EqualTo(RecruitLines.ChoiceCount));
            Assert.That(first[0].Kind, Is.EqualTo(RecruitChoiceKind.Greet), "the greeting is answered first");
            Assert.That(first.Any(c => c.Kind == RecruitChoiceKind.About), Is.True);
            Assert.That(first.Any(c => c.Kind == RecruitChoiceKind.Pitch || c.Kind == RecruitChoiceKind.Trait), Is.False);
            for (int i = 0; i < 20; i++)
            {
                var later = b.Offer(State(1 + i % 5), rng);
                Assert.That(later.Count, Is.EqualTo(RecruitLines.ChoiceCount));
                Assert.That(later.Distinct().Count(), Is.EqualTo(later.Count));
                Assert.That(later.Count(c => RecruitLines.IsArgument(c.Kind)), Is.EqualTo(3));
                Assert.That(later.Any(c => c.Kind == RecruitChoiceKind.Invite), Is.False, "joining is won, not asked for");
            }
        }

        /// <summary>The choices follow what the visitor just said (user report 2026-10-29).</summary>
        [Test]
        public void Offer_PutsWhatTheVisitorAskedForFirst()
        {
            var b = Shipped();
            var rng = new GameRandom(5);
            for (int i = 0; i < 20; i++)
            {
                var s = State(2);
                s.FollowKind = RecruitChoiceKind.Pitch;
                s.FollowTopic = RecruitLines.AllValues[i % RecruitLines.AllValues.Length];
                var offer = b.Offer(s, rng);
                Assert.That(offer.Count, Is.EqualTo(RecruitLines.ChoiceCount));
                Assert.That(offer[0].Kind == RecruitChoiceKind.Pitch && offer[0].Topic == s.FollowTopic, Is.True, s.FollowTopic);
                Assert.That(offer.Count(c => RecruitLines.IsArgument(c.Kind)), Is.EqualTo(3));
                Assert.That(offer.Distinct().Count(), Is.EqualTo(offer.Count));

                var q = State(2);
                q.FollowKind = RecruitChoiceKind.AskValues;
                var asked = b.Offer(q, rng);
                Assert.That(asked[0].Kind, Is.EqualTo(RecruitChoiceKind.AskValues));
                Assert.That(asked.Count(c => RecruitLines.IsArgument(c.Kind)), Is.EqualTo(3));
            }
        }

        [Test]
        public void FollowUp_ReadsTheVisitorsAnswer()
        {
            var b = Shipped();
            RecruitChoice Of(RecruitChoiceKind k, string topic = null) => b.AllChoices.First(c => c.Kind == k && (topic == null || c.Topic == topic));
            Assert.That(RecruitLines.FollowUp(Of(RecruitChoiceKind.Greet), null, PersuadeState.Ongoing, "돈", null, null).kind, Is.EqualTo(RecruitChoiceKind.About));
            Assert.That(RecruitLines.FollowUp(Of(RecruitChoiceKind.About), null, PersuadeState.Ongoing, "돈", null, null).kind, Is.EqualTo(RecruitChoiceKind.AskValues));
            Assert.That(RecruitLines.FollowUp(Of(RecruitChoiceKind.AskValues), null, PersuadeState.Ongoing, "돈", "돈", null), Is.EqualTo(((RecruitChoiceKind?)RecruitChoiceKind.Pitch, "돈")));
            var money = Of(RecruitChoiceKind.Pitch, "돈");
            Assert.That(RecruitLines.FollowUp(money, PersuadeRoll.Success, PersuadeState.Ongoing, "돈", null, null), Is.EqualTo(((RecruitChoiceKind?)RecruitChoiceKind.Pitch, "돈")), "keen: more of the same");
            Assert.That(RecruitLines.FollowUp(money, PersuadeRoll.Failure, PersuadeState.Ongoing, "돈", null, null).kind, Is.Null);
            Assert.That(RecruitLines.FollowUp(money, PersuadeRoll.Success, PersuadeState.StageCleared, "지식", null, "안전"), Is.EqualTo(((RecruitChoiceKind?)RecruitChoiceKind.Pitch, "안전")), "what they asked about next");
            var v = new RecruitLines.VisitorInfo { Values = "지식", Register = NpcVoice.Register.Warm };
            Assert.That(b.StageNext(NpcVoice.Register.Warm, "안전", null), Is.EqualTo(b.Line("stage.next.안전", NpcVoice.Register.Warm, null)));
        }

        /// <summary>2026-10-30: no questions are offered once the visitor has had enough, or for one just dodged.</summary>
        [Test]
        public void Offer_LeavesOutQuestionsTheVisitorWillNotTake()
        {
            var b = Shipped();
            var rng = new GameRandom(9);
            var all = new HashSet<RecruitChoiceKind> { RecruitChoiceKind.AskValues, RecruitChoiceKind.AskPersonality, RecruitChoiceKind.AskTraits };
            for (int i = 0; i < 20; i++)
            {
                var s = State(2 + i % 3);
                s.NoAsk = all;
                s.FollowKind = RecruitChoiceKind.AskValues;
                var offer = b.Offer(s, rng);
                Assert.That(offer.Any(c => all.Contains(c.Kind)), Is.False);
                Assert.That(offer.Count, Is.EqualTo(RecruitLines.ChoiceCount));
                var first = b.Offer(new RecruitChoiceState { FirstTalk = true, Used = new HashSet<string>(), NoAsk = all, KnownTraits = new List<string>() }, rng);
                Assert.That(first.Any(c => all.Contains(c.Kind)), Is.False);
            }
        }

        /// <summary>User report 2026-10-29: a reply should sound like the trait that was won over.</summary>
        [Test]
        public void TraitHit_SoundsLikeThatTrait_AndHabitsShowNowAndThen()
        {
            var b = Shipped();
            foreach (var topic in b.AllChoices.Where(c => c.Kind == RecruitChoiceKind.Trait).Select(c => c.Topic).Distinct())
                foreach (NpcVoice.Register r in System.Enum.GetValues(typeof(NpcVoice.Register)))
                    Assert.That(b.Line("trait.hit." + topic, r, null), Is.Not.Null.And.Not.Empty, topic + " " + r);

            var v = new RecruitLines.VisitorInfo { Values = "지식", TraitIds = new[] { "marksman", "lazy" }, Register = NpcVoice.Register.Formal, TraitName = id => id };
            var aim = b.AllChoices.First(c => c.Kind == RecruitChoiceKind.Trait && c.Topic == "marksman");
            Assert.That(b.RespondArgument(aim, v, PersuadeRoll.Success, PersuadeState.Ongoing, null, null),
                        Is.EqualTo(b.Line("trait.hit.marksman", NpcVoice.Register.Formal, null)), "a marksman answers as a marksman");

            Assert.That(RecruitLines.Tic(new[] { "lazy" }, "좋아요.", null), Is.EqualTo("좋아요."), "no dice, no habit");
            Assert.That(RecruitLines.Tic(new[] { "brave" }, "좋아요.", new GameRandom(1)), Is.EqualTo("좋아요."));
            Assert.That(RecruitLines.Tic(new[] { "lazy" }, "하암… 편한 게 좋아요.", new GameRandom(1)), Is.EqualTo("하암… 편한 게 좋아요."), "not twice");
            var rng = new GameRandom(2);
            int yawns = 0;
            for (int i = 0; i < 200; i++) if (RecruitLines.Tic(new[] { "lazy" }, "좋아요.", rng).StartsWith("하암")) yawns++;
            Assert.That(yawns, Is.InRange(30, 100), "sometimes, not always");
        }

        [Test]
        public void Offer_OnceTheirValuesAreKnown_ThatPitchComesUpMostTurns()
        {
            var b = Shipped();
            var rng = new GameRandom(3);
            int hits = 0;
            for (int i = 0; i < 40; i++)
                if (b.Offer(State(3, known: "안전"), rng).Any(c => c.Kind == RecruitChoiceKind.Pitch && c.Topic == "안전")) hits++;
            Assert.That(hits, Is.GreaterThan(20));
        }

        [Test]
        public void Offer_SaidLinesAreNotOfferedAgainWhileOthersAreLeft()
        {
            var b = Shipped();
            var rng = new GameRandom(11);
            var s = State(0, first: true);
            var greet = b.Offer(s, rng).First(c => c.Kind == RecruitChoiceKind.Greet);
            s.Used.Add(greet.Id);
            for (int i = 0; i < 10; i++)
                Assert.That(b.Offer(s, rng).Any(c => c.Id == greet.Id), Is.False);
        }

        [Test]
        public void Respond_FollowsWhoTheVisitorIs()
        {
            var b = Shipped();
            var rng = new GameRandom(1);
            var money = b.AllChoices.First(c => c.Kind == RecruitChoiceKind.Pitch && c.Topic == "돈");
            Assert.That(b.Respond(money, Info("돈"), 40, null, rng).Reaction, Is.EqualTo(RecruitReaction.ValueMatch));
            Assert.That(b.Respond(money, Info("지식"), 40, null, rng).Reaction, Is.EqualTo(RecruitReaction.Friendly));

            var trait = b.AllChoices.First(c => c.Kind == RecruitChoiceKind.Trait && c.Topic == "loyal");
            Assert.That(b.Respond(trait, Info("지식", NpcVoice.Register.Warm, "loyal"), 40, null, rng).Reaction, Is.EqualTo(RecruitReaction.TraitMatch));
            Assert.That(RecruitLines.WantedTrait(trait, new[] { "loyal" }), Is.EqualTo("loyal"));
            Assert.That(b.Respond(trait, Info("지식"), 40, null, rng).Reaction, Is.EqualTo(RecruitReaction.Friendly));

            var invite = b.AllChoices.First(c => c.Kind == RecruitChoiceKind.Invite);
            Assert.That(b.Respond(invite, Info("돈"), 70, null, rng).Decision, Is.EqualTo(RecruitDecision.Join));
            Assert.That(b.Respond(invite, Info("돈"), 30, null, rng).Decision, Is.EqualTo(RecruitDecision.Undecided));

            var ask = b.AllChoices.First(c => c.Kind == RecruitChoiceKind.AskValues);
            var haughty = Info("돈", NpcVoice.Register.Haughty);
            string confession = b.Confession(VisitorReveal.Values, haughty);
            Assert.That(confession, Is.Not.Empty);
            Assert.That(b.Respond(ask, haughty, 30, new List<string>(), rng).Text, Does.Not.Contain(confession), "asked too early: dodges");
            Assert.That(b.Respond(ask, haughty, 50, new List<string> { VisitorReveal.Values }, rng).Text, Does.Contain(confession));

            var about = b.AllChoices.First(c => c.Kind == RecruitChoiceKind.About);
            string intro = b.Respond(about, Info("돈"), 40, null, rng).Text;
            Assert.That(intro, Does.Not.Contain("{class}"));
            Assert.That(intro, Does.Contain("궁수"));
        }
    }
}
