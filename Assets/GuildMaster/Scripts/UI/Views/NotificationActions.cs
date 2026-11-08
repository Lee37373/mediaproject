using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Notifications;
using GuildMaster.Quests;
using GuildMaster.Recruitment;
using UnityEngine;

namespace GuildMaster.UI
{
    /// <summary>What clicking a notification does (F-1): open the window it is about, or jump to the place.</summary>
    public static class NotificationActions
    {
        public static Color KindColor(NotificationKind kind) => kind switch
        {
            NotificationKind.Danger => UITheme.Bad,
            NotificationKind.Warning => UITheme.Warn,
            NotificationKind.Quest => UITheme.Accent,
            NotificationKind.Success => UITheme.Good,
            _ => UITheme.Exp,
        };

        /// <summary>Marks the notification read and follows its link.</summary>
        public static void Go(Notification n)
        {
            if (n == null) return;
            if (Services.TryGet<NotificationCenter>(out var center)) center.MarkRead(n.Id);
            Follow(n.Link);
        }

        public static void Follow(NotificationLink link)
        {
            switch (link.Target)
            {
                case NotificationTarget.QuestBoard:
                    Show<QuestBoardView>();
                    break;
                case NotificationTarget.Roster:
                    Show<RosterView>();
                    break;
                case NotificationTarget.Negotiation:
                {
                    var quest = Services.TryGet<QuestBoard>(out var board) ? board.FindById(link.Id) : null;
                    // Taken or cancelled in the meantime: the board shows what became of it.
                    if (quest != null && quest.IsActive && Services.TryGet<PersuadeDialogView>(out var dialog)) dialog.Begin(quest);
                    else Show<QuestBoardView>();
                    break;
                }
                case NotificationTarget.Adventurer:
                {
                    var who = FindAdventurer(link.Id);
                    if (who != null) FocusOn(who);
                    else Show<RosterView>();
                    break;
                }
                case NotificationTarget.Location:
                    if (Services.TryGet<CameraController>(out var cam)) cam.JumpTo(link.Position);
                    break;
                case NotificationTarget.Management:
                {
                    var who = FindAdventurer(link.Id);
                    if (who != null && Services.TryGet<MemberCareView>(out var care)) care.Show(who);
                    else Show<RosterView>();
                    break;
                }
                case NotificationTarget.Proposals:
                    Show<QuestProposalView>();
                    break;
                case NotificationTarget.GuildRank:
                    Show<GuildRankView>();
                    break;
                case NotificationTarget.Bestiary:
                    Show<BestiaryView>();
                    break;
                case NotificationTarget.Dilemmas:
                    Show<DilemmaView>();
                    break;
                case NotificationTarget.Memorial:
                {
                    if (!Services.TryGet<MemorialView>(out var wall)) break;
                    int.TryParse(link.Id, out int recordId);
                    wall.ShowRecord(recordId);
                    break;
                }
                case NotificationTarget.Report:
                {
                    if (!Services.TryGet<DailyReportView>(out var view)) break;
                    if (int.TryParse(link.Id, out int day)) view.ShowDay(day);
                    if (!view.Visible) view.Open();
                    else view.BringToFront();
                    break;
                }
                case NotificationTarget.News:
                {
                    if (!Services.TryGet<NewspaperView>(out var paper)) break;
                    if (int.TryParse(link.Id, out int day)) paper.ShowDay(day);
                    if (!paper.Visible) paper.Open();
                    else paper.BringToFront();
                    break;
                }
                case NotificationTarget.Resident:
                {
                    // The resident and its request window (P4-134).
                    if (!Services.TryGet<Social.ResidentService>(out var residents) || residents.Find(link.Id) is not { } who) break;
                    if (Services.TryGet<CameraController>(out var camera)) camera.JumpTo(who.Position);
                    if (Services.TryGet<InspectorView>(out var inspector)) inspector.Show(who);
                    if (Services.TryGet<ResidentRequestView>(out var request)) request.Show(who);
                    break;
                }
            }
        }

        /// <summary>Camera and inspector to this member or visitor (the newspaper's stories, P4-130). False when nobody by that id is around.</summary>
        public static bool GoToPerson(string id)
        {
            var who = FindAdventurer(id);
            if (who == null || !who.IsAlive) return false;
            FocusOn(who);
            return true;
        }

        static void Show<T>() where T : UIPanel
        {
            if (!Services.TryGet<T>(out var panel)) return;
            if (!panel.Visible) panel.Open();
            else panel.BringToFront();
        }

        static Adventurer FindAdventurer(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (Services.TryGet<GuildRoster>(out var roster))
            {
                var member = roster.Find(id);
                if (member != null) return member;
            }
            return Services.TryGet<VisitorService>(out var visitors)
                ? visitors.Visitors.FirstOrDefault(v => v != null && v.Data.id == id)
                : null;
        }

        /// <summary>Same as clicking a row in the roster: camera to the member, inspector on them.</summary>
        static void FocusOn(Adventurer a)
        {
            if (Services.TryGet<CameraController>(out var cam)) cam.JumpTo(a.Position);
            if (!Services.TryGet<InspectorView>(out var inspector)) return;
            inspector.Show(a);
            inspector.FocusSelected();
        }
    }
}
