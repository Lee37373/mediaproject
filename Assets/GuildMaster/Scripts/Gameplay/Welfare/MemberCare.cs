using System;

namespace GuildMaster.Welfare
{
    /// <summary>Why a member is leaving the guild on their own.</summary>
    public enum QuitCause { Neglect, Burnout }

    /// <summary>What one game hour of watching a member turned up (see <see cref="MemberCare.Tick"/>).</summary>
    [Flags]
    public enum CareStep
    {
        None = 0,
        LeaveEnded = 1,
        DemandRaised = 2,
        /// <summary>Calmed down by themselves before the guild master answered.</summary>
        DemandWithdrawn = 4,
        DemandIgnored = 8,
        /// <summary>Gave notice: leaves after <see cref="CareRules.ResignNoticeHours"/> unless calmed down.</summary>
        ResignNotice = 16,
        LimitWarning = 32,
        /// <summary>Walks away from the quest they are on.</summary>
        Sabotage = 64,
        Quit = 128,
    }

    /// <summary>The state of a member as one hour of care logic sees it.</summary>
    public struct CareInput
    {
        /// <summary>Game time in hours.</summary>
        public float Now;
        public float Stress;
        public float Fatigue;
        public string Values;
        /// <summary>On a quest that is still running.</summary>
        public bool OnQuest;
        /// <summary>Free and in town (waiting, deciding, resting, at the guild): the only time they can quit.</summary>
        public bool InTownFree;
    }

    public readonly struct CareCheck
    {
        public bool Ok { get; }
        public string Reason { get; }

        CareCheck(bool ok, string reason)
        {
            Ok = ok;
            Reason = reason;
        }

        public static CareCheck Yes => new(true, null);
        public static CareCheck No(string reason) => new(false, reason);
    }

    /// <summary>
    /// The care record of one guild member (F-2): the demand they have made, cooldowns of the guild master's tools,
    /// leave, grudges and resignation. Pure logic driven by game hours, so it is covered by EditMode tests;
    /// <see cref="GuildWelfare"/> applies what it reports to the real adventurer.
    /// </summary>
    public class MemberCare
    {
        public CareDemand Demand { get; private set; }
        public float DemandRaisedAt { get; private set; }
        public float DemandCooldownUntil { get; private set; }
        public float InterviewReadyAt { get; private set; }
        public float LeaveReadyAt { get; private set; }
        public float BonusReadyAt { get; private set; }
        public float LeaveUntil { get; private set; }
        /// <summary>Demands the guild master ignored (fading over time, answered demands forgive one).</summary>
        public int Grudges { get; private set; }
        public float LastGrudgeAt { get; private set; }
        /// <summary>Game hours in a row at the breaking point.</summary>
        public float BreakingHours { get; private set; }
        public bool Warned { get; private set; }
        /// <summary>Game time at which the member leaves (0 = not leaving).</summary>
        public float ResignAt { get; private set; }
        public QuitCause Cause { get; private set; }

        public bool Resigning => ResignAt > 0f;

        // ---------------- Save (P5-4) ----------------

        [Serializable]
        public class CareSave
        {
            public string member;
            public CareDemand demand;
            public float demand_raised, demand_cooldown, interview_ready, leave_ready, bonus_ready, leave_until;
            public int grudges;
            public float last_grudge, breaking_hours;
            public bool warned;
            public float resign_at;
            public QuitCause cause;
        }

        internal CareSave Capture(string member) => new()
        {
            member = member, demand = Demand, demand_raised = DemandRaisedAt, demand_cooldown = DemandCooldownUntil,
            interview_ready = InterviewReadyAt, leave_ready = LeaveReadyAt, bonus_ready = BonusReadyAt, leave_until = LeaveUntil,
            grudges = Grudges, last_grudge = LastGrudgeAt, breaking_hours = BreakingHours, warned = Warned, resign_at = ResignAt, cause = Cause,
        };

        internal static MemberCare Restore(CareSave s) => new()
        {
            Demand = s.demand, DemandRaisedAt = s.demand_raised, DemandCooldownUntil = s.demand_cooldown, InterviewReadyAt = s.interview_ready,
            LeaveReadyAt = s.leave_ready, BonusReadyAt = s.bonus_ready, LeaveUntil = s.leave_until, Grudges = s.grudges,
            LastGrudgeAt = s.last_grudge, BreakingHours = s.breaking_hours, Warned = s.warned, ResignAt = s.resign_at, Cause = s.cause,
        };

        public bool OnLeave(float now) => LeaveUntil > 0f && now < LeaveUntil;

        public float LeaveHoursLeft(float now) => OnLeave(now) ? LeaveUntil - now : 0f;

        public float DemandHoursLeft(float now)
            => Demand == CareDemand.None ? 0f : Math.Max(0f, CareRules.DemandHours - (now - DemandRaisedAt));

        public float ResignHoursLeft(float now) => Resigning ? Math.Max(0f, ResignAt - now) : 0f;

        // ---------------- The guild master's tools ----------------

        public CareCheck CanInterview(float now)
            => now >= InterviewReadyAt ? CareCheck.Yes : CareCheck.No($"방금 면담했습니다. {Hours(InterviewReadyAt - now)} 뒤에 다시 할 수 있습니다.");

