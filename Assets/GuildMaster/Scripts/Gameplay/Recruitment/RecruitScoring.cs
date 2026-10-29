using System.Collections.Generic;

namespace GuildMaster.Recruitment
{
    /// <summary>What kind of thing the guild master just said, as the visitor took it.</summary>
    public enum RecruitReaction { ValueMatch, TraitMatch, Answered, Friendly, LowEffort, Clash, Rude }

    /// <summary>
    /// How much a line in a recruit talk moves affinity (user request 2026-09-30: "호감도를 떨어트리는
    /// 기준도 애매하고 어떤 대화를 해야 올릴지도 애매하다"). The model used to pick a number itself: the
    /// same line scored differently every time, kanana gave +0~+2 even to a perfect pitch, and a visitor who
    /// started low got stingier still. Now the model (or the rule-based fallback) only says what KIND of line
    /// it was, and this table decides the points — the same for every model, testable, and tunable here.
    ///
    /// On top of the table: the same kind of line in a row is worth less each time (a pitch repeated is not
    /// news), and temperament adds a little (feeling types like praise, thinking types like concrete terms,
    /// extroverts like a real conversation). Nagging is handled by RecruitmentSession.NagPenalty.
    ///
    /// Tuning targets (auto-join at <see cref="RecruitmentSession.AutoJoinAffinity"/> 80): an easy visitor
    /// (start 55) joins in about 3-4 good lines, a normal one (40) in 5-6, a hard one (25) in 7-9. The
    /// EditMode tests walk those conversations, so a change here that breaks the pacing fails a test.
    /// Pure logic (no Unity objects).
    /// </summary>
    public static class RecruitScoring
    {
        public const int ValueMatchPoints = 8;
        public const int TraitMatchPoints = 6;
        public const int AnsweredPoints = 4;
        public const int FriendlyPoints = 2;
        public const int LowEffortPoints = -2;
        public const int ClashPoints = -5;
        public const int RudePoints = -12;
        /// <summary>Temperament bonus on a well-received line (see <see cref="TemperamentBonus"/>).</summary>
        public const int TemperamentPoints = 2;

        /// <summary>The model's JSON codes, in enum order.</summary>
        public static readonly string[] Codes = { "value", "trait", "answer", "friendly", "low", "clash", "rude" };

        public static int Base(RecruitReaction r) => r switch
        {
            RecruitReaction.ValueMatch => ValueMatchPoints,
            RecruitReaction.TraitMatch => TraitMatchPoints,
            RecruitReaction.Answered => AnsweredPoints,
            RecruitReaction.Friendly => FriendlyPoints,
            RecruitReaction.LowEffort => LowEffortPoints,
            RecruitReaction.Clash => ClashPoints,
            RecruitReaction.Rude => RudePoints,
            _ => 0,
        };

        public static bool IsPositive(RecruitReaction r) => Base(r) > 0;

        /// <summary>Model code → reaction; anything unknown counts as ordinary friendly talk.</summary>
        public static RecruitReaction Parse(string code)
        {
            for (int i = 0; i < Codes.Length; i++)
                if (code == Codes[i]) return (RecruitReaction)i;
            return RecruitReaction.Friendly;
        }

        public static string Code(RecruitReaction r) => Codes[(int)r];

        /// <summary>
        /// The model's call checked against what the code can tell for sure from the words (live test
        /// 2026-10-01: kanana scored "ㅇㅇ" as <c>trait</c>). Too short, rude or a known clash always
        /// counts as the rules say; a line that names the visitor's value or what their trait wants is at
        /// least that good unless the model heard it as negative (e.g. "모험은 없어요"). Otherwise the
        /// model decides — it catches paraphrases no word list does.
        /// </summary>
        public static RecruitReaction Reconcile(RecruitReaction model, RecruitReaction rules)
        {
            if (rules == RecruitReaction.LowEffort || rules == RecruitReaction.Rude || rules == RecruitReaction.Clash) return rules;
            if ((rules == RecruitReaction.ValueMatch || rules == RecruitReaction.TraitMatch)
                && (model == RecruitReaction.Friendly || model == RecruitReaction.Answered)) return rules;
            return model;
        }

        /// <summary>
        /// A positive line of the same kind as the one before is worth half, and from the third in a row
        /// nothing (<paramref name="streak"/> = how many of that kind came right before). Negatives always count.
        /// </summary>
        public static int Diminish(int points, int streak)
            => points <= 0 || streak <= 0 ? points : streak == 1 ? points / 2 : 0;

