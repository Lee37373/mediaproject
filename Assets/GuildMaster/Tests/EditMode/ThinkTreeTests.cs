using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Quests;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// The think tree (P4-104a, G3): the first layer with anything worth doing decides, needs take the highest, quests are
    /// drawn among the best few in proportion to their scores, and layers past the deciding one are never asked.
    /// </summary>
    public class ThinkTreeTests
    {
        static ThinkOption O(ThinkLayer layer, float score, string label = null)
            => new() { Layer = layer, Score = score, Label = label ?? $"{layer} {score}", Action = AdventurerAction.Idle };

        /// <summary>A source that offers fixed options and counts how often it was asked.</summary>
        sealed class Fixed : IThinkSource
        {
            readonly ThinkOption[] _options;
            public int Asked;

            public Fixed(ThinkLayer layer, params ThinkOption[] options)
            {
                Layer = layer;
                _options = options;
            }

            public ThinkLayer Layer { get; }

            public void Offer(Adventurer a, IReadOnlyList<Quest> available, List<ThinkOption> into)
            {
                Asked++;
                into.AddRange(_options);
            }
        }

        [Test]
        public void Needs_TakeTheHighest_TheFirstOnATie_AndNeverRoll()
        {
            var options = new List<ThinkOption> { O(ThinkLayer.Body, 0.8f), O(ThinkLayer.Body, 1f), O(ThinkLayer.Body, 1f) };
            Assert.That(ThinkTree.Pick(options, 0, () => throw new AssertionException("no dice for needs")), Is.EqualTo(1));
        }

        [Test]
        public void NothingWorthAnything_IsNoPick()
        {
            var options = new List<ThinkOption> { O(ThinkLayer.Work, 0f), O(ThinkLayer.Work, 0f) };
            Assert.That(ThinkTree.Pick(options, 0, () => 0f), Is.EqualTo(-1));
        }

        [Test]
        public void Quests_AreDrawnAmongTheBestFew_InProportionToTheirScores()
        {
            // 100 and 80 are in (70% of the best), 60 is not; 0 never is.
            var options = new List<ThinkOption> { O(ThinkLayer.Work, 60f), O(ThinkLayer.Work, 80f), O(ThinkLayer.Work, 0f), O(ThinkLayer.Work, 100f) };
            var counts = new Dictionary<int, int>();
            for (int i = 0; i < 1000; i++)
            {
                float roll = (i + 0.5f) / 1000f;
                int pick = ThinkTree.Pick(options, 0, () => roll);
                counts[pick] = counts.TryGetValue(pick, out var n) ? n + 1 : 1;
            }
            Assert.That(counts.Keys.OrderBy(k => k), Is.EqualTo(new[] { 1, 3 }), "only the best two are in the draw");
            Assert.That(counts[3], Is.EqualTo(556).Within(2), "100 of 180");
            Assert.That(counts[1], Is.EqualTo(444).Within(2), "80 of 180");
        }

        [Test]
        public void TheDraw_TakesAtMostThree_AndRollsOnlyWithAChoice()
        {
            var many = Enumerable.Range(0, 6).Select(i => O(ThinkLayer.Work, 100f - i)).ToList();
            var picked = new HashSet<int>();
            for (int i = 0; i < 100; i++)
            {
                float roll = i / 100f;
                picked.Add(ThinkTree.Pick(many, 0, () => roll));
            }
            Assert.That(picked, Is.EquivalentTo(new[] { 0, 1, 2 }), "the best three");

            var single = new List<ThinkOption> { O(ThinkLayer.Work, 100f), O(ThinkLayer.Work, 10f) };
            Assert.That(ThinkTree.Pick(single, 0, () => throw new AssertionException("one in the draw: no dice")), Is.EqualTo(0));
        }

        [Test]
        public void TheFirstLayerWithAnythingDecides_AndLaterLayersAreNeverAsked()
        {
            var body = new Fixed(ThinkLayer.Body, O(ThinkLayer.Body, 0f));
            var work = new Fixed(ThinkLayer.Work, O(ThinkLayer.Work, 5f, "의뢰 A"));
            var roam = new Fixed(ThinkLayer.Roam, O(ThinkLayer.Roam, 1f, "사냥"));
            // Offered out of order: the tree still asks by layer.
            var tree = new ThinkTree(new IThinkSource[] { roam, work, body });
            var d = tree.Decide(null, null, () => 0f);
            Assert.That((body.Asked, work.Asked, roam.Asked), Is.EqualTo((1, 1, 0)), "the hunt is never weighed with a quest to take");
            Assert.That(d.Action, Is.EqualTo(AdventurerAction.Idle));
        }

        [Test]
        public void TheRecord_ShowsTheChoiceAgainstItsLayer_AndTheUnfitQuests()
        {
            var record = new ThinkRecord();
            record.Options.AddRange(new[] { O(ThinkLayer.Work, 50f, "의뢰 'B'"), O(ThinkLayer.Work, 100f, "의뢰 'A'"), O(ThinkLayer.Work, 0f, "의뢰 'C'") });
            // 50 is outside the draw (70%): the best is taken, the other still shows against it.
            record.Chosen = ThinkTree.Pick(record.Options, 0, () => 0.99f);
            Assert.That(record.Describe(), Is.EqualTo("의뢰 'A'(고름) · 의뢰 'B' 50% · 안 맞는 의뢰 1건"));
            Assert.That(new ThinkRecord().Describe(), Is.EqualTo("-"), "nothing decided yet");
        }
    }
}
