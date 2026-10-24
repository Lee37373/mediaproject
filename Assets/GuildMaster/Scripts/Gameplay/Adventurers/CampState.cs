using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// At a camp out in the wild (P4-66): walks to its place by the fire and sits (the talking hour),
    /// then keeps watch or sleeps. The watch sees as usual and mends slowly; a sleeper mends fast
    /// but sees next to nothing, and wakes only when the watch calls or something hits it. Raiders
    /// get everyone up and fighting; once it has been quiet a while they settle down again. The camp
    /// itself (<see cref="CampService"/>) decides when the fire is put out and where each goes then.
    /// </summary>
    public class CampState : State<Adventurer>
    {
        const float SeatReached = 0.35f;
        /// <summary>Further from its seat than this, a member is still on its way to the fire (P4-78).</summary>
        const float WalkInDistance = 3f;
        /// <summary>Seconds near the fire it tries for its exact seat before sitting where it stands.</summary>
        const float SeatTrySeconds = 8f;
        const float WatchScanInterval = 0.4f;
        /// <summary>Seconds between "Zz" lines from a sleeper.</summary>
        const float SnoreInterval = 9f;

        Camp _camp;
        float _scanTimer;
        float _snoreTimer;
        float _nearTime;
        bool _asleep;

        /// <summary>At its place by the fire and sitting down.</summary>
        public bool Seated { get; private set; }

        /// <summary>Still on its way to a fire further off (P4-78).</summary>
        public bool WalkingIn => !Seated && _camp != null && Owner != null && Vector2.Distance(Owner.Position, _camp.SeatOf(Owner)) > WalkInDistance;

        public override string Label
            => _camp == null ? "야영"
             : WalkingIn ? "야영지로 가는 중"
             : _camp.Alarmed ? "야영지 습격"
             : _camp.Watcher == Owner ? "불침번"
             : _asleep ? "잠"
             : "야영";

        CampService Service => Services.TryGet<CampService>(out var s) ? s : null;

        public override void Enter()
        {
            var a = Owner;
            _camp = Service?.CampOf(a);
            Seated = false;
            _asleep = false;
            _scanTimer = 0f;
            _nearTime = 0f;
            _snoreTimer = GameRandom.Shared.Range(2f, SnoreInterval);
            a.Looting.Cancel();
            if (_camp == null) { a.ChangeState(a.ReturnState); return; }
            if (!a.Mover.SetDestination(_camp.SeatOf(a), 0.1f)) CannotGetThere();
        }

        /// <summary>
        /// Back by the fire after a load (P4-98): as on entering, and one already at its seat sits, keeps watch or lies down at
        /// once. Entering afresh left a sleeper with its full sight until its first step — through a paused load, for good —
        /// and the fog lifted round the camp as far as an awake eye sees.
        /// </summary>
        public override void ResumeState(string json, SaveContext ctx)
        {
            Enter();
            var a = Owner;
            if (_camp == null || a.Fsm.Current != this || _camp.Alarmed) return; // no camp (it went on), or everyone is up
            // Still walking in from afar. Nearer than that it sits (and sleeps) where it is: one that could not reach its exact seat
            // sat down where it stood (SeatTrySeconds) — loaded, it came back awake (P4-102: a third at the fire did).
            if (Vector2.Distance(a.Position, _camp.SeatOf(a)) > WalkInDistance) return;
            a.Mover.Stop();
            Seated = true;
            SetAsleep(!_camp.Sitting(AdvCtx.Clock.TotalHours) && _camp.Watcher != a);
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (_camp == null) return;
            float hour = AdvCtx.Clock.TotalHours;

            // Still on its way to a fire further off (P4-78: the whole party gathers, wherever it was):
            // it walks there as on any road and fights what comes at it on the way. Its fights there do
            // not wake the fire; a bad one pulls the party back as anywhere (P4-71).
            if (WalkingIn)
            {
                WalkIn(a, dt);
                return;
            }

            // Hit in the dark: up at once, and the others with it.
            if (Time.time - a.LastAttackedTime < 1f) Service?.Alarm(_camp, a.LastAttacker, warned: false);

            if (_camp.Alarmed)
            {
                SetAsleep(false);
                // Badly hurt in a raid, it runs for home like anywhere else.
                if (a.CheckRetreat(wearinessCounts: false)) return;
                if (a.HandleCombat(dt)) return;
                Seated = false;
                if (!a.Mover.IsMoving && Vector2.Distance(a.Position, _camp.SeatOf(a)) > SeatReached) a.Mover.SetDestination(_camp.SeatOf(a), 0.1f);
                return;
            }

            if (!Seated)
            {
                _nearTime += dt;
                if (Vector2.Distance(a.Position, _camp.SeatOf(a)) > SeatReached && a.Mover.IsMoving) return;
                if (Vector2.Distance(a.Position, _camp.SeatOf(a)) > SeatReached && _nearTime < SeatTrySeconds)
                {
                    a.Mover.SetDestination(_camp.SeatOf(a), 0.1f);
                    return;
                }
                a.Mover.Stop();
                Seated = true;
            }

            bool sitting = _camp.Sitting(hour);
            bool watching = _camp.Watcher == a;
            SetAsleep(!sitting && !watching);
            Mend(dt, sitting ? CampRules.SitHealPerHour : watching ? CampRules.WatchHealPerHour : CampRules.SleepHealPerHour,
                     sitting ? CampRules.SitFatiguePerHour : watching ? CampRules.WatchFatiguePerHour : CampRules.SleepFatiguePerHour);

            if (_asleep)
            {
                _snoreTimer -= dt;
                if (_snoreTimer <= 0f) { _snoreTimer = SnoreInterval; EventBus.Publish(new CampSnoreEvent { Sleeper = a }); }
                return;
            }

            // Awake by the fire (the watch, or everyone while sitting): eyes on the dark.
            _scanTimer -= dt;
            if (_scanTimer > 0f) return;
            _scanTimer = WatchScanInterval;
            // Whatever would come for us, and whatever is already fighting one of ours out there.
            var raider = UnitRegistry.FindNearest(a.Position, a.Stats.SightRadius,
                u => u is Monsters.Monster m && m.IsAlive && (m.IsHostileTo(a) || m.CombatEngagement?.Target is { Faction: Faction.Guild })
                     && a.CanSee(m));
            if (raider == null) return;
            a.Say(watching ? "습격이다! 모두 일어나!" : "뭔가 온다! 무기 들어!");
            Service?.Alarm(_camp, raider, warned: true);
        }

        /// <summary>The walk to a fire further off: fights first (a bad one ends in a retreat), then on to its seat.</summary>
        void WalkIn(Adventurer a, float dt)
        {
            _nearTime = 0f;
            // Badly hurt and under attack: pulled out like anywhere else. Just worn or scratched, it
            // keeps walking — the fire is where it mends.
            if (a.IsInFight() && a.CheckRetreat(wearinessCounts: false)) return;
            if (a.HandleCombat(dt)) return;
            var seat = _camp.SeatOf(a);
            if (a.Mover.IsMoving && Vector2.Distance(a.Mover.Destination, seat) < 0.5f) return;
            if (!a.Mover.SetDestination(seat, 0.1f)) CannotGetThere();
        }

        /// <summary>No way to the fire: it goes on with its own road and is not called there again.</summary>
        void CannotGetThere()
        {
            var a = Owner;
            _camp.CannotReach.Add(a);
            a.ChangeState(CampService.NextRoad(_camp, a));
        }

        void Mend(float dt, float healPerHour, float fatiguePerHour)
        {
            var a = Owner;
            float hours = dt / AdvCtx.Clock.RealSecondsPerGameHour;
            a.Heal(a.Stats.MaxHp * healPerHour * hours);
            a.Data.fatigue = Mathf.Max(0f, a.Data.fatigue - fatiguePerHour * hours);
            a.Data.stress = Mathf.Max(0f, a.Data.stress - 6f * hours);
        }

        /// <summary>Lies down (and sees next to nothing), or gets up.</summary>
        void SetAsleep(bool asleep)
        {
            if (_asleep == asleep) return;
            _asleep = asleep;
            var a = Owner;
            a.Visual.Lying = asleep;
            if (a.TryGetComponent<VisionSource>(out var vision)) vision.SightRadius = asleep ? CampRules.SleeperSight : a.Stats.SightRadius;
        }

        public override void Exit()
        {
            SetAsleep(false);
            Seated = false;
            Owner.Mover.SpeedMultiplier = 1f;
            Service?.Leave(Owner);
            _camp = null;
        }
    }
}
