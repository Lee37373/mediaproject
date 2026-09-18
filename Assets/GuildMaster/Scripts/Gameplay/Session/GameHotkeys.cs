using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace GuildMaster.Session
{
    /// <summary>Global shortcuts: Space pause, 1-3 speed, F follow avatar, F3 toggle fog (debug).</summary>
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

            if (kb.fKey.wasPressedThisFrame && Services.TryGet<CameraController>(out var cam) && Services.TryGet<PlayerAvatar>(out var avatar))
                cam.Follow(cam.FollowTarget == avatar.transform ? null : avatar.transform);

            if (kb.f3Key.wasPressedThisFrame && Services.TryGet<FogOfWar>(out var fog))
                fog.Disabled = !fog.Disabled;
        }

        static bool IsTyping()
        {
            var es = EventSystem.current;
            return es != null && es.currentSelectedGameObject != null
                   && es.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>() != null;
        }
    }
}