        /// <summary>
        /// A small extra for a well-received line that suits the visitor's temperament: feeling types (F) for
        /// praise, thinking types (T) for concrete terms (numbers), extroverts (E) for a long, real message.
        /// At most one bonus per line.
        /// </summary>
        public static int TemperamentBonus(RecruitReaction r, string mbti, string playerText)
        {
            if (!IsPositive(r) || string.IsNullOrEmpty(mbti) || mbti.Length < 4 || string.IsNullOrWhiteSpace(playerText)) return 0;
            char ei = char.ToUpperInvariant(mbti[0]), tf = char.ToUpperInvariant(mbti[2]);
            if (tf == 'F' && RuleBasedRecruiter.HasPraise(playerText)) return TemperamentPoints;
            if (tf == 'T' && HasDigit(playerText)) return TemperamentPoints;
            if (ei == 'E' && playerText.Trim().Length >= 40) return TemperamentPoints;
            return 0;
        }

        /// <summary>The affinity change for one line (before any nagging penalty).</summary>
        public static int Score(RecruitReaction r, int streak, string mbti, string playerText)
        {
            int points = Diminish(Base(r), streak);
            if (points > 0) points += TemperamentBonus(r, mbti, playerText);
            return points;
        }

        static bool HasDigit(string s)
        {
            foreach (char c in s) if (char.IsDigit(c)) return true;
            return false;
        }

        /// <summary>
        /// The hint shown under the visitor's line: which way it went and roughly why, never the number, so
        /// the player can learn what this person responds to.
        /// </summary>
        public static string Feedback(RecruitReaction r, int delta, bool nagged)
        {
            if (nagged) return "▼ 조르는 것 같아 부담스러워함";
            if (delta <= 0 && IsPositive(r)) return "― 같은 얘기에 시큰둥함";
            return r switch
            {
                RecruitReaction.ValueMatch => "▲ 관심 있어 하는 이야기",
                RecruitReaction.TraitMatch => "▲ 마음에 드는 이야기",
                RecruitReaction.Answered => "▲ 궁금하던 걸 들어 만족함",
                RecruitReaction.Friendly => "△ 무난한 대화",
                RecruitReaction.LowEffort => "▼ 성의 없다고 느낌",
                RecruitReaction.Clash => "▼ 마음에 안 드는 이야기",
                RecruitReaction.Rude => "▼ 불쾌해함",
                _ => "―",
            };
        }

        // ---- what each visitor wants to hear (fed to the prompt, and to the rule-based fallback) ----

        /// <summary>What goes against a value, in words for the prompt.</summary>
        public static string ValueClash(string values) => values switch
        {
            "돈" => "보수가 적다, 공짜로 해 달라, 돈 얘기는 나중에",
            "명예" => "이름 없는 잡일, 네 실력을 낮춰 보는 말",
            "동료" => "각자 알아서 하라, 길드원끼리 데면데면하다",
            "모험" => "늘 같은 반복 업무, 멀리는 안 간다",
            "안전" => "위험한 일이 많다, 다치는 건 각오해라",
            "지식" => "배울 건 없다, 생각하지 말고 시키는 대로",
            _ => "네가 소중히 여기는 것을 무시하는 말",
        };

        /// <summary>What a trait likes to hear, in words for the prompt (empty if nothing particular).</summary>
        public static string TraitWants(string traitId) => traitId switch
        {
            "brave" => "강한 적이나 큰 의뢰에 도전할 기회",
            "sturdy" => "튼튼한 몸을 믿어 주는 말",
            "marksman" => "솜씨를 알아봐 주는 말",
            "diligent" => "할 일이 분명하고 성실함을 인정받는 것",
            "quick_learner" => "배우고 성장할 기회, 훈련",
            "nimble" => "빠른 몸놀림을 살릴 수 있는 일",
            "hawkeye" => "눈썰미를 알아봐 주는 말",
            "sociable" => "길드원들과 어울리고 함께 다니는 것",
            "loyal" => "믿음과 오래 함께하자는 약속",
            "coward" => "위험한 의뢰를 강요하지 않는다는 약속, 안전",
            "frail" => "무리시키지 않고 치료와 휴식을 챙겨 준다는 말",
            "clumsy" => "실수해도 괜찮다는 배려",
            "lazy" => "쉬는 시간과 편한 일",
            "greedy" => "두둑한 보상과 네 몫",
            "sluggish" => "서두르지 않아도 된다는 여유",
            "loner" => "혼자 움직여도 된다는 자유",
            "arrogant" => "네 실력을 인정하고 추켜세우는 말",
            _ => "",
        };

