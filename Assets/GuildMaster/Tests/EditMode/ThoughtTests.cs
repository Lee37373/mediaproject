using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Welfare;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Thoughts and mood (P4-104c, G2): a thought held once per subject, stacking and lasting longer when it comes again,
    /// fading in time; traits colouring what a member feels; mood as the sum, its levels, stress's weight on it, and what
    /// mood does to the keenness for work.
    /// </summary>
    public class ThoughtTests
    {
        [Test]
        public void TheSameThoughtAgain_StacksUpToItsLimit_AndLastsFromNow()
        {
            var held = new List<ThoughtEntry>();
            var def = ThoughtRules.QuestDone; // +6, a day, three at most
            var first = ThoughtRules.Add(held, def, 10f, null, null, null);
            Assert.That((first.stack, first.until), Is.EqualTo((1, 10f + def.Hours)));
            ThoughtRules.Add(held, def, 20f, null, null, null);
            ThoughtRules.Add(held, def, 30f, null, null, null);
            ThoughtRules.Add(held, def, 40f, null, null, null);
            Assert.That(held.Count, Is.EqualTo(1), "one entry for the kind");
            Assert.That((first.stack, first.until), Is.EqualTo((def.MaxStack, 40f + def.Hours)), "stacked to its limit, lasting from the last time");
            Assert.That(ThoughtRules.Weight(first), Is.EqualTo(def.Mood * (1f + ThoughtRules.StackShare * (def.MaxStack - 1))).Within(1e-4f),
                "each repeat half as heavy as the first");
        }

        [Test]
        public void OnePerSubject_AndTheyFade()
        {
            var held = new List<ThoughtEntry>();
            ThoughtRules.Add(held, ThoughtRules.ComradeDiedFriend, 0f, "leo", "레오", null);
            ThoughtRules.Add(held, ThoughtRules.ComradeDiedFriend, 1f, "ria", "리아", null);
            Assert.That(held.Count, Is.EqualTo(2), "a friend each");
            Assert.That(held[0].label, Is.EqualTo("동료 레오가 죽었다"));
            Assert.That(ThoughtRules.Expire(held, ThoughtRules.ComradeDiedFriend.Hours + 0.5f), Is.EqualTo(1), "the first has faded");
            Assert.That(held[0].about, Is.EqualTo("ria"));
        }

        [Test]
        public void Traits_ColourWhatAMemberFeels()
        {
            var brave = new[] { "brave" };
            var coward = new[] { "coward" };
            Assert.That(ThoughtRules.TraitScale(ThoughtTone.Fear, brave), Is.EqualTo(0.5f), "the brave shrug fear off");
            Assert.That(ThoughtRules.TraitScale(ThoughtTone.Fear, coward), Is.EqualTo(1.5f));
            Assert.That(ThoughtRules.TraitScale(ThoughtTone.People, new[] { "sociable" }), Is.EqualTo(1.5f));
            Assert.That(ThoughtRules.TraitScale(ThoughtTone.People, new[] { "loner" }), Is.EqualTo(0.5f));
            Assert.That(ThoughtRules.TraitScale(ThoughtTone.Gain, new[] { "greedy" }), Is.EqualTo(1.5f));
            Assert.That(ThoughtRules.TraitScale(ThoughtTone.Plain, coward), Is.EqualTo(1f), "plain thoughts are everyone's");
            var held = new List<ThoughtEntry>();
            Assert.That(ThoughtRules.Add(held, ThoughtRules.NearDeath, 0f, "슬라임", "슬라임", brave).value, Is.EqualTo(ThoughtRules.NearDeath.Mood * 0.5f));
        }

        [Test]
        public void Mood_IsTheSum_HeldWithinItsRange_AndStressWeighsOnIt()
        {
            var held = new List<ThoughtEntry>();
            Assert.That(ThoughtRules.Mood(held, 0f), Is.EqualTo(ThoughtRules.BaseMood));
            ThoughtRules.Add(held, ThoughtRules.ComradeDiedClose, 0f, "leo", "레오", null);
            Assert.That(ThoughtRules.Mood(held, -5f), Is.EqualTo(ThoughtRules.BaseMood + ThoughtRules.ComradeDiedClose.Mood - 5f).Within(1e-4f));
            Assert.That(ThoughtRules.Mood(held, -100f), Is.EqualTo(0f), "never below nothing");
            Assert.That(ThoughtRules.Mood(held, 200f), Is.EqualTo(100f));

            Assert.That(ThoughtRules.LevelOf(20f), Is.EqualTo(MoodLevel.Breaking));
            Assert.That(ThoughtRules.LevelOf(35f), Is.EqualTo(MoodLevel.Low));
            Assert.That(ThoughtRules.LevelOf(50f), Is.EqualTo(MoodLevel.Steady));
            Assert.That(ThoughtRules.LevelOf(70f), Is.EqualTo(MoodLevel.Good));
            Assert.That(ThoughtRules.LevelOf(90f), Is.EqualTo(MoodLevel.High));

            Assert.That(ThoughtRules.HeavyHeartMood(StressLevel.Calm), Is.Zero);
            Assert.That(ThoughtRules.HeavyHeartMood(StressLevel.Breaking), Is.LessThan(ThoughtRules.HeavyHeartMood(StressLevel.Tense)), "the heavier the stress the lower");
        }

        [Test]
        public void Low_AMemberSometimesSitsWorkOut_TheLowerTheMoreOften()
        {
            Assert.That(ThoughtRules.SitOutChance(ThoughtRules.BaseMood), Is.Zero);
            Assert.That(ThoughtRules.SitOutChance(90f), Is.Zero);
            Assert.That(ThoughtRules.SitOutChance(35f), Is.EqualTo(0.1f));
            Assert.That(ThoughtRules.SitOutChance(10f), Is.EqualTo(0.35f));
        }

        [Test]
        public void Names_GetTheirParticles()
        {
            Assert.That(ThoughtRules.Fill("동료 {0}이(가) 죽었다", "레오"), Is.EqualTo("동료 레오가 죽었다"));
            Assert.That(ThoughtRules.Fill("동료 {0}이(가) 죽었다", "리안"), Is.EqualTo("동료 리안이 죽었다"));
            Assert.That(ThoughtRules.Fill("{0}을(를) 쓰러뜨렸다", "고블린"), Is.EqualTo("고블린을 쓰러뜨렸다"));
            Assert.That(ThoughtRules.Fill("{0}와(과) 같은 파티다", "세라"), Is.EqualTo("세라와 같은 파티다"));
            Assert.That(ThoughtRules.Fill("{0}와(과) 같은 파티다", "카엘"), Is.EqualTo("카엘과 같은 파티다"));
            Assert.That(ThoughtRules.Fill("의뢰를 해냈다", null), Is.EqualTo("의뢰를 해냈다"));
        }

        [Test]
        public void TheCatalogue_HasNoTwoAlike()
        {
            var ids = new HashSet<string>();
            foreach (var d in ThoughtRules.All)
            {
                Assert.That(ids.Add(d.Id), Is.True, d.Id);
                Assert.That(ThoughtRules.Find(d.Id), Is.SameAs(d));
                Assert.That(d.MaxStack, Is.GreaterThanOrEqualTo(1), d.Id);
            }
        }
    }
}
