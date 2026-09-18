using System;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace GuildMaster.Player
{
    public struct MapPickingChangedEvent { public bool Active; public string Prompt; }

    /// <summary>
    /// Modal "click a point on the map" helper used by UI forms (explore quests, nest selection).
    /// While active it consumes left/right clicks so other map inputs ignore them.
    /// </summary>
    public class MapPicker : MonoBehaviour
    {
        Action<Vector2> _onPicked;
        Action _onCancelled;
        int _startedFrame;

        public bool IsPicking => _onPicked != null;

        public void Begin(string prompt, Action<Vector2> onPicked, Action onCancelled = null)
        {
            Cancel();
            _onPicked = onPicked;
            _onCancelled = onCancelled;
            _startedFrame = Time.frameCount;
            EventBus.Publish(new MapPickingChangedEvent { Active = true, Prompt = prompt });
        }

        public void Cancel()
        {
            if (!IsPicking) return;
            var cb = _onCancelled;
            End();
            cb?.Invoke();
        }

        void End()
        {
            _onPicked = null;
            _onCancelled = null;
            EventBus.Publish(new MapPickingChangedEvent { Active = false });
        }

        void Update()
        {
            if (!IsPicking || Time.frameCount == _startedFrame) return;
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (mouse != null && mouse.rightButton.wasPressedThisFrame))
            {
                Cancel();
                return;
            }
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            var pos = Services.Get<CameraController>().MouseWorldPosition();
            var cb = _onPicked;
            End();
            cb?.Invoke(pos);
        }
    }
}
