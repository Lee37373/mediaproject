using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.LLM;
using GuildMaster.Notifications;
using GuildMaster.Proposals;
using GuildMaster.Quests;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>The rule half and the prompt half of the LLM quest proposals (F-8).</summary>
    public class ProposalTests
    {
        static ProposalTarget Hunt(string key, DangerLevel danger, float distance = 20f, int observed = 10) => new()
        {
            Key = "hunt:" + key, Type = QuestType.Hunt, Danger = danger, Label = key, Area = $"마을 북쪽 {(int)distance}칸",
            Distance = distance, Observed = observed, Point = new Vector2(distance, 0f),
        };

        static ProposalTarget NestTarget(string key, DangerLevel danger, float distance = 30f) => new()
        {
            Key = "nest:" + key, Type = QuestType.ClearNest, Danger = danger, Label = key, Area = $"마을 동쪽 {(int)distance}칸",
            Distance = distance, Point = new Vector2(distance, 5f),
        };

        static ProposalPlan Plan(string key, ProposalStyle style, float distance) => new()
        {
            Target = Hunt(key, DangerLevel.Low, distance), Style = style, Danger = DangerLevel.Low, Count = 3, Party = 1,
            RewardGold = 40, BaseRewardGold = 40, RewardExp = 25, Risk = QuestRisk.Easy,
        };

        /// <summary>Strong enough that low and medium dangers are comfortable (power 4, four members).</summary>
        static List<ProposalPlan> Pool(params ProposalTarget[] targets)
            => ProposalRules.BuildPool(targets, 4f, 4, 5000, 5000);

        // ---------------- Numbers ----------------

        [Test]
        public void EffectiveDanger_MirrorsTheQuestBoard()
        {
            Assert.That(ProposalRules.EffectiveDanger(QuestType.Hunt, DangerLevel.Low, 8), Is.EqualTo(DangerLevel.Medium));
            Assert.That(ProposalRules.EffectiveDanger(QuestType.Hunt, DangerLevel.Low, 7), Is.EqualTo(DangerLevel.Low));
            Assert.That(ProposalRules.EffectiveDanger(QuestType.Hunt, DangerLevel.High, 12), Is.EqualTo(DangerLevel.High));
            Assert.That(ProposalRules.EffectiveDanger(QuestType.ClearNest, DangerLevel.Low, 8), Is.EqualTo(DangerLevel.Low));
        }

        [Test]
        public void Rewards_GrowWithStyleAndDanger_AndAreWholeFives()
        {
            int safe = ProposalRules.RewardGold(QuestType.Hunt, DangerLevel.Low, 5, 20f, ProposalStyle.Safe);
            int balanced = ProposalRules.RewardGold(QuestType.Hunt, DangerLevel.Low, 5, 20f, ProposalStyle.Balanced);
            int bold = ProposalRules.RewardGold(QuestType.Hunt, DangerLevel.Low, 5, 20f, ProposalStyle.Bold);
            Assert.That(safe, Is.LessThan(balanced));
            Assert.That(balanced, Is.LessThan(bold));

            int medium = ProposalRules.RewardGold(QuestType.Hunt, DangerLevel.Medium, 5, 20f, ProposalStyle.Balanced);
            int high = ProposalRules.RewardGold(QuestType.Hunt, DangerLevel.High, 5, 20f, ProposalStyle.Balanced);
            Assert.That(balanced, Is.LessThan(medium));
            Assert.That(medium, Is.LessThan(high));

            foreach (int g in new[] { safe, balanced, bold, medium, high })
            {
                Assert.That(g % 5, Is.EqualTo(0));
                Assert.That(g, Is.GreaterThanOrEqualTo(10));
            }
        }

        [Test]
        public void Rewards_AreBiggerForFarTargets_AndForNests()
        {
            int near = ProposalRules.RewardGold(QuestType.Hunt, DangerLevel.Medium, 5, 5f, ProposalStyle.Balanced);
            int far = ProposalRules.RewardGold(QuestType.Hunt, DangerLevel.Medium, 5, 50f, ProposalStyle.Balanced);
            int nest = ProposalRules.RewardGold(QuestType.ClearNest, DangerLevel.Medium, 1, 5f, ProposalStyle.Balanced);
            Assert.That(far, Is.GreaterThan(near));
            Assert.That(nest, Is.GreaterThan(near));
        }

        [Test]
        public void RewardExp_FollowsGold_WithAFloor()
        {
            Assert.That(ProposalRules.RewardExp(100), Is.EqualTo(60));
            Assert.That(ProposalRules.RewardExp(10), Is.GreaterThanOrEqualTo(5));
        }

        [Test]
        public void PartyFor_SafeWantsMoreCompany_ThanBold()
        {
            int safe = ProposalRules.PartyFor(ProposalStyle.Safe, 4f, DangerLevel.Medium, 4);
            int balanced = ProposalRules.PartyFor(ProposalStyle.Balanced, 4f, DangerLevel.Medium, 4);
            int bold = ProposalRules.PartyFor(ProposalStyle.Bold, 4f, DangerLevel.Medium, 4);
            Assert.That(safe, Is.EqualTo(2));
            Assert.That(balanced, Is.EqualTo(1));
            Assert.That(bold, Is.EqualTo(1));
            Assert.That(safe, Is.GreaterThanOrEqualTo(balanced));
            Assert.That(balanced, Is.GreaterThanOrEqualTo(bold));
        }

        [Test]
        public void PartyFor_NeverExceedsTheGuildOrFour()
        {
            Assert.That(ProposalRules.PartyFor(ProposalStyle.Safe, 0.1f, DangerLevel.High, 3), Is.EqualTo(3));
            Assert.That(ProposalRules.PartyFor(ProposalStyle.Safe, 0.1f, DangerLevel.High, 9), Is.EqualTo(4));
            Assert.That(ProposalRules.PartyFor(ProposalStyle.Bold, 0.1f, DangerLevel.High, 0), Is.EqualTo(1));
        }

        // ---------------- The pool ----------------

        [Test]
        public void BuildPool_NeverProposesARecklessQuest()
        {
            var pool = ProposalRules.BuildPool(new[] { Hunt("boss", DangerLevel.High), Hunt("rat", DangerLevel.Low) }, 1f, 4, 5000, 5000);
            Assert.That(pool, Is.Not.Empty);
            Assert.That(pool.All(p => p.Risk != QuestRisk.Reckless));
            Assert.That(pool.Any(p => p.Target.Key == "hunt:boss"), Is.False);
            Assert.That(pool.Any(p => p.Target.Key == "hunt:rat"), Is.True);
        }

        [Test]
        public void BuildPool_DropsWhatTheTreasuryCannotPayInAdvance()
        {
            var pool = ProposalRules.BuildPool(new[] { Hunt("rat", DangerLevel.Low) }, 4f, 4, 40, 5000);
            Assert.That(pool, Is.Not.Empty);
            Assert.That(pool.All(p => p.RewardGold <= 40));
            Assert.That(pool.Any(p => p.Style == ProposalStyle.Bold), Is.False);

            Assert.That(ProposalRules.BuildPool(new[] { Hunt("rat", DangerLevel.Low) }, 4f, 4, 0, 0), Is.Empty);
            Assert.That(ProposalRules.BuildPool(new[] { Hunt("rat", DangerLevel.Low) }, 4f, 4, 5000, 0), Is.Empty, "EXP is escrowed too");
        }

        [Test]
        public void BuildPool_IsEmptyWithoutMembersOrTargets()
        {
            Assert.That(ProposalRules.BuildPool(new[] { Hunt("rat", DangerLevel.Low) }, 4f, 0, 5000, 5000), Is.Empty);
            Assert.That(ProposalRules.BuildPool(new List<ProposalTarget>(), 4f, 4, 5000, 5000), Is.Empty);
            Assert.That(ProposalRules.BuildPool(null, 4f, 4, 5000, 5000), Is.Empty);
        }

        [Test]
        public void BuildPool_NumbersThePlans_AndKeepsToPoolSize()
        {
            var targets = Enumerable.Range(1, 6).Select(i => Hunt("t" + i, DangerLevel.Low, 10f * i)).ToArray();
            var pool = Pool(targets);
            Assert.That(pool.Count, Is.LessThanOrEqualTo(ProposalRules.PoolSize));
            Assert.That(pool.Select(p => p.Id), Is.Unique);
            Assert.That(pool.Select(p => p.Id), Is.EqualTo(Enumerable.Range(1, pool.Count).Select(i => "P" + i)));
        }

        [Test]
        public void BuildPool_HuntsNeverAskForMoreThanWasSeen()
        {
            var pool = ProposalRules.BuildPool(new[] { Hunt("rat", DangerLevel.Low, 20f, observed: 4) }, 4f, 4, 5000, 5000);
            Assert.That(pool.All(p => p.Count <= 4));
            var unknown = ProposalRules.BuildPool(new[] { Hunt("rat", DangerLevel.Low, 20f, observed: 0) }, 4f, 4, 5000, 5000);
            Assert.That(unknown.Any(p => p.Style == ProposalStyle.Bold && p.Count == 8));
        }

        [Test]
        public void BuildPool_BigHuntsCountAsMoreDangerous()
        {
            var pool = ProposalRules.BuildPool(new[] { Hunt("rat", DangerLevel.Low, 20f, observed: 20) }, 4f, 4, 5000, 5000);
            var bold = pool.First(p => p.Style == ProposalStyle.Bold);
            Assert.That(bold.Count, Is.EqualTo(8));
            Assert.That(bold.Danger, Is.EqualTo(DangerLevel.Medium));
            Assert.That(pool.First(p => p.Style == ProposalStyle.Balanced).Danger, Is.EqualTo(DangerLevel.Low));
        }

        [Test]
        public void BuildPool_NestsAreASingleTarget()
        {
            var pool = ProposalRules.BuildPool(new[] { NestTarget("동굴", DangerLevel.Medium) }, 4f, 4, 5000, 5000);
            Assert.That(pool, Is.Not.Empty);
            Assert.That(pool.All(p => p.Count == 1 && p.Target.Type == QuestType.ClearNest));
        }

        // ---------------- Picking a varied few ----------------

        [Test]
        public void Select_ShowsEveryStyleAndDifferentTargetsFirst()
        {
            var plans = new List<ProposalPlan>();
            foreach (var (key, dist) in new[] { ("near", 10f), ("mid", 20f), ("far", 30f) })
                foreach (var style in new[] { ProposalStyle.Safe, ProposalStyle.Balanced, ProposalStyle.Bold })
                    plans.Add(Plan(key, style, dist));

            var picked = ProposalRules.Select(plans, 4);
            Assert.That(picked, Has.Count.EqualTo(4));
            Assert.That(picked.Take(3).Select(p => p.Style), Is.Unique);
            Assert.That(picked.Take(3).Select(p => p.Target.Key), Is.Unique);
            Assert.That(picked, Is.Unique);
        }

        [Test]
        public void Select_PrefersNearerTargets_WithoutAShuffle()
        {
            var plans = new List<ProposalPlan> { Plan("far", ProposalStyle.Balanced, 50f), Plan("near", ProposalStyle.Balanced, 10f) };
            Assert.That(ProposalRules.Select(plans, 1)[0].Target.Key, Is.EqualTo("hunt:near"));
        }

        [Test]
        public void Select_FillsWithOtherStylesOfTheSameTarget_WhenThereAreFewTargets()
        {
            var plans = new List<ProposalPlan>
            {
                Plan("only", ProposalStyle.Safe, 10f), Plan("only", ProposalStyle.Balanced, 10f), Plan("only", ProposalStyle.Bold, 10f),
            };
            var picked = ProposalRules.Select(plans, 4);
            Assert.That(picked, Has.Count.EqualTo(3));
            Assert.That(picked.Select(p => p.Style), Is.Unique);
        }

        [Test]
        public void Select_WithAShuffle_StillReturnsValidDistinctPlans()
        {
            var plans = new List<ProposalPlan>();
            for (int i = 1; i <= 5; i++)
                foreach (var style in new[] { ProposalStyle.Safe, ProposalStyle.Balanced, ProposalStyle.Bold })
                    plans.Add(Plan("t" + i, style, 10f * i));
            for (int seed = 1; seed <= 20; seed++)
            {
                var picked = ProposalRules.Select(plans, ProposalRules.MaxCandidates, new GameRandom(seed));
                Assert.That(picked, Has.Count.EqualTo(ProposalRules.MaxCandidates));
                Assert.That(picked, Is.Unique);
                Assert.That(picked.Take(3).Select(p => p.Style), Is.Unique);
            }
        }

        [Test]
        public void Recommend_IsDeterministic()
        {
            var pool = Pool(Hunt("a", DangerLevel.Low, 10f), Hunt("b", DangerLevel.Medium, 20f), NestTarget("c", DangerLevel.Low, 30f));
            var first = ProposalRules.Recommend(pool).Select(p => p.Id).ToList();
            var second = ProposalRules.Recommend(pool).Select(p => p.Id).ToList();
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Has.Count.LessThanOrEqualTo(ProposalRules.MaxCandidates));
        }

        // ---------------- Adjusting the reward ----------------

        [Test]
        public void AdjustReward_MovesInStepsAndStaysInRange()
        {
            var plan = Plan("a", ProposalStyle.Balanced, 10f);
            plan.RewardGold = plan.BaseRewardGold = 100;
            Assert.That(plan.MinRewardGold, Is.EqualTo(50));
            Assert.That(plan.MaxRewardGold, Is.EqualTo(200));

            Assert.That(plan.AdjustReward(1), Is.True);
            Assert.That(plan.RewardGold, Is.EqualTo(100 + ProposalRules.RewardStep));
            for (int i = 0; i < 50; i++) plan.AdjustReward(1);
            Assert.That(plan.RewardGold, Is.EqualTo(200));
            Assert.That(plan.AdjustReward(1), Is.False, "already at the top");
            for (int i = 0; i < 50; i++) plan.AdjustReward(-1);
            Assert.That(plan.RewardGold, Is.EqualTo(50));
            Assert.That(plan.AdjustReward(-1), Is.False);
        }

        [Test]
        public void ToDraft_CarriesTheReward_PartyAndTarget()
        {
            var hunt = Plan("rat", ProposalStyle.Balanced, 20f);
            hunt.Count = 5;
            hunt.Party = 2;
            hunt.RewardGold = 55;
            hunt.RewardExp = 30;
            var d = ProposalRules.ToDraft(hunt);
            Assert.That(d.Type, Is.EqualTo(QuestType.Hunt));
            Assert.That(d.HasPoint, Is.True);
            Assert.That(d.Point, Is.EqualTo(hunt.Target.Point));
            Assert.That((d.Count, d.MaxParty, d.RewardGold, d.RewardExp), Is.EqualTo((5, 2, 55, 30)));
            Assert.That(d.Radius, Is.EqualTo(QuestBoard.HuntRadius));

            var nest = new ProposalPlan { Target = NestTarget("동굴", DangerLevel.Low), Style = ProposalStyle.Safe, Count = 1, Party = 3, RewardGold = 80, RewardExp = 45 };
            var nd = ProposalRules.ToDraft(nest);
            Assert.That(nd.Type, Is.EqualTo(QuestType.ClearNest));
            Assert.That(nd.HasPoint, Is.False);
            Assert.That(nd.MaxParty, Is.EqualTo(3));
        }

        // ---------------- The LLM's part ----------------

        static ProposalPrompt.ResponseDto Dto(params (string pick, string title, string pitch)[] slots)
        {
            var dto = new ProposalPrompt.ResponseDto();
            for (int i = 0; i < slots.Length && i < 4; i++)
            {
                var (pick, title, pitch) = slots[i];
                switch (i)
                {
                    case 0: dto.pick1 = pick; dto.title1 = title; dto.pitch1 = pitch; break;
                    case 1: dto.pick2 = pick; dto.title2 = title; dto.pitch2 = pitch; break;
                    case 2: dto.pick3 = pick; dto.title3 = title; dto.pitch3 = pitch; break;
                    default: dto.pick4 = pick; dto.title4 = title; dto.pitch4 = pitch; break;
                }
            }
            return dto;
        }

        static List<ProposalPlan> BigPool() => Pool(
            Hunt("a", DangerLevel.Low, 10f), Hunt("b", DangerLevel.Medium, 20f), Hunt("c", DangerLevel.Low, 30f), NestTarget("d", DangerLevel.Medium, 40f));

        [Test]
        public void Interpret_TakesTheModelsPicksAndWording_InOrder()
        {
            var pool = BigPool();
            var dto = Dto(("P2", "북쪽 소동", "지금 나서면 좋습니다."), ("P1", "\"조용한 산책\"", "가볍게 다녀오세요."));
            Assert.That(ProposalPrompt.TryInterpret(dto, pool, out var picked, out var reject), Is.True, reject);
            Assert.That(picked[0].Id, Is.EqualTo("P2"));
            Assert.That(picked[1].Id, Is.EqualTo("P1"));
            Assert.That(picked[0].Title, Is.EqualTo("북쪽 소동"));
            Assert.That(picked[1].Title, Is.EqualTo("조용한 산책"), "quotes are trimmed");
            Assert.That(picked[0].FromLlm, Is.True);
            Assert.That(picked[1].Pitch, Is.EqualTo("가볍게 다녀오세요."));
        }

        [Test]
        public void Interpret_FillsTheRestFromTheRules_WithRuleWording()
        {
            var pool = BigPool();
            var dto = Dto(("P1", "이름", "소개입니다."), ("P2", "이름2", "소개2입니다."));
            Assert.That(ProposalPrompt.TryInterpret(dto, pool, out var picked, out _), Is.True);
            Assert.That(picked, Has.Count.EqualTo(Mathf.Min(ProposalRules.MaxCandidates, pool.Count)));
            Assert.That(picked, Is.Unique);
            foreach (var extra in picked.Skip(2))
            {
                Assert.That(extra.FromLlm, Is.False);
                Assert.That(extra.Title, Is.Not.Empty);
                Assert.That(extra.Pitch, Is.Not.Empty);
            }
        }

        [Test]
        public void Interpret_DropsRepeatedAndUnknownIds()
        {
            var pool = BigPool();
            var dto = Dto(("P1", "가", "가 소개입니다."), ("P1", "나", "나 소개입니다."), ("P99", "다", "다 소개입니다."), ("P3", "라", "라 소개입니다."));
            Assert.That(ProposalPrompt.TryInterpret(dto, pool, out var picked, out _), Is.True);
            Assert.That(picked, Is.Unique);
            Assert.That(picked.Count(p => p.Id == "P1"), Is.EqualTo(1));
            Assert.That(picked.Any(p => p.Id == "P99"), Is.False);
            Assert.That(picked.First(p => p.Id == "P1").Title, Is.EqualTo("가"), "the first mention wins");
        }

        [Test]
        public void Interpret_RejectsTooFewValidPicks_AndNull()
        {
            var pool = BigPool();
            Assert.That(ProposalPrompt.TryInterpret(Dto(("P1", "가", "가 소개입니다.")), pool, out _, out var reject), Is.False);
            Assert.That(reject, Is.EqualTo("picks"));
            Assert.That(ProposalPrompt.TryInterpret(Dto(("P77", "가", "나"), ("P88", "다", "라")), pool, out _, out _), Is.False);
            Assert.That(ProposalPrompt.TryInterpret(null, pool, out _, out reject), Is.False);
            Assert.That(reject, Is.EqualTo("parse"));
        }

        [Test]
        public void Interpret_KeepsRuleWording_WhenTheTextIsEmpty()
        {
            var pool = BigPool();
            var dto = Dto(("P1", "", ""), ("P2", "이름", "소개입니다."));
            Assert.That(ProposalPrompt.TryInterpret(dto, pool, out var picked, out _), Is.True);
            var first = picked.First(p => p.Id == "P1");
            Assert.That(first.FromLlm, Is.False);
            Assert.That(first.Title, Is.Not.Empty);
            Assert.That(first.Pitch, Is.Not.Empty);
        }

        [Test]
        public void Grammar_AllowsOnlyThePoolIds_InFourSlots()
        {
            string g = ProposalPrompt.BuildGrammar(new[] { "P1", "P2" });
            Assert.That(g, Does.Contain("P1").And.Contain("P2"));
            Assert.That(g, Does.Not.Contain("P3"));
            Assert.That(g, Does.Contain("pick1").And.Contain("title4").And.Contain("pitch4"));
        }

        [Test]
        public void Context_ListsThePool_AndBecomesAGrammarConstrainedRequest()
        {
            var pool = BigPool();
            var ctx = ProposalPrompt.BuildContext(3, 400, 4, 5, "게시판이 비어 있다", pool);
            Assert.That(ctx.pool, Has.Count.EqualTo(pool.Count));
            Assert.That(ctx.pool[0].id, Is.EqualTo("P1"));
            string json = LlmJson.ToJson(ctx);
            Assert.That(json, Does.Contain("\"pool\"").And.Contain("reward_gold").And.Contain("P1"));

            var request = ProposalPrompt.BuildRequest(ctx);
            Assert.That(request.Grammar, Does.Contain("P1"));
            Assert.That(request.System, Is.Not.Empty);
            Assert.That(request.User, Is.EqualTo(json));
            Assert.That(request.Tag, Is.EqualTo("proposals"));
        }

        // ---------------- Wording and links ----------------

        [Test]
        public void Texts_AreNeverEmpty_ForEveryStyleAndKind()
        {
            foreach (ProposalStyle style in System.Enum.GetValues(typeof(ProposalStyle)))
            {
                Assert.That(ProposalTexts.StyleLabel(style), Is.Not.Empty);
                foreach (var target in new[] { Hunt("슬라임", DangerLevel.Low), NestTarget("동굴", DangerLevel.Low) })
                {
                    var plan = new ProposalPlan { Target = target, Style = style, Count = 5 };
                    ProposalTexts.ApplyFallback(plan);
                    Assert.That(plan.Title, Does.Contain("슬라임").Or.Contain("동굴"));
                    Assert.That(plan.Pitch, Is.Not.Empty);
                    Assert.That(plan.FromLlm, Is.False);
                    ProposalTexts.ApplyFallback(plan, new GameRandom(3));
                    Assert.That(plan.Pitch, Is.Not.Empty);
                }
            }
            Assert.That(ProposalTexts.NewProposals(3), Does.Contain("3"));
        }

        [Test]
        public void ProposalsLink_CarriesItsTarget()
        {
            Assert.That(NotificationLink.Proposals().Target, Is.EqualTo(NotificationTarget.Proposals));
            Assert.That(NotificationLink.Proposals().IsNone, Is.False);
        }
    }
}
