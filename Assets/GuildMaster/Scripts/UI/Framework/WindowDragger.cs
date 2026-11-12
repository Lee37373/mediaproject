using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GuildMaster.UI
{
    /// <summary>Title-bar drag handle: moves the window frame, keeping part of it on screen.</summary>
    public class WindowDragger : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        RectTransform _frame;
        Canvas _canvas;
        Action _dropped;

        /// <param name="dropped">Called when a drag ends (P6-2: the window remembers the spot).</param>
        public void Init(RectTransform frame, Action dropped = null)
        {
            _frame = frame;
            _dropped = dropped;
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (_canvas == null) _canvas = _frame.GetComponentInParent<Canvas>()?.rootCanvas;
        }

        public void OnDrag(PointerEventData e)
        {
            if (_frame == null) return;
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            var parent = (RectTransform)_frame.parent;
            _frame.anchoredPosition = WindowPlacement.Clamp(_frame.anchoredPosition + e.delta / scale, parent.rect.size, _frame.rect.size);
        }

        public void OnEndDrag(PointerEventData e) => _dropped?.Invoke();
    }
}
