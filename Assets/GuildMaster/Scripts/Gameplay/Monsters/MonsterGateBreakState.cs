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

        /// <summary>Seconds between checks for a way in through a gate someone else already broke.</summary>
        const float RerouteSeconds = 3f;
        static readonly System.Collections.Generic.List<Vector2Int> s_path = new();

        TownGate _gate;
        bool _patient;
        float _repath;
        float _rerouteTimer;

        public override string Label => "성문 공격";

        /// <summary>Sets the gate to break; <paramref name="untilBroken"/> for raiders, who never give up on it.</summary>
        public void Begin(TownGate gate, bool untilBroken)
        {
            _gate = gate;
            _patient = untilBroken;
        }

        public override void Enter()
        {
            _repath = 0f;
            _rerouteTimer = RerouteSeconds;
        }

        [System.Serializable]
        class Saved
        {
            /// <summary>The gate's place in <see cref="GateService.Gates"/> (the map's gate order).</summary>
            public int gate = -1;
            public bool patient;
        }

        public override string SaveState()
        {
            int index = -1;
            if (_gate != null && Core.Services.TryGet<GateService>(out var gates))
                for (int i = 0; i < gates.Gates.Count; i++) if (gates.Gates[i] == _gate) index = i;
            return JsonUtility.ToJson(new Saved { gate = index, patient = _patient });
        }

        public override void ResumeState(string json, Core.SaveContext ctx)
        {
            var s = JsonUtility.FromJson<Saved>(json);
            _gate = s.gate >= 0 && Core.Services.TryGet<GateService>(out var gates) && s.gate < gates.Gates.Count ? gates.Gates[s.gate] : null;
            _patient = s.patient;
            Enter();
        }

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
                    m.NextAttackTime = Time.time + m.AttackCooldownNow;
                    _gate.Damage(m.Stats.Attack * RaidState.BuildingDamageMul, m);
                    m.Visual?.Flash();
                    m.Visual?.PlayAttack();
                }
                return;
            }

            _repath -= dt;
            if (_repath > 0f && m.Mover.IsMoving) return;
            _repath = RepathSeconds;
            var world = Services.Get<World.WorldService>();

            // Another gate already gave way: a raider takes that road in instead (P4-40).
            _rerouteTimer -= RepathSeconds;
            if (_patient && !world.Grid.TownSealed && _rerouteTimer <= 0f)
            {
                _rerouteTimer = RerouteSeconds;
                if (world.FindPath(m.Position, world.TownCenterWorld, s_path, passesGates: false))
                {
                    m.GoToDefaultState();
                    return;
                }
            }

            var spot = ApproachSpot(m, world);
            if (!m.Mover.SetDestination(spot, 0.4f)) m.GoToDefaultState(); // cannot even reach the gate
        }

        /// <summary>
        /// Where this attacker stands: one of the gate's three lanes, a step off it for melee and
        /// further back for archers (P4-40 — they all used to queue for the one cell in front, and
        /// whoever could not reach it milled about doing nothing).
        /// </summary>
        Vector2 ApproachSpot(Monster m, World.WorldService world)
        {
            bool inside = world.Grid.IsInTown(m.Position) && !_gate.Contains(m.Position);
            var outward = (Vector2)_gate.Outward * (inside ? -1f : 1f);
            var side = new Vector2(_gate.Outward.y, _gate.Outward.x);
            int lane = Mathf.Abs(m.GetInstanceID()) % 3 - 1;
            float back = m.Stats.IsRanged ? Mathf.Clamp(m.Stats.AttackRange * 0.7f, 1f, 3.5f) : 1f;
            var spot = _gate.Center + side * lane + outward * back;
            return world.Grid.TryFindNearestWalkable(World.GridMap.WorldToCell(spot), 2, out var cell) && !world.Grid.IsClosedGate(cell)
                ? World.GridMap.CellToWorld(cell)
                : (inside ? _gate.InsidePoint : _gate.OutsidePoint);
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
