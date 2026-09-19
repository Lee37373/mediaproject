using UnityEngine;
using UnityEngine.EventSystems;

namespace GuildMaster.UI
{
    /// <summary>Title-bar drag handle: moves the window frame, keeping part of it on screen.</summary>
    public class WindowDragger : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        const float KeepVisible = 60f;

        RectTransform _frame;
        Canvas _canvas;

        public void Init(RectTransform frame) => _frame = frame;

        public void OnBeginDrag(PointerEventData e)
        {
            if (_canvas == null) _canvas = _frame.GetComponentInParent<Canvas>()?.rootCanvas;
        }

        public void OnDrag(PointerEventData e)
        {
            if (_frame == null) return;
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            var pos = _frame.anchoredPosition + e.delta / scale;
            // Frames are centre-anchored: keep at least KeepVisible px of the frame inside the parent.
            var parent = (RectTransform)_frame.parent;
            var half = parent.rect.size * 0.5f;
            var size = _frame.rect.size * 0.5f;
            pos.x = Mathf.Clamp(pos.x, -half.x - size.x + KeepVisible, half.x + size.x - KeepVisible);
            // Vertically the title bar (top edge) must stay reachable.
            pos.y = Mathf.Clamp(pos.y, -half.y + KeepVisible - size.y, half.y - size.y);
            _frame.anchoredPosition = pos;
        }
    }
}