        /// <param name="freeInTown">Waiting, deciding, resting or at the guild: not out on a quest or fighting.</param>
        public CareCheck CanGrantLeave(float now, bool freeInTown)
        {
            if (OnLeave(now)) return CareCheck.No("이미 휴가 중입니다.");
            if (!freeInTown) return CareCheck.No("의뢰나 전투 중에는 휴가를 줄 수 없습니다.");
            if (now < LeaveReadyAt) return CareCheck.No($"최근에 휴가를 다녀왔습니다. {Hours(LeaveReadyAt - now)} 뒤에 다시 줄 수 있습니다.");
            return CareCheck.Yes;
        }

        public CareCheck CanGiveBonus(float now)
            => now >= BonusReadyAt ? CareCheck.Yes : CareCheck.No($"방금 보너스를 받았습니다. {Hours(BonusReadyAt - now)} 뒤에 다시 줄 수 있습니다.");

        /// <summary>Notes an interview. Returns true when it was what the member had asked for.</summary>
        public bool RecordInterview(float now)
        {
            InterviewReadyAt = now + CareRules.InterviewCooldownHours;
            return Answer(CareDemand.Talk);
        }

        public bool RecordLeave(float now)
        {
            LeaveUntil = now + CareRules.LeaveHours;
            LeaveReadyAt = LeaveUntil + CareRules.LeaveCooldownHours;
            return Answer(CareDemand.Leave);
        }

        public bool RecordBonus(float now)
        {
            BonusReadyAt = now + CareRules.BonusCooldownHours;
            return Answer(CareDemand.Bonus);
        }

        bool Answer(CareDemand kind)
        {
            if (Demand != kind) return false;
            Demand = CareDemand.None;
            Grudges = Math.Max(0, Grudges - 1);
            return true;
        }

        /// <summary>Called after the guild master's help: someone who ended up below "지침" no longer wants to leave.</summary>
        public bool Reassure(float stressNow)
        {
            if (!Resigning || stressNow >= StressRules.StrainedAt) return false;
            ResignAt = 0f;
            return true;
        }

        /// <summary>The member walked away from a quest (or was talked out of it): the count of bad hours starts over.</summary>
        public void ResetBreaking()
        {
            BreakingHours = 0f;
            Warned = false;
        }

        // ---------------- One game hour ----------------

        /// <summary>
        /// Advances the record by one game hour and reports what happened. <paramref name="roll"/> gives numbers
        /// in [0, 1) and is only called when a chance decides something.
        /// </summary>
        public CareStep Tick(in CareInput s, Func<float> roll)
        {
            var steps = CareStep.None;
            float now = s.Now;

            if (LeaveUntil > 0f && now >= LeaveUntil)
            {
                LeaveUntil = 0f;
                steps |= CareStep.LeaveEnded;
            }

            if (Grudges > 0 && now - LastGrudgeAt >= CareRules.GrudgeFadeHours)
            {
                Grudges--;
                LastGrudgeAt = now;
            }

            if (s.Stress >= StressRules.BreakingAt) BreakingHours += 1f;
            else ResetBreaking();

            if (Demand != CareDemand.None)
            {
                if (s.Stress < StressRules.TenseAt)
                {
                    Demand = CareDemand.None;
                    steps |= CareStep.DemandWithdrawn;
                }
                else if (now - DemandRaisedAt >= CareRules.DemandHours)
                {
                    Demand = CareDemand.None;
                    Grudges++;
                    LastGrudgeAt = now;
                    DemandCooldownUntil = now + CareRules.DemandCooldownHours;
                    steps |= CareStep.DemandIgnored;
                    if (Grudges >= CareRules.QuitGrudges && !Resigning)
                    {
                        ResignAt = now + CareRules.ResignNoticeHours;
                        Cause = QuitCause.Neglect;
                        steps |= CareStep.ResignNotice;
                    }
                }
            }
            else if (s.Stress >= StressRules.StrainedAt && now >= DemandCooldownUntil && roll() < CareRules.DemandChancePerHour)
            {
                Demand = CareRules.ChooseDemand(s.Values, s.Fatigue);
                DemandRaisedAt = now;
                steps |= CareStep.DemandRaised;
            }

            if (BreakingHours >= CareRules.WarnAfterBreakingHours && !Warned)
            {
                Warned = true;
                steps |= CareStep.LimitWarning;
            }

            if (!Resigning && BreakingHours >= CareRules.QuitAfterBreakingHours && roll() < CareRules.QuitChancePerHour)
            {
                ResignAt = now;
                Cause = QuitCause.Burnout;
            }
            // Calmed down (by themselves or with help) before they left.
            if (Resigning && s.Stress < StressRules.TenseAt) ResignAt = 0f;

            if (Resigning && now >= ResignAt && s.InTownFree)
            {
                ResignAt = 0f;
                steps |= CareStep.Quit;
            }
            else if (s.OnQuest && s.Stress >= StressRules.BreakingAt && roll() < CareRules.SabotageChancePerHour)
            {
                steps |= CareStep.Sabotage;
            }
            return steps;
        }

        static string Hours(float h) => $"약 {Math.Max(1, (int)Math.Ceiling(h))}시간";
    }
}
