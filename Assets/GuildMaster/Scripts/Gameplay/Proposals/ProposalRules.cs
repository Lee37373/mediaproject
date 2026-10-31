using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Quests;
using UnityEngine;

namespace GuildMaster.Proposals
{
    /// <summary>How a proposed quest trades safety against reward (F-8).</summary>
    public enum ProposalStyle { Safe, Balanced, Bold }

    /// <summary>Something the guild could go after: a hunting ground or a nest it knows about.</summary>
    public struct ProposalTarget
    {
        /// <summary>Unique per target ("hunt:slime"): two plans never share a key unless they are the same target.</summary>
        public string Key;
        /// <summary>Always Hunt (escorts need the guild master to walk along, so they are not proposed).</summary>
        public QuestType Type;
        /// <summary>The species' danger.</summary>
        public DangerLevel Danger;
        /// <summary>Species name, for texts.</summary>
        public string Label;
        /// <summary>"마을 북동쪽 23칸".</summary>
        public string Area;
        /// <summary>World units from the town centre.</summary>
        public float Distance;
        /// <summary>How many the guild has seen (0 = unknown).</summary>
        public int Observed;
        /// <summary>Where to hunt.</summary>
        public Vector2 Point;
        /// <summary>The <see cref="MonsterDefinition"/> behind the target (handed back untouched).</summary>
        public object Source;
        /// <summary>The den (a <c>Nest</c>) the hunt is for (handed back untouched; null in tests).</summary>
        public object Den;
        /// <summary>A region lord's lair with its lord (P4-83): one kill, weighed against the lord's own requirement.</summary>
        public bool IsLord;
    }

    /// <summary>One candidate quest on the proposal list: what would be posted, and how it is described.</summary>
    public class ProposalPlan
    {
        /// <summary>"P1", "P2"...: what the LLM picks by.</summary>
        public string Id;
        public ProposalTarget Target;
        public ProposalStyle Style;
        public DangerLevel Danger;
        public int Count;
        public int Party;
        /// <summary>Everything the client pays (P4-82): the guild keeps <see cref="FeeGold"/>, the members share <see cref="MemberGold"/>.</summary>
        public int RewardGold;
        public int RewardExp;
        /// <summary>How the guild's strongest member sees it with this party (<see cref="QuestRisk"/>).</summary>
        public string Risk;
        public string Title;
        public string Pitch;
        public bool FromLlm;

        public int MemberGold => QuestPricing.Split(RewardGold).members;
        public int FeeGold => QuestPricing.Split(RewardGold).fee;
    }

    /// <summary>
    /// The rule half of the LLM quest proposals (F-8), pure so it is covered by EditMode tests: turns what the guild
    /// knows into plans that are always valid, and picks a varied few. The LLM only chooses among these and words
    /// them (<see cref="ProposalPrompt"/>); without it the rules alone fill the list. P4-82 (user decision
    /// 2026-10-17): a card is another client's request for a den not on the board yet — the client pays, so the
    /// treasury no longer limits the list, and the guild master only takes a card or passes it.
    /// </summary>
    public static class ProposalRules
    {
        /// <summary>Cards on the list.</summary>
        public const int MaxCandidates = 4;
        /// <summary>Plans the LLM may choose from.</summary>
        public const int PoolSize = 8;
        /// <summary>Game hours before the list can be redrawn.</summary>
        public const float RerollCooldownHours = 4f;
        /// <summary>No automatic proposals while this many taken cards (patron quests) are still open.</summary>
        public const int MaxOpenQuests = 5;

        static readonly ProposalStyle[] Styles = { ProposalStyle.Balanced, ProposalStyle.Safe, ProposalStyle.Bold };

        // ---------------- Numbers ----------------

        /// <summary>Same rule as <c>QuestBoard.EvaluateDanger</c>: a big hunt is one step more dangerous.</summary>
        public static DangerLevel EffectiveDanger(QuestType type, DangerLevel baseDanger, int count)
            => type == QuestType.Hunt && count >= 8 && baseDanger < DangerLevel.High ? baseDanger + 1 : baseDanger;

        public static int HuntCount(ProposalStyle style) => style switch
        {
            ProposalStyle.Safe => 3,
            ProposalStyle.Balanced => 5,
            _ => 8,
        };

        public static float StyleFactor(ProposalStyle style) => style switch
        {
            ProposalStyle.Safe => 0.85f,
            ProposalStyle.Balanced => 1f,
            _ => 1.4f,
        };

        /// <summary>What the client pays, fee included: the same price as the town's own hunt (<see cref="QuestPricing.HuntReward"/>), by style.</summary>
        public static int RewardGold(QuestType type, DangerLevel danger, int count, float distance, ProposalStyle style)
            => type == QuestType.Hunt
                ? QuestPricing.HuntReward(danger, count, distance, StyleFactor(style))
                // A nest is worth a little more than a five-kill hunt.
                : QuestPricing.RoundGold(QuestPricing.DangerGold(danger) * 1.5f * QuestPricing.FarFactor(distance) * StyleFactor(style));

        public static int RewardExp(int gold) => QuestPricing.RewardExp(gold);

        /// <summary>
        /// Party size to recommend: the smallest that reaches the comfort the style stands for — "여유" for safe,
        /// "적당" for balanced, "위험" for bold — or the largest allowed.
        /// </summary>
        public static int PartyFor(ProposalStyle style, float power, DangerLevel danger, int maxParty)
            => PartyFor(style, power, RuleBasedDecisionMaker.DangerRequirement(danger), Mathf.Min(maxParty, 4));

