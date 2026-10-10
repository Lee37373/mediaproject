using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.LLM;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>A party's pool was split at the guild (log, UI, memories).</summary>
    public struct PartySettledEvent
    {
        public Party Party;
        public int Pool;
        public IReadOnlyList<(Adventurer member, int share)> Shares;
        public bool FromLlm;
        public string Say;
    }

    /// <summary>
    /// Splitting a party's pool back at the guild (P4-50, user decision 2026-09-22): the leader
    /// decides (LLM, from its personality, each member's part in the fights and its feelings), with
    /// a rule split as fallback — half equal, half by contribution (damage dealt and taken, healing,
    /// kills). Then each member weighs its
    /// share: under <see cref="UnfairShare"/> of an equal share it is unhappy and likely to leave
    /// (more so if it dislikes the leader); a member on bad terms with another member leaves too.
    /// EXP is not split here: kills share it and quest EXP goes by contribution, as before.
    /// </summary>
    public class PartySettlement
    {
        /// <summary>A share below this fraction of an equal one is unfair (user decision: 70%).</summary>
        public const float UnfairShare = 0.7f;
        /// <summary>Chance an unfairly paid member leaves before feelings for the leader count.</summary>
        public const float UnfairLeaveBase = 0.6f;
        /// <summary>At or below this feeling for another member (적대) a member always leaves.</summary>
        public const int HostileToMate = -40;
        /// <summary>At or below this feeling (껄끄러움) a member may leave.</summary>
        public const int UneasyWithMate = -15;
        public const float UneasyLeaveChance = 0.35f;

        readonly LlmGateway _gateway;

        public PartySettlement(LlmGateway gateway) { _gateway = gateway; }

        // ---------------- Pure rules (EditMode-tested) ----------------

        /// <summary>
        /// Splits <paramref name="pool"/> in proportion to <paramref name="weights"/> (all zero →
        /// equally). Floors each share and hands the leftover gold out one by one, largest remainder first,
        /// so the shares always add up to the pool.
        /// </summary>
        public static int[] Distribute(int pool, IReadOnlyList<float> weights)
        {
            int n = weights.Count;
            var shares = new int[n];
            if (n == 0 || pool <= 0) return shares;
            float total = 0f;
            for (int i = 0; i < n; i++) total += Mathf.Max(0f, weights[i]);
            var exact = new float[n];
            int given = 0;
            for (int i = 0; i < n; i++)
            {
                exact[i] = total > 0f ? pool * Mathf.Max(0f, weights[i]) / total : pool / (float)n;
                shares[i] = Mathf.FloorToInt(exact[i]);
                given += shares[i];
            }
            var order = Enumerable.Range(0, n).OrderByDescending(i => exact[i] - shares[i]).ThenBy(i => i).ToList();
            for (int k = 0; given < pool; k = (k + 1) % n, given++) shares[order[k]]++;
            return shares;
        }

        /// <summary>Rule split: half of the pool equally, half by each member's part in the fights.</summary>
        public static int[] RuleSplit(int pool, IReadOnlyList<float> contribution)
        {
            int n = contribution.Count;
            if (n == 0) return Array.Empty<int>();
            float total = contribution.Sum(c => Mathf.Max(0f, c));
            var weights = new float[n];
            for (int i = 0; i < n; i++)
                weights[i] = 0.5f / n + (total > 0f ? 0.5f * Mathf.Max(0f, contribution[i]) / total : 0.5f / n);
            return Distribute(pool, weights);
        }

        /// <summary>
        /// Chance this member walks out after the split: an unfair share (under 70% of an equal one)
        /// starts at 60%, raised by disliking the leader and lowered by liking it (feeling / 200).
        /// </summary>
        public static float LeaveChanceOverShare(int share, int pool, int members, int feelingToLeader)
        {
            if (pool <= 0 || members <= 1) return 0f;
            float fair = pool / (float)members;
            if (share >= fair * UnfairShare) return 0f;
            return Mathf.Clamp(UnfairLeaveBase - feelingToLeader / 200f, 0.1f, 0.95f);
        }

        /// <summary>Chance to leave over bad blood with another member (적대 always, 껄끄러움 sometimes).</summary>
        public static float LeaveChanceOverMates(int worstFeeling)
            => worstFeeling <= HostileToMate ? 1f : worstFeeling <= UneasyWithMate ? UneasyLeaveChance : 0f;

        // ---------------- The split at the guild ----------------

        public void Begin(Party p, GuildParties parties)
        {
            var members = p.Arrived.Where(m => m != null && m.IsAlive && p.Has(m)).ToList();
            // The leader first (m1), then joining order.
            members.Sort((x, y) => p.Members.IndexOf(x).CompareTo(p.Members.IndexOf(y)));
            if (members.Count == 0 || p.Pool <= 0)
            {
                Conclude(p, parties, members, new int[members.Count], null, false);
                return;
            }
            _ = SplitAsync(p, parties, members);
        }

        async Task SplitAsync(Party p, GuildParties parties, List<Adventurer> members)
        {
            int pool = p.Pool;
            var contribution = members.Select(m => p.Out.TryGetValue(m, out var r) ? r.Weight : 0f).ToList();
            int[] shares = null;
            string say = null;
            bool fromLlm = false;
            var splitter = members[0]; // the leader, or the first one back if the leader is still out
            int session = Services.Generation;
            try
            {
                if (_gateway != null && _gateway.Enabled && _gateway.IsReady && members.Count >= 2 && members.Count <= SplitPrompt.MaxMembers)
                {
                    var ctx = BuildContext(splitter, members, p, pool);
                    var result = await _gateway.RequestAsync(SplitPrompt.BuildRequest(ctx, splitter.Data.id));
                    // Loaded or restarted while the leader was thinking: this party is not in the game
                    // any more (its split and leavers were announced to the next game's log and alerts).
                    if (session != Services.Generation) return;
                    if (result.Ok && LlmJson.TryParse<SplitPrompt.ResponseDto>(result.Text, out var dto)
                        && SplitPrompt.TryInterpret(dto, members.Count, pool, out shares, out say))
                    {
                        fromLlm = true;
                        if (result.Exchange != null) result.Exchange.Note = "채택: " + string.Join("/", shares);
                    }
                    else if (result.Exchange != null) result.Exchange.Note = "규칙 분배로 대체: " + (result.Ok ? "parse" : result.Error);
                }
            }
            catch (Exception ex) { Debug.LogException(ex); }

            if (shares == null)
            {
                shares = RuleSplit(pool, contribution);
                say = "활약한 만큼, 그리고 고르게 나누자.";
            }
            Conclude(p, parties, members, shares, say, fromLlm);
        }

        static SplitPrompt.ContextDto BuildContext(Adventurer leader, List<Adventurer> members, Party p, int pool)
        {
            var basic = DecisionPrompt.BuildContext(leader, Array.Empty<Quests.Quest>());
            return new SplitPrompt.ContextDto
            {
                name = leader.Data.name,
                mbti = leader.Data.mbti,
                values = leader.Data.values,
                traits = basic.traits,
                pool_gold = pool,
                members = members.Select((m, i) =>
                {
                    p.Out.TryGetValue(m, out var r);
                    return new SplitPrompt.MemberDto
                    {
                        key = SplitPrompt.Key(i),
                        name = m.Data.name,
                        @class = AdventurerData.ClassLabel(m.Data.cls),
                        level = m.Data.level,
                        damage = Mathf.RoundToInt(r?.Damage ?? 0f),
                        damage_taken = Mathf.RoundToInt(r?.Taken ?? 0f),
                        healing = Mathf.RoundToInt(r?.Healing ?? 0f),
                        kills = r?.Kills ?? 0,
                        my_feeling = m == leader ? "나" : Relationships.Describe(Relationships.Get(leader, m)),
                    };
                }).ToArray(),
            };
        }

        /// <summary>
        /// Shares of members who died while the split was being worked out (the LLM takes seconds)
        /// go to the living, evenly (the first ones take the remainder); they used to be paid into
        /// the purse of a corpse and vanish. Pure bookkeeping on <paramref name="shares"/>.
        /// </summary>
        public static void GiveTheFallensShares(IReadOnlyList<bool> alive, int[] shares)
        {
            int orphaned = 0, living = 0;
            for (int i = 0; i < alive.Count; i++)
            {
                if (alive[i]) { living++; continue; }
                orphaned += shares[i];
                shares[i] = 0;
            }
            if (orphaned <= 0 || living == 0) return;
            int each = orphaned / living, extra = orphaned % living;
            for (int i = 0; i < alive.Count; i++)
            {
                if (!alive[i]) continue;
                shares[i] += each + (extra-- > 0 ? 1 : 0);
            }
        }

        void Conclude(Party p, GuildParties parties, List<Adventurer> members, int[] shares, string say, bool fromLlm)
        {
            var splitter = members.Count > 0 ? members[0] : null;
            if (splitter != null && !string.IsNullOrEmpty(say) && p.Pool > 0) splitter.Say(say);
            GiveTheFallensShares(members.Select(m => m.IsAlive).ToList(), shares);
            var paid = new List<(Adventurer, int)>();
            Services.TryGet<LivingCosts>(out var keep);
            for (int i = 0; i < members.Count; i++)
            {
                if (!members[i].IsAlive) continue;
                members[i].Data.gold += shares[i];
                paid.Add((members[i], shares[i]));
                keep?.Settle(members[i]); // a share pays off the slate first (P4-68)
            }
            int pool = p.Pool;
            EventBus.Publish(new PartySettledEvent { Party = p, Pool = pool, Shares = paid, FromLlm = fromLlm, Say = say });

            // Who stays: an unfair share, or bad blood with another member, sends one away.
            var rng = GameRandom.Shared;
            var leaving = new List<(Adventurer, string)>();
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m == splitter || !m.IsAlive) continue;
                int feeling = Relationships.Get(m, splitter);
                float overShare = LeaveChanceOverShare(shares[i], pool, members.Count, feeling);
                if (overShare > 0f)
                {
                    Relationships.Add(m, splitter, -10, "불공평한 분배");
                    if (rng.Value < overShare) { leaving.Add((m, "분배 불만")); m.Say($"{shares[i]}G? 이런 분배라면 같이 못 다니겠어."); continue; }
                    m.Say("이번 분배는 좀 섭섭한데...");
                    continue;
                }
                if (pool > 0 && shares[i] >= pool / (float)members.Count * 1.2f) Relationships.Add(m, splitter, 5, "후한 분배");
            }
            foreach (var m in p.Members.ToList())
            {
                if (leaving.Any(l => l.Item1 == m)) continue;
                int worst = p.Members.Where(o => o != m).Select(o => Relationships.Get(m, o)).DefaultIfEmpty(0).Min();
                if (rng.Value < LeaveChanceOverMates(worst)) { leaving.Add((m, "동료와 불화")); m.Say("이 파티와는 여기까지야."); }
            }
            foreach (var (m, reason) in leaving) parties.Remove(p, m, reason);
            parties.Finish(p);
        }
    }
}
