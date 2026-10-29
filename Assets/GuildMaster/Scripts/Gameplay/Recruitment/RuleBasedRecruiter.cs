using System.Collections.Generic;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;

namespace GuildMaster.Recruitment
{
    /// <summary>
    /// LLM-free recruitment replies: keyword scoring against the visitor's values plus
    /// praise/rudeness, and canned lines per mood. Always available as the fallback.
    /// </summary>
    public static class RuleBasedRecruiter
    {
        public struct Reply
        {
            public string Text;
            public int AffinityDelta;
            public RecruitDecision Decision;
            /// <summary>
            /// What this fixed line happens to state outright (a <see cref="VisitorReveal"/> key), or null. Informational:
            /// what the guild master finds out is decided by the VisitorReveal rules, not by the line.
            /// </summary>
            public string Reveals;
            /// <summary>What kind of line the player's was (scored by <see cref="RecruitScoring"/>).</summary>
            public RecruitReaction Reaction;
        }

        static readonly Dictionary<string, string[]> ValueKeywords = new()
        {
            ["돈"] = new[] { "돈", "골드", "보상", "부자", "수입", "벌" },
            ["명예"] = new[] { "명예", "영웅", "명성", "전설", "이름을", "존경" },
            ["동료"] = new[] { "동료", "친구", "함께", "가족", "팀", "같이" },
            ["모험"] = new[] { "모험", "탐험", "미지", "여행", "스릴", "새로운" },
            ["안전"] = new[] { "안전", "보호", "지켜", "안정", "든든", "쉬" },
            ["지식"] = new[] { "지식", "배움", "책", "연구", "지혜", "배우" },
        };

        static readonly string[] Praise = { "대단", "훌륭", "멋지", "최고", "실력", "강해", "믿음", "부탁" };
        static readonly string[] Rude = { "바보", "멍청", "꺼져", "닥쳐", "한심", "쓰레기", "약해 보", "시시" };
        /// <summary>Ordinary disinterest/refusal, milder than outright rudeness but still a dislike signal.</summary>
        static readonly string[] Dismissive = { "관심 없", "관심없", "별로", "싫어", "싫습니다", "필요 없", "필요없", "됐어", "됐습니다" };

        /// <summary>Affinity change for one player message (pure; used by tests).</summary>
        public static int Score(string message, string values)
        {
            if (string.IsNullOrWhiteSpace(message)) return -2;
            // Rebalanced again (user report 2026-09-23, same-day follow-up: "호감도 올리는 것도 생각보다
            // 쉽지가 않네"). The previous pass fixed "decline never shows" but overshot: any topic other
            // than the visitor's exact value got the same -4 as genuine rudeness-adjacent dismissal, so
            // ordinary conversation trended down more often than up. Now a substantial, on-topic-or-not
            // message defaults mildly positive (effort is rewarded on its own), an exact values match is
            // a big bonus, and only a real mismatch, low effort, dismissiveness or rudeness costs affinity.
            int delta = message.Trim().Length >= 10 ? 2 : -1;
            if (values != null && ValueKeywords.TryGetValue(values, out var keys) && ContainsAny(message, keys)) delta += 10;
            else if (Topic(message) != null) delta -= 2; // touched a value the visitor doesn't share; a mild nudge, not a real clash
            if (ContainsAny(message, Praise)) delta += 5;
            if (ContainsAny(message, Dismissive)) delta -= 6;
            if (ContainsAny(message, Rude)) delta -= 12;
            return delta;
        }

        public static bool HasPraise(string message) => !string.IsNullOrWhiteSpace(message) && ContainsAny(message, Praise);

        /// <summary>
        /// The rule-based reading of a player line, for <see cref="RecruitScoring"/> (the LLM path picks the
        /// same categories itself).
        /// </summary>
        public static RecruitReaction Classify(string message, string values, IEnumerable<string> traitIds = null)
        {
            if (string.IsNullOrWhiteSpace(message)) return RecruitReaction.LowEffort;
            if (ContainsAny(message, Rude)) return RecruitReaction.Rude;
            if (values != null && ValueKeywords.TryGetValue(values, out var keys) && ContainsAny(message, keys)) return RecruitReaction.ValueMatch;
            if (RecruitScoring.OffersTraitWant(message, traitIds)) return RecruitReaction.TraitMatch;
            if (RecruitScoring.ClashesWithValue(message, values) || ContainsAny(message, Dismissive)) return RecruitReaction.Clash;
            if (message.Trim().Length < 6) return RecruitReaction.LowEffort;
            return RecruitReaction.Friendly;
        }

