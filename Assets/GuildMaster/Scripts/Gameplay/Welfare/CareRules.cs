using System;

namespace GuildMaster.Welfare
{
    /// <summary>
    /// What the guild master can do for a stressed member and how members ask for it (F-2): relief amounts,
    /// costs, cooldowns and the timings of demands, warnings and resignations. Pure numbers, tuned in play.
    /// </summary>
    public static class CareRules
    {
        // ---- The guild master's tools ----

        public const float InterviewCooldownHours = 8f;
        /// <summary>How long a leave lasts: the member rests and takes no quest meanwhile.</summary>
        public const float LeaveHours = 8f;
        /// <summary>Time after a leave before the next one can be granted.</summary>
        public const float LeaveCooldownHours = 24f;
        public const float BonusCooldownHours = 12f;
        public const float LeaveRelief = 30f;
        /// <summary>Relief is larger when the tool is what the member asked for.</summary>
        public const float DemandMetFactor = 1.5f;

        // ---- Members asking ----

        /// <summary>A demand that is not answered within this many game hours counts as ignored.</summary>
        public const float DemandHours = 12f;
        /// <summary>After a demand ended (ignored) the member does not ask again for this long.</summary>
        public const float DemandCooldownHours = 12f;
        public const float DemandChancePerHour = 0.5f;
        /// <summary>Game hours in a row at the breaking point before the guild master is warned.</summary>
        public const float WarnAfterBreakingHours = 4f;
        /// <summary>Game hours in a row at the breaking point after which the member may simply quit.</summary>
        public const float QuitAfterBreakingHours = 10f;
        public const float QuitChancePerHour = 0.25f;
        /// <summary>Ignored demands (not yet forgotten) at which the member gives notice.</summary>
        public const int QuitGrudges = 2;
        /// <summary>Game hours between giving notice and leaving.</summary>
        public const float ResignNoticeHours = 6f;
        /// <summary>A grudge fades after this many game hours without a new one.</summary>
        public const float GrudgeFadeHours = 48f;
        /// <summary>Chance per game hour that a member at the breaking point walks away from their quest.</summary>
        public const float SabotageChancePerHour = 0.3f;

        public static string Label(CareDemand d) => d switch
        {
            CareDemand.Talk => "면담",
            CareDemand.Leave => "휴가",
            CareDemand.Bonus => "보너스",
            _ => "-",
        };

        /// <summary>Gold a bonus costs, from the member's level.</summary>
        public static int BonusCost(int level) => 40 + 10 * Math.Max(1, level);

        /// <summary>Stress an interview takes away: people who value company (and talkers) gain more, money people less.</summary>
        public static float InterviewRelief(string values, string mbti)
        {
            float r = 15f;
            if (values == "동료") r += 5f;
            else if (values == "돈") r -= 5f;
            if (!string.IsNullOrEmpty(mbti) && mbti[0] == 'E') r += 3f;
            return r;
        }

        /// <summary>Stress a bonus takes away: money people love it, honour seekers care less.</summary>
        public static float BonusRelief(string values)
            => values == "돈" ? 30f : values == "명예" ? 12f : 20f;

        /// <summary>What a stressed member asks for, from what they care about; very tired members ask for rest.</summary>
        public static CareDemand ChooseDemand(string values, float fatigue)
        {
            if (fatigue >= 60f) return CareDemand.Leave;
            return values switch
            {
                "돈" => CareDemand.Bonus,
                "안전" or "모험" => CareDemand.Leave,
                _ => CareDemand.Talk,
            };
        }
    }
}
