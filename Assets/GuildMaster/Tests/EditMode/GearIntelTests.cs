using System.Collections.Generic;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Quests;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>P4-22: what the guild saw on monsters, folded into one group summary and a danger step.</summary>
    public class GearIntelTests
    {
        GameDatabase _db;
        ItemDefinition _dagger, _helm, _crown;

        [SetUp]
        public void SetUp()
        {
            _db = DefaultContent.Build();
            _db.BuildIndex();
            _dagger = _db.Item("rusty_dagger");
            _helm = _db.Item("goblin_helm");
            _crown = _db.Item("crown_shard");
        }

        static GuildIntel.GearSighting Seen(float hour, float absorbed, params ItemDefinition[] worn)
            => new() { Worn = worn, AbsorbedHp = absorbed, Hour = hour };

        [Test]
        public void Aggregate_CountsWearersAndKeepsTheLatestSighting()
        {
            var gear = GuildIntel.Aggregate(new List<GuildIntel.GearSighting>
            {
                Seen(10f, 0f, _helm, _dagger),
                Seen(14f, 0f, _helm),
                Seen(12f, 0f), // seen carrying nothing
            });

            Assert.That(gear.GearedCount, Is.EqualTo(2), "only monsters wearing something count");
            Assert.That(gear.HasCrown, Is.False);
            Assert.That(gear.LastSeenHour, Is.EqualTo(14f));
            Assert.That(gear.Items[0].item, Is.EqualTo(_helm), "most common item first");
            Assert.That(gear.Items[0].count, Is.EqualTo(2));
            Assert.That(gear.Items[1].count, Is.EqualTo(1));
        }

        [Test]
        public void Aggregate_TakesTheHighestAbsorbedHpAndNoticesTheCrown()
        {
            var gear = GuildIntel.Aggregate(new[] { Seen(3f, 12f), Seen(5f, 30f), Seen(4f, 0f, _crown) });

            Assert.That(gear.AbsorbedHp, Is.EqualTo(30f));
            Assert.That(gear.HasCrown, Is.True);
            Assert.That(gear.GearedCount, Is.EqualTo(1), "swallowed items are not worn");
            Assert.That(gear.Any, Is.True);
        }

        [Test]
        public void Aggregate_NothingSeenIsEmpty()
        {
            var gear = GuildIntel.Aggregate(new GuildIntel.GearSighting[0]);
            Assert.That(gear.Any, Is.False);
            Assert.That(GuildIntel.RaisesDanger(gear), Is.False);
        }

        [Test]
        public void RaisesDanger_NeedsTwoArmedMonstersOrACrown()
        {
            Assert.That(GuildIntel.RaisesDanger(GuildIntel.Aggregate(new[] { Seen(1f, 0f, _dagger) })), Is.False, "one armed goblin is not enough");
            Assert.That(GuildIntel.RaisesDanger(GuildIntel.Aggregate(new[] { Seen(1f, 0f, _dagger), Seen(2f, 0f, _helm) })), Is.True);
            Assert.That(GuildIntel.RaisesDanger(GuildIntel.Aggregate(new[] { Seen(1f, 0f, _crown) })), Is.True, "a crown wearer alone is enough");
            Assert.That(GuildIntel.RaisesDanger(GuildIntel.Aggregate(new[] { Seen(1f, 40f) })), Is.False, "swallowed items do not raise the step");
        }

        [Test]
        public void Raise_StepsUpButNeverPastHigh()
        {
            Assert.That(QuestBoard.Raise(DangerLevel.Low, 1), Is.EqualTo(DangerLevel.Medium));
            Assert.That(QuestBoard.Raise(DangerLevel.Low, 2), Is.EqualTo(DangerLevel.High));
            Assert.That(QuestBoard.Raise(DangerLevel.Medium, 2), Is.EqualTo(DangerLevel.High), "capped at high");
            Assert.That(QuestBoard.Raise(DangerLevel.Medium, 0), Is.EqualTo(DangerLevel.Medium));
        }
    }
}
