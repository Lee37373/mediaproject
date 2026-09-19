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
            if (kb == null || IsTyping()) return;

            var clock = Services.Get<GameClock>();
            if (kb.spaceKey.wasPressedThisFrame) clock.TogglePause();
            if (kb.digit1Key.wasPressedThisFrame) clock.SetSpeed(1);
            if (kb.digit2Key.wasPressedThisFrame) clock.SetSpeed(2);
            if (kb.digit3Key.wasPressedThisFrame) clock.SetSpeed(3);

            if (kb.f3Key.wasPressedThisFrame && Services.TryGet<FogOfWar>(out var fog))
                fog.Disabled = !fog.Disabled;
            // F4: unit debug gizmos for every unit (editor Scene view / Game view with Gizmos on).
            if (Application.isEditor && kb.f4Key.wasPressedThisFrame)
            {
                Units.UnitGizmos.ShowAll = !Units.UnitGizmos.ShowAll;
                Debug.Log($"[Debug] 유닛 기즈모 전체 표시: {(Units.UnitGizmos.ShowAll ? "켬" : "끔")}");
            }
        }

        static bool IsTyping()
        {
            var es = EventSystem.current;
            return es != null && es.currentSelectedGameObject != null
                   && es.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>() != null;
        }
    }
}
