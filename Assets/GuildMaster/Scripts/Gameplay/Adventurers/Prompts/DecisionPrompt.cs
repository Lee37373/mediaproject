using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using GuildMaster.LLM;
using GuildMaster.Quests;
using GuildMaster.Session;

namespace GuildMaster.Adventurers.Prompts
{
    /// <summary>
    /// Prompt, request context DTOs and response grammar for the quest decision (P3-2).
    /// DTO field names are the JSON keys (snake_case) because JsonUtility serializes field names.
    /// </summary>
    public static class DecisionPrompt
    {
        public const int ReasonMaxChars = 50;
        public const int SayMaxChars = 40;
        public const int RecentEventCount = 5;

        public static readonly string[] Actions = { "accept_quest", "rest", "shop", "idle" };

        public const string SystemPrompt =
            "너는 판타지 모험가 길드에 소속된 모험가 NPC다. 주어진 JSON의 성격(mbti, values), 특성(traits), 상태(status)에 맞게 " +
            "지금 무엇을 할지 스스로 정한다.\n" +
            "- accept_quest: available_quests 중 하나를 맡는다. quest_id는 목록에 있는 값만 쓴다.\n" +
            "- rest: 여관에서 쉰다(체력이 낮거나 피로가 높을 때).\n" +
            "- shop: 길드에서 물약을 산다. status.can_buy_potion이 true일 때만 고른다.\n" +
            "- idle: 마을에서 기다린다(마음에 드는 의뢰가 없을 때).\n" +
            "risk_for_me가 '무모'인 의뢰는 성격상 아주 대담한 경우가 아니면 맡지 않는다. 체력이 30% 미만이면 의뢰를 맡지 않는다.\n" +
            "reason은 판단 이유를 한 문장으로 짧게 쓰고, say는 그 모험가가 동료에게 하는 짧은 한국어 대사 한 마디다. " +
            "반드시 JSON 한 개로만 답한다.";

        [Serializable]
        public class StatusDto
        {
            public int hp_percent;
            public int gold;
            public int fatigue;
            public int stress;
            public int potions;
            public bool can_buy_potion;
        }

        [Serializable]
        public class QuestDto
        {
            public string quest_id;
            public string target;
            public int reward_gold;
            public int reward_exp;
            public string danger;
            /// <summary>Danger relative to this adventurer's power (여유/적당/위험/무모).</summary>
            public string risk_for_me;
            public int party_size;
            public string[] members;
        }

        [Serializable]
        public class ContextDto
        {
            public string npc_id;
            public string name;
            public string @class;
            public int level;
            public string mbti;
            public string values;
            public string[] traits;
            public StatusDto status;
            public QuestDto[] available_quests;
            public string[] recent_events;
        }

        [Serializable]
        public class ResponseDto
        {
            public string action;
            public string quest_id;
            public string reason;
            public string say;
        }

        public static ContextDto BuildContext(Adventurer a, IReadOnlyList<Quest> available)
        {
            var d = a.Data;
            var recent = Services.TryGet<GameLog>(out var log) ? log.Recent(RecentEventCount).ToArray() : Array.Empty<string>();
            return new ContextDto
            {
                npc_id = d.id,
                name = d.name,
                @class = AdventurerData.ClassLabel(d.cls),
                level = d.level,
                mbti = d.mbti,
                values = d.values,
                traits = a.Traits != null ? a.Traits.PromptHints.ToArray() : Array.Empty<string>(),
                status = new StatusDto
                {
                    hp_percent = (int)(a.HpRatio * 100f),
                    gold = d.gold,
                    fatigue = (int)d.fatigue,
                    stress = (int)d.stress,
                    potions = d.potions,
                    can_buy_potion = ShopUseful(a),
                },
                available_quests = available.Select(q => new QuestDto
                {
                    quest_id = q.Id,
                    target = q.Title,
                    reward_gold = q.RewardGold,
                    reward_exp = q.RewardExp,
                    danger = Quest.DangerLabel(q.Danger),
                    risk_for_me = RiskLabel(a, q),
                    party_size = q.MaxParty,
                    members = q.Assignees.Select(t => t.TakerName).ToArray(),
                }).ToArray(),
                recent_events = recent,
            };
        }

        /// <summary>
        /// Shopping only makes sense with 0–1 potions left and when the guild has stock the
        /// adventurer can afford (the model otherwise tends to "shop" in a loop).
        /// </summary>
        public static bool ShopUseful(Adventurer a)
        {
            if (a.Data.potions >= 2) return false;
            if (!Services.TryGet<GuildTreasury>(out var t) || t.GetStock(DefaultContent.Potion) <= 0) return false;
            if (!Services.TryGet<TradeService>(out var trade) || !Services.TryGet<GameDatabase>(out var db)) return false;
            var potion = db.Item(DefaultContent.Potion);
            return potion != null && a.Data.gold >= trade.GoodsPrice(potion);
        }

        /// <summary>Same power/requirement margin as the rule-based scorer, as words the model can use.</summary>
        public static string RiskLabel(Adventurer a, Quest q)
        {
            int partySize = q.Assignees.Count + 1;
            float power = AdventurerStats.Power(a.Data) * (1f + 0.6f * (partySize - 1));
            float margin = power / RuleBasedDecisionMaker.DangerRequirement(q.Danger);
            return margin >= 2f ? "여유" : margin >= 1f ? "적당" : margin >= 0.8f ? "위험" : "무모";
        }

        /// <summary>Grammar allowing only the given quest ids (or "").</summary>
        public static string BuildGrammar(IEnumerable<string> questIds)
            => new GbnfObject()
                .Enum("action", Actions)
                .Enum("quest_id", questIds.Append(""))
                .String("reason", ReasonMaxChars)
                .String("say", SayMaxChars)
                .Build();

        public static LlmRequest BuildRequest(Adventurer a, IReadOnlyList<Quest> available) => new()
        {
            System = SystemPrompt,
            User = LlmJson.ToJson(BuildContext(a, available)),
            Grammar = BuildGrammar(available.Select(q => q.Id)),
            MaxTokens = 160,
            Temperature = 0.7f,
            Priority = LlmPriority.Decision,
            DedupeKey = a.Data.id,
            TimeoutSeconds = 40f,
            Tag = "decision:" + a.Data.id,
        };
    }
}
