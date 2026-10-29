using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Recruitment;
using GuildMaster.Social;
using GuildMaster.Welfare;

namespace GuildMaster.Guide
{
    /// <summary>The first things worth doing in a new game (F-19), in the order they are suggested.</summary>
    public enum GuideStep { PostQuest, TalkVisitor, Counsel, Build, Relationships, EventCard }

    /// <summary>What clicking a hint opens.</summary>
    public enum HintAction { None, Counsel, Visitor, QuestBoard, EventCard }

    /// <summary>One "do this now" line of the guide.</summary>
    public struct GuideHint
    {
        public string Text;
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
        /// <summary>Title of the event card waiting for an answer (null = none).</summary>
        public string PendingCard;
    }

    /// <summary>
    /// "다음 할 일" (F-19, user request 2026-10-11): a short checklist of the first things to try, and hints about what
    /// needs the guild master right now — a member who is worn out, a visitor who is keen to join, an empty quest
    /// board, an event card waiting. Pure rules, covered by EditMode tests.
    /// </summary>
    public static class GuideRules
    {
        public static readonly GuideStep[] Order =
        {
            GuideStep.PostQuest, GuideStep.TalkVisitor, GuideStep.Counsel, GuideStep.Build, GuideStep.Relationships, GuideStep.EventCard,
        };

        public const int MaxHints = 4;
        /// <summary>A visitor this keen is worth a word (same bar as a member's invitation in town).</summary>
        public const int KeenVisitor = TownTalkRules.InviteMinAffinity;
        public const int LowGold = 50;

        public static string Title(GuideStep s) => s switch
        {
            GuideStep.PostQuest => "의뢰 올리기",
            GuideStep.TalkVisitor => "방문자와 영입 대화하기",
            GuideStep.Counsel => "길드원 면담하기",
            GuideStep.Build => "건물 짓기·업그레이드",
            GuideStep.Relationships => "관계도 살펴보기",
            _ => "사건 카드에 답하기",
        };

        public static string HowTo(GuideStep s) => s switch
        {
            GuideStep.PostQuest => "Q 의뢰 게시판에서 의뢰를 올리거나 제안된 의뢰를 고르세요.",
            GuideStep.TalkVisitor => "마을에 온 방문자를 클릭하고 [영입 대화]로 말을 걸어 보세요.",
            GuideStep.Counsel => "R 길드원 → [관리] → [면담]. 지친 길드원을 말로 다독여 주세요.",
            GuideStep.Build => "건물을 클릭해 업그레이드하거나 B 건설에서 새 건물을 지으세요.",
            GuideStep.Relationships => "G 관계도에서 누가 누구와 친하고 사이가 나쁜지 보세요.",
            _ => "하루 0~2번 사건이 생깁니다. 카드가 뜨면 선택지를 고르세요.",
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
                hints.Add(new GuideHint { Text = $"사건 카드가 답을 기다립니다: '{s.PendingCard}'", Action = HintAction.EventCard, Urgency = 5 });
            foreach (var m in s.Members)
            {
                if (m.Demand != CareDemand.None)
                    hints.Add(new GuideHint
                    {
                        Text = $"{Ga(m.Name)} {DemandLabel(m.Demand)}{Eul(DemandLabel(m.Demand))} 요청했습니다.", Action = HintAction.Counsel, TargetId = m.Id, Urgency = 4,
                    });
                else if (m.Stress >= StressRules.StrainedAt)
                    hints.Add(new GuideHint
                    {
                        Text = $"{Ga(m.Name)} 지쳐 있습니다(스트레스 {Math.Round(m.Stress)}). 면담해 보세요.", Action = HintAction.Counsel, TargetId = m.Id,
                        Urgency = m.Stress >= StressRules.BreakingAt ? 4 : 3,
                    });
            }
            if (!s.RosterFull)
                foreach (var v in s.Visitors.Where(v => v.Affinity >= KeenVisitor).OrderByDescending(v => v.Affinity))
                    hints.Add(new GuideHint
                    {
                        Text = $"방문자 {Ga(v.Name)} 길드에 관심이 많습니다(호감 {v.Affinity}). 말을 걸어 권유해 보세요.", Action = HintAction.Visitor, TargetId = v.Id,
                        Urgency = 3,
                    });
            if (s.OpenQuests == 0 && s.IdleMembers > 0)
                hints.Add(new GuideHint { Text = $"게시판이 비어 쉬는 길드원이 {s.IdleMembers}명 있습니다. 의뢰를 올리세요.", Action = HintAction.QuestBoard, Urgency = 2 });
            if (s.Gold < LowGold)
                hints.Add(new GuideHint { Text = $"자금이 {s.Gold}G뿐입니다. 창고 물건을 팔거나 보상이 적은 의뢰로 버티세요.", Urgency = 1 });
            // Stable: equal urgency keeps the order above.
            return hints.Select((h, i) => (h, i)).OrderByDescending(x => x.h.Urgency).ThenBy(x => x.i).Take(Math.Max(0, max)).Select(x => x.h).ToList();
        }

        static string Ga(string w) => w + Adventurers.MemoryRecorder.Josa(w, "이", "가");
        static string Eul(string w) => Adventurers.MemoryRecorder.Josa(w, "을", "를");
    }
}
