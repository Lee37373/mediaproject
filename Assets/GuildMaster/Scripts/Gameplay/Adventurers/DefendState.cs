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
    /// Town defense during a raid (P3-6). Defenders do not go out to meet the enemy: each holds
    /// just inside the gate its raid is heading for (<see cref="Front"/>, one per raid, following
    /// it: <see cref="WaveManager.Fronts"/>) and fights raiders inside the town, at any front,
    /// or fighting a comrade nearby. A raider that got in elsewhere is hunted down inside the walls. The current quest is kept; <see cref="DefenseCoordinator"/>
    /// sends the adventurer back to it when the raid ends.
    /// </summary>
    public class DefendState : State<Adventurer>
    {
        /// <summary>Raiders this close to the rally point (i.e. at the gate) may be engaged.</summary>
        const float HoldRadius = 6f;
        const float SlotRadius = 1.6f;
        const float RepathSeconds = 1f;

        float _repath;

        /// <summary>The raid front this defender holds (<see cref="DefenseCoordinator"/> shares them out).</summary>
        public Vector2? Front { get; set; }

        public override string Label => "마을 방어";

        public override void Enter()
        {
            _repath = 0f;
            Front = null;
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (!Services.TryGet<WaveManager>(out var waves) || !waves.RaidSighted) return; // coordinator releases us
            var world = Services.Get<WorldService>();
            // The guild master's rally flag (P4-132): one that heeds it holds there instead of its gate.
            var flag = Services.TryGet<Commands.MasterCommands>(out var orders) ? orders.FlagFor(a) : null;
            var rally = flag ?? Front ?? (waves.Fronts.Count > 0 ? waves.Fronts[0].Point : world.TownCenterWorld);

            // Also whoever is shooting into the town from outside, and whoever just hit us: raid
            // archers stand back from the gate (P4-40) and were left to shoot at will while the
            // defenders tidied up loot (user report 2026-09-22). Any front counts, and so does a
            // raider fighting a comrade nearby (user report 2026-09-22: one member fought at the
            // gate while the others watched from the rally point).
            bool InZone(Unit u) => u != null && (world.Grid.IsInTown(u.Position) || AtAFront(u.Position)
                                                 || a.WasRecentlyAttackedBy(u) || ShootsIntoTown(u)
                                                 || u is Monster m && a.HearsFightingAComrade(m));
            bool AtAFront(Vector2 p)
            {
                if (Vector2.Distance(p, rally) <= HoldRadius) return true;
                foreach (var f in waves.Fronts)
                    if (Vector2.Distance(p, f.Point) <= HoldRadius) return true;
                return false;
            }
            bool ShootsIntoTown(Unit u) => u.Stats.IsRanged && AdvCtx.Town.WithinReachOfTown(u.Position, u.Stats.AttackRange + 1.5f);

            if (a.CheckRetreat(wearinessCounts: false)) return;
            if (a.HandleCombat(dt, u => u is Monster m && m.DefaultStateOverride != null && m.IsAlive, InZone)) return;
            // A lull: pick up what the fallen raiders dropped at the gate (P4-40, user report
            // 2026-09-21: after a raid the loot was left lying). Looting stops when an enemy shows.
            if (a.Looting.Tick(dt, p => world.Grid.IsInTown(p) || Vector2.Distance(p, rally) <= HoldRadius + 2f)) return;

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
            // Round a flag planted outside the walls, the slots are outside too.
            if (!world.Grid.IsWalkable(GridMap.WorldToCell(slot)) || flag == null && !world.Grid.IsInTown(slot)) slot = rally;
            if (Vector2.Distance(a.Position, slot) > 0.8f) a.Mover.SetDestination(slot, 0.3f);
        }
    }
}
