using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Proposals;
using GuildMaster.Quests;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>The errands and the item besides the gold (P4-133 F-5): prices, counts, the parts asked for, the item pool, who gets it.</summary>
    public class ErrandTests
    {
        [Test]
        public void Prices_PayForTheGoods_TheDanger_AndTheWalk()
        {
            // A dig pays the goods themselves over their worth (they are not sold), and more the farther and more dangerous.
            int near = ErrandRules.GatherGold(15, 5, DangerLevel.Low, 40f);
            Assert.That(near, Is.GreaterThan(15 * 5), "more than selling the ore");
            Assert.That(ErrandRules.GatherGold(15, 5, DangerLevel.Low, 200f), Is.GreaterThan(near));
            Assert.That(ErrandRules.GatherGold(15, 5, DangerLevel.High, 40f), Is.GreaterThan(near));
            Assert.That(ErrandRules.GatherGold(5, 5, DangerLevel.Low, 40f), Is.LessThan(near), "herbs are worth less than ore");
            // Parts: the hunt it takes and the parts' worth.
            Assert.That(ErrandRules.CollectGold(DangerLevel.Medium, 6, 6, 5, 80f), Is.GreaterThan(QuestPricing.HuntReward(DangerLevel.Medium, 6, 80f)));
            // The road: longer and more dangerous pays more; the style scales it.
            int road = ErrandRules.DeliverGold(DangerLevel.Low, 100f);
            Assert.That(ErrandRules.DeliverGold(DangerLevel.Low, 250f), Is.GreaterThan(road));
            Assert.That(ErrandRules.DeliverGold(DangerLevel.High, 100f), Is.GreaterThan(road));
            Assert.That(ErrandRules.DeliverGold(DangerLevel.Low, 100f, 1.4f), Is.GreaterThan(road));
            // EXP: a hunt's for parts, less for a walk or a dig; few hands for a dig.
            Assert.That(ErrandRules.Exp(QuestType.Collect, 200), Is.EqualTo(QuestPricing.RewardExp(200)));
            Assert.That(ErrandRules.Exp(QuestType.Deliver, 200), Is.LessThan(QuestPricing.RewardExp(200)));
            Assert.That((ErrandRules.PartyFor(QuestType.Gather, DangerLevel.Low), ErrandRules.PartyFor(QuestType.Gather, DangerLevel.High)), Is.EqualTo((1, 3)));
            Assert.That(ErrandRules.PartyFor(QuestType.Deliver, DangerLevel.Medium), Is.EqualTo(QuestPricing.PartyFor(DangerLevel.Medium)));
        }

        [Test]
        public void Counts_NeverAskMoreThanTheGroundGives()
        {
            Assert.That(ErrandRules.Count(5, 12), Is.EqualTo(5));
            Assert.That(ErrandRules.Count(5, 3), Is.EqualTo(3));
            Assert.That(ErrandRules.Count(5, 1), Is.EqualTo(0), "too few: not asked");
            Assert.That(ErrandRules.GatherSupply(2, ore: true), Is.EqualTo(2 * Places.PlaceRules.OreMin));
            Assert.That(ErrandRules.GatherSupply(2, ore: false), Is.EqualTo(2 * Places.PlaceRules.HerbMin));
            // What errands already on the board still need is spoken for (2026-11-10: two errands on one vein).
            int one = Places.PlaceRules.OreMin;
            Assert.That(ErrandRules.GatherSupply(1, ore: true, spokenFor: one), Is.EqualTo(0));
            Assert.That(ErrandRules.GatherSupply(2, ore: true, spokenFor: one), Is.EqualTo(one));
            Assert.That(ErrandRules.GatherSupply(1, ore: true, spokenFor: 99), Is.EqualTo(0), "never below nothing");
            Assert.That(ErrandRules.KeptForErrands(1, ore: true, spokenFor: one), Is.True, "the one vein is the errand's");
            Assert.That(ErrandRules.KeptForErrands(2, ore: true, spokenFor: one), Is.False, "one to spare");
            Assert.That(ErrandRules.KeptForErrands(1, ore: true, spokenFor: 0), Is.False, "nothing asked: dig away");
            Assert.That(ErrandRules.CollectKills(5, 0.5f), Is.EqualTo(10));
            Assert.That(ErrandRules.CollectKills(4, 1.275f), Is.EqualTo(4));
            Assert.That(ErrandRules.CollectSupply(5, 1f), Is.EqualTo(6), "what was seen, a little over");
            Assert.That(ErrandRules.CollectSupply(0, 1f), Is.EqualTo(2), "two seen at least");
        }

        [Test]
        public void TheParts_AreAKindsLikeliestLoot_NeverAUniqueOne_NorADragons()
        {
            var db = DefaultContent.Build();
            db.BuildIndex();
            var slime = db.Monster("slime");
            var entry = ErrandRules.CollectEntry(slime);
            Assert.That(entry?.item?.id, Is.EqualTo("slime_jelly"));
            Assert.That(ErrandRules.PerKill(entry), Is.EqualTo(0.85f * 1.5f).Within(0.001f));
            foreach (var m in db.monsters)
            {
                var e = ErrandRules.CollectEntry(m);
                if (m.isDragon || m.isRegionLord) Assert.That(e, Is.Null, m.id);
                if (e == null) continue;
                Assert.That(e.item.IsUnique, Is.False, $"{m.id}: {e.item.id}");
                Assert.That(ErrandRules.PerKill(e), Is.GreaterThanOrEqualTo(ErrandRules.MinPerKill), m.id);
            }
        }

        [Test]
        public void TheItemPool_GrowsWithTheDanger_RareThingsRarely_AndEveryItemExists()
        {
            var db = DefaultContent.Build();
            db.BuildIndex();
            var rng = new GameRandom(133);
            var byDanger = new Dictionary<DangerLevel, List<ItemStack>>();
            foreach (DangerLevel d in new[] { DangerLevel.Low, DangerLevel.Medium, DangerLevel.High })
            {
                var list = byDanger[d] = new List<ItemStack>();
                for (int i = 0; i < 3000; i++) list.Add(ErrandRules.PickReward(d, i % 2 == 0 ? 60f : 220f, rng));
                foreach (var s in list)
                {
                    Assert.That(db.Item(s.itemId), Is.Not.Null, s.itemId);
                    Assert.That(s.count, Is.GreaterThanOrEqualTo(1));
                }
            }
            float Share(DangerLevel d, System.Func<string, bool> pred) => byDanger[d].Count(s => pred(s.itemId)) / 3000f;
            bool Rare(string id) => ErrandRules.RareNear.Contains(id) || ErrandRules.RareFar.Contains(id);
            Assert.That(Share(DangerLevel.Low, id => id == "gear_t2" || id == "gear_t3" || ErrandRules.RareFar.Contains(id) && !ErrandRules.RareNear.Contains(id)), Is.Zero,
                "낮음: first-tier gear at most, the near rare things only");
            Assert.That(Share(DangerLevel.Medium, id => id == "gear_t3"), Is.Zero);
            Assert.That(Share(DangerLevel.Low, Rare), Is.InRange(0.01f, 0.06f));
            Assert.That(Share(DangerLevel.High, Rare), Is.InRange(0.10f, 0.20f));
            Assert.That(Share(DangerLevel.High, id => id == "gear_t3"), Is.InRange(0.06f, 0.14f));
            Assert.That(Share(DangerLevel.Low, id => id == ErrandRules.Potion || id == ErrandRules.Ore || id == ErrandRules.Herb || id == Flares.FlareRules.ItemId),
                Is.GreaterThan(0.8f), "mostly everyday things");
            // Middling work near home never parts with the far rare things.
            var near = Enumerable.Range(0, 2000).Select(_ => ErrandRules.PickReward(DangerLevel.Medium, 60f, rng)).ToList();
            Assert.That(near.Any(s => ErrandRules.RareFar.Contains(s.itemId) && !ErrandRules.RareNear.Contains(s.itemId)), Is.False);
        }

        [Test]
        public void TheItem_GoesToTheOneWhoDidMost_Once_AndNotWhenSettledShort()
        {
            var draft = new QuestDraft
            {
                Type = QuestType.Gather, ItemId = ErrandRules.Ore, Count = 5, Point = Vector2.one, HasPoint = true, RewardGold = 100, RewardExp = 30,
                MaxParty = 2, RewardItem = new ItemStack("gear_t1", 1),
            };
            var q = new Quest("Q_1", draft, 0f);
            var a = new Taker("a");
            var b = new Taker("b");
            q.AddAssignee(a);
            q.AddAssignee(b);
            q.AddContribution(a, 2);
            q.AddContribution(b, 3);
            q.ComputePayouts();
            q.ChooseItemTaker();
            Assert.That(q.ItemTakerId, Is.EqualTo("b"));
            Assert.That(q.TryTakeRewardItem(a, out _), Is.False);
            Assert.That(q.TryTakeRewardItem(b, out var item), Is.True);
            Assert.That((item.itemId, item.count), Is.EqualTo(("gear_t1", 1)));
            Assert.That(q.TryTakeRewardItem(b, out _), Is.False, "once");
            Assert.That(q.Title, Does.StartWith("[채집]"));

            // The den gone with 2 of 5 in: paid two fifths, the fee too, and no item.
            var c = new Quest("Q_2", draft, 0f) { FeeGold = 25 };
            c.AddAssignee(a);
            c.Progress = 2;
            c.SettleShort(2 / 5f);
            c.ComputePayouts();
            c.ChooseItemTaker();
            Assert.That((c.PayGold, c.FeeGold, c.ItemTakerId), Is.EqualTo((40, 10, (string)null)));
            Assert.That(c.ProgressText, Does.Contain("2/5"));
        }

        [Test]
        public void ProposalCards_OfferTheErrands_PricedByStyle_WithAnItem()
        {
            var gather = new ProposalTarget
            {
                Key = "gather:iron_ore", Type = QuestType.Gather, Danger = DangerLevel.Low, Label = "철광석", ItemId = ErrandRules.Ore, ItemName = "철광석",
                ItemValue = 15, Supply = 4, Area = "마을 북쪽 30칸", Distance = 30f, Point = new Vector2(0f, 30f),
            };
            var deliver = new ProposalTarget
            {
                Key = "deliver:1", Type = QuestType.Deliver, Danger = DangerLevel.Low, Label = "하늘 마을", Area = "마을 동쪽 90칸", Distance = 90f,
                Point = new Vector2(90f, 0f),
            };
            var scarce = gather;
            scarce.Key = "gather:herb";
            scarce.Supply = 1;
            var pool = ProposalRules.BuildPool(new[] { gather, deliver, scarce }, 4f, 4, new GameRandom(7));
            Assert.That(pool.Any(p => p.Target.Key == "gather:herb"), Is.False, "too few to ask for");
            var gathers = pool.Where(p => p.Target.Type == QuestType.Gather).ToList();
            Assert.That(gathers, Is.Not.Empty);
            Assert.That(gathers.All(p => p.Count <= 4 && p.Count >= ErrandRules.MinCount), Is.True, "never more than the spots give");
            var road = pool.First(p => p.Target.Type == QuestType.Deliver);
            Assert.That(road.GoodsName, Is.Not.Empty);
            Assert.That(pool.All(p => !string.IsNullOrEmpty(p.RewardItem.itemId) && p.RewardItem.count > 0), Is.True, "a patron pays an item too");
            var d = ProposalRules.ToDraft(road);
            Assert.That((d.Type, d.GoodsName, d.RewardItem.itemId), Is.EqualTo((QuestType.Deliver, road.GoodsName, road.RewardItem.itemId)));
            Assert.That(ProposalTexts.FallbackTitle(road), Does.Contain("하늘 마을"));
            var g = ProposalRules.ToDraft(gathers[0]);
            Assert.That((g.Type, g.ItemId, g.Count), Is.EqualTo((QuestType.Gather, ErrandRules.Ore, gathers[0].Count)));
            Assert.That(ProposalTexts.WhatLine(gathers[0]), Does.Contain("채집"));
        }

        sealed class Taker : IQuestTaker
        {
            public Taker(string id) => TakerId = id;
            public string TakerId { get; }
            public string TakerName => TakerId;
            public bool IsAvailableForQuest => true;
        }
    }
}
