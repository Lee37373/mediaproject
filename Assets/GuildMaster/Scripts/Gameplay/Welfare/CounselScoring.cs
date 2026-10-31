using System;

namespace GuildMaster.Welfare
{
    /// <summary>What kind of thing the guild master just said in a counselling talk, as the member took it.</summary>
    public enum CounselReaction { Empathy, Listen, Solution, Praise, Small, LowEffort, Dismiss, Rude }

    /// <summary>
    /// How much one line of a counselling talk (면담) moves a member's stress (user request 2026-10-01: stress care
    /// by real conversation instead of a button). Same idea as <see cref="Recruitment.RecruitScoring"/>: the model
    /// (or the rule-based fallback) only says what KIND of line it was, and this table decides the relief — the
    /// same for every model, testable and tunable here.
    ///
    /// Positive points take stress away, negative ones add to it. On top of the table: hitting what really weighs
    /// on the member (<see cref="CounselConcern"/>) is worth more, the same kind of line in a row is worth less
    /// each time, feeling types (F) take comfort and praise better, thinking types (T) concrete help, and a talk
    /// the member asked for counts <see cref="CareRules.DemandMetFactor"/> times. A talk can take at most
    /// <see cref="MaxRelief"/> off and add at most <see cref="MaxHarm"/>.
    ///
    /// Pacing target: a caring talk (ask, sympathise, touch the real worry, offer help) settles a member in
    /// about 4-5 lines; empty or harsh words make things worse. Pure logic, covered by EditMode tests.
    /// </summary>
    public static class CounselScoring
    {
        public const int EmpathyPoints = 7;
        public const int ListenPoints = 5;
        public const int SolutionPoints = 8;
        public const int PraisePoints = 5;
        public const int SmallPoints = 1;
        public const int LowEffortPoints = -3;
        public const int DismissPoints = -7;
        public const int RudePoints = -12;
        /// <summary>Extra for a well-received line that goes to what really weighs on the member.</summary>
        public const int TouchedBonus = 4;
        public const int TemperamentPoints = 2;

        /// <summary>Most stress one talk can take away.</summary>
        public const int MaxRelief = 45;
        /// <summary>Most stress one talk can add (15 → 24 on 2026-10-31, so failed lines can cost something).</summary>
        public const int MaxHarm = 24;
        /// <summary>Relief at which the member feels better and ends the talk themselves.</summary>
        public const int ReliefGoal = 30;
        /// <summary>Below this stress the member has nothing left to get off their chest.</summary>
        public const float CalmStress = 15f;
        /// <summary>Hurtful lines after which the member walks out.</summary>
        public const int WalkoutNegatives = 3;
        /// <summary>Guild master lines after which the talk winds down on its own.</summary>
        public const int MaxTurns = 8;

        public static readonly string[] Codes = { "empathy", "listen", "solution", "praise", "small", "low", "dismiss", "rude" };

        public static int Base(CounselReaction r) => r switch
        {
            CounselReaction.Empathy => EmpathyPoints,
            CounselReaction.Listen => ListenPoints,
            CounselReaction.Solution => SolutionPoints,
            CounselReaction.Praise => PraisePoints,
            CounselReaction.Small => SmallPoints,
            CounselReaction.LowEffort => LowEffortPoints,
            CounselReaction.Dismiss => DismissPoints,
            CounselReaction.Rude => RudePoints,
            _ => 0,
        };

        public static bool IsPositive(CounselReaction r) => Base(r) > 0;

        /// <summary>A line that hurts (counts towards walking out).</summary>
        public static bool IsHurtful(CounselReaction r) => r == CounselReaction.Dismiss || r == CounselReaction.Rude;

        /// <summary>Lines that make a guarded member start talking about what is wrong.</summary>
        public static bool OpensUp(CounselReaction r)
            => r == CounselReaction.Empathy || r == CounselReaction.Listen || r == CounselReaction.Praise;

        public static CounselReaction Parse(string code)
        {
            for (int i = 0; i < Codes.Length; i++)
                if (code == Codes[i]) return (CounselReaction)i;
            return CounselReaction.Small;
        }

        public static string Code(CounselReaction r) => Codes[(int)r];

        /// <summary>Second of a kind in a row is worth half, from the third nothing (negatives always count).</summary>
        public static int Diminish(int points, int streak)
            => points <= 0 || streak <= 0 ? points : streak == 1 ? points / 2 : 0;

        /// <summary>F: comfort and praise land better. T: concrete help does.</summary>
        public static int TemperamentBonus(CounselReaction r, string mbti)
        {
            if (string.IsNullOrEmpty(mbti) || mbti.Length < 3) return 0;
            char tf = char.ToUpperInvariant(mbti[2]);
            if (tf == 'F' && (r == CounselReaction.Empathy || r == CounselReaction.Praise)) return TemperamentPoints;
            if (tf == 'T' && r == CounselReaction.Solution) return TemperamentPoints;
            return 0;
        }

