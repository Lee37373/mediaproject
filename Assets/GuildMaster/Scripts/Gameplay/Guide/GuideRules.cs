using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Recruitment;
using GuildMaster.Social;
using GuildMaster.Welfare;

namespace GuildMaster.Guide
{
    /// <summary>
    /// The first things worth doing in a new game (F-19), in the order they are suggested. The guild master does not post
    /// quests (P4-82): the first step is a look at the board (P4-118, user 2026-11-02).
    /// </summary>
    public enum GuideStep { Board, TalkVisitor, Counsel, Build, Relationships, Decision }

    /// <summary>What clicking a hint opens.</summary>
    public enum HintAction { None, Counsel, Visitor, QuestBoard, Decision }

    /// <summary>One "do this now" line of the guide.</summary>
    public struct GuideHint
    {
        public string Text;
        /// <summary>A few words for the hint card (2026-11-13: "도윤 영입 기회"); <see cref="Text"/> goes to its tooltip.</summary>
        public string Title;
        /// <summary>One short line under the title ("호감 63 · 말을 걸어 권유").</summary>
        public string Sub;
        public HintAction Action;
        /// <summary>The member or visitor it is about (Counsel, Visitor).</summary>
        public string TargetId;
        /// <summary>Higher first.</summary>
        public int Urgency;
    }

    /// <summary>The state of the guild the hints are read from (built by the UI each refresh; plain data for tests).</summary>
    public class GuideSnapshot
    {
        public struct Member
        {
            public string Id, Name;
            public float Stress;
            public CareDemand Demand;
        }

        public struct Visitor
        {
            public string Id, Name;
            /// <summary>Recruit affinity (-1 = never talked to).</summary>
            public int Affinity;
        }

        public List<Member> Members = new();
        public List<Visitor> Visitors = new();
        public int OpenQuests;
        public int IdleMembers;
        public int Gold;
        public bool RosterFull;
        /// <summary>Title of a decision card waiting for an answer (null = none).</summary>
        public string PendingCard;
    }

    /// <summary>
    /// "다음 할 일" (F-19, user request 2026-10-11): a short checklist of the first things to try, and hints about what
    /// needs the guild master right now — a member who is worn out, a visitor who is keen to join, an empty quest
    /// board, a decision card waiting. Pure rules, covered by EditMode tests.
    /// </summary>
    public static class GuideRules
    {
        public static readonly GuideStep[] Order =
        {
            GuideStep.Board, GuideStep.TalkVisitor, GuideStep.Counsel, GuideStep.Build, GuideStep.Relationships, GuideStep.Decision,
        };

        public const int MaxHints = 4;
        /// <summary>A visitor this keen is worth a word (same bar as a member's invitation in town).</summary>
        public const int KeenVisitor = TownTalkRules.InviteMinAffinity;
        public const int LowGold = 50;

        public static string Title(GuideStep s) => s switch
        {
            GuideStep.Board => "의뢰 게시판 살펴보기",
            GuideStep.TalkVisitor => "방문자와 영입 대화하기",
            GuideStep.Counsel => "길드원 면담하기",
            GuideStep.Build => "건물 짓기·업그레이드",
            GuideStep.Relationships => "관계도 살펴보기",
            _ => "결정 카드에 답하기",
        };

        public static string HowTo(GuideStep s) => s switch
        {
            GuideStep.Board => "Q 게시판에서 마을이 올린 의뢰를 보세요. 길드원들이 알아서 맡습니다.",
            GuideStep.TalkVisitor => "마을에 온 방문자를 클릭하고 [영입 대화]로 말을 걸어 보세요.",
            GuideStep.Counsel => "R 길드원 → [관리] → [면담]. 지친 길드원을 말로 다독여 주세요.",
            GuideStep.Build => "건물을 클릭해 업그레이드하거나 B 건설에서 새 건물을 지으세요.",
            GuideStep.Relationships => "V 관계도에서 누가 누구와 친하고 사이가 나쁜지 보세요.",
            _ => "아침 9시와 저녁 5시에 정할 일이 생길 수 있습니다. 카드가 뜨면 선택지를 고르세요(J).",
        };