        static bool ContainsAny(string text, string[] words)
        {
            foreach (var w in words) if (text.Contains(w)) return true;
            return false;
        }

        static readonly string[] Greetings = { "안녕", "반갑", "하이", "ㅎㅇ", "안뇽", "처음 뵙" };
        static readonly string[] SelfQuestions = { "뭐해", "뭐 해", "뭐하", "뭘 해", "누구", "이름", "어디서", "어떻게 왔", "왜 왔", "왜 오", "무슨 일", "직업", "뭐 하", "어디 가", "어디 살" };

        static bool IsGreeting(string message) => ContainsAny(message, Greetings);
        static bool IsSelfQuestion(string message) => message.Contains('?') || ContainsAny(message, SelfQuestions);

        /// <summary>What the visitor is after, as one sentence per value (used when asked about themselves).</summary>
        static string Want(string values) => values switch
        {
            "돈" => "돈 되는 일이 없나 둘러보던 참이에요.",
            "명예" => "이름을 떨칠 기회를 찾고 있어요.",
            "동료" => "믿고 함께할 동료를 찾는 중이에요.",
            "모험" => "아직 못 가 본 곳을 찾아다니는 중이에요.",
            "안전" => "위험하지 않고 안정적인 일을 찾고 있어요.",
            "지식" => "배울 게 있는 곳을 찾아다니고 있어요.",
            _ => "잠깐 쉬어 가는 중이에요.",
        };

        static string Intro(string classLabel, string values, GameRandom rng)
        {
            string job = string.IsNullOrEmpty(classLabel) ? "떠돌이 모험가" : classLabel + " 모험가";
            string want = Want(values);
            return rng.Pick(new[]
            {
                $"저요? {job}예요. {want}",
                $"{job}로 이곳저곳 다니고 있어요. {want}",
                $"이 마을엔 잠깐 들렀어요. {want} 그쪽은 무슨 일이세요?",
            });
        }

        /// <summary>
        /// Lines that name the topic the player raised. Without these every fallback reply was a
        /// generic "그렇군요. 다른 건요?" no matter what was said, which reads as the visitor not
        /// listening (user report 2026-09-20). Keyed by the value word the visitor cares about.
        /// </summary>
        static readonly Dictionary<string, string[]> ValueReplies = new()
        {
            ["돈"] = new[] { "보수 이야기라면 귀가 트이네요. 얼마나 되죠?", "돈 이야기를 먼저 꺼내는 길드는 오랜만이네요." },
            ["명예"] = new[] { "이름을 남길 수 있는 일이라면 관심이 가요.", "명예로운 길드라… 그 말은 무겁게 들리네요." },
            ["동료"] = new[] { "같이 싸워 줄 사람이 있다는 건 크죠.", "동료 이야기는 언제 들어도 좋네요." },
            ["모험"] = new[] { "아직 아무도 안 가 본 곳이 있나요?", "새로운 걸 보게 해 준다면 솔깃하죠." },
            ["안전"] = new[] { "무리한 의뢰를 안 받는다니, 그건 중요하죠.", "돌아올 수 있는 일인지가 저한텐 제일 중요해요." },
            ["지식"] = new[] { "배울 게 있는 곳이라면 머물 만하죠.", "그런 이야기는 더 듣고 싶네요." },
        };

        /// <summary>
        /// A topic the visitor does NOT care about most: acknowledged, then turned to what they do want. The
        /// old fallback answered any raised topic as if it were the visitor's own passion (a 명예 visitor
        /// told "we keep you safe" replied "돌아올 수 있는 일인지가 저한텐 제일 중요해요"), which is one
        /// reason every visitor sounded alike (user report 2026-09-29). Saying what they prefer instead
        /// also tells the guild master their values.
        /// </summary>
        static readonly Dictionary<string, string> TopicAck = new()
        {
            ["돈"] = "보수 이야기군요.",
            ["명예"] = "명성이라…",
            ["동료"] = "동료라, 좋죠.",
            ["모험"] = "모험이요?",
            ["안전"] = "안전하다니 다행이긴 하네요.",
            ["지식"] = "배울 거리라…",
        };

