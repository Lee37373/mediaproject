using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Town;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Monsters
{
    /// <summary>
    /// Smashing a town gate (P4-39). Entered when what a monster wants is behind the walls and every
    /// way in is shut: raiders marching on a building, and — user decision 2026-09-21 — anything
    /// that chased a guild member back into town. A raider keeps at it until the gate gives; a
    /// chaser loses interest after <see cref="ChaserPatience"/>, but the damage stays until the
    /// gate is repaired. Whoever attacks it meanwhile is fought first.
    /// </summary>
    public class MonsterGateBreakState : State<Monster>
    {
        /// <summary>Game seconds a chaser spends on a gate before it gives up and goes home.</summary>
        public const float ChaserPatience = 15f;
        const float RepathSeconds = 1f;
        /// <summary>Extra reach against a gate: it is a wall three cells wide, not a body.</summary>
        const float Reach = 0.9f;

        TownGate _gate;
        bool _patient;
        float _repath;

        public override string Label => "성문 공격";

        /// <summary>Sets the gate to break; <paramref name="untilBroken"/> for raiders, who never give up on it.</summary>
        public void Begin(TownGate gate, bool untilBroken)
        {
            _gate = gate;
            _patient = untilBroken;
        }

        public override void Enter() => _repath = 0f;

        public override void Tick(float dt)
        {
            var m = Owner;
            if (_gate == null || _gate.IsBroken || (!_patient && TimeInState > ChaserPatience))
            {
                m.GoToDefaultState();
                return;
            }

            // Someone hitting us from our side of the wall comes first.
            var attacker = m.LastAttacker;
            if (attacker != null && attacker.IsAlive && m.WasRecentlyAttackedBy(attacker, 3f) && SameSide(m, attacker))
            {
                m.Engage.SetTarget(attacker);
                Machine.ChangeState(m.EngageState);
                return;
            }

            var edge = _gate.ClosestPoint(m.Position);
            if (Vector2.Distance(m.Position, edge) <= m.Stats.AttackRange + m.Radius + Reach)
            {
                m.Mover.Stop();
                m.Visual?.FaceTowards(edge);
                if (Time.time >= m.NextAttackTime)
                {
                    m.NextAttackTime = Time.time + m.Stats.AttackCooldown;
                    _gate.Damage(m.Stats.Attack * RaidState.BuildingDamageMul, m);
                    m.Visual?.Flash();
                }
                return;
            }

            _repath -= dt;
            if (_repath > 0f && m.Mover.IsMoving) return;
            _repath = RepathSeconds;
            var world = Services.Get<World.WorldService>();
            var spot = world.Grid.IsInTown(m.Position) ? _gate.InsidePoint : _gate.OutsidePoint;
            if (!m.Mover.SetDestination(spot, 0.4f)) m.GoToDefaultState(); // cannot even reach the gate
        }

        public override void Exit() => Owner.Mover.Stop();

        /// <summary>Both inside the walls or both outside: a fight that needs no gate.</summary>
        static bool SameSide(Unit a, Unit b)
        {
            var grid = Services.Get<World.WorldService>().Grid;
            return grid.IsInTown(a.Position) == grid.IsInTown(b.Position);
        }
    }
}
