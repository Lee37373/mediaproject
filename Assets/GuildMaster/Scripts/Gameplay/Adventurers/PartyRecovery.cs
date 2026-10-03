using System.Collections.Generic;
using GuildMaster.AI;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// A member of a party out together has to pull out, badly hurt or worn out (P4-71, user
    /// decision 2026-10-03: "the party stops to mend" — members used to walk home alone while the
    /// rest hunted on). Nobody leaves alone:
    /// - no enemy about: the party makes camp right there (<see cref="CampService.PitchToMend"/>)
    ///   and goes back to its quest or hunt once mended; close to town it simply walks home together;
    /// - enemies about: the whole party pulls back together (<see cref="ReturnToTownState"/>, in
    ///   formation) and, once the fight is behind it and town is still far, camps and goes back
    ///   to what it was doing (<see cref="ReturnToTownState.ResumeAfter"/>).
    /// A hunting band is <see cref="HuntBand.Paused"/> meanwhile, so it is not broken up.
    /// </summary>
    public static class PartyRecovery
    {
        /// <summary>Closer to town than this, a party that has to mend walks home instead of camping.</summary>
        public const float CampBeyond = CampRules.FarFromTown * 0.5f;

        static readonly List<Adventurer> s_group = new();

        /// <summary>
        /// The party takes over <paramref name="hurt"/>'s pull-out. False when it is on its own
        /// (the caller then retreats alone as before).
        /// </summary>
        public static bool Handle(Adventurer hurt)
        {
            Services.TryGet<CampService>(out var camps);
            var camp = camps != null ? camps.CampOf(hurt) : null;
            var group = GroupOf(hurt, camp);
            if (group.Count < 2) return false;

            bool quiet = camp == null && camps != null && AllQuiet(group);
            if (quiet && Vector2.Distance(hurt.Position, AdvCtx.GuildDoor) >= CampBeyond && camps.PitchToMend(group)) return true;
            // Close to town with nothing about, the party just hurries home together (no alarm).
            PullBack(group, camps, calm: quiet);
            if (quiet) hurt.Say("다들 지쳤어. 마을로 돌아가자.");
            return true;
        }

        /// <summary>Who goes with <paramref name="hurt"/>, leader first: its camp, or its party out together.</summary>
        static List<Adventurer> GroupOf(Adventurer hurt, Camp camp)
        {
            s_group.Clear();
            if (camp != null)
            {
                foreach (var m in camp.Members) if (m.IsAlive && m.Fsm.Current == m.CampState) s_group.Add(m);
                return s_group;
            }
            if (Services.TryGet<PartyTactics>(out var tactics)) tactics.Party(hurt, s_group);
            return s_group;
        }

        /// <summary>Nobody fighting, hit lately, or with an enemy in sight.</summary>
        static bool AllQuiet(List<Adventurer> group)
        {
            foreach (var m in group)
            {
                if (m.Engage.Target != null || Time.time - m.LastAttackedTime < CampRules.QuietSeconds) return false;
                if (CampService.ThreatInSight(m) != null) return false;
            }
            return true;
        }

        /// <summary>
        /// What a member goes back to after mending: its quest, its hunt (the band waits), what its
        /// camp or retreat already meant to go back to. Null: home.
        /// </summary>
        internal static State<Adventurer> ResumeOf(Adventurer m, CampService camps)
        {
            var s = m.Fsm.Current;
            if ((s == m.TravelState || s == m.HuntState) && m.CurrentQuest != null && m.CurrentQuest.IsActive) return m.TravelState;
            if (s == m.FreeHuntState && Services.TryGet<HuntBandService>(out var bands) && bands.BandOf(m) is { Mustering: false }) return m.FreeHuntState;
            if (s == m.CampState && camps != null && camps.CampOf(m) is { } camp && camp.ResumeTo.TryGetValue(m, out var next)) return next;
            if (s == m.ReturnState) return m.ReturnState.ResumeAfter;
            return null;
        }

        /// <summary>Holds the hunting bands of these members together while they are away mending.</summary>
        internal static void PauseBands(List<Adventurer> group)
        {
            if (!Services.TryGet<HuntBandService>(out var bands)) return;
            foreach (var m in group)
                if (m.Fsm.Current == m.FreeHuntState && bands.BandOf(m) is { Mustering: false } band) band.Paused = true;
        }

        static void PullBack(List<Adventurer> group, CampService camps, bool calm)
        {
            var members = new List<Adventurer>(group);
            var resume = new Dictionary<Adventurer, State<Adventurer>>();
            foreach (var m in members) resume[m] = ResumeOf(m, camps);
            PauseBands(members);
            foreach (var m in members)
            {
                m.Engage.Clear();
                m.Looting.Cancel();
                m.ReturnState.RequestRetreat(resume[m], calm);
                m.ChangeState(m.ReturnState);
            }
        }
    }
}