        /// <summary>As above, against a given requirement (a region lord's, P4-83: up to two parties).</summary>
        public static int PartyFor(ProposalStyle style, float power, float requirement, int maxParty)
        {
            maxParty = Mathf.Clamp(maxParty, 1, Monsters.RegionLords.QuestParty);
            float want = style switch { ProposalStyle.Safe => 2f, ProposalStyle.Balanced => 1f, _ => 0.8f };
            for (int size = 1; size <= maxParty; size++)
                if (QuestRisk.MarginAgainst(power, size, requirement) >= want) return size;
            return maxParty;
        }

        // ---------------- Plans ----------------

        static ProposalPlan MakePlan(ProposalTarget t, ProposalStyle style, float power, int maxParty)
        {
            if (t.IsLord) return MakeLordPlan(t, style, power, maxParty);
            // Never ask for more than the guild has seen, when it has seen any.
            int count = t.Type == QuestType.Hunt ? QuestPricing.HuntCount(t.Observed, HuntCount(style)) : 1;
            var danger = EffectiveDanger(t.Type, t.Danger, count);
            int party = PartyFor(style, power, danger, maxParty);
            int gold = RewardGold(t.Type, danger, count, t.Distance, style);
            return new ProposalPlan
            {
                Target = t,
                Style = style,
                Danger = danger,
                Count = count,
                Party = party,
                RewardGold = gold,
                RewardExp = RewardExp(gold),
                Risk = QuestRisk.Label(QuestRisk.Margin(power, party, danger)),
            };
        }

        /// <summary>A region lord (P4-83): one kill, priced like its lair, weighed against the lord's own requirement.</summary>
        static ProposalPlan MakeLordPlan(ProposalTarget t, ProposalStyle style, float power, int maxParty)
        {
            float need = Monsters.RegionLords.RiskRequirement;
            int party = PartyFor(style, power, need, maxParty);
            int gold = QuestPricing.LordReward(t.Observed, t.Distance, StyleFactor(style));
            return new ProposalPlan
            {
                Target = t,
                Style = style,
                Danger = DangerLevel.High,
                Count = 1,
                Party = party,
                RewardGold = gold,
                RewardExp = RewardExp(gold),
                Risk = QuestRisk.Label(QuestRisk.MarginAgainst(power, party, need)),
            };
        }

        /// <summary>
        /// Plans for every target in every style, minus the ones that are reckless for the guild's strongest member;
        /// then a varied <see cref="PoolSize"/> of them, numbered P1, P2... A <paramref name="rng"/> shuffles
        /// near-equal choices so a redraw looks different.
        /// </summary>
        public static List<ProposalPlan> BuildPool(IReadOnlyList<ProposalTarget> targets, float power, int members, GameRandom rng = null)
        {
            var all = new List<ProposalPlan>();
            if (targets == null || members <= 0) return all;
            int maxParty = Mathf.Clamp(members, 1, 4);
            int lordParty = Mathf.Clamp(members, 1, Monsters.RegionLords.QuestParty);
            foreach (var t in targets)
            foreach (var style in Styles)
            {
                var plan = MakePlan(t, style, power, t.IsLord ? lordParty : maxParty);
                if (plan.Risk == QuestRisk.Reckless) continue;
                all.Add(plan);
            }
            var pool = Select(all, PoolSize, rng);
            for (int i = 0; i < pool.Count; i++) pool[i].Id = "P" + (i + 1);
            return pool;
        }

        /// <summary>
        /// Picks up to <paramref name="n"/> plans that differ: first one per target, cycling through the styles so
        /// safe, balanced and bold all show up when they exist (nearer targets first), then anything left.
        /// </summary>
        public static List<ProposalPlan> Select(IReadOnlyList<ProposalPlan> plans, int n, GameRandom rng = null)
        {
            var remaining = plans
                .Select(p => (plan: p, order: p.Target.Distance + (rng != null ? rng.Range(0f, 25f) : 0f)))
                .OrderBy(x => x.order)
                .Select(x => x.plan)
                .ToList();
            var result = new List<ProposalPlan>();
            var usedTargets = new HashSet<string>();
            int next = 0;
            while (result.Count < n)
            {
                ProposalPlan pick = null;
                for (int k = 0; k < Styles.Length && pick == null; k++)
                {
                    var style = Styles[(next + k) % Styles.Length];
                    pick = remaining.FirstOrDefault(p => p.Style == style && !usedTargets.Contains(p.Target.Key));
                    if (pick != null) next = (next + k + 1) % Styles.Length;
                }
                if (pick == null) break;
                result.Add(pick);
                usedTargets.Add(pick.Target.Key);
                remaining.Remove(pick);
            }
            foreach (var p in remaining)
            {
                if (result.Count >= n) break;
                result.Add(p);
            }
            return result;
        }

        /// <summary>The list shown when the LLM is off or fails: the same varied pick, without a shuffle.</summary>
        public static List<ProposalPlan> Recommend(IReadOnlyList<ProposalPlan> pool, int n = MaxCandidates) => Select(pool, n);

        // ---------------- Posting ----------------

        /// <summary>The quest the plan stands for, ready for <c>QuestBoard.TryPost</c>.</summary>
        public static QuestDraft ToDraft(ProposalPlan p) => new()
        {
            Type = QuestType.Hunt,
            Species = p.Target.Source as MonsterDefinition,
            HasPoint = true,
            Point = p.Target.Point,
            RewardGold = p.RewardGold,
            RewardExp = p.RewardExp,
            MaxParty = p.Party,
            Count = p.Count,
            Radius = QuestBoard.HuntRadius,
        };
    }
}
