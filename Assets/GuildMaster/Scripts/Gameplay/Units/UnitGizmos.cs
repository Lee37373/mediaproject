#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace GuildMaster.Units
{
    /// <summary>
    /// Editor debug drawing for units (Scene view, and Game view with its Gizmos button on):
    /// sight radius, attack range, current target line, ignored (given-up) targets, monster leash,
    /// and a label with name · tier · state · chase stall. Selected units always draw;
    /// F4 (<see cref="ShowAll"/>) draws every unit.
    /// </summary>
    public static class UnitGizmos
    {
        /// <summary>Draw every unit, not just the selected one (toggled by F4 in editor/dev builds).</summary>
        public static bool ShowAll;
        /// <summary>
        /// Unit picked in the game's inspector. Only drawn while <see cref="ShowAll"/> is on:
        /// clicking a unit is ordinary play, not debugging, and it used to paint sight and range
        /// circles over the game whenever the Game view's Gizmos button happened to be on
        /// (user report 2026-09-20).
        /// </summary>
        public static Unit Focus;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ShowAll = false;
            Focus = null;
        }

        static readonly Color SightColor = new(1f, 0.92f, 0.3f, 0.35f);
        static readonly Color RangeColor = new(1f, 0.35f, 0.3f, 0.8f);
        static readonly Color LeashColor = new(0.35f, 0.6f, 1f, 0.5f);
        static readonly Color GuildTargetColor = new(0.3f, 1f, 0.4f, 0.9f);
        static readonly Color EnemyTargetColor = new(1f, 0.25f, 0.2f, 0.9f);
        static readonly Color IgnoredColor = new(0.6f, 0.6f, 0.6f, 0.7f);

        public static void Draw(Unit u, bool selected)
        {
#if UNITY_EDITOR
            selected |= ShowAll && u == Focus;
            if (u == null || !u.IsAlive || (!selected && !ShowAll) || !Application.isPlaying) return;
            var p = u.Position;
            var guild = u.Faction == Faction.Guild;

            Handles.color = SightColor;
            Handles.DrawWireDisc(p, Vector3.forward, u.Stats.SightRadius);
            Handles.color = RangeColor;
            Handles.DrawWireDisc(p, Vector3.forward, u.Stats.AttackRange + Combat.ContactPadding + u.Radius);

            var engage = u.CombatEngagement;
            if (u.CurrentTarget != null && u.CurrentTarget.IsAlive)
            {
                Handles.color = guild ? GuildTargetColor : EnemyTargetColor;
                Handles.DrawLine(p, u.CurrentTarget.Position, 2f);
            }
            if (engage != null)
            {
                Handles.color = IgnoredColor;
                foreach (var ignored in engage.IgnoredUnits)
                    if (ignored != null && ignored.IsAlive) Handles.DrawDottedLine(p, ignored.Position, 4f);
            }
            if (selected && u is Monsters.Monster m && m.Def != null)
            {
                Handles.color = LeashColor;
                Handles.DrawWireDisc(m.HomePosition, Vector3.forward, m.Def.leashRadius);
            }

            string label = $"{u.DisplayName} [{CombatTiers.Label(u.Tier)}] {u.DebugState}";
            if (engage != null && engage.IsHoldingOff) label += " (어부지리 대기)";
            if (engage != null && engage.StallTime > 0.5f) label += $"\n추격 정체 {engage.StallTime:0.0}/{CombatTiers.ChaseGiveUp(u.Tier):0.0}s";
            var style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = guild ? GuildTargetColor : EnemyTargetColor } };
            Handles.Label(p + new Vector2(0.4f, 1.1f), label, style);
#endif
        }
    }
}
