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
            bool lord = q.Species != null && q.Species.isRegionLord
                        || q.TargetNest != null && q.TargetNest.IsLair && Core.Services.TryGet<Ecosystem.GuildIntel>(out var intel) && intel.KnownLordAt(q.TargetNest) != null;
            return lord ? Monsters.RegionLords.RiskRequirement : RuleBasedDecisionMaker.DangerRequirement(q.Danger);
        }

        /// <summary>The margin for this quest (its own requirement, see <see cref="Requirement"/>).</summary>
        public static float Margin(float leaderPower, int partySize, Quests.Quest q)
            => MarginAgainst(leaderPower, partySize, Requirement(q));

        // ---------------- Work for a party (P4-85) ----------------

        /// <summary>A quest meant for a whole party (a rare monster): it is rated by the party that could go, not by one alone.</summary>
        public static bool IsPartyWork(Quests.Quest q) => q?.Incident != null && Incidents.IncidentRules.IsPartyWork(q.Incident);

        /// <summary>
        /// The party a member reckons with when weighing a quest: those already on it plus itself — and, for party work, as
        /// many as are free in town to be invited (up to the quest's limit). Without this the rules weighed a rare hunt
        /// as a lone walk and never took it.
        /// </summary>
        public static int ExpectedParty(Quests.Quest q)
        {
            int now = q.Assignees.Count + 1;
            if (!IsPartyWork(q) || !Core.Services.TryGet<GuildRoster>(out var roster)) return now;
            int free = 0;
            foreach (var m in roster.Members)
                if (m.IsAlive && !m.IsVisitor && m.CurrentQuest == null && m.IsIdleInTown) free++;
            return Mathf.Clamp(Mathf.Max(now, free), now, Mathf.Max(now, q.MaxParty));
        }

        /// <summary>The party that actually set out is too small for party work: "무모" with this many (the leader's power).</summary>
        public static bool TooFewFor(Quests.Quest q, float leaderPower, int partySize)
            => IsPartyWork(q) && Label(Margin(leaderPower, partySize, q)) == Reckless;

        public static string Label(float margin)
            => margin >= 2f ? Easy : margin >= 1f ? Fair : margin >= 0.8f ? Risky : Reckless;

        // ---------------- Too green for it (P4-88) ----------------

        /// <summary>
        /// The level at which a member pulls its weight on this quest: alone for an ordinary one (its danger's requirement),
        /// as one of a full party for a region lord's. Shown to the model and the player.
        /// </summary>
        public static int RecommendedLevel(Quests.Quest q)
        {
            bool party = q.Species != null && q.Species.isRegionLord || q.Incident != null && Incidents.IncidentRules.IsPartyWork(q.Incident);
            float need = party ? Requirement(q) / (1f + 0.6f * (Mathf.Max(1, q.MaxParty) - 1)) : Requirement(q);
            return Mathf.Max(1, Mathf.CeilToInt(need));
        }

        /// <summary>A member this many levels (gear counted) under a quest's <see cref="RecommendedLevel"/> is too green for it.</summary>
        public const int GreenGap = 3;

        /// <summary>
        /// Too green for this quest and it is no safe outing for them (위험 or 무모 by <paramref name="riskLabel"/>): P4-88, user
        /// decision 2026-10-18 — new members followed their party to far 위험 quests and died (seven in five days on one seed).
        /// </summary>
        public static bool TooGreen(AdventurerData d, Quests.Quest q, string riskLabel)
            => (riskLabel == Risky || riskLabel == Reckless) && AdventurerStats.Power(d) <= RecommendedLevel(q) - GreenGap;

        /// <summary>Odds a too-green member takes or joins such a quest anyway: the bold dare twice as often.</summary>
        public static float GreenDareChance(float dangerAversion) => dangerAversion < 0f ? 0.3f : 0.15f;

        /// <summary>The roll behind <see cref="TooGreen"/>: true when this member, too green for the quest, holds back (not pure: dice).</summary>
        public static bool HoldsBack(Adventurer a, Quests.Quest q)
            => a != null && q != null && TooGreen(a.Data, q, Prompts.DecisionPrompt.RiskLabel(a, q))
               && !Core.GameRandom.Shared.Chance(GreenDareChance(a.Traits?.DangerAversion ?? 0f));

        /// <summary>Lowest level at which one adventurer with this gear tier reaches "적당" alone.</summary>
        public static int SoloLevel(DangerLevel danger, int gearTier)
            => Mathf.Max(1, Mathf.CeilToInt(RuleBasedDecisionMaker.DangerRequirement(danger) - gearTier * 1.5f));

        /// <summary>Smallest party (up to <paramref name="maxParty"/>) led by this power that reaches "적당"; 0 if none.</summary>
        public static int PartyNeeded(float leaderPower, DangerLevel danger, int maxParty = 4)
        {
            for (int size = 1; size <= maxParty; size++)
                if (Margin(leaderPower, size, danger) >= 1f) return size;
            return 0;
        }
    }
}
