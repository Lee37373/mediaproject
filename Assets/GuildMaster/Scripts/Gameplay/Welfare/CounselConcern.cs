using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GuildMaster.Welfare
{
    /// <summary>What weighs on a member most right now (what a counselling talk should get to).</summary>
    public enum CounselConcernKind { Grief, Neglect, Fear, Failure, Conflict, Overwork, Money, Vague }

    /// <summary>The state of a member as the concern check sees it (read from the game, or made up in tests).</summary>
    public struct CounselInput
    {
        public float Stress;
        public float Fatigue;
        public float HpRatio;
        public bool Wounded;
        public int Gold;
        public int Debt;
        /// <summary>Ignored demands not yet forgotten (<see cref="MemberCare.Grudges"/>).</summary>
        public int Grudges;
        public string Values;
        /// <summary>Texts of what happened to them lately (newest last).</summary>
        public IReadOnlyList<string> RecentMemories;
        /// <summary>The member they get on worst with, and how badly (<see cref="Adventurers.Relationships"/>).</summary>
        public string RivalName;
        public int RivalFeeling;
    }

    /// <summary>One member's worry: what it is, how the model hears about it, and the words that show a line gets to it.</summary>
    public class CounselConcern
    {
        public CounselConcernKind Kind;
        /// <summary>Short, for the window once it is out ("동료를 잃은 슬픔").</summary>
        public string Label;
        /// <summary>The worry in the member's own terms, for the prompt.</summary>
        public string Detail;
        /// <summary>Who it is about (the dead friend, the rival), or null.</summary>
        public string Subject;
        public string[] Keywords;

        /// <summary>Does this line of the guild master's go to the worry?</summary>
        public bool Touches(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            if (!string.IsNullOrEmpty(Subject) && message.Contains(Subject)) return true;
            if (Keywords != null) foreach (var k in Keywords) if (message.Contains(k)) return true;
            return false;
        }
    }

    /// <summary>
    /// Finds what is behind a member's stress (user request 2026-10-01: counselling by conversation). A talk that
    /// gets to the real worry helps far more (<see cref="CounselScoring.TouchedBonus"/>), so the player is
    /// rewarded for asking and listening, and each member's talk is about something of their own. Pure logic.
    /// </summary>
    public static class CounselConcerns
    {
        public const int RivalFeeling = -15;
        public const float OverworkFatigue = 60f;
        public const int PoorGold = 40;

        static readonly Regex DeadFriend = new(@"동료 (\S+?)(?:이|가) .*목숨을 잃었다");

        static bool Any(IReadOnlyList<string> memories, string word)
        {
            if (memories == null) return false;
            foreach (var m in memories) if (m != null && m.Contains(word)) return true;
            return false;
        }

        public static CounselConcern Detect(in CounselInput s)
        {
            var mem = s.RecentMemories;
            // Most recent loss first: that is what they will be thinking of.
            if (mem != null)
                for (int i = mem.Count - 1; i >= 0; i--)
                {
                    var match = mem[i] != null ? DeadFriend.Match(mem[i]) : Match.Empty;
                    if (!match.Success) continue;
                    string who = match.Groups[1].Value;
                    return Make(CounselConcernKind.Grief, "동료를 잃은 슬픔",
                        $"얼마 전 동료 {who}{Adventurers.MemoryRecorder.Josa(who, "을", "를")} 잃었다. 자꾸 그 생각이 나고, 나도 언젠가 저렇게 될까 봐 마음이 무겁다.", who,
                        "죽", "잃", "떠나", "보내", "명복", "슬프", "슬픔", "그리", "빈자리");
                }
            if (s.Grudges > 0 || Any(mem, "들어주지 않았다"))
                return Make(CounselConcernKind.Neglect, "무관심에 대한 서운함",
                    "힘들다고 부탁했는데 길드 마스터가 들어주지 않았다. 나를 신경 쓰지 않는 것 같아 서운하다.", null,
                    "미안", "못 들어", "무시", "신경", "챙기지", "소홀", "몰랐", "늦었", "약속");
            if (s.Wounded || s.HpRatio < 0.5f || Any(mem, "죽을 뻔") || Any(mem, "크게 다쳐서"))
                return Make(CounselConcernKind.Fear, "죽을 뻔한 공포",
                    "최근 크게 다쳐 죽을 뻔했다. 다시 나가는 게 무섭고, 몸도 아직 성치 않다.", null,
                    "무섭", "무서웠", "두렵", "위험", "다쳐", "다친", "안전", "치료", "무리하지", "몸");
            if (Any(mem, "포기했다"))
                return Make(CounselConcernKind.Failure, "실패에 대한 자책",
                    "얼마 전 의뢰를 끝내지 못하고 포기했다. 내 실력이 모자란 것 같아 자꾸 자책하게 된다.", null,
                    "실패", "포기", "다음엔", "다음에", "실수", "네 탓", "자책", "누구나");
            if (!string.IsNullOrEmpty(s.RivalName) && s.RivalFeeling <= RivalFeeling)
                return Make(CounselConcernKind.Conflict, "동료와의 갈등",
                    $"길드원 {s.RivalName}{Adventurers.MemoryRecorder.Josa(s.RivalName, "과", "와")} 사이가 나쁘다. 같이 지내는 게 불편하고 신경이 쓰인다.", s.RivalName,
                    "사이", "다툼", "다퉜", "싸웠", "싸움", "화해", "갈등", "불편");
            if (s.Fatigue >= OverworkFatigue)
                return Make(CounselConcernKind.Overwork, "쉬지 못한 피로",
                    "쉴 틈 없이 일해서 몸도 마음도 지쳤다. 좀 쉬고 싶은데 말을 못 꺼내고 있다.", null,
                    "쉬", "휴가", "피곤", "지쳤", "지친", "무리", "일을 줄", "잠");
            if (s.Debt > 0 || s.Gold < PoorGold || s.Values == "돈")
                return Make(CounselConcernKind.Money, "돈 걱정",
                    s.Debt > 0 ? "밥값과 숙박비가 밀려 빚이 생겼다. 일해도 남는 게 없어 막막하다."
                               : "일한 만큼 벌지 못하는 것 같다. 주머니 사정이 빠듯해 불안하다.", null,
                    "돈", "보수", "보너스", "빚", "골드", "급여", "보상", "벌이");
            return Make(CounselConcernKind.Vague, "막연한 답답함",
                "딱히 무슨 일이 있는 건 아닌데, 요즘 이유 없이 마음이 무겁고 답답하다.", null,
                "기분", "마음", "답답", "우울", "무거");
        }

        static CounselConcern Make(CounselConcernKind kind, string label, string detail, string subject, params string[] keywords)
            => new() { Kind = kind, Label = label, Detail = detail, Subject = subject, Keywords = keywords };
    }
}
