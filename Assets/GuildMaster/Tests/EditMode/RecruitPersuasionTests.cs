using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.Recruitment;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>Bannerlord-style recruiting (user decision 2026-10-29): stages of three successes, a no is final.</summary>
    public class RecruitPersuasionTests
    {
        [Test]
        public void TwoStages_OrThreeForAHardVisitor()
        {
            Assert.That(RecruitPersuasion.StagesFor(0f), Is.EqualTo(2));
            Assert.That(RecruitPersuasion.StagesFor(-0.3f), Is.EqualTo(2), "easy to recruit");
            Assert.That(RecruitPersuasion.StagesFor(0.3f), Is.EqualTo(3), "hard to recruit");
        }

        [Test]
        public void ThreeSuccessesClearAStage_AThirdFailureOrACriticalOneLoses()
        {
            int stage = 0, wins = 0, fails = 0;
            Assert.That(RecruitPersuasion.Advance(PersuadeRoll.Success, 2, ref stage, ref wins, ref fails), Is.EqualTo(PersuadeState.Ongoing));
            Assert.That(RecruitPersuasion.Advance(PersuadeRoll.Failure, 2, ref stage, ref wins, ref fails), Is.EqualTo(PersuadeState.Ongoing));
            Assert.That(RecruitPersuasion.Advance(PersuadeRoll.CriticalSuccess, 2, ref stage, ref wins, ref fails), Is.EqualTo(PersuadeState.StageCleared), "a critical counts twice");
            Assert.That((stage, wins, fails), Is.EqualTo((1, 0, 0)), "the next stage starts clean");
            Assert.That(RecruitPersuasion.Advance(PersuadeRoll.Failure, 2, ref stage, ref wins, ref fails), Is.EqualTo(PersuadeState.Ongoing));
            Assert.That(RecruitPersuasion.Advance(PersuadeRoll.Failure, 2, ref stage, ref wins, ref fails), Is.EqualTo(PersuadeState.Ongoing), "two failures are forgiven");
            Assert.That(RecruitPersuasion.Advance(PersuadeRoll.Failure, 2, ref stage, ref wins, ref fails), Is.EqualTo(PersuadeState.Lost), "the third is not");

            stage = 1; wins = 2; fails = 0;
            Assert.That(RecruitPersuasion.Advance(PersuadeRoll.Success, 2, ref stage, ref wins, ref fails), Is.EqualTo(PersuadeState.Won), "the last stage won");
            stage = 0; wins = 2; fails = 0;
            Assert.That(RecruitPersuasion.Advance(PersuadeRoll.CriticalFailure, 3, ref stage, ref wins, ref fails), Is.EqualTo(PersuadeState.Lost), "a critical failure ends it at once");
        }

        [Test]
        public void Chances_FollowTheVisitor_Affinity_AndRepetition()
        {
            var value = RecruitPersuasion.Chance(RecruitReaction.ValueMatch, 40, 0, false);
            var plain = RecruitPersuasion.Chance(RecruitReaction.Friendly, 40, 0, false);
            Assert.That(value.Success, Is.GreaterThan(plain.Success));
            Assert.That(RecruitPersuasion.Chance(RecruitReaction.Friendly, 70, 0, false).Success, Is.GreaterThan(plain.Success), "liked better, easier");
            Assert.That(RecruitPersuasion.Chance(RecruitReaction.Friendly, 40, 0, true).Success, Is.LessThan(plain.Success), "the same again");
            Assert.That(RecruitPersuasion.Chance(RecruitReaction.Friendly, 40, 2, false).Success, Is.LessThan(plain.Success), "later stages are harder");
            Assert.That(RecruitPersuasion.Chance(RecruitReaction.Rude, 0, 2, true).Success, Is.EqualTo(RecruitPersuasion.MinChance));
            Assert.That(RecruitPersuasion.Chance(RecruitReaction.ValueMatch, 100, 0, false).Success, Is.LessThanOrEqualTo(RecruitPersuasion.MaxChance));
            // 2026-10-29: harder than before — a matching pitch at neutral affinity is no sure thing, and the bonus is capped.
            // 2026-10-30: set between the old and the hard numbers (user: "너무 잘 안됨").
            Assert.That(value.Success, Is.EqualTo(78));
            Assert.That(RecruitPersuasion.Chance(RecruitReaction.ValueMatch, 100, 0, false).Success, Is.EqualTo(93));
            Assert.That(RecruitPersuasion.Chance(RecruitReaction.Friendly, 40, 1, false).CriticalFail, Is.EqualTo(10));

            var c = new PersuadeChance { Success = 60, Critical = 10, CriticalFail = 6 };
            Assert.That(RecruitPersuasion.Roll(c, 0.05f), Is.EqualTo(PersuadeRoll.CriticalSuccess));
            Assert.That(RecruitPersuasion.Roll(c, 0.5f), Is.EqualTo(PersuadeRoll.Success));
            Assert.That(RecruitPersuasion.Roll(c, 0.7f), Is.EqualTo(PersuadeRoll.Failure));
            Assert.That(RecruitPersuasion.Roll(c, 0.97f), Is.EqualTo(PersuadeRoll.CriticalFailure));
        }

        [Test]
        public void TheChanceShowsOnceTheGuildMasterKnowsEnough()
        {
            var b = RecruitLines.LoadResources();
            var pitch = b.AllChoices.First(c => c.Kind == RecruitChoiceKind.Pitch);
            var trait = b.AllChoices.First(c => c.Kind == RecruitChoiceKind.Trait && c.Topic == "loyal");
            var guild = b.AllChoices.First(c => c.Kind == RecruitChoiceKind.Guild);
            var none = new List<string>();
            Assert.That(RecruitLines.ChanceKnown(pitch, null, none, true), Is.False);
            Assert.That(RecruitLines.ChanceKnown(pitch, "돈", none, true), Is.True);
            Assert.That(RecruitLines.ChanceKnown(trait, null, none, true), Is.False);
            Assert.That(RecruitLines.ChanceKnown(trait, null, new List<string> { "loyal" }, true), Is.True);
            Assert.That(RecruitLines.ChanceKnown(trait, null, none, false), Is.True, "every trait found out");
            Assert.That(RecruitLines.ChanceKnown(guild, null, none, true), Is.True);
        }

        [Test]
        public void TheAnswerFollowsTheRoll()
        {
            var b = RecruitLines.LoadResources();
            var rng = new GameRandom(4);
            var v = new RecruitLines.VisitorInfo { Values = "돈", TraitIds = new string[0], Mbti = "ISTP", ClassLabel = "궁수", Register = NpcVoice.Register.Curt, TraitName = id => id };
            var money = b.AllChoices.First(c => c.Kind == RecruitChoiceKind.Pitch && c.Topic == "돈");
            var guild = b.AllChoices.First(c => c.Kind == RecruitChoiceKind.Guild);
            string Line(string key) => b.Line(key, NpcVoice.Register.Curt, null);

            Assert.That(b.RespondArgument(money, v, PersuadeRoll.Success, PersuadeState.Ongoing, null, null), Is.EqualTo(Line("keen.돈")), "what they care about");
            Assert.That(b.RespondArgument(guild, v, PersuadeRoll.Success, PersuadeState.Ongoing, null, null), Is.EqualTo(Line("roll.ok")));
            Assert.That(b.RespondArgument(money, v, PersuadeRoll.Failure, PersuadeState.Ongoing, null, null), Is.EqualTo(Line("roll.no")), "unlucky");
            Assert.That(b.RespondArgument(guild, v, PersuadeRoll.Failure, PersuadeState.Ongoing, null, rng), Is.Not.Empty);
            Assert.That(b.RespondArgument(guild, v, PersuadeRoll.Success, PersuadeState.StageCleared, null, null), Is.EqualTo(Line("roll.ok")), "the next stage is a line of its own");
            Assert.That(b.StageNext(NpcVoice.Register.Curt, "돈", null), Is.EqualTo(Line("stage.next.돈")));
            Assert.That(b.StageNext(NpcVoice.Register.Curt, null, null), Is.EqualTo(Line("stage.next")));
            Assert.That(b.RespondArgument(guild, v, PersuadeRoll.Success, PersuadeState.Won, null, null), Is.EqualTo(Line("join")));
            Assert.That(b.RespondArgument(guild, v, PersuadeRoll.Failure, PersuadeState.Lost, null, null), Does.Contain(Line("refuse")));
            Assert.That(b.RespondArgument(guild, v, PersuadeRoll.CriticalFailure, PersuadeState.Lost, null, null), Does.StartWith(Line("roll.offended")));
        }
    }
}
