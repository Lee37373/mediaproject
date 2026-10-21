using GuildMaster.Data;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// How risky a quest is for a given strength (pure). One formula shared by the adventurers'
    /// decisions (risk_for_me) and the quest board warning (P4-11), so what the player is told matches
    /// what the adventurers will think.
    /// </summary>
    public static class QuestRisk
    {
        public const string Easy = "여유";
        public const string Fair = "적당";
        public const string Risky = "위험";
        public const string Reckless = "무모";

        /// <summary>Party strength (leader power × 1.6 per extra member) over the danger's requirement.</summary>
        public static float Margin(float leaderPower, int partySize, DangerLevel danger)
            => MarginAgainst(leaderPower, partySize, RuleBasedDecisionMaker.DangerRequirement(danger));

        /// <summary>Party strength over a given requirement (a region lord's is beyond the danger steps, P4-83).</summary>
        public static float MarginAgainst(float leaderPower, int partySize, float requirement)
            => leaderPower * (1f + 0.6f * (Mathf.Max(1, partySize) - 1)) / Mathf.Max(0.01f, requirement);

        /// <summary>
        /// What a quest asks for: its danger's requirement, or a region lord's own (P4-83) — for the lord's quest, and
        /// for a hunt on a lair whose living lord the guild has seen there (its guards fight beside it).
        /// </summary>
        public static float Requirement(Quests.Quest q)
        {
            // An incident asks for its own (a rare variant: a full party of its level, P4-85).
            if (q.Incident != null && Incidents.IncidentRules.Requirement(q.Incident) is { } own) return own;
            return IsDragonWork(q) ? Monsters.Dragon.RiskRequirement
                 : IsLordWork(q) ? Monsters.RegionLords.RiskRequirement : RuleBasedDecisionMaker.DangerRequirement(q.Danger);
        }

        /// <summary>A hunt for the dragon (P4-93): beyond a lord, and work for a whole party.</summary>
        public static bool IsDragonWork(Quests.Quest q) => q?.Species != null && q.Species.isDragon && q.Type == Quests.QuestType.Hunt;

        /// <summary>A region lord's quest, or a hunt on a lair whose living lord the guild has seen there (P4-83).</summary>
        public static bool IsLordWork(Quests.Quest q)
            => q != null && (q.Species != null && q.Species.isRegionLord
                             || q.TargetNest != null && q.TargetNest.IsLair && Core.Services.TryGet<Ecosystem.GuildIntel>(out var intel) && intel.KnownLordAt(q.TargetNest) != null);

        /// <summary>The margin for this quest (its own requirement, see <see cref="Requirement"/>).</summary>
        public static float Margin(float leaderPower, int partySize, Quests.Quest q)
            => MarginAgainst(leaderPower, partySize, Requirement(q));

        // ---------------- Work for a party (P4-85) ----------------

        /// <summary>
        /// A quest meant for a whole party (a rare monster, a horde, a ruin — and a region lord, P4-90): it is rated by the
        /// party that could go, not by one alone. The lord's requirement (90) alone was beyond anyone, so nobody took it.
        /// </summary>
        public static bool IsPartyWork(Quests.Quest q)
            => q?.Incident != null && Incidents.IncidentRules.IsPartyWork(q.Incident) || IsLordWork(q) || IsDragonWork(q);

        /// <summary>A region lord's or the dragon's quest: the guild's biggest fights, gathered for until the party is up to it (P4-97).</summary>
        public static bool IsBossWork(Quests.Quest q) => IsLordWork(q) || IsDragonWork(q);

        /// <summary>
        /// The party a member reckons with when weighing a quest: those already on it plus itself — and, for party work, as
        /// many as the guild could gather for it (up to the quest's limit). Without this the rules weighed a rare hunt
        /// as a lone walk and never took it. P4-97 (user decision 2026-10-21): not only those idle in town this moment —
        /// the first taker gathers the rest as they come home (<see cref="PartyService"/>), so every member without a
        /// quest who may go out and is not far too green for it counts. Counting the idle alone, nobody ever took the
        /// dragon or a lord: the guild was out hunting, and three at most stood in town at once in three days.
        /// </summary>
        public static int ExpectedParty(Quests.Quest q)
        {
            int now = q.Assignees.Count + 1;
            if (!IsPartyWork(q)) return now;
            return Mathf.Clamp(Mathf.Max(now, GatherableCount(q)), now, Mathf.Max(now, q.MaxParty));
        }

        /// <summary>
        /// Those on the quest and every member a gathering could still win for it, not far too green (P4-88), up to the
        /// quest's limit (P4-97). A gathering that could not get past "무모" even with all of them is not begun.
        /// </summary>
        public static int GatherableCount(Quests.Quest q)
        {
            int could = q.Assignees.Count;
            if (Core.Services.TryGet<GuildRoster>(out var roster))
            {
                int green = RecommendedLevel(q) - GreenGap;
                foreach (var m in roster.Members)
                    if (CouldGather(m) && AdventurerStats.RawPower(m.Data) > green) could++;
            }
            return Mathf.Min(could, q.MaxParty);
        }

        /// <summary>A member a gathering for party work could still win: in the guild, with no quest, fit to go out (P4-69).</summary>
        public static bool CouldGather(Adventurer m)
            => m != null && m.IsAlive && m.IsGuildMember && !m.IsVisitor && m.CurrentQuest == null && Welfare.GuildWelfare.MayGoOut(m);

        /// <summary>The party that actually set out is too small for party work: "무모" with this many (the leader's power).</summary>
        public static bool TooFewFor(Quests.Quest q, float leaderPower, int partySize)
            => IsPartyWork(q) && Label(Margin(leaderPower, partySize, q)) == Reckless;

        /// <summary>
        /// A gathering for party work may set out (P4-97): when the quest is full, or when this many are up to it — for a
        /// lord or the dragon "적당" (nobody walks into the guild's biggest fight on a 위험 margin while more could still
        /// come), for an incident no longer "무모" (as before: a rare monster or a horde does not wait). When time runs
        /// out a gathering goes on what <see cref="TooFewFor"/> allows.
        /// </summary>
        public static bool ReadyToSetOut(Quests.Quest q, float leaderPower, int partySize)
        {
            if (partySize >= q.MaxParty) return true;
            float margin = Margin(leaderPower, partySize, q);
            return IsBossWork(q) ? margin >= 1f : Label(margin) != Reckless;
        }

        public static string Label(float margin)
            => margin >= 2f ? Easy : margin >= 1f ? Fair : margin >= 0.8f ? Risky : Reckless;

        /// <summary>This member's risk on this quest as words (the party it reckons with, see <see cref="ExpectedParty"/>).</summary>
        public static string RiskLabel(Adventurer a, Quests.Quest q)
            => Label(Margin(AdventurerStats.Power(a.Data), ExpectedParty(q), q));

        // ---------------- Too green for it (P4-88) ----------------

        /// <summary>
        /// The level at which a member pulls its weight on this quest: alone for an ordinary one (its danger's requirement),
        /// as one of a full party for party work (<see cref="IsPartyWork"/>) — at the guild's present strength (P4-87: with
        /// no stat buildings a member needs more levels than the old requirement). Shown to the model and the player.
        /// </summary>
        public static int RecommendedLevel(Quests.Quest q)
        {
            float need = IsPartyWork(q) ? Requirement(q) / (1f + 0.6f * (Mathf.Max(1, q.MaxParty) - 1)) : Requirement(q);
            return Mathf.Max(1, Mathf.CeilToInt(need / Mathf.Max(0.1f, AdventurerStats.PowerScale) - 0.001f));
        }

        /// <summary>A member this many levels (gear counted) under a quest's <see cref="RecommendedLevel"/> is too green for it.</summary>
        public const int GreenGap = 3;

        /// <summary>
        /// Too green for this quest and it is no safe outing for them (위험 or 무모 by <paramref name="riskLabel"/>): P4-88, user
        /// decision 2026-10-18 — new members followed their party to far 위험 quests and died (seven in five days on one seed).
        /// Levels against levels: the recommendation already counts the guild's strength.
        /// </summary>
        public static bool TooGreen(AdventurerData d, Quests.Quest q, string riskLabel)
            => (riskLabel == Risky || riskLabel == Reckless) && AdventurerStats.RawPower(d) <= RecommendedLevel(q) - GreenGap;

        /// <summary>Odds a too-green member takes or joins such a quest anyway: the bold dare twice as often.</summary>
        public static float GreenDareChance(float dangerAversion) => dangerAversion < 0f ? 0.3f : 0.15f;

        /// <summary>The roll behind <see cref="TooGreen"/>: true when this member, too green for the quest, holds back (not pure: dice).</summary>
        public static bool HoldsBack(Adventurer a, Quests.Quest q)
            => a != null && q != null && TooGreen(a.Data, q, RiskLabel(a, q))
               && !Core.GameRandom.Shared.Chance(GreenDareChance(a.Traits?.DangerAversion ?? 0f));

        /// <summary>Lowest level at which one adventurer with this gear tier reaches "적당" alone (at the guild's present strength, P4-87).</summary>
        public static int SoloLevel(DangerLevel danger, int gearTier)
            => Mathf.Max(1, Mathf.CeilToInt(RuleBasedDecisionMaker.DangerRequirement(danger) / Mathf.Max(0.1f, AdventurerStats.PowerScale) - gearTier * 1.5f - 0.001f));

        /// <summary>Smallest party (up to <paramref name="maxParty"/>) led by this power that reaches "적당"; 0 if none.</summary>
        public static int PartyNeeded(float leaderPower, DangerLevel danger, int maxParty = 4)
        {
            for (int size = 1; size <= maxParty; size++)
                if (Margin(leaderPower, size, danger) >= 1f) return size;
            return 0;
        }
    }
}
