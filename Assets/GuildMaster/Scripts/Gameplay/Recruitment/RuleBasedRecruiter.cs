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

        public static Reply Respond(string message, string values, int affinity, bool proposal, GameRandom rng)
        {
            int delta = Score(message, values);
            int after = affinity + delta;
            RecruitDecision decision = RecruitDecision.Undecided;
            if (proposal) decision = after >= 55 ? RecruitDecision.Join : after < 35 ? RecruitDecision.Refuse : RecruitDecision.Undecided;
            else if (after < 25) decision = RecruitDecision.Refuse;

            string[] pool = decision switch
            {
                RecruitDecision.Join => new[] { "좋아요, 당신 길드라면 믿어 볼게요.", "그 말을 기다렸어요. 함께하죠!", "결정했어요. 오늘부터 잘 부탁해요." },
                RecruitDecision.Refuse => new[] { "미안하지만 저랑은 안 맞는 것 같네요.", "이 이야기는 여기까지 하죠.", "다른 길드를 알아볼게요." },
                _ when delta >= 6 => new[] { "오, 제가 원하던 이야기네요.", "그 부분은 마음에 드네요.", "흥미가 생기는데요?" },
                _ when delta < 0 => new[] { "음… 글쎄요.", "그런 말은 별로네요.", "좀 더 설득해 보시겠어요?" },
                _ => new[] { "그렇군요. 다른 건요?", "조금 더 들어 볼게요.", "길드 이야기를 더 해 주세요." },
            };
            return new Reply { Text = rng.Pick(pool), AffinityDelta = delta, Decision = decision };
        }
    }
}
