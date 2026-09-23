using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Recruitment;

namespace GuildMaster.Persuasion
{
    /// <summary>
    /// LLM-free persuasion replies: the recruitment keyword scoring (values, praise, rudeness) plus
    /// quest-specific arguments (reward, company, safety), with canned lines per mood.
    /// Always available as the fallback.
    /// </summary>
    public static class RuleBasedPersuader
    {
        public struct Reply
        {
            public string Text;
            public int WillingnessDelta;
            public PersuadeDecision Decision;
        }

        static readonly string[] RewardWords = { "보상", "골드", "경험치", "EXP", "돈", "수당" };
        static readonly string[] CompanyWords = { "같이", "함께", "동료", "지원", "물약", "치료", "든든" };
        static readonly string[] SafetyWords = { "안전", "안심", "무리하지", "위험하면", "후퇴" };

        /// <summary>Willingness change for one guild master message (pure; used by tests).</summary>
        public static int Score(string message, string values, string riskLabel, int maxParty)
        {
            if (string.IsNullOrWhiteSpace(message)) return -2;
            int delta = RuleBasedRecruiter.Score(message, values);
            if (ContainsAny(message, RewardWords)) delta += 3;
            if (maxParty > 1 && ContainsAny(message, CompanyWords)) delta += 3;
            if ((riskLabel == QuestRisk.Risky || riskLabel == QuestRisk.Reckless) && ContainsAny(message, SafetyWords)) delta += 3;
            return delta;
        }

        static bool ContainsAny(string text, string[] words)
        {
            foreach (var w in words) if (text.Contains(w)) return true;
            return false;
        }

        /// <summary>Decides the decision and picks a line. <paramref name="threshold"/> is the willingness needed to accept.</summary>
        public static Reply Respond(string message, string values, string riskLabel, int maxParty, int willingness, int threshold,
            bool proposal, GameRandom rng)
        {
            int delta = Score(message, values, riskLabel, maxParty);
            int after = willingness + delta;
            var decision = PersuadeDecision.Undecided;
            if (proposal) decision = after >= threshold ? PersuadeDecision.Accept : after < threshold - 15 ? PersuadeDecision.Refuse : PersuadeDecision.Undecided;
            else if (after < threshold - 30) decision = PersuadeDecision.Refuse;

            string[] pool = decision switch
            {
                PersuadeDecision.Accept => new[] { "좋아요, 그렇게까지 말씀하시니 이 의뢰는 제가 맡죠.", "알겠어요. 위험하지만 해 볼게요.", "믿어 볼게요. 다녀오겠습니다." },
                PersuadeDecision.Refuse => new[] { "죄송하지만 이건 정말 무리예요.", "제 목숨이 걸린 일이라... 다른 분께 부탁하세요.", "더 설득하셔도 마음은 안 바뀔 거예요." },
                _ when delta >= 6 => new[] { "오, 그 말은 마음에 드네요.", "듣고 보니 해 볼 만할 수도 있겠네요.", "조금 마음이 움직이는데요." },
                _ when delta < 0 => new[] { "그런 말로는 어렵겠어요.", "음... 글쎄요.", "좀 더 그럴듯한 이유가 있으면 좋겠네요." },
                _ => new[] { "위험하다는 건 여전한데요...", "조금 더 들어 볼게요.", "다른 이유는 없나요?" },
            };
            return new Reply { Text = rng.Pick(pool), WillingnessDelta = delta, Decision = decision };
        }
    }
}
