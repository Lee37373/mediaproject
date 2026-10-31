using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Hit inside the walls, too hurt to fight and with nothing to drink (P4-100, user decision 2026-10-21: "맞으면 반격하거나
    /// 도망"): it runs from its attacker to the far side of the town — the standing building door or the town centre furthest
    /// from it — keeps its head down there, mending as at rest, and goes back to what it was doing once no monster has been at
    /// it for a while. Mended enough (a priest, a potion) and hit again, it fights back like anyone in town
    /// (<see cref="TownGuardState"/>). In a measured run raiders broke in and killed thirteen members where they lay resting
    /// under half health — the defence leaves those to rest, and they neither fought nor ran — and three retreating ones on
    /// their way to the guild door the raiders stood at. Hit from outside the walls by one the town cannot take on, it does the
    /// same whatever its health (P4-108, user decision 2026-10-27 "못 이기면 성 안으로", <see cref="TownGuardState.BeyondUs"/>).
    /// </summary>
    public class TakeCoverState : State<Adventurer>
    {
        /// <summary>Real seconds a hit counts as "just now".</summary>
        public const float HitSeconds = 3f;
        /// <summary>Real seconds with no monster at it, or this close (cells), before it goes back.</summary>
        public const float CalmSeconds = 6f, ThreatRange = 10f;
        const float CheckInterval = 0.25f, RepathSeconds = 1f, RunSpeed = 1.15f;
        /// <summary>Health mended an hour while lying low: as at rest (<see cref="RestState"/>).</summary>
        const float HealPerHour = 0.25f;

        public State<Adventurer> Next { get; internal set; }
        float _calm, _repath, _check;
        Unit _threat;

        public override string Label => "피신 중";

        /// <summary>Hit just now by a monster still standing.</summary>
        internal static bool HitNow(Adventurer a) => a.LastAttacker is Monster m && m.IsAlive && a.WasRecentlyAttackedBy(m, HitSeconds);

        /// <summary>
        /// Called by town states (through <see cref="TownGuardState.TryStart"/>), a town guard and a retreat inside the walls:
        /// hit in town, too hurt to fight and with no potion, it runs for cover and comes back to <paramref name="next"/>.
        /// </summary>
        public static bool TryStart(Adventurer a, State<Adventurer> next)
        {
            if (!a.IsAlive || a.IsVisitor || !a.IsGuildMember) return false;
            if (!HitNow(a) || !AdvCtx.Town.IsInTown(a.Position)) return false;
            // Hit from outside the walls by one the town cannot take on (P4-108): back into the town whatever its health.
            if ((a.HpRatio >= a.RetreatThreshold || a.Data.potions > 0) && !TownGuardState.Outmatched(a)) return false;
            a.TakeCoverState.Next = next;
            a.ChangeState(a.TakeCoverState);
            return true;
        }

        public override void Enter()
        {
            var a = Owner;
            _calm = CalmSeconds;
            _repath = 0f;
            _check = 0f;
            _threat = null;
            a.Engage.Clear();
            a.Mover.SpeedMultiplier = RunSpeed;
            a.Say(TownGuardState.Outmatched(a) && a.HpRatio >= a.RetreatThreshold ? "저건 못 이겨, 성 안으로!" : "피해야 해!");
        }

        public override void Exit() => Owner.Mover.SpeedMultiplier = 1f;

        public override void Tick(float dt)
        {
            var a = Owner;
            bool hit = HitNow(a);
            // Mended enough to rejoin a fight (as one fallen back does, P4-96 — not back and forth at the line), or with a
            // potion to drink now, and hit: it turns on its attacker — not on one beyond the town from outside the walls (P4-108).
            if (hit && (a.HpRatio >= FallBackRules.RejoinAt(a.RetreatThreshold) || a.Data.potions > 0) && !TownGuardState.Outmatched(a))
            {
                a.TownGuardState.Next = Next;
                a.ChangeState(a.TownGuardState);
                return;
            }
            _check -= dt;
            if (_check <= 0f)
            {
                _check = CheckInterval;
                _threat = Threat(a);
            }
            if (hit || _threat != null)
            {
                _calm = CalmSeconds;
                _repath -= dt;
                if (_repath <= 0f)
                {
                    _repath = RepathSeconds;
                    RunFrom(a, hit ? a.LastAttacker : _threat);
                }
                if (a.Mover.IsMoving) return;
            }
            else if (a.Mover.IsMoving) return;
            // Lying low mends it as resting does.
            a.Heal(a.Stats.MaxHp * HealPerHour * dt / AdvCtx.Clock.RealSecondsPerGameHour);
            if (_threat != null) return;
            _calm -= dt;
            if (_calm > 0f) return;
            a.ChangeState(Next ?? a.RestState);
        }

        /// <summary>The nearest monster the guild sees this close that is hostile to it, or null.</summary>
        static Unit Threat(Adventurer a)
        {
            Services.TryGet<FogOfWar>(out var fog);
            return UnitRegistry.FindNearest(a.Position, ThreatRange, (a, fog),
                static (u, s) => u is Monster m && m.IsAlive && m.IsHostileTo(s.a) && (s.fog == null || s.fog.IsVisibleRaw(m.Position)));
        }

        /// <summary>To the standing building door or the town centre furthest from <paramref name="from"/>, inside the walls.</summary>
        static void RunFrom(Adventurer a, Unit from)
        {
            if (from == null) return;
            var town = AdvCtx.Town;
            var best = a.Position;
            float bestSq = (a.Position - from.Position).sqrMagnitude;
            void Consider(Vector2 p)
            {
                if (!town.IsInTown(p)) return;
                float sq = (p - from.Position).sqrMagnitude;
                if (sq <= bestSq) return;
                bestSq = sq;
                best = p;
            }
            foreach (var b in town.Buildings)
                if (b != null && !b.IsDestroyed) Consider(b.DoorPosition);
            if (Services.TryGet<WorldService>(out var world)) Consider(world.TownCenterWorld);
            if (best != a.Position) a.Mover.SetDestination(best, 0.6f);
        }

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class Saved { public string next; }

        public override string SaveState() => JsonUtility.ToJson(new Saved { next = Adventurer.NameOf(Next) });

        public override void ResumeState(string json, SaveContext ctx)
        {
            Next = Owner.StateNamed(JsonUtility.FromJson<Saved>(json).next);
            _calm = CalmSeconds;
            _repath = 0f;
            _check = 0f;
            _threat = null;
            Owner.Mover.SpeedMultiplier = RunSpeed;
        }
    }
}
