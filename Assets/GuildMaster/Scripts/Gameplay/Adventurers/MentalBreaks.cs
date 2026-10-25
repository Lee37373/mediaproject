using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>A member broke (P4-104d), or acted on it (a spree's gold), or came out of it (a fistfight over, a berserker come to).</summary>
    public struct MentalBreakEvent
    {
        public enum Phase { Began, Acted, Over }

        public Adventurer Adventurer;
        /// <summary>The other one in a fistfight.</summary>
        public Adventurer Other;
        public BreakKind Kind;
        public BreakSeverity Severity;
        public Phase Stage;
        /// <summary>A spree's cost.</summary>
        public int Gold;
    }

    /// <summary>A member was inspired (P4-104d), or its inspiration ran out.</summary>
    public struct InspirationEvent
    {
        public Adventurer Adventurer;
        public InspirationKind Kind;
        public bool Ended;
    }

    /// <summary>
    /// Breaks and inspirations (P4-104d, G2 — RimWorld's mental breaks, Darkest Dungeon's afflictions): every game hour a member low
    /// enough may break (<see cref="BreakRules"/>), one high enough may be inspired. Breaks are acted out by <see cref="BreakState"/>
    /// and <see cref="TagAlongState"/>, or end in the member leaving; afterwards the member is lighter for it and does not break
    /// again for a day. Kept apart from F-2's stress, which has its own walking off and quitting (user decision 2026-10-25).
    /// </summary>
    public class MentalBreaks
    {
        readonly GameClock _clock;
        readonly GuildRoster _roster;
        readonly Func<float> _roll;

        /// <summary>Debug and tests: no breaks or inspirations come by themselves.</summary>
        public static bool Enabled = true;

        public MentalBreaks(GameClock clock, GuildRoster roster, Func<float> roll = null)
        {
            _clock = clock;
            _roster = roster;
            _roll = roll ?? (() => GameRandom.Shared.Value);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        void OnHour(HourPassedEvent e)
        {
            float now = _clock.TotalHours;
            foreach (var a in _roster.Members.ToList())
            {
                if (a == null || !a.IsAlive || !a.IsGuildMember || a.IsVisitor) continue;
                var d = a.Data;
                if (d.inspiration != InspirationKind.None && now >= d.inspirationUntil) EndInspiration(a);
                if (!Enabled || InABreak(a)) continue;
                float mood = ThoughtRecorder.MoodOf(a);
                if (now >= d.breakReadyAt)
                {
                    var severity = BreakRules.SeverityAt(mood);
                    if (severity != BreakSeverity.None && _roll() < BreakRules.ChanceOf(severity) && TryBreak(a, severity)) continue;
                }
                if (d.inspiration == InspirationKind.None && now >= d.inspirationReadyAt && mood >= BreakRules.InspiredAbove
                    && _roll() < BreakRules.InspirationChance)
                    Inspire(a, BreakRules.ChooseInspiration(!AdvCtx.Town.IsInTown(a.Position), d.traitIds, _roll()));
            }
        }

        /// <summary>Acting a break out, or trailing its party after walking off.</summary>
        public static bool InABreak(Adventurer a) => a.Fsm.Current == a.BreakState || a.Fsm.Current == a.TagAlongState;

        // ---------------- Breaking ----------------

        /// <summary>Breaks this member as badly as given, in the way its situation allows; false when nothing fits (debug too).</summary>
        public bool TryBreak(Adventurer a, BreakSeverity severity, BreakKind forced = BreakKind.None)
        {
            var context = ContextOf(a, out var rival);
            var kind = forced != BreakKind.None ? forced : BreakRules.Choose(severity, context, _roll());
            if (kind == BreakKind.None) return false;
            if (kind == BreakKind.Brawl && rival == null) return false;
            a.Data.breakReadyAt = _clock.TotalHours + BreakRules.CooldownHours;
            a.Say(BreakRules.Line(kind));
            EventBus.Publish(new MentalBreakEvent { Adventurer = a, Other = kind == BreakKind.Brawl ? rival : null, Kind = kind, Severity = severity, Stage = MentalBreakEvent.Phase.Began });
            // Letting it out lightens it (RimWorld's catharsis): no break again soon, whatever comes.
            ThoughtRecorder.Give(a, ThoughtRules.LetItOut);
            switch (kind)
            {
                case BreakKind.Sabotage: WalkOff(a, followParty: true); break;
                case BreakKind.Desert: WalkOff(a, followParty: false); break;
                case BreakKind.Quit: Quit(a); break;
                case BreakKind.Brawl:
                    a.BreakState.Begin(BreakKind.Brawl, rival);
                    rival.BreakState.Begin(BreakKind.Brawl, a);
                    a.ChangeState(a.BreakState);
                    rival.ChangeState(rival.BreakState);
                    rival.Say("뭐야, 해보자는 거야?");
                    break;
                default:
                    a.BreakState.Begin(kind);
                    a.ChangeState(a.BreakState);
                    break;
            }
            return true;
        }

        BreakContext ContextOf(Adventurer a, out Adventurer rival)
        {
            var d = a.Data;
            var q = a.CurrentQuest;
            var s = a.Fsm.Current;
            bool out_ = !AdvCtx.Town.IsInTown(a.Position);
            bool working = q != null && q.IsActive && q.IsAssigned(a) || s == a.FreeHuntState || s == a.TravelState || s == a.HuntState;
            rival = null;
            if (!out_)
            {
                int worst = -1;
                foreach (var m in _roster.Members)
                {
                    if (m == a || m == null || !m.IsAlive || m.IsVisitor || !m.IsGuildMember || !m.IsAtLeisureInTown) continue;
                    int v = Relationships.Get(a, m);
                    if (v < worst) { worst = v; rival = m; }
                }
            }
            return new BreakContext
            {
                OnQuest = working && out_,
                WithParty = Comrades(a).Count > 0,
                InTownFree = !out_ && a.IsAtLeisureInTown && q == null,
                Gold = d.gold,
                RivalInTown = rival != null,
                AnyoneNear = UnitRegistry.FindNearest(a.Position, BreakRules.BerserkReach, a,
                    (u, me) => u != me && u.IsAlive && (u is Monster || u is Adventurer { IsGuildMember: true, IsVisitor: false })) != null,
                Values = d.values,
                TraitIds = d.traitIds,
            };
        }

        static readonly List<Adventurer> PartyBuffer = new();

        /// <summary>The others out with it now (its quest party or hunting band).</summary>
        static List<Adventurer> Comrades(Adventurer a)
        {
            var list = new List<Adventurer>();
            if (!Services.TryGet<PartyTactics>(out var tactics)) return list;
            foreach (var m in tactics.Party(a, PartyBuffer)) if (m != a) list.Add(m);
            return list;
        }

        /// <summary>
        /// Gives up its work (P4-104d minor: behind its party; major: deserting it). The quest goes back on the board for the rest
        /// — its share with it (user decision 2026-10-21: no pay for walking off).
        /// </summary>
        public static void WalkOff(Adventurer a, bool followParty)
        {
            var comrades = Comrades(a);
            var q = a.CurrentQuest;
            if (q != null && Services.TryGet<QuestBoard>(out var board) && q.IsAssigned(a)) board.Abandon(q, a);
            a.CurrentQuest = null;
            if (followParty && comrades.Count > 0)
            {
                a.TagAlongState.Follow(comrades);
                a.ChangeState(a.TagAlongState);
            }
            else a.ChangeState(a.ReturnState);
        }

        /// <summary>Quits on the spot: the guild hears of it and its friends take it hard (as F-2's quitting).</summary>
        void Quit(Adventurer a)
        {
            if (Services.TryGet<Welfare.GuildWelfare>(out var welfare)) welfare.Release(a, $"{a.Data.name}이(가) 무너져 길드를 떠났습니다.");
            else _roster.Dismiss(a);
        }

        // ---------------- Inspiration ----------------

        public void Inspire(Adventurer a, InspirationKind kind)
        {
            if (kind == InspirationKind.None) return;
            var d = a.Data;
            d.inspiration = kind;
            d.inspirationUntil = _clock.TotalHours + BreakRules.InspirationHours;
            d.inspirationReadyAt = d.inspirationUntil + BreakRules.InspirationCooldownHours;
            if (kind == InspirationKind.Combat) a.RecalculateStats();
            a.Say(kind switch
            {
                InspirationKind.Combat => "오늘은 누구한테도 안 질 것 같아!",
                InspirationKind.Social => "오늘은 누구랑 얘기해도 즐거울 것 같아!",
                _ => "뭔가 감이 왔어. 지금이라면 뭐든 배울 수 있겠어!",
            });
            ThoughtRecorder.Give(a, ThoughtRules.Inspired);
            EventBus.Publish(new InspirationEvent { Adventurer = a, Kind = kind });
        }

        void EndInspiration(Adventurer a)
        {
            var kind = a.Data.inspiration;
            a.Data.inspiration = InspirationKind.None;
            a.Data.inspirationUntil = 0f;
            if (kind == InspirationKind.Combat) a.RecalculateStats();
            EventBus.Publish(new InspirationEvent { Adventurer = a, Kind = kind, Ended = true });
        }
    }
}
