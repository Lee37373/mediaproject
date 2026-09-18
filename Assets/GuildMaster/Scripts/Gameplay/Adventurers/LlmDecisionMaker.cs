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
        public string LastFallbackReason { get; private set; }

        public LlmDecisionMaker(LlmGateway gateway) { _gateway = gateway; }

        public async Task<AdventurerDecision> DecideAsync(Adventurer a, IReadOnlyList<Quest> available, CancellationToken ct)
        {
            if (_gateway == null || !_gateway.Enabled || !_gateway.IsReady)
                return Fallback(a, available, "offline");

            var result = await _gateway.RequestAsync(DecisionPrompt.BuildRequest(a, available), ct);
            // The adventurer may have died or left while the model was thinking.
            if (a == null || !a.IsAlive) { Note(result, "무시: 모험가 없음"); return AdventurerDecision.Idle(); }
            if (!result.Ok) return Fallback(a, available, result.Error, result);
            if (!LlmJson.TryParse<DecisionPrompt.ResponseDto>(result.Text, out var dto))
                return Fallback(a, available, "parse", result);

            var ids = available.Select(q => q.Id).ToList();
            if (!TryInterpret(dto, a.HpRatio, ids, DecisionPrompt.ShopUseful(a), out var decision, out var reject))
                return Fallback(a, available, reject, result);

            LlmDecisions++;
            Note(result, $"채택: {dto.action} {dto.quest_id}");
            Debug.Log($"[LLM-DECISION] {a.Data.name}({a.Data.mbti}/{a.Data.values}/{string.Join(",", a.Data.traitIds)}) " +
                      $"→ {dto.action} {dto.quest_id} | {decision.Reason} | \"{decision.Say}\" ({result.Seconds:0.0}s)");
            return decision;
        }

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
                default: rejectReason = "action"; return false;
            }

            if (action == AdventurerAction.AcceptQuest)
            {
                if (string.IsNullOrEmpty(dto.quest_id) || !questIds.Contains(dto.quest_id)) { rejectReason = "quest_id"; return false; }
                if (hpRatio < MinHpToAccept) { rejectReason = "unsafe"; return false; }
            }

            decision = new AdventurerDecision
            {
                Action = action,
                QuestId = action == AdventurerAction.AcceptQuest ? dto.quest_id : null,
                Reason = LlmJson.CleanLine(dto.reason, DecisionPrompt.ReasonMaxChars),
                Say = LlmJson.CleanLine(dto.say, DecisionPrompt.SayMaxChars),
                FromLLM = true,
            };
            return true;
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