        /// <summary>One short line for the hint card (2026-11-13); <see cref="HowTo"/> is its tooltip.</summary>
        public static string Short(GuideStep s) => s switch
        {
            GuideStep.Board => "Q 게시판에서 마을 의뢰 확인",
            GuideStep.TalkVisitor => "방문자 클릭 → [영입 대화]",
            GuideStep.Counsel => "R 길드원 → [관리] → [면담]",
            GuideStep.Build => "건물 클릭 또는 B 건설",
            GuideStep.Relationships => "V 관계도 열어 보기",
            _ => "카드가 뜨면 선택지 고르기 (J)",
        };

        /// <summary>The first step not done yet (null when all are done).</summary>
        public static GuideStep? Next(Func<GuideStep, bool> done)
        {
            foreach (var s in Order)
                if (!done(s)) return s;
            return null;
        }

        public static string DemandLabel(CareDemand d) => d switch
        {
            CareDemand.Talk => "면담",
            CareDemand.Leave => "휴가",
            CareDemand.Bonus => "보너스",
            _ => "",
        };

        /// <summary>What needs the guild master now, most urgent first, at most <paramref name="max"/>.</summary>
        public static List<GuideHint> Hints(GuideSnapshot s, int max = MaxHints)
        {
            var hints = new List<GuideHint>();
            if (s == null) return hints;
            if (!string.IsNullOrEmpty(s.PendingCard))
                hints.Add(new GuideHint
                {
                    Text = $"결정 카드가 답을 기다립니다: '{s.PendingCard}'", Title = "결정 카드 대기", Sub = $"'{s.PendingCard}' 답하기 (J)",
                    Action = HintAction.Decision, Urgency = 5,
                });
            foreach (var m in s.Members)
            {
                if (m.Demand != CareDemand.None)
                    hints.Add(new GuideHint
                    {
                        Text = $"{Ga(m.Name)} {DemandLabel(m.Demand)}{Eul(DemandLabel(m.Demand))} 요청했습니다.", Action = HintAction.Counsel, TargetId = m.Id, Urgency = 4,
                        Title = $"{m.Name}의 {DemandLabel(m.Demand)} 요청", Sub = "관리 창에서 들어주기",
                    });
                else if (m.Stress >= StressRules.StrainedAt)
                    hints.Add(new GuideHint
                    {
                        Text = $"{Ga(m.Name)} 지쳐 있습니다(스트레스 {Math.Round(m.Stress)}). 면담해 보세요.", Action = HintAction.Counsel, TargetId = m.Id,
                        Title = $"{Ga(m.Name)} 지침", Sub = $"스트레스 {Math.Round(m.Stress)} · 면담하기",
                        Urgency = m.Stress >= StressRules.BreakingAt ? 4 : 3,
                    });
            }
            if (!s.RosterFull)
                foreach (var v in s.Visitors.Where(v => v.Affinity >= KeenVisitor).OrderByDescending(v => v.Affinity))
                    hints.Add(new GuideHint
                    {
                        Text = $"방문자 {Ga(v.Name)} 길드에 관심이 많습니다(호감 {v.Affinity}). 말을 걸어 권유해 보세요.", Action = HintAction.Visitor, TargetId = v.Id,
                        Title = $"{v.Name} 영입 기회", Sub = $"호감 {v.Affinity} · 말을 걸어 권유",
                        Urgency = 3,
                    });
            if (s.OpenQuests == 0 && s.IdleMembers > 0)
                hints.Add(new GuideHint
                {
                    Text = $"게시판이 비어 쉬는 길드원이 {s.IdleMembers}명 있습니다. 제안 카드에서 의뢰를 골라 맡겨 보세요.",
                    Title = $"쉬는 길드원 {s.IdleMembers}명", Sub = "제안 카드에서 의뢰 맡기기", Action = HintAction.QuestBoard, Urgency = 2,
                });
            if (s.Gold < LowGold)
                hints.Add(new GuideHint
                {
                    Text = $"자금이 {s.Gold}G뿐입니다. 창고 물건을 팔거나 보상이 적은 의뢰로 버티세요.", Title = $"자금 {s.Gold}G뿐", Sub = "창고 물건 팔기 · 작은 의뢰로 버티기",
                    Urgency = 1,
                });
            // Stable: equal urgency keeps the order above.
            return hints.Select((h, i) => (h, i)).OrderByDescending(x => x.h.Urgency).ThenBy(x => x.i).Take(Math.Max(0, max)).Select(x => x.h).ToList();
        }

        static string Ga(string w) => w + Adventurers.MemoryRecorder.Josa(w, "이", "가");
        static string Eul(string w) => Adventurers.MemoryRecorder.Josa(w, "을", "를");
    }
}
