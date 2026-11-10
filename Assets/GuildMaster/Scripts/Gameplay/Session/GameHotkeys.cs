using GuildMaster.Core;
using GuildMaster.Fog;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace GuildMaster.Session
{
    /// <summary>Global shortcuts: Space pause, 1-3 speed, F3 toggle fog, F4 unit gizmos (debug). F (focus) lives in InspectorView.</summary>
    public class GameHotkeys : MonoBehaviour
    {
        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || IsTyping() || MenuState.IsOpen) return;

            var clock = Services.Get<GameClock>();
            if (kb.spaceKey.wasPressedThisFrame) clock.TogglePause();
            if (kb.digit1Key.wasPressedThisFrame) clock.SetSpeed(1);
            if (kb.digit2Key.wasPressedThisFrame) clock.SetSpeed(2);
            if (kb.digit3Key.wasPressedThisFrame) clock.SetSpeed(3);
            if (kb.digit4Key.wasPressedThisFrame) clock.SetSpeed(4);

            // F3: fog off (debug): it shows the whole map, so never in a player build (P4-69).
            if (Debug.isDebugBuild && kb.f3Key.wasPressedThisFrame && Services.TryGet<FogOfWar>(out var fog))
                fog.Disabled = !fog.Disabled;
            // F6: guild invulnerable for testing (editor / development builds only).
            if (Debug.isDebugBuild && kb.f6Key.wasPressedThisFrame) DebugCommands.Invulnerable(!Units.Unit.GuildInvulnerable);
            // F4: unit debug gizmos for every unit (editor Scene view / Game view with Gizmos on).
            if (Application.isEditor && kb.f4Key.wasPressedThisFrame)
            {
                Units.UnitGizmos.ShowAll = !Units.UnitGizmos.ShowAll;
                // The drawing is gizmo-based: the Game view's Gizmos toggle follows F4 both ways.
                // Leaving it on turned every inspector click into a sight/range overlay long after
                // debug mode was off (user report 2026-09-20).
                SetGameViewGizmos(Units.UnitGizmos.ShowAll);
                Debug.Log($"[Debug] 유닛 기즈모 전체 표시: {(Units.UnitGizmos.ShowAll ? "켬" : "끔")}");
            }
        }

        /// <summary>Editor only: drives the Gizmos toggle of every Game view (internal API, via reflection).</summary>
        static void SetGameViewGizmos(bool on)
        {
#if UNITY_EDITOR
            var type = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var prop = type?.GetProperty("drawGizmos", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (prop == null || !prop.CanWrite)
            {
                if (on) Debug.Log("[Debug] Game 뷰 상단의 Gizmos 버튼을 켜야 기즈모가 보입니다.");
                return;
            }
            foreach (var view in Resources.FindObjectsOfTypeAll(type)) prop.SetValue(view, on);
#endif
        }

        static bool IsTyping() => TextFocus.Typing; // P6-2: a field with the caret, not just a selected one
    }
}
