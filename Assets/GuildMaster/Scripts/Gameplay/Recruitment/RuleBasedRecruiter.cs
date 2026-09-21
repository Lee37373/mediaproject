using System.Collections.Generic;
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

        /// <summary>Affinity change for one player message (pure; used by tests).</summary>
        public static int Score(string message, string values)
        {
            if (string.IsNullOrWhiteSpace(message)) return -2;
            int delta = message.Trim().Length >= 10 ? 1 : -1;
            if (values != null && ValueKeywords.TryGetValue(values, out var keys) && ContainsAny(message, keys)) delta += 8;
            if (ContainsAny(message, Praise)) delta += 4;
            if (ContainsAny(message, Rude)) delta -= 10;
            return delta;
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
        /// Picks the reply. Small talk (greetings, questions about the visitor) gets a fitting answer
        /// instead of a verdict on the message, and a short message is not punished for being short.
        /// </summary>
        public static Reply Respond(string message, string values, int affinity, bool proposal, GameRandom rng, string classLabel = null)
        {
            bool empty = string.IsNullOrWhiteSpace(message);
            bool rude = !empty && ContainsAny(message, Rude);
            int delta = Score(message, values);
            if (delta < 0 && !rude && !empty) delta = 0;
            int after = affinity + delta;
            RecruitDecision decision = RecruitDecision.Undecided;
            if (proposal) decision = after >= 55 ? RecruitDecision.Join : after < 35 ? RecruitDecision.Refuse : RecruitDecision.Undecided;
            else if (after < 25) decision = RecruitDecision.Refuse;

            string text;
            if (decision == RecruitDecision.Join)
                text = rng.Pick(new[] { "좋아요, 당신 길드라면 믿어 볼게요.", "그 말을 기다렸어요. 함께하죠!", "결정했어요. 오늘부터 잘 부탁해요." });
            else if (decision == RecruitDecision.Refuse)
                text = rng.Pick(new[] { "미안하지만 저랑은 안 맞는 것 같네요.", "이 이야기는 여기까지 하죠.", "다른 길드를 알아볼게요." });
            else if (!empty && !rude && !proposal && delta < 6 && IsGreeting(message))
                text = rng.Pick(new[] { "안녕하세요. 이 마을은 처음이라 둘러보던 참이에요.", "네, 반가워요. 길드에서 오신 분인가요?", "안녕하세요. 저한테 하실 말씀이 있나요?" });
            else if (!empty && !rude && !proposal && delta < 6 && IsSelfQuestion(message))
                text = Intro(classLabel, values, rng);
            else if (delta >= 6)
                text = rng.Pick(new[] { "오, 제가 원하던 이야기네요.", "그 부분은 마음에 드네요.", "흥미가 생기는데요?", "그런 조건이라면 솔깃하네요." });
            else if (delta < 0)
                text = rng.Pick(new[] { "음… 글쎄요.", "그런 말은 별로네요.", "말씀이 좀 그렇네요.", "좀 더 설득해 보시겠어요?" });
            else
                text = rng.Pick(new[]
                {
                    "그 길드는 어떤 곳인가요?", "제가 얻는 건 뭔가요?", "좀 더 자세히 들려주시겠어요?",
                    "어떤 일을 주로 하나요?", "일단 더 들어 볼게요.", "길드 사정이 궁금하네요.",
                });
            return new Reply { Text = text, AffinityDelta = delta, Decision = decision };
        }
    }
}