        static readonly Dictionary<string, string> Prefer = new()
        {
            ["돈"] = "그래도 저한텐 결국 얼마를 버느냐가 제일 중요해요.",
            ["명예"] = "그래도 저는 이름을 남길 만한 일이 더 끌려요.",
            ["동료"] = "그래도 저는 누구랑 함께하느냐가 더 중요해요.",
            ["모험"] = "그래도 저는 새로운 곳에 가 보는 게 제일 좋아요.",
            ["안전"] = "그래도 저는 무사히 돌아오는 게 제일이에요.",
            ["지식"] = "그래도 저는 뭔가 배울 수 있느냐가 더 궁금해요.",
        };

        static readonly string[] Invitations =
        {
            "들어와", "들어오", "합류", "영입", "가입", "함께하자", "함께 하자", "함께해", "함께 해", "같이 하자", "같이하자",
            "우리 길드에 오", "우리 길드로", "길드원이 되", "길드원으로", "우리랑 같이", "우리와 함께",
            // polite forms (live test 2026-10-01: "같이 하실래요?" twice in a row was not caught as nagging)
            "같이 하실", "같이하실", "함께 하실", "함께하실", "같이 할래", "같이할래", "함께 할래", "함께할래", "길드에 오실",
        };

        /// <summary>
        /// The guild master asking the visitor to join, in their own words. With the formal proposal button
        /// gone (user request 2026-09-30) this is how an offer is made.
        /// </summary>
        public static bool IsInvitation(string message)
            => !string.IsNullOrWhiteSpace(message) && ContainsAny(message, Invitations);

        /// <summary>Which topic the message hit, or null.</summary>
        public static string Topic(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return null;
            foreach (var kv in ValueKeywords)
                if (ContainsAny(message, kv.Value)) return kv.Key;
            return null;
        }

        /// <summary>
        /// Picks the reply. Small talk (greetings, questions about the visitor) gets a fitting answer, a
        /// topic gets an answer from the visitor's own point of view (keen on their value, lukewarm and
        /// steering back otherwise), and every stock line comes in the visitor's own speech level.
        /// </summary>
        public static Reply Respond(string message, string values, int affinity, bool proposal, GameRandom rng, string classLabel = null,
            NpcVoice.Register register = NpcVoice.Register.Warm, IEnumerable<string> traitIds = null)
        {
            bool empty = string.IsNullOrWhiteSpace(message);
            bool rude = !empty && ContainsAny(message, Rude);
            // Used to zero out every non-rude negative delta (user report 2026-09-23: "호감도 하락이 잘
            // 나타나지 않는다"); Score() keeps the ordinary-negative case mild, so let it through.
            // Scored by kind of line (user request 2026-09-30), the same table the LLM path uses.
            var reaction = Classify(message, values, traitIds);
            int delta = RecruitScoring.Base(reaction);
            int after = affinity + delta;
            RecruitDecision decision = RecruitDecision.Undecided;
            bool invited = !proposal && !rude && IsInvitation(message);
            if (proposal) decision = after >= 55 ? RecruitDecision.Join : after < 35 ? RecruitDecision.Refuse : RecruitDecision.Undecided;
            else if (after < 25) decision = RecruitDecision.Refuse;
            // An invitation in plain talk (user request 2026-09-30): accepted once they are fairly well
            // disposed; RecruitmentSession.Judge still needs ConversationJoinFloor.
            else if (invited && after >= InviteJoinAffinity) decision = RecruitDecision.Join;

            string topic = !empty && !rude && !proposal ? Topic(message) : null;
            string reveals = null;
            int r = (int)register;

            string text;
            if (decision == RecruitDecision.Join)
                text = rng.Pick(JoinLines[r]);
            else if (decision == RecruitDecision.Refuse)
                text = rng.Pick(RefuseLines[r]);
            else if (invited)
                text = rng.Pick(NotYetLines[r]);
            else if (!empty && !rude && !proposal && delta < 6 && IsGreeting(message))
                text = rng.Pick(GreetingLines[r]);
            else if (topic != null && topic == values && ValueReplies.TryGetValue(topic, out var keen))
                text = rng.Pick(keen);
            else if (topic != null && values != null && TopicAck.TryGetValue(topic, out var ack) && Prefer.TryGetValue(values, out var prefer))
            {
                text = ack + " " + prefer;
                reveals = VisitorReveal.Values;
            }
            else if (topic != null && ValueReplies.TryGetValue(topic, out var topical))
                text = rng.Pick(topical);
            else if (!empty && !rude && !proposal && delta < 6 && IsSelfQuestion(message))
            {
                text = Intro(classLabel, values, rng);
                if (!string.IsNullOrEmpty(values) && ValueKeywords.ContainsKey(values)) reveals = VisitorReveal.Values;
            }
            else if (delta >= 6)
                text = rng.Pick(LikedLines[r]);
            else if (delta < 0)
                text = rng.Pick(DislikedLines[r]);
            else
                text = rng.Pick(FillerLines[r]);
            return new Reply { Text = text, AffinityDelta = delta, Decision = decision, Reveals = reveals, Reaction = reaction };
        }

