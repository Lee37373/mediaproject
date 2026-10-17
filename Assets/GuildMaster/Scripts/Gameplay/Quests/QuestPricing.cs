using GuildMaster.Data;
using UnityEngine;

namespace GuildMaster.Quests
{
    /// <summary>
    /// What a client-funded quest pays (P4-82, user decision 2026-10-17): the client puts up the whole reward,
    /// the guild keeps <see cref="CommissionRate"/> of the gold as its fee and the members split the rest; EXP
    /// goes to the members whole. Shared by the town's own postings, the proposal cards and the disasters, so
    /// the same hunt costs the same wherever it came from. Pure, so it is covered by EditMode tests.
    /// </summary>
    public static class QuestPricing
    {
        /// <summary>Share of a client's gold the guild keeps for brokering the quest.</summary>
        public const float CommissionRate = 0.2f;

        /// <summary>Kills a hunt on a den asks for: five, but never more than were seen there (at least two).</summary>
        public const int HuntKills = 5;

        /// <summary>Distance from town past which a hunt pays no more for the walk (beyond the farthest dens).</summary>
        public const float FarCap = 300f;
        /// <summary>Every this many cells from town adds the base reward once more.</summary>
        public const float FarStep = 120f;

        /// <summary>The client's total split into the members' reward and the guild's fee (rounded to the fee).</summary>
        public static (int members, int fee) Split(int total)
        {
            total = Mathf.Max(0, total);
            int fee = Mathf.RoundToInt(total * CommissionRate);
            return (total - fee, fee);
        }

        /// <summary>Gold for five kills at this danger, before the trip and the size.</summary>
        public static int DangerGold(DangerLevel d) => d switch
        {
            DangerLevel.Low => 40,
            DangerLevel.Medium => 100,
            _ => 220,
        };

        /// <summary>
        /// The longer walk pays: ×1.5 at 60 cells (the old proposal cap, unchanged near town), ×2.5 at the inner
        /// edge (180), up to ×3.5 at <see cref="FarCap"/>. Danger alone stops at 높음 from level 10, so on the
        /// 512 map (levels to 25) the distance is what tells a far den from a middling one.
        /// </summary>
        public static float FarFactor(float distance) => 1f + Mathf.Clamp(distance, 0f, FarCap) / FarStep;

        /// <summary>Rounded to 5 gold, never below 10.</summary>
        public static int RoundGold(float value) => Mathf.Max(10, Mathf.RoundToInt(value / 5f) * 5);

        /// <summary>The client's total for a hunt (fee included): danger, kills asked and the walk; <paramref name="factor"/> scales it (proposal styles).</summary>
        public static int HuntReward(DangerLevel danger, int count, float distance, float factor = 1f)
            => RoundGold(DangerGold(danger) * Mathf.Max(1, count) / (float)HuntKills * FarFactor(distance) * factor);

        /// <summary>EXP the client adds to a gold reward (the members get it whole).</summary>
        public static int RewardExp(int gold) => Mathf.Max(5, Mathf.RoundToInt(gold * 0.6f / 5f) * 5);

        /// <summary>Kills to ask at a den where <paramref name="observed"/> were last seen (0 = unknown).</summary>
        public static int HuntCount(int observed, int wanted = HuntKills)
            => observed > 0 ? Mathf.Min(wanted, Mathf.Max(2, observed)) : wanted;

        /// <summary>
        /// Most members the town lets go on its hunt: it knows the danger, not the guild, so the more dangerous
        /// the more may go (two, three, four). The group still decides who comes along (party invitations).
        /// </summary>
        public static int PartyFor(DangerLevel d) => d switch
        {
            DangerLevel.Low => 2,
            DangerLevel.Medium => 3,
            _ => 4,
        };
    }
}
