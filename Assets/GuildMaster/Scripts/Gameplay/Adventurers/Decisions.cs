using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using GuildMaster.Quests;
using GuildMaster.Traits;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    public enum AdventurerAction { AcceptQuest, Rest, Shop, Idle, Hunt, Train }

    public struct AdventurerDecision
    {
        public AdventurerAction Action;
        public string QuestId;
        public string Reason;
        public string Say;

        public static AdventurerDecision Idle(string reason = null) => new() { Action = AdventurerAction.Idle, Reason = reason };
    }

    /// <summary>
    /// High-level decision source for adventurers, resolved through Services. Since P4-91 (user decision 2026-10-19)
    /// every NPC behaviour is decided by the rules (<see cref="RuleBasedDecisionMaker"/>); the model only talks
    /// (recruitment, persuasion) and words the proposal cards.
    /// </summary>
    public interface IAdventurerDecisionMaker
    {
        Task<AdventurerDecision> DecideAsync(Adventurer adventurer, IReadOnlyList<Quest> available, CancellationToken ct);
    }

    /// <summary>Deterministic utility-based decisions (no LLM).</summary>
    public class RuleBasedDecisionMaker : IAdventurerDecisionMaker
    {
        public Task<AdventurerDecision> DecideAsync(Adventurer a, IReadOnlyList<Quest> available, CancellationToken ct)
            => Task.FromResult(Decide(a, available));

        public static float DangerRequirement(DangerLevel d) => d switch
        {
            // A fresh member alone, even with no stat building (P4-87: power is scaled by the facilities, and at 1 a Lv1
            // read every 낮음 quest — a single slime — as 무모, so nobody took the first quests).
            DangerLevel.Low => Town.FacilityRules.BaseStatFactor,
            DangerLevel.Medium => 5f, // P4-58: levels now run 1..15 across the map (were 1..5)
            _ => 9f,
        };

        public static AdventurerDecision Decide(Adventurer a, IReadOnlyList<Quest> available)
        {
            var d = a.Data;
            if (a.HpRatio < 0.6f || d.fatigue > 70f)
                return new AdventurerDecision { Action = AdventurerAction.Rest, Reason = "몸 상태가 좋지 않다", Say = "좀 쉬어야겠어..." };

            if (d.potions == 0 && Services.TryGet<GuildTreasury>(out var t) && t.GetStock(DefaultContent.Potion) > 0
                && Services.TryGet<TradeService>(out var trade) && Services.TryGet<GameDatabase>(out var db)
                && d.gold >= trade.GoodsPrice(db.Item(DefaultContent.Potion)))
                return new AdventurerDecision { Action = AdventurerAction.Shop, Reason = "물약이 없다", Say = "물약부터 챙기자." };

            if (GearShopping.CanBuy(a))
                return new AdventurerDecision { Action = AdventurerAction.Shop, Reason = "더 좋은 장비를 살 수 있다", Say = "모은 돈으로 장비를 바꿔야겠어." };

            // F-2: on leave, badly wounded or at the breaking point nobody takes a quest, however good
            // it looks — and nobody goes out hunting either (P4-69: with an empty board this check was
            // skipped and the member walked out to hunt on its leave, or with its wound).
            string tired = Welfare.GuildWelfare.AnyQuestRefusal(a);
            if (tired != null)
                return new AdventurerDecision
                {
                    Action = AdventurerAction.Idle,
                    Reason = tired,
                    Say = Welfare.StressRules.LevelOf(d.stress) == Welfare.StressLevel.Breaking ? "지금은... 아무것도 하고 싶지 않아." : null,
                };

            Quest best = null;
            float bestScore = 0f;
            foreach (var q in available)
            {
                float score = Score(a, q);
                if (score > bestScore) { bestScore = score; best = q; }
            }
            if (best != null)
                return new AdventurerDecision { Action = AdventurerAction.AcceptQuest, QuestId = best.Id, Reason = "보상 대비 위험이 적당하다", Say = $"{best.Title}, 내가 맡지!" };

            // Weaker than the monsters round the town (P4-99, user decision 2026-10-21): it trains while the training ground
            // teaches; otherwise it hunts — alone and close in (FreeHuntState).
            if (a.IsNewcomer && Services.TryGet<Town.UpgradeService>(out var upgrades) && upgrades.Trains)
                return new AdventurerDecision { Action = AdventurerAction.Train, Reason = "아직 마을 밖 몬스터보다 약하다", Say = "훈련장에서 실력부터 쌓자." };

            // No quest worth taking: go out and hunt rather than stand in the plaza (P4-37). Timid
            // members and those who value safety stay in more often — the LLM path gives this its
            // own personality, this is the fallback's version of it.
            if (!WaitsInTown(a))
                return new AdventurerDecision { Action = AdventurerAction.Hunt, Reason = "의뢰가 없으니 직접 사냥하자", Say = "의뢰가 없으면 직접 나가야지." };

            return AdventurerDecision.Idle("마땅한 의뢰가 없다");
        }

        /// <summary>Rule-based reasons to sit an empty board out instead of hunting (P4-37).</summary>
        public static bool WaitsInTown(Adventurer a) => GameRandom.Shared.Chance(StayChance(a.Data, a.Traits));

        /// <summary>
        /// Odds of staying in town with nothing posted (pure). A low-level member with no potion
        /// has nothing to fall back on; otherwise it is personality — cowards (dangerAversion +1)
        /// often wait, the brave (-0.5) never do.
        /// </summary>
        public static float StayChance(AdventurerData d, TraitSet traits)
        {
            // No potion and barely trained: often waits, but never for good — with an empty board
            // hunting is the only way to earn the gold for one (measured run: a Lv1 with no potion
            // and no gold sat in the plaza for a whole day).
            if (d.potions == 0 && d.level <= 2) return 0.5f;
            float stay = 0.3f * (traits?.DangerAversion ?? 0f) + (d.values == "안전" ? 0.3f : 0f);
            return Mathf.Clamp01(stay);
        }

        /// <summary>Utility score of a quest for this adventurer (0 = refuse).</summary>
        public static float Score(Adventurer a, Quest q)
        {
            var d = a.Data;
            // F-2: stress (and leave) decide whether a quest is considered at all; a tense member is less keen.
            if (Welfare.GuildWelfare.QuestRefusal(a, q.Danger) != null) return 0f;
            // Party work gathered for lately in vain (P4-97): not taken up again at once, to stand in the plaza for nothing.
            if (Services.TryGet<PartyService>(out var parties) && parties.RestingAfterFailure(q)) return 0f;
            // Party work (a rare monster P4-85, a region lord P4-90) counts the party that could go along —
            // everyone the guild could gather for it, not only those idle in town this moment (P4-97).
            int partySize = QuestRisk.ExpectedParty(q);
            // Same margin as risk_for_me and the quest board warning (QuestRisk); "무모" is refused.
            float margin = QuestRisk.Margin(AdventurerStats.Power(d), partySize, q);
            string risk = QuestRisk.Label(margin);
            if (risk == QuestRisk.Reckless) return 0f;
            // Far under the quest's level, a 위험 one is not for it (P4-88): the rules never dare (the model may, rarely).
            if (QuestRisk.TooGreen(d, q, risk)) return 0f;
            // A newcomer works only where the young are no stronger than it (P4-99).
            if (QuestRisk.BeyondNewcomer(a, q)) return 0f;

            float value = q.RewardGold / (float)partySize + q.RewardExp * 1.2f / partySize;
            // Personality flavour for the rule-based fallback.
            if (d.values == "돈") value += q.RewardGold * 0.3f;
            if (d.values == "안전" && q.Danger != DangerLevel.Low) value *= 0.5f;
            if (d.values == "명예" && q.Danger == DangerLevel.High) value *= 1.5f;
            if (d.values == "동료" && q.Assignees.Count > 0) value *= 1.3f;
            if (a.Traits != null) value = a.Traits.AdjustQuestValue(q, value);

            float distance = Vector2.Distance(a.Position, q.Point);
            return value / (1f + (int)q.Danger * 0.5f) / (1f + distance / 80f) * Mathf.Min(margin, 2f) * Welfare.StressRules.QuestScoreFactor(d.stress);
        }
    }
}