        /// <summary>
        /// Stress relief for one line (negative = more stress), before the per-talk caps.
        /// <paramref name="streak"/> = how many lines of the same kind came right before.
        /// </summary>
        public static int Score(CounselReaction r, int streak, string mbti, bool touched, bool demandMet)
        {
            int points = Diminish(Base(r), streak);
            if (points <= 0) return points;
            points += TemperamentBonus(r, mbti);
            if (touched) points += TouchedBonus;
            if (demandMet) points = (int)Math.Round(points * CareRules.DemandMetFactor);
            return points;
        }

        /// <summary>Keeps one line's effect inside what is left of this talk's caps.</summary>
        public static int Cap(int delta, int reliefSoFar, int harmSoFar)
            => delta >= 0 ? Math.Min(delta, Math.Max(0, MaxRelief - reliefSoFar))
                          : -Math.Min(-delta, Math.Max(0, MaxHarm - harmSoFar));

        /// <summary>
        /// The model's call checked against what the words say for sure (same idea as
        /// <see cref="Recruitment.RecruitScoring.Reconcile"/>): too short, rude or dismissive always counts as the
        /// rules say; a clear offer of help or sympathy is at least that good unless the model heard it as harsh.
        /// </summary>
        public static CounselReaction Reconcile(CounselReaction model, CounselReaction rules)
        {
            if (rules == CounselReaction.LowEffort || rules == CounselReaction.Rude || rules == CounselReaction.Dismiss) return rules;
            if ((rules == CounselReaction.Solution || rules == CounselReaction.Empathy) && model == CounselReaction.Small) return rules;
            return model;
        }

        /// <summary>How the line landed, shown under the member's answer: a direction, never the number.</summary>
        public static string Feedback(CounselReaction r, int delta, bool touched)
        {
            if (delta <= 0 && IsPositive(r)) return "― 같은 말만 되풀이한다고 느낌";
            string text = r switch
            {
                CounselReaction.Empathy => "▲ 마음을 알아줘서 조금 풀림",
                CounselReaction.Listen => "▲ 얘기를 들어 주려는 게 느껴짐",
                CounselReaction.Solution => "▲ 구체적인 도움에 안심함",
                CounselReaction.Praise => "▲ 인정받아 힘이 남",
                CounselReaction.Small => "△ 무난한 대화",
                CounselReaction.LowEffort => "▼ 성의 없다고 느낌",
                CounselReaction.Dismiss => "▼ 고민을 가볍게 여긴다고 느낌",
                CounselReaction.Rude => "▼ 상처받음",
                _ => "―",
            };
            return touched && delta > 0 ? text + " · 진짜 고민을 짚음" : text;
        }

        // ---------------- The rule-based reading of a line ----------------

        static readonly string[] RudeWords = { "한심", "멍청", "바보", "닥쳐", "꺼져", "쓸모없", "쓸모 없", "짜증나게", "귀찮게", "병신" };
        static readonly string[] DismissWords =
        {
            "참아라", "좀 참아", "그냥 참", "참으세요", "참고 해", "징징", "별것도 아닌", "별거 아닌", "엄살", "핑계", "그만 좀", "다들 힘들",
            "네가 약해", "일이나 해", "알아서 해", "나약", "그 정도로", "유난", "투정",
        };
        static readonly string[] SolutionWords =
        {
            "휴가", "쉬어", "쉬도록", "쉬게", "쉬자", "보너스", "보상", "빼 줄", "빼줄", "바꿔 줄", "줄여", "약속", "도와줄", "도와 줄", "치료",
            "당분간", "챙겨 줄", "챙겨줄", "해결", "같이 가", "함께 가", "대신",
        };
        static readonly string[] EmpathyWords =
        {
            "힘들었", "힘들지", "힘들었겠", "괜찮아", "괜찮아요", "괜찮을", "걱정", "미안", "고생", "이해해", "이해가", "속상", "슬펐", "슬프",
            "아팠", "그랬구나", "그랬군요", "마음이", "위로", "곁에",
        };
        static readonly string[] PraiseWords = { "잘했", "잘 해", "잘해 주", "대단", "고마워", "고맙", "덕분", "믿어", "믿고", "자랑", "최고", "든든" };
        static readonly string[] ListenWords = { "무슨 일", "왜 그", "어떤", "말해", "얘기해", "이야기해", "들어 줄", "들어줄", "털어놔", "털어놓", "무엇이", "뭐가" };

        static bool ContainsAny(string text, string[] words)
        {
            foreach (var w in words) if (text.Contains(w)) return true;
            return false;
        }

        /// <summary>What kind of line this is by its words (rule-based fallback, and the check on the model).</summary>
        public static CounselReaction Classify(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return CounselReaction.LowEffort;
            string m = message.Trim();
            if (ContainsAny(m, RudeWords)) return CounselReaction.Rude;
            if (ContainsAny(m, DismissWords)) return CounselReaction.Dismiss;
            if (ContainsAny(m, SolutionWords)) return CounselReaction.Solution;
            if (ContainsAny(m, EmpathyWords)) return CounselReaction.Empathy;
            if (ContainsAny(m, PraiseWords)) return CounselReaction.Praise;
            if (m.Length < 5) return CounselReaction.LowEffort;
            if (m.Contains('?') || ContainsAny(m, ListenWords)) return CounselReaction.Listen;
            return CounselReaction.Small;
        }
    }
}
