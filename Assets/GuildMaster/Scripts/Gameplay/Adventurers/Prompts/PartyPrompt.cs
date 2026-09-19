using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.LLM;
using GuildMaster.Quests;

namespace GuildMaster.Adventurers.Prompts
{
    /// <summary>
    /// Party invitation (P3-4): an idle member decides, from their own point of view, whether to
    /// join a leader who just took a multi-person quest. DTO field names are the JSON keys.
    /// </summary>
    public static class PartyPrompt
    {
        public const int SayMaxChars = 120;

        public const string SystemPrompt =
            "너는 판타지 모험가 길드에 소속된 모험가 NPC다. 동료(leader)가 방금 의뢰를 맡고 너에게 함께 가자고 제안했다. " +
            "주어진 JSON의 네 성격(mbti, values), 특성(traits), 상태(status), 리더와의 기억(memories_with_leader), 의뢰 정보(quest)를 보고 " +
            "제안을 받아들일지(accept: yes/no) 스스로 정한다.\n" +
            "- 가치관이 '동료'이거나 사교적이면 대체로 함께 간다. 혼자 일하기를 좋아하면 대체로 거절한다.\n" +
            "- 체력이 낮거나(hp_percent 50 미만) 피로가 높으면(fatigue 70 이상) 거절한다.\n" +
            "- quest.risk_for_me가 '무모'면 겁 많거나 안전을 중시하는 성격은 거절한다.\n" +
            "- 리더와 좋은 기억이 있으면 받아들이기 쉽고, 나쁜 기억이 있으면 망설인다.\n" +
            "say는 리더에게 직접 하는 짧은 한국어 대답 한 마디다(결정과 일치해야 한다). " +
            "반드시 JSON 한 개로만 답한다.";

        [Serializable]
        public class LeaderDto
        {
            public string name;
            public string @class;
            public int level;
            public string mbti;
            public string invite;
        }

        [Serializable]
        public class QuestDto
        {
            public string target;
            public int my_share_gold;
            public int my_share_exp;
            public string danger;
            public string risk_for_me;
            public int party_size;
        }

        [Serializable]
        public class ContextDto
        {
            public string name;
            public string @class;
            public int level;
            public string mbti;
            public string values;
            public string[] traits;
            public DecisionPrompt.StatusDto status;
            public LeaderDto leader;
            public QuestDto quest;
            public string[] memories_with_leader;
            public string[] memories;
        }

        [Serializable]
        public class ResponseDto
        {
            public string accept;
            public string say;
        }

        public static string InviteLine(Quest q) => $"'{q.Title}' 같이 가지 않을래?";

        public static ContextDto BuildContext(Adventurer c, Adventurer leader, Quest q, string invite)
        {
            var d = c.Data;
            int partySize = q.Assignees.Count + 1;
            var basic = DecisionPrompt.BuildContext(c, Array.Empty<Quest>());
            var withLeader = d.memories.Where(m => m.Text.Contains(leader.Data.name))
                .OrderByDescending(m => m.hour).Take(3).Select(m => m.Format()).ToArray();
            return new ContextDto
            {
                name = d.name,
                @class = basic.@class,
                level = d.level,
                mbti = d.mbti,
                values = d.values,
                traits = basic.traits,
                status = basic.status,
                leader = new LeaderDto
                {
                    name = leader.Data.name,
                    @class = AdventurerData.ClassLabel(leader.Data.cls),
                    level = leader.Data.level,
                    mbti = leader.Data.mbti,
                    invite = invite,
                },
                quest = new QuestDto
                {
                    target = q.Title,
                    my_share_gold = q.RewardGold / partySize,
                    my_share_exp = q.RewardExp / partySize,
                    danger = Quest.DangerLabel(q.Danger),
                    risk_for_me = DecisionPrompt.RiskLabel(c, q),
                    party_size = q.MaxParty,
                },
                memories_with_leader = withLeader,
                memories = basic.memories,
            };
        }

        /// <summary>Decision field first so the line is written to match it.</summary>
        public static string Grammar { get; } = new GbnfObject()
            .Enum("accept", new[] { "yes", "no" })
            .String("say", SayMaxChars)
            .Build();

        public static LlmRequest BuildRequest(Adventurer c, Adventurer leader, Quest q, string invite) => new()
        {
            System = SystemPrompt,
            User = LlmJson.ToJson(BuildContext(c, leader, q, invite)),
            Grammar = Grammar,
            MaxTokens = 300,
            Temperature = 0.7f,
            Priority = LlmPriority.Party,
            DedupeKey = "party:" + c.Data.id,
            TimeoutSeconds = 40f,
            Tag = "party:" + c.Data.id,
        };

        /// <summary>
        /// Game-rule check of a model answer. Returns false (→ rule-based fallback) when the answer
        /// is malformed; a "yes" is turned into "no" when unsafe (low HP or a forbidden reckless quest).
        /// </summary>
        public static bool TryInterpret(ResponseDto dto, float hpRatio, string riskLabel, float dangerAversion, string values,
            out bool accept, out string say)
        {
            accept = false;
            say = null;
            if (dto == null || (dto.accept != "yes" && dto.accept != "no")) return false;
            say = LlmJson.CleanSentence(dto.say, SayMaxChars, SayMaxChars, true);
            accept = dto.accept == "yes";
            if (accept && (hpRatio < LlmDecisionMaker.MinHpToAccept
                           || !LlmDecisionMaker.RecklessAllowed(riskLabel, dangerAversion, values, joiningParty: true)))
            {
                accept = false;
                say = "미안, 이번엔 무리야.";
            }
            return true;
        }

        /// <summary>Rule-based acceptance chance (spec P3-4): values 동료 0.8, loner-type traits 0.2, else 0.55.</summary>
        public static float RuleAcceptChance(string values, float partyAffinity)
        {
            if (values == "동료") return 0.8f;
            if (partyAffinity < 1f) return 0.2f;
            return 0.55f;
        }
    }
}
