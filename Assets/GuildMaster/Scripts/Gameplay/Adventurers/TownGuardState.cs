using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Town;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Guild members in town drop what they are doing to fight monsters inside the walls — e.g. one
    /// chased home by a retreating member (user bug report: nobody in town attacked it). Only monsters
    /// the guild can see count (information rule). Raids are left to <see cref="DefendState"/>.
    /// When the town is quiet again the member resumes the state it was in (<see cref="Next"/>).
    /// </summary>
    public class TownGuardState : State<Adventurer>
    {
        const float CalmSeconds = 2f;
        const float CheckInterval = 0.5f;
        /// <summary>Hurt members only fight back when hit themselves.</summary>
        const float MinHpToJoin = 0.5f;

        public State<Adventurer> Next { get; private set; }
        float _calm;
        float _repath;

        public override string Label => "마을 경비";

        /// <summary>
        /// Called from town states every tick (cheap: throttled per adventurer). Switches to guarding
        /// when a visible monster is inside the town; returns true if it did.
        /// </summary>
        public static bool TryStart(Adventurer a, State<Adventurer> resume, ref float timer, float dt)
        {
            timer -= dt;
            if (timer > 0f) return false;
            timer = CheckInterval;
            if (!a.IsAlive || a.IsVisitor || !a.IsGuildMember) return false;
            if (Services.TryGet<WaveManager>(out var waves) && waves.RaidSighted) return false;
            bool hitNow = a.LastAttacker is Monster attacker && a.WasRecentlyAttackedBy(attacker, 3f);
            // Too hurt to fight at all (it would retreat right away): stay put and let others handle it.
            if (a.HpRatio < a.RetreatThreshold || (a.HpRatio < MinHpToJoin && !hitNow)) return false;
            if (!hitNow && FindIntruder(a) == null) return false;
            a.TownGuardState.Next = resume;
            a.ChangeState(a.TownGuardState);
            return true;
        }

        /// <summary>
        /// Nearest living monster the guild currently sees inside the walls, or breaking a gate from
        /// outside (user report 2026-09-22: a stone slime that chased a member home bashed the gate
        /// while everyone inside stood about).
        /// </summary>
        static Unit FindIntruder(Adventurer a)
        {
            var town = AdvCtx.Town;
            Services.TryGet<FogOfWar>(out var fog);
            return UnitRegistry.FindNearest(a.Position, 60f,
                u => u is Monster m && m.IsAlive && IsIntruder(town, m) && (fog == null || fog.IsVisibleRaw(m.Position)));
        }

        /// <summary>Just outside the wall counts too once it fights us there: it stops bashing the gate then.</summary>
        const float WallReach = 4f;

        static bool IsIntruder(TownService town, Monster m)
        {
            if (town.IsInTown(m.Position) || m.IsBreakingGate) return true;
            return m.CombatEngagement?.Target is Unit t && t.Faction == Faction.Guild && town.WithinReachOfTown(m.Position, WallReach);
        }

        public override void Enter()
        {
            _calm = CalmSeconds;
            _repath = 0f;
            Owner.Say("마을에 몬스터가 들어왔다!");
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (a.CheckRetreat(wearinessCounts: false)) return;
            var town = AdvCtx.Town;
            bool Allowed(Unit u) => (u is Monster m && IsIntruder(town, m)) || a.WasRecentlyAttackedBy(u);
            // Monsters inside the walls are targets even if not aggressive (e.g. a fleeing wolf).
            if (a.HandleCombat(dt, u => u is Monster m && m.IsAlive && IsIntruder(town, m), Allowed))
            {
                _calm = CalmSeconds;
                return;
            }

            var intruder = FindIntruder(a);
            if (intruder != null)
            {
                _calm = CalmSeconds;
                _repath -= dt;
                if (_repath > 0f && a.Mover.IsMoving) return;
                _repath = 0.8f;
                a.Mover.SetDestination(intruder.Position, a.Stats.AttackRange * 0.8f);
                return;
            }

            // Clear what the intruders dropped inside the walls (P4-21).
            if (a.Looting.Tick(dt, AdvCtx.TownLootZone)) { _calm = CalmSeconds; return; }

            _calm -= dt;
            if (_calm > 0f) return;
            a.Engage.Clear();
            a.ChangeState(Next ?? a.IdleState);
        }

        public override void Exit() => Owner.Mover.SpeedMultiplier = 1f;
    }
}
