using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Monsters;
using GuildMaster.Town;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Town defense during a raid (P3-6). Defenders do not go out to meet the enemy: they hold
    /// just inside the gate facing the raid (<see cref="WaveManager.RallyPoint"/>) and fight
    /// raiders that are inside the town or right at that gate. A raider that got in elsewhere
    /// is hunted down inside the walls. The current quest is kept; <see cref="DefenseCoordinator"/>
    /// sends the adventurer back to it when the raid ends.
    /// </summary>
    public class DefendState : State<Adventurer>
    {
        /// <summary>Raiders this close to the rally point (i.e. at the gate) may be engaged.</summary>
        const float HoldRadius = 6f;
        const float SlotRadius = 1.6f;
        const float RepathSeconds = 1f;

        float _repath;

        public override string Label => "마을 방어";

        public override void Enter() => _repath = 0f;

        public override void Tick(float dt)
        {
            var a = Owner;
            if (!Services.TryGet<WaveManager>(out var waves) || !waves.RaidSighted) return; // coordinator releases us
            var world = Services.Get<WorldService>();
            var rally = waves.RallyPoint;

            bool InZone(Unit u) => u != null && (world.Grid.IsInTown(u.Position) || Vector2.Distance(u.Position, rally) <= HoldRadius);

            if (a.CheckRetreat()) return;
            if (a.HandleCombat(dt, u => u is Monster m && m.DefaultStateOverride != null && m.IsAlive, InZone)) return;

            _repath -= dt;
            if (_repath > 0f && a.Mover.IsMoving) return;
            _repath = RepathSeconds;

            // An intruder inside the walls: go get it.
            Unit intruder = null;
            float best = float.MaxValue;
            foreach (var m in waves.Raiders)
            {
                if (m == null || !m.IsAlive || !world.Grid.IsInTown(m.Position)) continue;
                float d = (m.Position - a.Position).sqrMagnitude;
                if (d < best) { best = d; intruder = m; }
            }
            if (intruder != null)
            {
                a.Mover.SetDestination(intruder.Position, a.Stats.AttackRange * 0.8f);
                return;
            }

            // Otherwise hold a slot at the rally point (spread out, ranged a step further back).
            float angle = (Mathf.Abs(a.Data.id.GetHashCode()) % 360) * Mathf.Deg2Rad;
            float r = SlotRadius * (a.Stats.IsRanged ? 1.6f : 1f);
            var slot = rally + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
            if (!world.Grid.IsWalkable(GridMap.WorldToCell(slot)) || !world.Grid.IsInTown(slot)) slot = rally;
            if (Vector2.Distance(a.Position, slot) > 0.8f) a.Mover.SetDestination(slot, 0.3f);
        }
    }
}