        /// <summary>What a trait hates to hear, in words for the prompt (empty if nothing particular).</summary>
        public static string TraitDislikes(string traitId) => traitId switch
        {
            "brave" => "몸 사리라는 말",
            "sturdy" => "약해 보인다는 말",
            "marksman" => "실력을 의심하는 말",
            "diligent" => "대충 해도 된다는 말",
            "quick_learner" => "배울 게 없다는 말",
            "sociable" => "혼자 다니라는 말",
            "loyal" => "사람을 쉽게 갈아치운다는 말",
            "coward" => "위험한 일이 많다는 말",
            "frail" => "험한 일을 다 해야 한다는 말",
            "clumsy" => "실수를 탓하는 말",
            "lazy" => "쉬지 말고 일하라는 말",
            "greedy" => "보수가 적다는 말",
            "sluggish" => "빨리빨리 하라는 재촉",
            "loner" => "늘 파티로 몰려다녀야 한다는 말",
            "arrogant" => "가르치려 들거나 깔보는 말",
            _ => "",
        };

        /// <summary>Words the rule-based fallback reads as "this is what they like to hear".</summary>
        public static readonly Dictionary<string, string[]> TraitWantWords = new()
        {
            ["brave"] = new[] { "강한", "도전", "큰 의뢰", "보스" },
            ["sturdy"] = new[] { "튼튼", "체력" },
            ["marksman"] = new[] { "솜씨", "명중", "정확" },
            ["diligent"] = new[] { "성실", "꾸준", "맡은 일" },
            ["quick_learner"] = new[] { "배우", "성장", "훈련" },
            ["nimble"] = new[] { "빠르", "날렵" },
            ["hawkeye"] = new[] { "눈썰미", "정찰" },
            ["sociable"] = new[] { "동료", "함께", "어울" },
            ["loyal"] = new[] { "믿", "오래", "가족" },
            ["coward"] = new[] { "안전", "무리하지", "위험하지 않" },
            ["frail"] = new[] { "치료", "무리하지", "휴식" },
            ["clumsy"] = new[] { "실수해도", "괜찮아" },
            ["lazy"] = new[] { "쉬", "휴식", "편한" },
            ["greedy"] = new[] { "보상", "보수", "몫", "골드" },
            ["sluggish"] = new[] { "천천히", "여유" },
            ["loner"] = new[] { "혼자", "자유롭게" },
            ["arrogant"] = new[] { "실력", "대단", "최고", "인정" },
        };

        /// <summary>Words the rule-based fallback reads as going against a value.</summary>
        public static readonly Dictionary<string, string[]> ValueClashWords = new()
        {
            ["돈"] = new[] { "공짜", "무보수", "적은 보수", "보수는 적" },
            ["명예"] = new[] { "잡일", "허드렛" },
            ["동료"] = new[] { "각자 알아서", "혼자 해" },
            ["모험"] = new[] { "반복", "지루", "같은 일" },
            ["안전"] = new[] { "위험한 일", "목숨", "희생", "다쳐도" },
            ["지식"] = new[] { "배울 건 없", "시키는 대로" },
        };

        /// <summary>For the rule-based fallback: does the message offer something one of these traits wants?</summary>
        public static bool OffersTraitWant(string message, IEnumerable<string> traitIds) => WantedTrait(message, traitIds) != null;

        /// <summary>Which of these traits the message offers something to (the first that matches), or null.</summary>
        public static string WantedTrait(string message, IEnumerable<string> traitIds)
        {
            if (string.IsNullOrWhiteSpace(message) || traitIds == null) return null;
            foreach (var id in traitIds)
                if (id != null && TraitWantWords.TryGetValue(id, out var words))
                    foreach (var w in words) if (message.Contains(w)) return id;
            return null;
        }

        /// <summary>For the rule-based fallback: does the message go against this value?</summary>
        public static bool ClashesWithValue(string message, string values)
        {
            if (string.IsNullOrWhiteSpace(message) || values == null || !ValueClashWords.TryGetValue(values, out var words)) return false;
            foreach (var w in words) if (message.Contains(w)) return true;
            return false;
        }
    }
}
