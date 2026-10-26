using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using GuildMaster.Quests;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// The sources of the standard think tree (P4-104a): the rules of <see cref="RuleBasedDecisionMaker.Decide"/> as they were,
    /// one source a rule. Within a layer that takes the highest, the scores keep the old order (rest before a potion before
    /// gear).
    /// </summary>
    public static class ThinkSources
    {
        public static IEnumerable<IThinkSource> Standard() => new IThinkSource[]
        {
            new RestSource(), new PotionSource(), new GearSource(), new PlacesSource(urgent: true),
            new HoldSource(), new MoodSource(),
            new QuestSource(),
            new TrainingSource(),
            new HuntSource(), new PlacesSource(urgent: false),
            new IdleSource(),
        };

        /// <summary>Hurt or worn out: rest first.</summary>
        sealed class RestSource : IThinkSource
        {
            public ThinkLayer Layer => ThinkLayer.Body;

            public void Offer(Adventurer a, IReadOnlyList<Quest> available, List<ThinkOption> into)
            {
                if (a.HpRatio >= 0.6f && a.Data.fatigue <= 70f) return;
                into.Add(new ThinkOption
                {
                    Action = AdventurerAction.Rest, Score = 1f, Label = "휴식",
                    Reason = "몸 상태가 좋지 않다", Say = "좀 쉬어야겠어...",
                });
            }
        }

        /// <summary>No potion left while the guild sells them and it can pay.</summary>
        sealed class PotionSource : IThinkSource
        {
            public ThinkLayer Layer => ThinkLayer.Body;

            public void Offer(Adventurer a, IReadOnlyList<Quest> available, List<ThinkOption> into)
            {
                var d = a.Data;
                if (d.potions != 0 || !Services.TryGet<GuildTreasury>(out var t) || t.GetStock(DefaultContent.Potion) <= 0
                    || !Services.TryGet<TradeService>(out var trade) || !Services.TryGet<GameDatabase>(out var db)
                    || d.gold < trade.GoodsPrice(db.Item(DefaultContent.Potion))) return;
                into.Add(new ThinkOption
                {
                    Action = AdventurerAction.Shop, Score = 0.9f, Label = "물약 사기",
                    Reason = "물약이 없다", Say = "물약부터 챙기자.",
                });
            }
        }

        /// <summary>Better gear it can afford.</summary>
        sealed class GearSource : IThinkSource
        {
            public ThinkLayer Layer => ThinkLayer.Body;

            public void Offer(Adventurer a, IReadOnlyList<Quest> available, List<ThinkOption> into)
            {
                if (!GearShopping.CanBuy(a)) return;
                into.Add(new ThinkOption
                {
                    Action = AdventurerAction.Shop, Score = 0.8f, Label = "장비 바꾸기",
                    Reason = "더 좋은 장비를 살 수 있다", Say = "모은 돈으로 장비를 바꿔야겠어.",
                });
            }
        }

        /// <summary>
        /// F-2: on leave, badly wounded or at the breaking point nobody takes a quest, however good it looks — and nobody goes
        /// out hunting either (P4-69: with an empty board this check was skipped and the member walked out to hunt on its leave,
        /// or with its wound).
        /// </summary>
        sealed class HoldSource : IThinkSource
        {
            public ThinkLayer Layer => ThinkLayer.Hold;

            public void Offer(Adventurer a, IReadOnlyList<Quest> available, List<ThinkOption> into)
            {
                string tired = Welfare.GuildWelfare.AnyQuestRefusal(a);
                if (tired == null) return;
                into.Add(new ThinkOption
                {
                    Action = AdventurerAction.Idle, Score = 1f, Label = "쉬며 버팀", Reason = tired,
                    Say = Welfare.StressRules.LevelOf(a.Data.stress) == Welfare.StressLevel.Breaking ? "지금은... 아무것도 하고 싶지 않아." : null,
                });
            }
        }

        /// <summary>
        /// Low spirits (P4-104c): a member feeling low now and then sits the day out rather than go to work — the dice roll at
        /// each decision, more often the lower it is (<see cref="ThoughtRules.SitOutChance"/>). Its breaks are P4-104d.
        /// </summary>
        sealed class MoodSource : IThinkSource
        {
            public ThinkLayer Layer => ThinkLayer.Hold;

            public void Offer(Adventurer a, IReadOnlyList<Quest> available, List<ThinkOption> into)
            {
                float chance = ThoughtRules.SitOutChance(ThoughtRecorder.MoodOf(a));
                if (chance <= 0f || !GameRandom.Shared.Chance(chance)) return;
                into.Add(new ThinkOption
                {
                    Action = AdventurerAction.Idle, Score = 1f, Label = "기분이 안 남",
                    Reason = "기분이 영 아니라 오늘은 일할 마음이 없다", Say = "오늘은... 그냥 쉴래.",
                });
            }
        }

        /// <summary>Every quest on the board it could take, scored by <see cref="RuleBasedDecisionMaker.Score"/>.</summary>
        sealed class QuestSource : IThinkSource
        {
            public ThinkLayer Layer => ThinkLayer.Work;

            public void Offer(Adventurer a, IReadOnlyList<Quest> available, List<ThinkOption> into)
            {
                foreach (var q in available)
                {
                    // A newcomer's quest out beyond its ground is a dare (P4-100): rolled here, once a look.
                    float score = QuestRisk.NewcomerHoldsBack(a, q) ? 0f : RuleBasedDecisionMaker.Score(a, q);
                    into.Add(new ThinkOption
                    {
                        Action = AdventurerAction.AcceptQuest, Quest = q, Score = score, Label = $"의뢰 '{q.Title}'",
                        Reason = "보상 대비 위험이 적당하다", Say = $"{q.Title}, 내가 맡지!",
                    });
                }
            }
        }

        /// <summary>
        /// Weaker than the monsters round the town (P4-99, user decision 2026-10-21): it trains while the training ground
        /// teaches; otherwise it hunts — alone and close in (FreeHuntState).
        /// </summary>
        sealed class TrainingSource : IThinkSource
        {
            public ThinkLayer Layer => ThinkLayer.Growth;

            public void Offer(Adventurer a, IReadOnlyList<Quest> available, List<ThinkOption> into)
            {
                if (!a.IsNewcomer || !Services.TryGet<Town.UpgradeService>(out var upgrades) || !upgrades.Trains) return;
                into.Add(new ThinkOption
                {
                    Action = AdventurerAction.Train, Score = 1f, Label = "훈련",
                    Reason = "아직 마을 밖 몬스터보다 약하다", Say = "훈련장에서 실력부터 쌓자.",
                });
            }
        }

        /// <summary>
        /// No quest worth taking: go out and hunt rather than stand in the plaza (P4-37). Timid members and those who value
        /// safety stay in more often (<see cref="RuleBasedDecisionMaker.WaitsInTown"/>).
        /// </summary>
        sealed class HuntSource : IThinkSource
        {
            public ThinkLayer Layer => ThinkLayer.Roam;

            public void Offer(Adventurer a, IReadOnlyList<Quest> available, List<ThinkOption> into)
            {
                if (RuleBasedDecisionMaker.WaitsInTown(a)) return;
                into.Add(new ThinkOption
                {
                    Action = AdventurerAction.Hunt, Score = 1f, Label = "자율 사냥",
                    Reason = "의뢰가 없으니 직접 사냥하자", Say = "의뢰가 없으면 직접 나가야지.",
                });
            }
        }

        /// <summary>
        /// The places worth going to (P4-105b, G3's smart objects: each advertises what it does). With no quest to take: a soak
        /// when stressed, tired or low (<see cref="Places.PlaceRules.WantsSpring"/>) and a prayer when low or valuing safety or
        /// honour come before a hunt; gathering comes first for a newcomer, one valuing safety or a diligent one, and is what the
        /// rest do on a day they stay off the hunt. Stressed past <see cref="Places.PlaceRules.UrgentSpringStress"/> it goes
        /// to soak before any work (the urgent source, in the body layer). Only places the guild knows, safe and close enough
        /// for it (<see cref="Places.PlaceVisits.Best"/>); not a standing party's leader with others waiting on it, unless urgent.
        /// </summary>
        sealed class PlacesSource : IThinkSource
        {
            readonly bool _urgent;

            public PlacesSource(bool urgent) => _urgent = urgent;

            public ThinkLayer Layer => _urgent ? ThinkLayer.Body : ThinkLayer.Roam;

            public void Offer(Adventurer a, IReadOnlyList<Quest> available, List<ThinkOption> into)
            {
                if (a == null || a.Data == null || !a.IsGuildMember || a.IsVisitor
                    || !Services.TryGet<Places.PlaceVisits>(out var visits) || !Services.TryGet<GameClock>(out var clock)) return;
                var d = a.Data;
                if (_urgent)
                {
                    // The body layer comes before the reasons to stay in: on leave or badly wounded it stays.
                    if (!Places.PlaceRules.NeedsSpring(d.stress) || Places.PlaceVisits.SoakedLately(a)
                        || Services.TryGet<Welfare.GuildWelfare>(out var welfare) && welfare.IsOnLeave(a)
                        || Legacy.GuildLegacy.WoundedRefusal(a) != null) return;
                    Add(into, visits.Best(a, World.PlaceKind.HotSpring), Places.PlaceRules.UrgentSpringScore,
                        "마음이 너무 무겁다. 온천에서 좀 풀어야겠다", "온천에 좀 다녀올게...");
                    return;
                }
                if (LeadsOthers(a)) return;
                float mood = ThoughtRecorder.MoodOf(a);
                if (Places.PlaceRules.WantsSpring(d.stress, d.fatigue, mood) && !Places.PlaceVisits.SoakedLately(a))
                    Add(into, visits.Best(a, World.PlaceKind.HotSpring), Places.PlaceRules.SpringScore,
                        "지쳤으니 온천에 몸을 담그자", "온천에 다녀와야겠다.");
                if (Places.PlaceRules.WantsShrine(mood, d.values, clock.TotalHours, d.shrineReadyAt))
                    Add(into, visits.Best(a, World.PlaceKind.Shrine), Places.PlaceRules.ShrineScore,
                        "신전에 가서 가호를 빌자", "신전에 기도하러 가야지.");
                // The nearer of an ore vein and a herb patch.
                var ore = visits.Best(a, World.PlaceKind.OreVein);
                var herb = visits.Best(a, World.PlaceKind.HerbPatch);
                var gather = ore == null ? herb
                           : herb == null ? ore
                           : (ore.WorldPosition - a.Position).sqrMagnitude <= (herb.WorldPosition - a.Position).sqrMagnitude ? ore : herb;
                float score = Places.PlaceRules.GathersFirst(a.IsNewcomer, d.values, d.traitIds)
                    ? Places.PlaceRules.GatherFirstScore : Places.PlaceRules.GatherScore;
                if (gather != null)
                    Add(into, gather, score, gather.Kind == World.PlaceKind.OreVein ? "광맥에서 철광석을 캐 오자" : "약초를 따 오자",
                        gather.Kind == World.PlaceKind.OreVein ? "광맥에 다녀올게." : "약초 좀 따 올게.");
            }

            /// <summary>A standing party's leader with others in it: they wait on it, so it does not wander off.</summary>
            static bool LeadsOthers(Adventurer a)
                => Services.TryGet<GuildParties>(out var parties) && parties.PartyOf(a) is { } p && p.Leader == a && p.Members.Count > 1;

            static void Add(List<ThinkOption> into, World.PlaceSite place, float score, string reason, string say)
            {
                if (place == null) return;
                into.Add(new ThinkOption
                {
                    Action = AdventurerAction.Visit, Place = place, Score = score, Label = place.Name(), Reason = reason, Say = say,
                });
            }
        }

        /// <summary>Nothing else: stays about the plaza.</summary>
        sealed class IdleSource : IThinkSource
        {
            public ThinkLayer Layer => ThinkLayer.Leisure;

            public void Offer(Adventurer a, IReadOnlyList<Quest> available, List<ThinkOption> into)
                => into.Add(new ThinkOption
                {
                    Action = AdventurerAction.Idle, Score = 1f, Label = "마을에서 기다림", Reason = "마땅한 의뢰가 없다",
                });
        }
    }
}
