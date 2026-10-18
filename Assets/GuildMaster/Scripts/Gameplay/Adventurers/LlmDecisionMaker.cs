using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.LLM;
using GuildMaster.Quests;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Quest decisions made by the local LLM (grammar-constrained JSON), validated against the
    /// game state. Any failure — offline, timeout, bad parse, unknown quest id, unsafe choice —
    /// falls back to <see cref="RuleBasedDecisionMaker"/>.
    /// </summary>
    public class LlmDecisionMaker : IAdventurerDecisionMaker
    {
        /// <summary>The model may not take quests below this HP ratio (safety rule).</summary>
        public const float MinHpToAccept = 0.3f;

        readonly LlmGateway _gateway;

        public int LlmDecisions { get; private set; }
        public int FallbackDecisions { get; private set; }
        /// <summary>Decided by rules on purpose (no quest available), not a failure.</summary>
        public int RuleDecisions { get; private set; }
        public string LastFallbackReason { get; private set; }

        public LlmDecisionMaker(LlmGateway gateway) { _gateway = gateway; }

        public async Task<AdventurerDecision> DecideAsync(Adventurer a, IReadOnlyList<Quest> available, CancellationToken ct)
        {
            if (_gateway == null || !_gateway.Enabled || !_gateway.IsReady)
                return Fallback(a, available, "offline");
            // Nothing to take: rest/shop/wait is a rules question. Keeps the model free for quest
            // choices, invitations and small talk (it was mostly busy saying "no quests, I'll wait").
            // F-2: on leave or at the breaking point the answer is "no quest" whatever the model would say.
            if (available.Count == 0 || Welfare.GuildWelfare.AnyQuestRefusal(a) != null)
            {
                RuleDecisions++;
                return RuleBasedDecisionMaker.Decide(a, available);
            }

            // The rules below still weigh every quest; the model only sees the few this member would weigh most.
            var all = available;
            available = Shortlist(a, all);
            var result = await _gateway.RequestAsync(DecisionPrompt.BuildRequest(a, available), ct);
            // The adventurer may have died or left while the model was thinking.
            if (a == null || !a.IsAlive) { Note(result, "무시: 모험가 없음"); return AdventurerDecision.Idle(); }
            // Cancelled by the caller (the decision is no longer wanted): nothing to fall back to.
            if (!result.Ok && result.Error == "cancelled" && ct.IsCancellationRequested)
            {
                Note(result, "취소: 다른 행동(파티 초대 등)으로 대체");
                return AdventurerDecision.Idle();
            }
            if (!result.Ok) return Fallback(a, all, result.Error, result);
            if (!LlmJson.TryParse<DecisionPrompt.ResponseDto>(result.Text, out var dto))
                return Fallback(a, all, "parse", result);

            var ids = available.Select(q => q.Id).ToList();
            if (!TryInterpret(dto, a.HpRatio, ids, DecisionPrompt.ShopUseful(a), out var decision, out var reject))
                return Fallback(a, all, reject, result);

            if (decision.Action == AdventurerAction.AcceptQuest)
            {
                var quest = available.FirstOrDefault(q => q.Id == decision.QuestId);
                if (quest != null && !RecklessAllowed(DecisionPrompt.RiskLabel(a, quest), a.Traits?.DangerAversion ?? 0f,
                        a.Data.values, quest.Assignees.Count > 0))
                    return Fallback(a, all, "reckless", result);
                // P4-88: far under the quest's level, a 위험 quest is taken only now and then.
                if (QuestRisk.HoldsBack(a, quest)) return Fallback(a, all, "too green", result);
                // F-2: too stressed for a quest this dangerous.
                if (quest != null && Welfare.GuildWelfare.QuestRefusal(a, quest.Danger) != null)
                    return Fallback(a, all, "stress", result);
            }

            LlmDecisions++;
            Note(result, $"채택: {dto.action} {dto.quest_id}");
            Debug.Log($"[LLM-DECISION] {a.Data.name}({a.Data.mbti}/{a.Data.values}/{string.Join(",", a.Data.traitIds)}) " +
                      $"→ {dto.action} {dto.quest_id} | {decision.Reason} | \"{decision.Say}\" ({result.Seconds:0.0}s)");
            return decision;
        }

        /// <summary>Quests shown to the model at most (P4-82: the town's postings can fill the board with dozens).</summary>
        public const int MaxPromptQuests = 6;

        /// <summary>
        /// The quests this member would weigh most by the rules (<see cref="RuleBasedDecisionMaker.Score"/>), at most
        /// <paramref name="max"/>, so the prompt stays short however full the board is. All of them when they fit.
        /// </summary>
        public static List<Quest> Shortlist(Adventurer a, IReadOnlyList<Quest> available, int max = MaxPromptQuests)
            => available.Count <= max
                ? available.ToList()
                : available.Select(q => (q, score: RuleBasedDecisionMaker.Score(a, q)))
                           .OrderByDescending(x => x.score).ThenBy(x => x.q.Id, System.StringComparer.Ordinal)
                           .Take(max).Select(x => x.q).ToList();

        /// <summary>
        /// Semantic validation of a model response. Returns false with a reason when the
        /// response must be replaced by the rule-based decision.
        /// </summary>
        public static bool TryInterpret(DecisionPrompt.ResponseDto dto, float hpRatio, IReadOnlyCollection<string> questIds, bool shopAllowed,
            out AdventurerDecision decision, out string rejectReason)
        {
            decision = default;
            rejectReason = null;
            if (dto == null) { rejectReason = "parse"; return false; }

            AdventurerAction action;
            switch (dto.action)
            {
                case "accept_quest": action = AdventurerAction.AcceptQuest; break;
                case "rest": action = AdventurerAction.Rest; break;
                case "shop":
                    if (!shopAllowed) { rejectReason = "shop"; return false; }
                    action = AdventurerAction.Shop;
                    break;
                case "idle": action = AdventurerAction.Idle; break;
                case "hunt": action = AdventurerAction.Hunt; break;
                default: rejectReason = "action"; return false;
            }

            if (action == AdventurerAction.AcceptQuest)
            {
                if (string.IsNullOrEmpty(dto.quest_id) || !questIds.Contains(dto.quest_id)) { rejectReason = "quest_id"; return false; }
                if (hpRatio < MinHpToAccept) { rejectReason = "unsafe"; return false; }
            }
            // Walking out to hunt is the same risk as taking a quest, so it needs the same health.
            if (action == AdventurerAction.Hunt && hpRatio < MinHpToAccept) { rejectReason = "unsafe"; return false; }

            decision = new AdventurerDecision
            {
                Action = action,
                QuestId = action == AdventurerAction.AcceptQuest ? dto.quest_id : null,
                Reason = LlmJson.CleanSentence(dto.reason, DecisionPrompt.ReasonMaxChars, DecisionPrompt.ReasonGrammarChars, false),
                // A cut-off line is worse than silence.
                Say = LlmJson.CleanSentence(dto.say, DecisionPrompt.SayMaxChars, DecisionPrompt.SayGrammarChars, true),
                FromLLM = true,
            };
            return true;
        }

        /// <summary>
        /// Personality gate for "무모" (reckless) quests — taking them is a character trait, not
        /// a bug, but the model alone walks whole guilds into certain death:
        /// cautious (danger aversion &gt; 0 or values 안전) never; bold (aversion &lt; 0 or values
        /// 명예/모험) always; everyone else only when joining someone who already took it.
        /// </summary>
        public static bool RecklessAllowed(string riskLabel, float dangerAversion, string values, bool joiningParty)
        {
            if (riskLabel != "무모") return true;
            if (dangerAversion > 0f || values == "안전") return false;
            if (dangerAversion < 0f || values == "명예" || values == "모험") return true;
            return joiningParty;
        }

        static void Note(LlmResult result, string note)
        {
            if (result.Exchange != null) result.Exchange.Note = note;
        }

        AdventurerDecision Fallback(Adventurer a, IReadOnlyList<Quest> available, string reason, LlmResult result = default)
        {
            FallbackDecisions++;
            LastFallbackReason = reason;
            Note(result, "규칙 기반으로 대체: " + reason);
            if (reason != "offline") Debug.Log($"[LLM-DECISION] {a.Data.name}: 규칙 기반으로 대체 ({reason})");
            return RuleBasedDecisionMaker.Decide(a, available);
        }
    }
}
