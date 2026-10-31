using System;

namespace GuildMaster.Welfare
{
    public enum CounselRoll { Success, Fail, CritFail }

    /// <summary>
    /// Whether a kind line in a counselling talk (면담) actually reaches the member (user request 2026-10-31: "면담이
    /// 너무 다 성공하는 느낌 … 영입 대화처럼 실패할 경우 스트레스가 올라가는 게"). Same idea as the recruit talk's
    /// <see cref="Recruitment.RecruitPersuasion"/>: every caring line is rolled; on success it is scored by
    /// <see cref="CounselScoring"/> as before, on failure it falls flat and stress goes up, a bad failure stings more
    /// and counts as hurtful, and <see cref="FailLimit"/> failures make the member walk out.
    ///
    /// The odds: a question or plain talk is safe-ish, comfort and praise less so; a line on the real worry is the best
    /// bet once it is known, a guess at the wrong worry or help for a problem they do not have rarely lands. Feeling
    /// types (F) take comfort and praise better, thinking types (T) concrete help; a talk the member asked for and a
    /// member who has opened up are easier, ignored demands (grudges), very high stress and repeating the same kind of
    /// line make it harder. Harsh lines are not rolled: they always hurt. Pure, covered by EditMode tests.
    /// Tuned so a sensible talk settles the member about 3 times in 4; the rest end in a walkout (Monte Carlo in
    /// CounselOddsTests).
    /// </summary>
    public static class CounselOdds
    {
        public const int ListenChance = 60;
        public const int SmallChance = 55;
        public const int EmpathyChance = 50;
        public const int PraiseChance = 45;
        /// <summary>A kind word on the worry the member really has.</summary>
        public const int TouchChance = 65;
        /// <summary>Help aimed at the worry the member really has.</summary>
        public const int SolveChance = 60;
        /// <summary>An offer of help with no particular worry in mind (typed lines).</summary>
        public const int LooseSolutionChance = 35;
        /// <summary>A guess at, or help for, a worry the member does not have.</summary>
        public const int OffTopicChance = 20;

        public const int TemperamentBonus = 10;
        public const int AskedBonus = 10;
        public const int OpenBonus = 5;
        public const int GrudgePenalty = 8;
        public const int MaxGrudgePenalty = 16;
        public const float HighStress = 80f;
        public const int HighStressPenalty = 10;
        public const int RepeatPenalty = 15;
        public const int MinChance = 5;
        public const int MaxChance = 95;

        /// <summary>Share of failures that go badly wrong (stings more, counts as hurtful).</summary>
        public const float CritShare = 0.2f;
        public const int FailHarm = 4;
        public const int CritHarm = 8;
        /// <summary>Failed lines after which the member has had enough and walks out.</summary>
        public const int FailLimit = 3;

        /// <summary>Only lines meant to help are rolled; harsh and empty ones already count against the guild master.</summary>
        public static bool Rolls(CounselReaction r) => CounselScoring.IsPositive(r);

        public static int Base(CounselReaction r, bool touched, bool offTopic) => r switch
        {
            CounselReaction.Empathy => offTopic ? OffTopicChance : touched ? TouchChance : EmpathyChance,
            CounselReaction.Solution => offTopic ? OffTopicChance : touched ? SolveChance : LooseSolutionChance,
            CounselReaction.Listen => ListenChance,
            CounselReaction.Praise => PraiseChance,
            CounselReaction.Small => SmallChance,
            _ => 0,
        };

        /// <summary>The chance (%) that this line lands; 0 for lines that are not rolled.</summary>
        public static int Chance(CounselReaction r, bool touched, bool offTopic, string mbti, int grudges, float stress,
                                 bool asked, bool open, bool repeat)
        {
            if (!Rolls(r)) return 0;
            int c = Base(r, touched, offTopic);
            if (CounselScoring.TemperamentBonus(r, mbti) > 0) c += TemperamentBonus;
            if (asked) c += AskedBonus;
            if (open) c += OpenBonus;
            c -= Math.Min(MaxGrudgePenalty, Math.Max(0, grudges) * GrudgePenalty);
            if (stress >= HighStress) c -= HighStressPenalty;
            if (repeat) c -= RepeatPenalty;
            return Math.Max(MinChance, Math.Min(MaxChance, c));
        }

        /// <summary><paramref name="roll"/> and <paramref name="critRoll"/> in [0, 1).</summary>
        public static CounselRoll Roll(int chance, double roll, double critRoll)
            => roll * 100.0 < chance ? CounselRoll.Success : critRoll < CritShare ? CounselRoll.CritFail : CounselRoll.Fail;

        /// <summary>Stress a failed line adds.</summary>
        public static int Harm(CounselRoll roll) => roll switch
        {
            CounselRoll.CritFail => CritHarm,
            CounselRoll.Fail => FailHarm,
            _ => 0,
        };

        public static string Feedback(CounselRoll roll)
            => roll == CounselRoll.CritFail ? "▼ 오히려 마음이 상함 (크게 실패)" : "▽ 와닿지 않음 (실패)";
    }
}
