using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Monsters;
using GuildMaster.Player;
using GuildMaster.Quests;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Escort quest (P3-4a): keep a formation slot around the guild master, fight monsters that
    /// come near them, and never chase far away. Ends when the quest completes (see QuestBoard).
    /// </summary>
    public class EscortState : State<Adventurer>
    {
        const float SlotRadius = 1.7f;
        const float SlotTolerance = 1.0f;
        const float CatchUpDistance = 6f;
        const float RepathSeconds = 0.4f;

        float _repath;

        public override string Label => "호위 중";

        public override void Enter() => _repath = 0f;

        public override void Exit() => Owner.Mover.SpeedMultiplier = 1f;

        public override void Tick(float dt)
        {
            var a = Owner;
            if (!AdvCtx.QuestStillValid(a) || a.CheckRetreat()) return;
            if (!Services.TryGet<PlayerAvatar>(out var master) || !master.IsAlive) return;
            var q = a.CurrentQuest;
            float guard = QuestBoard.EscortRange;

            // Protect the guild master: don't get dragged off chasing something far from them.
            var target = a.Engage.Target;
            if (target != null && Vector2.Distance(target.Position, master.Position) > guard + 4f) a.Engage.Clear();
            if (a.HandleCombat(dt, u => u is Monster m && m.IsAlive && Vector2.Distance(m.Position, master.Position) < guard)) return;

            // Formation slot around the master, evenly spaced by party index.
            int index = 0, count = Mathf.Max(1, q.Assignees.Count);
            for (int i = 0; i < q.Assignees.Count; i++) if (ReferenceEquals(q.Assignees[i], a)) index = i;
            float angle = Mathf.PI * 0.5f + index * Mathf.PI * 2f / count;
            var slot = master.Position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * SlotRadius;
            var world = Services.Get<WorldService>();
            if (!world.Grid.IsWalkable(GridMap.WorldToCell(slot))) slot = master.Position;

            float dist = Vector2.Distance(a.Position, slot);
            a.Mover.SpeedMultiplier = Vector2.Distance(a.Position, master.Position) > CatchUpDistance ? 1.35f : 1f;
            _repath -= dt;
            if (dist <= SlotTolerance || _repath > 0f) return;
            _repath = RepathSeconds;
            if (!a.Mover.IsMoving || Vector2.Distance(a.Mover.Destination, slot) > 0.8f)
                a.Mover.SetDestination(slot, 0.3f);
        }
    }
}
