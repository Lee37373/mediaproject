using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Quests;
using GuildMaster.Traits;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    public class TraitTests
    {
        GameDatabase _db;

        [SetUp]
        public void SetUp()
        {
            EventBus.Clear();
            _db = DefaultContent.Build();
            _db.BuildIndex();
        }

        [TearDown]
        public void TearDown() => EventBus.Clear();

        [Test]
        public void DefaultTraits_HaveUniqueIds_AndSymmetricConflicts()
        {
            Assert.That(_db.traits.Count, Is.GreaterThanOrEqualTo(16));
            Assert.That(_db.traits.Select(t => t.id).Distinct().Count(), Is.EqualTo(_db.traits.Count));
            foreach (var t in _db.traits)
            foreach (var c in t.conflicts)
                Assert.That(c.conflicts, Does.Contain(t), $"{t.id} ↔ {c.id}");
            Assert.That(_db.Trait("brave").ConflictsWith(_db.Trait("coward")), Is.True);
        }

        [Test]
        public void Roll_NeverPicksDuplicateOrConflictingTraits()
        {
            var rng = new GameRandom(7);
            for (int i = 0; i < 2000; i++)
            {
                var rolled = TraitRoller.Roll(rng, _db.traits, 3);
                Assert.That(rolled.Count, Is.EqualTo(3));
                Assert.That(rolled.Distinct().Count(), Is.EqualTo(rolled.Count));
                for (int a = 0; a < rolled.Count; a++)
                for (int b = a + 1; b < rolled.Count; b++)
                    Assert.That(rolled[a].ConflictsWith(rolled[b]), Is.False, $"{rolled[a].id} + {rolled[b].id}");
            }
        }

        [Test]
        public void Roll_CountAndPolarity_FollowDistribution()
        {
            var rng = new GameRandom(11);
            int[] counts = new int[4];
            int positive = 0, total = 0;
            for (int i = 0; i < 5000; i++)
            {
                var rolled = TraitRoller.Roll(rng, _db.traits);
                counts[rolled.Count]++;
                positive += rolled.Count(t => t.isPositive);
                total += rolled.Count;
            }
            Assert.That(counts[0], Is.EqualTo(0));
            Assert.That(counts[1] / 5000f, Is.EqualTo(0.4f).Within(0.03f));
            Assert.That(counts[2] / 5000f, Is.EqualTo(0.4f).Within(0.03f));
            Assert.That(counts[3] / 5000f, Is.EqualTo(0.2f).Within(0.03f));
            Assert.That(positive / (float)total, Is.EqualTo(TraitRoller.PositiveChance).Within(0.04f));
        }

        [Test]
        public void Generator_StoresRolledTraitIds()
        {
            var d = AdventurerGenerator.Generate(new GameRandom(3), 1, _db.traits);
            Assert.That(d.traitIds.Count, Is.InRange(1, 3));
            Assert.That(d.traitIds.All(id => _db.Trait(id) != null), Is.True);
        }

        [Test]
        public void StatModifiers_ApplyToComputedStats()
        {
            var data = new AdventurerData { cls = AdventurerClass.Warrior, level = 1 };
            var plain = AdventurerStats.Compute(data, null);
            var set = TraitSet.FromIds(new[] { "sturdy", "marksman", "nimble", "hawkeye" }, _db);
            var s = AdventurerStats.Compute(data, set.Modifiers);
            Assert.That(s.MaxHp, Is.EqualTo(UnityEngine.Mathf.Round(plain.MaxHp * 1.2f)));
            Assert.That(s.Attack, Is.EqualTo(plain.Attack * 1.15f).Within(1e-4f));
            Assert.That(s.MoveSpeed, Is.EqualTo(plain.MoveSpeed * 1.15f).Within(1e-4f));
            Assert.That(s.SightRadius, Is.EqualTo(plain.SightRadius + 2f).Within(1e-4f));
        }

        [Test]
        public void BehaviourModifiers_Aggregate()
        {
            var set = TraitSet.FromIds(new[] { "coward", "lazy", "quick_learner", "unknown_id" }, _db);
            Assert.That(set.Traits.Count, Is.EqualTo(3));
            Assert.That(set.RetreatThreshold, Is.EqualTo(TraitSet.BaseRetreatThreshold + 0.15f).Within(1e-4f));
            Assert.That(set.FatigueMultiplier, Is.EqualTo(1.4f).Within(1e-4f));
            Assert.That(set.ExpMultiplier, Is.EqualTo(1.3f).Within(1e-4f));
        }

        [Test]
        public void AdjustQuestValue_ReflectsGreedAndDanger()
        {
            var q = new Quest("Q_1", new QuestDraft { Type = QuestType.Hunt, Species = _db.Monster("goblin_warrior"), Count = 3, RewardGold = 100, RewardExp = 20, MaxParty = 1 }, 0f)
            {
                Danger = DangerLevel.Medium,
            };
            const float baseValue = 124f;
            var none = new TraitSet(null);
            Assert.That(none.AdjustQuestValue(q, baseValue), Is.EqualTo(baseValue));
            Assert.That(TraitSet.FromIds(new[] { "greedy" }, _db).AdjustQuestValue(q, baseValue), Is.GreaterThan(baseValue));
            Assert.That(TraitSet.FromIds(new[] { "coward" }, _db).AdjustQuestValue(q, baseValue), Is.LessThan(baseValue));
            Assert.That(TraitSet.FromIds(new[] { "brave" }, _db).AdjustQuestValue(q, baseValue), Is.GreaterThan(baseValue));
        }
    }
}