        /// <summary>Rule-based answers accept an invitation in the talk from here (the LLM decides for itself).</summary>
        public const int InviteJoinAffinity = 45;

        // Stock lines per speech level, indexed by NpcVoice.Register (Warm, Formal, Curt, Haughty).

        static readonly string[][] NotYetLines =
        {
            new[] { "음… 아직은 좀 더 알아보고 싶어요.", "마음이 아예 없진 않은데, 조금만 더 이야기해 봐요." },
            new[] { "아직 결정하기엔 이르다고 봅니다.", "조금 더 듣고 판단하겠습니다." },
            new[] { "…아직요.", "좀 더 두고 볼게요." },
            new[] { "벌써? 날 설득하려면 좀 더 애써야지.", "그렇게 쉽게 넘어갈 줄 알았어?" },
        };

        static readonly string[][] JoinLines =
        {
            new[] { "좋아요, 당신 길드라면 믿어 볼게요.", "그 말을 기다렸어요. 함께하죠!", "결정했어요. 오늘부터 잘 부탁해요!" },
            new[] { "좋습니다. 귀 길드에 합류하겠습니다.", "결정했습니다. 앞으로 잘 부탁드립니다." },
            new[] { "…좋아요. 들어갈게요.", "알겠어요. 해 보죠." },
            new[] { "뭐, 그렇게까지 원한다면 들어가 주지.", "좋아. 나 같은 인재를 얻은 걸 다행으로 알라고." },
        };

        static readonly string[][] RefuseLines =
        {
            new[] { "미안하지만 저랑은 안 맞는 것 같아요.", "이 이야기는 여기까지 할게요.", "다른 길드를 알아볼게요." },
            new[] { "유감이지만 거절하겠습니다.", "이 제안은 받아들일 수 없습니다." },
            new[] { "싫어요.", "…됐어요. 그만하죠." },
            new[] { "됐어. 너희랑은 급이 안 맞아.", "그 정도로 날 데려가겠다고? 사양하지." },
        };

        static readonly string[][] GreetingLines =
        {
            new[] { "안녕하세요! 이 마을은 처음이라 둘러보던 참이에요.", "네, 반가워요! 길드에서 오신 분인가요?", "안녕하세요. 저한테 하실 말씀이라도?" },
            new[] { "안녕하십니까. 무슨 용건이십니까?", "반갑습니다. 길드 분이십니까?" },
            new[] { "…네, 안녕하세요.", "네. 무슨 일이죠?" },
            new[] { "어, 그래. 무슨 볼일인데?", "나한테 말을 걸다니, 보는 눈은 있네." },
        };

        static readonly string[][] LikedLines =
        {
            new[] { "오, 제가 원하던 이야기네요!", "그 부분은 정말 마음에 들어요.", "와, 흥미가 생기는데요?" },
            new[] { "그 조건은 좋군요.", "납득이 가는 말씀입니다." },
            new[] { "…그건 괜찮네요.", "나쁘지 않아요." },
            new[] { "호오, 좀 아는데?", "그 정도면 들어 줄 만하네." },
        };

        static readonly string[][] DislikedLines =
        {
            new[] { "음… 글쎄요.", "그런 말은 좀 서운한데요.", "좀 더 설득해 보시겠어요?" },
            new[] { "그 말씀엔 동의하기 어렵습니다.", "그건 좋은 조건이 아니군요." },
            new[] { "별로네요.", "…그래서요?" },
            new[] { "하, 그걸 말이라고.", "그 정도로는 어림없어." },
        };

        static readonly string[][] FillerLines =
        {
            new[] { "그 길드는 어떤 곳인가요?", "좀 더 자세히 들려주실래요?", "어떤 일을 주로 해요?", "길드 사정이 궁금하네요!" },
            new[] { "구체적으로 어떤 조건입니까?", "귀 길드는 주로 어떤 일을 합니까?", "좀 더 설명해 주시겠습니까?" },
            new[] { "그래서요?", "…계속 말해 보세요.", "제가 얻는 건요?" },
            new[] { "그래서 나한테 뭘 해 줄 수 있는데?", "좀 더 그럴듯한 얘기는 없어?", "흠, 계속해 봐." },
        };
    }
}
