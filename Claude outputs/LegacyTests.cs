using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using GuildMaster.Items;
using GuildMaster.Legacy;
using GuildMaster.Notifications;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>What death leaves behind (F-6): relics, memorials, the last stand and the wound, wording, and the record keeping.</summary>
    public class LegacyTests
    {
        [SetUp]
        public void SetUp() => EventBus.Clear();

        [TearDown]
        public void TearDown() => EventBus.Clear();

        static FallenRecord Record(float diedAt = 100f, params RelicItem[] relics)
        {
            var rec = new FallenRecord { Name = "바론", ClassLabel = "전사", Values = "명예", Level = 3, Day = 5, DiedAtHour = diedAt };
            rec.Relics.AddRange(relics);
            return rec;
        }

        static RelicItem Relic(string id, string name, int count = 1, float x = 1f, float y = 2f)
            => new() { ItemId = id, Name = name, Count = count, Position = new Vector2(x, y), State = RelicState.Lying };

        // ---------------- Rules ----------------

        [Test]
        public void RelicHours_MatchTheGroundItemsLifetime() => Assert.That(LegacyRules.RelicHours, Is.EqualTo(GroundItemService.LifetimeHours));

        [Test]
        public void MemorialCost_GrowsWithLevel_AndNeverDropsBelowTheBase()
        {
            Assert.That(LegacyRules.MemorialCost(1), Is.EqualTo(LegacyRules.MemorialBaseCost + LegacyRules.MemorialCostPerLevel));
            Assert.That(LegacyRules.MemorialCost(5), Is.GreaterThan(LegacyRules.MemorialCost(2)));
            Assert.That(LegacyRules.MemorialCost(0), Is.EqualTo(LegacyRules.MemorialCost(1)));
        }

        [Test]
        public void TreatCost_GrowsWithLevel()
        {
            Assert.That(LegacyRules.TreatCost(1), Is.EqualTo(LegacyRules.TreatBaseCost + LegacyRules.TreatCostPerLevel));
            Assert.That(LegacyRules.TreatCost(8), Is.GreaterThan(LegacyRules.TreatCost(3)));
        }

        [Test]
        public void LastStandChance_StartsAtTheBase_AndRisesWithLevel()
        {
            Assert.That(LegacyRules.LastStandChance(1, "이익"), Is.EqualTo(LegacyRules.LastStandBase).Within(0.0001f));
            Assert.That(LegacyRules.LastStandChance(6, "이익"), Is.GreaterThan(LegacyRules.LastStandChance(1, "이익")));
        }

        [Test]
        public void LastStandChance_LevelBonusIsCapped()
        {
            float high = LegacyRules.LastStandChance(50, "이익");
            Assert.That(high, Is.EqualTo(LegacyRules.LastStandBase + LegacyRules.LastStandMaxLevelBonus).Within(0.0001f));
            Assert.That(LegacyRules.LastStandChance(500, "이익"), Is.EqualTo(high).Within(0.0001f));
        }

        [Test]
        public void LastStandChance_HonourAndComradesHoldOnMoreOften()
        {
            float plain = LegacyRules.LastStandChance(4, "이익");
            Assert.That(LegacyRules.LastStandChance(4, "명예"), Is.EqualTo(plain + LegacyRules.LastStandSpiritBonus).Within(0.0001f));
            Assert.That(LegacyRules.LastStandChance(4, "동료"), Is.EqualTo(plain + LegacyRules.LastStandSpiritBonus).Within(0.0001f));
        }

        [Test]
        public void LastStandChance_NeverExceedsTheMaximum_AndIsNeverCertain()
        {
            Assert.That(LegacyRules.LastStandChance(99, "명예"), Is.LessThanOrEqualTo(LegacyRules.LastStandMaxChance));
            Assert.That(LegacyRules.LastStandMaxChance, Is.LessThan(1f));
        }

        [Test]
        public void LastStand_HasACooldown()
        {
            Assert.That(LegacyRules.CanLastStand(100f, -999f), Is.True);
            Assert.That(LegacyRules.CanLastStand(100f, 100f - LegacyRules.LastStandCooldownHours + 1f), Is.False);
            Assert.That(LegacyRules.CanLastStand(100f, 100f - LegacyRules.LastStandCooldownHours), Is.True);
        }

        [Test]
        public void TheWoundIsShorterThanTheCooldown_SoTheBrushWithDeathCannotChain()
            => Assert.That(LegacyRules.WoundedHours, Is.LessThan(LegacyRules.LastStandCooldownHours));

        [Test]
        public void ANewMemberHasNeverHadALastStand()
        {
            var d = new AdventurerData();
            Assert.That(LegacyRules.CanLastStand(0f, d.lastStandHour), Is.True);
            Assert.That(d.woundedUntilHour, Is.EqualTo(0f));
        }

        // ---------------- Records ----------------

        [Test]
        public void Relics_StartLying_AndAreCounted()
        {
            var rec = Record(100f, Relic("sword", "검"), Relic("horn", "뿔", 2));
            Assert.That(rec.HasRelics, Is.True);
            Assert.That(rec.HasLying, Is.True);
            Assert.That(rec.Count(RelicState.Lying), Is.EqualTo(2));
            Assert.That(rec.Count(RelicState.Recovered), Is.EqualTo(0));
        }

        [Test]
        public void AnEmptyRecord_HasNoRelics()
        {
            var rec = Record();
            Assert.That(rec.HasRelics, Is.False);
            Assert.That(rec.HasLying, Is.False);
            Assert.That(rec.FirstLying(), Is.Null);
        }

        [Test]
        public void MarkRecovered_RecordsWhoBroughtItBack()
        {
            var relic = Relic("sword", "검");
            var rec = Record(100f, relic);
            rec.MarkRecovered(relic, "리나");
            Assert.That(relic.State, Is.EqualTo(RelicState.Recovered));
            Assert.That(relic.Note, Is.EqualTo("리나"));
            Assert.That(rec.HasLying, Is.False);
        }

        [Test]
        public void MarkLost_KeepsWhy()
        {
            var relic = Relic("sword", "검");
            var rec = Record(100f, relic);
            rec.MarkLost(relic, "슬라임에게 삼켜졌다");
            Assert.That(relic.State, Is.EqualTo(RelicState.Lost));
            Assert.That(relic.Note, Is.EqualTo("슬라임에게 삼켜졌다"));
        }

        [Test]
        public void ARelicIsResolvedOnlyOnce()
        {
            var relic = Relic("sword", "검");
            var rec = Record(100f, relic);
            rec.MarkRecovered(relic, "리나");
            rec.MarkLost(relic, "사라졌다");
            Assert.That(relic.State, Is.EqualTo(RelicState.Recovered));
            Assert.That(relic.Note, Is.EqualTo("리나"));
            rec.MarkRecovered(null, "누군가");
        }

        [Test]
        public void FirstLying_SkipsWhatIsResolved()
        {
            var a = Relic("sword", "검");
            var b = Relic("horn", "뿔");
            var rec = Record(100f, a, b);
            rec.MarkRecovered(a, "리나");
            Assert.That(rec.FirstLying(), Is.SameAs(b));
        }

        [Test]
        public void HoursLeft_CountsDownToZero()
        {
            var rec = Record(100f, Relic("sword", "검"));
            Assert.That(rec.HoursLeft(100f), Is.EqualTo(LegacyRules.RelicHours).Within(0.001f));
            Assert.That(rec.HoursLeft(120f), Is.EqualTo(LegacyRules.RelicHours - 20f).Within(0.001f));
            Assert.That(rec.HoursLeft(100f + LegacyRules.RelicHours + 5f), Is.EqualTo(0f));
        }

        // ---------------- The service (what needs no adventurer) ----------------

        GuildLegacy NewLegacy(NotificationCenter center = null) => new(null, null, new GuildTreasury(500, 0, 0f, 0), center);

        [Test]
        public void Add_NumbersTheRecords_AndFindsThem()
        {
            using var legacy = NewLegacy();
            var a = legacy.Add(Record());
            var b = legacy.Add(Record());
            Assert.That(b.Id, Is.EqualTo(a.Id + 1));
            Assert.That(legacy.Find(a.Id), Is.SameAs(a));
            Assert.That(legacy.Find(999), Is.Null);
            Assert.That(legacy.Fallen.Count, Is.EqualTo(2));
        }

        [Test]
        public void ARelicTakenByAMonster_IsLost_AndTheGuildIsTold()
        {
            var center = new NotificationCenter();
            using var legacy = NewLegacy(center);
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.id = "sword";
            item.displayName = "검";
            var relic = Relic("sword", "검", 1, 3f, 4f);
            var rec = legacy.Add(Record(100f, relic));

            EventBus.Publish(new GroundItemTakenEvent { Item = item, Count = 1, Taker = null, Position = new Vector2(3f, 4f), Absorbed = false });

            Assert.That(relic.State, Is.EqualTo(RelicState.Lost));
            Assert.That(rec.HasLying, Is.False);
            Assert.That(center.Entries.Count, Is.EqualTo(1));
            Assert.That(center.Entries[0].Title, Is.EqualTo(LegacyTexts.LostTitle));
            Object.DestroyImmediate(item);
        }

        [Test]
        public void ARelicSwallowedBySlime_IsLost()
        {
            using var legacy = NewLegacy();
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.id = "horn";
            var relic = Relic("horn", "뿔", 1, 1f, 1f);
            legacy.Add(Record(100f, relic));

            EventBus.Publish(new GroundItemTakenEvent { Item = item, Count = 1, Taker = null, Position = new Vector2(1f, 1f), Absorbed = true });

            Assert.That(relic.State, Is.EqualTo(RelicState.Lost));
            Assert.That(relic.Note, Does.Contain("슬라임"));
            Object.DestroyImmediate(item);
        }

        [Test]
        public void AnItemElsewhere_IsNotTakenForARelic()
        {
            using var legacy = NewLegacy();
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.id = "sword";
            var relic = Relic("sword", "검", 1, 3f, 4f);
            legacy.Add(Record(100f, relic));

            EventBus.Publish(new GroundItemTakenEvent { Item = item, Count = 1, Taker = null, Position = new Vector2(9f, 9f), Absorbed = false });

            Assert.That(relic.State, Is.EqualTo(RelicState.Lying));
            Object.DestroyImmediate(item);
        }

        [Test]
        public void ADifferentItemAtTheSameSpot_IsNotTakenForARelic()
        {
            using var legacy = NewLegacy();
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.id = "horn";
            var relic = Relic("sword", "검", 1, 3f, 4f);
            legacy.Add(Record(100f, relic));

            EventBus.Publish(new GroundItemTakenEvent { Item = item, Count = 1, Taker = null, Position = new Vector2(3f, 4f), Absorbed = false });

            Assert.That(relic.State, Is.EqualTo(RelicState.Lying));
            Object.DestroyImmediate(item);
        }

        [Test]
        public void CannotHoldAMemorialTwice_OrForNobody()
        {
            using var legacy = NewLegacy();
            Assert.That(legacy.CanHoldMemorial(null).Ok, Is.False);
            var rec = legacy.Add(Record());
            rec.MemorialHeld = true;
            var again = legacy.CanHoldMemorial(rec);
            Assert.That(again.Ok, Is.False);
            Assert.That(again.Reason, Is.Not.Empty);
        }

        [Test]
        public void CannotSendAnyoneForRelicsThatAreGone()
        {
            using var legacy = NewLegacy();
            Assert.That(legacy.CanSendRetriever(null).Ok, Is.False);
            var relic = Relic("sword", "검");
            var rec = legacy.Add(Record(100f, relic));
            rec.MarkLost(relic, "사라졌다");
            var check = legacy.CanSendRetriever(rec);
            Assert.That(check.Ok, Is.False);
            Assert.That(check.Reason, Is.Not.Empty);
        }

        [Test]
        public void NobodyIsWoundedWithoutAnAdventurer()
        {
            using var legacy = NewLegacy();
            Assert.That(legacy.IsWounded(null), Is.False);
            Assert.That(legacy.WoundedHoursLeft(null), Is.EqualTo(0f));
            Assert.That(legacy.CanTreat(null).Ok, Is.False);
        }

        [Test]
        public void Dispose_StopsListening()
        {
            var legacy = NewLegacy();
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.id = "sword";
            var relic = Relic("sword", "검", 1, 3f, 4f);
            legacy.Add(Record(100f, relic));
            legacy.Dispose();

            EventBus.Publish(new GroundItemTakenEvent { Item = item, Count = 1, Taker = null, Position = new Vector2(3f, 4f), Absorbed = false });

            Assert.That(relic.State, Is.EqualTo(RelicState.Lying));
            Assert.That(legacy.Fallen.Count, Is.EqualTo(0));
            Object.DestroyImmediate(item);
        }

        // ---------------- Wording ----------------

        [Test]
        public void ItemLabel_ShowsTheCountOnlyForStacks()
        {
            Assert.That(LegacyTexts.ItemLabel("검", 1), Is.EqualTo("검"));
            Assert.That(LegacyTexts.ItemLabel("뿔", 3), Is.EqualTo("뿔 x3"));
        }

        [Test]
        public void Killer_HandlesAnUnknownKiller()
        {
            Assert.That(LegacyTexts.Killer(null), Is.EqualTo("쓰러졌다"));
            Assert.That(LegacyTexts.Killer("고블린"), Does.Contain("고블린"));
        }

        [Test]
        public void RelicLine_DescribesWhereEverythingStands()
        {
            var lying = Relic("sword", "검");
            var back = Relic("horn", "뿔");
            var lost = Relic("cloth", "천");
            var rec = Record(100f, lying, back, lost);
            rec.MarkRecovered(back, "리나");
            rec.MarkLost(lost, "사라졌다");
            string line = LegacyTexts.RelicLine(rec, 110f);
            Assert.That(line, Does.Contain("현장에 1개"));
            Assert.That(line, Does.Contain("되찾음 1개"));
            Assert.That(line, Does.Contain("잃음 1개"));
            Assert.That(line, Does.Contain($"{Mathf.CeilToInt(LegacyRules.RelicHours - 10f)}시간"));
        }

        [Test]
        public void RelicLine_SaysWhenNothingWasLeft()
        {
            Assert.That(LegacyTexts.RelicLine(Record(), 100f), Does.Contain("없"));
            Assert.That(LegacyTexts.RelicLine(null, 100f), Does.Contain("없"));
        }

        [Test]
        public void RelicList_NamesEveryItemAndItsFate()
        {
            var a = Relic("sword", "검");
            var b = Relic("horn", "뿔", 2);
            var rec = Record(100f, a, b);
            rec.MarkRecovered(b, "리나");
            string list = LegacyTexts.RelicList(rec);
            Assert.That(list, Does.Contain("검"));
            Assert.That(list, Does.Contain("현장에 있음"));
            Assert.That(list, Does.Contain("뿔 x2"));
            Assert.That(list, Does.Contain("리나"));
            Assert.That(LegacyTexts.RelicList(Record()), Is.Empty);
        }

        [Test]
        public void Notices_NameTheDeadAndTheDeadline()
        {
            string notice = LegacyTexts.RelicNotice("바론", 3, 47.2f);
            Assert.That(notice, Does.Contain("바론"));
            Assert.That(notice, Does.Contain("3개"));
            Assert.That(notice, Does.Contain("48시간"));
        }

        [Test]
        public void LastStandNotice_NamesTheKiller_WhenKnown()
        {
            Assert.That(LegacyTexts.LastStandNotice("바론", "오크", 12f), Does.Contain("오크"));
            string unknown = LegacyTexts.LastStandNotice("바론", null, 12f);
            Assert.That(unknown, Does.Contain("바론"));
            Assert.That(unknown, Does.Contain("12시간"));
        }

        [Test]
        public void MemorialDone_MentionsTheFriends_OnlyWhenThereWereAny()
        {
            Assert.That(LegacyTexts.MemorialDone("바론", 70, 5, 2), Does.Contain("2명"));
            Assert.That(LegacyTexts.MemorialDone("바론", 70, 5, 0), Does.Not.Contain("가까웠던"));
            Assert.That(LegacyTexts.MemorialDone("바론", 70, 5, 0), Does.Contain("-70G"));
        }

        [Test]
        public void Summary_HasAnEmptyState() => Assert.That(LegacyTexts.Summary(0), Does.Contain("없"));

        [Test]
        public void EveryLine_IsNotEmpty()
        {
            var rng = new GameRandom(1);
            Assert.That(LegacyTexts.LastStandLine(rng), Is.Not.Empty);
            Assert.That(LegacyTexts.RetrieverLine("바론"), Does.Contain("바론"));
            Assert.That(LegacyTexts.WoundedRefusal, Is.Not.Empty);
            Assert.That(LegacyTexts.MemoryLastStand(null), Is.Not.Empty);
            Assert.That(LegacyTexts.MemoryLastStand("오크"), Does.Contain("오크"));
            Assert.That(LegacyTexts.MemoryRelic("바론"), Does.Contain("바론"));
            Assert.That(LegacyTexts.MemoryRelicHome("바론"), Does.Contain("바론"));
            Assert.That(LegacyTexts.MemoryMemorial("바론"), Does.Contain("바론"));
            Assert.That(LegacyTexts.MemoryMemorialFriend("바론"), Does.Contain("바론"));
            Assert.That(LegacyTexts.MemoryTreated(45), Does.Contain("45G"));
            Assert.That(LegacyTexts.SentRetriever("리나", "바론"), Does.Contain("리나"));
            Assert.That(LegacyTexts.Treated("리나", 45, 70), Does.Contain("70%"));
            Assert.That(LegacyTexts.Recovered("리나", "바론", "검"), Does.Contain("검"));
            Assert.That(LegacyTexts.LostTo("바론", "검", "오크"), Does.Contain("오크"));
            Assert.That(LegacyTexts.LostRotted("바론", "검"), Does.Contain("검"));
        }

        [Test]
        public void KoreanParticles_FollowTheLastSyllable()
        {
            // 바론 ends in a consonant, 리나 in a vowel.
            Assert.That(LegacyTexts.SentRetriever("바론", "리나"), Does.StartWith("바론이"));
            Assert.That(LegacyTexts.SentRetriever("리나", "바론"), Does.StartWith("리나가"));
        }
    }
}
