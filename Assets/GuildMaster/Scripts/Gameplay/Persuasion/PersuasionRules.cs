using GuildMaster.Adventurers;
using GuildMaster.Quests;
using UnityEngine;

namespace GuildMaster.Persuasion
{
    public enum PersuadeDecision { Undecided, Accept, Refuse }
    /// <summary><see cref="NoFunds"/>: a deal was struck but the treasury cannot pay the promised extra gold (P4-25).</summary>
    public enum PersuadeOutcome { Ongoing, Accepted, Refused, Unavailable, NoFunds }

    /// <summary>
    /// Pure rules of quest persuasion (P4-24): when the guild master may start persuading, how willing
    /// an adventurer is at the start, how much is needed to say yes, and the verdict after each reply.
    /// No Unity objects are touched here so everything is covered by EditMode tests.
    /// </summary>
    public static class PersuasionRules
    {
        /// <summary>Game hours a quest must stay untaken before the guild master may persuade someone.</summary>
        public const float OpenAfterHours = 6f;
        /// <summary>Guild master messages per (adventurer, quest) talk before the adventurer gives a final answer.</summary>
        public const int MaxPlayerTurns = 5;
        /// <summary>Largest willingness change one reply may cause.</summary>
        public const int MaxDelta = 15;

        /// <summary>Persuasion opens for a quest nobody has taken once it has been posted long enough.</summary>
        public static bool IsOpen(QuestStatus status, int assignees, float postedAtHour, float nowHour)
            => status == QuestStatus.Open && assignees == 0 && nowHour - postedAtHour >= OpenAfterHours;

        /// <summary>Game hours left until persuasion opens (0 when it is open).</summary>
        public static float HoursUntilOpen(float postedAtHour, float nowHour)
            => Mathf.Max(0f, OpenAfterHours - (nowHour - postedAtHour));

        /// <summary>
        /// Starting willingness (0..100) from how risky the quest is for this adventurer and their
        /// personality: cautious traits start lower, bold traits and 명예/모험 values start higher.
        /// </summary>
        public static int InitialWillingness(string riskLabel, float dangerAversion, string values)
        {
            int v = riskLabel switch
            {
                QuestRisk.Easy => 65,
                QuestRisk.Fair => 55,
                QuestRisk.Risky => 40,
                _ => 20,
            };
            v -= Mathf.RoundToInt(dangerAversion * 20f);
            if (values == "명예" || values == "모험") v += 5;
            else if (values == "안전") v -= 5;
            return Mathf.Clamp(v, 5, 80);
        }

        /// <summary>Below this health a member takes no quest, however it is talked to.</summary>
        public const float MinHpToAccept = 0.3f;

        /// <summary>A "무모" quest the adventurer would never take on their own.</summary>
        public static bool IsRecklessBlocked(string riskLabel, float dangerAversion, string values)
            => riskLabel == QuestRisk.Reckless && !RecklessAllowed(riskLabel, dangerAversion, values, joiningParty: false);

        /// <summary>
        /// Personality gate for "무모" (reckless) quests — taking one is a character trait, not a bug, but a guild that
        /// takes them freely walks into certain death: cautious (danger aversion &gt; 0 or values 안전) never; bold
        /// (aversion &lt; 0 or values 명예/모험) always; everyone else only when joining someone who already took it.
        /// </summary>
        public static bool RecklessAllowed(string riskLabel, float dangerAversion, string values, bool joiningParty)
        {
            if (riskLabel != QuestRisk.Reckless) return true;
            if (dangerAversion > 0f || values == "안전") return false;
            if (dangerAversion < 0f || values == "명예" || values == "모험") return true;
            return joiningParty;
        }

        /// <summary>Willingness needed to say yes: 50, +15 for a reckless quest, +10 more when they would never take it alone.</summary>
        public static int AcceptThreshold(string riskLabel, bool recklessBlocked)
        {
            int t = 50;
            if (riskLabel == QuestRisk.Reckless) t += 15;
            if (recklessBlocked) t += 10;
            return t;
        }

        /// <summary>
        /// Verdict after an adventurer's reply. <paramref name="canAccept"/> is false while a safety rule
        /// forbids taking any quest (badly hurt); then persuasion can only end in a refusal.
        /// </summary>
        public static PersuadeOutcome Judge(int willingness, PersuadeDecision decision, int playerTurns, int threshold, bool canAccept)
        {
            if (canAccept && ((decision == PersuadeDecision.Accept && willingness >= threshold) || willingness >= threshold + 30))
                return PersuadeOutcome.Accepted;
            if (decision == PersuadeDecision.Refuse && willingness < threshold - 20)
                return PersuadeOutcome.Refused;
            if (playerTurns >= MaxPlayerTurns)
                return canAccept && willingness >= threshold + 10 ? PersuadeOutcome.Accepted : PersuadeOutcome.Refused;
            return PersuadeOutcome.Ongoing;
        }
    }
}
