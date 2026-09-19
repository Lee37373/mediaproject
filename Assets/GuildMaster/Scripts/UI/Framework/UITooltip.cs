using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Shared hover tooltip: one panel on top of the HUD that follows the mouse. UI elements opt in
    /// with <see cref="UIBuilder.Tooltip"/> (adds a <see cref="TooltipTarget"/>).
    /// </summary>
    public class UITooltip : MonoBehaviour
    {
        const float MaxWidth = 360f;
        static readonly Vector2 Offset = new(18f, -18f);

        static UITooltip s_instance;

        RectTransform _canvas, _panel;
        Text _text;
        LayoutElement _layout;
        TooltipTarget _owner;

        public void Build(RectTransform canvas)
        {
            s_instance = this;
            _canvas = canvas;
            var bg = UIBuilder.Panel(canvas, "Tooltip", new Color(0.06f, 0.05f, 0.09f, 0.97f), raycast: false);
            _panel = bg.rectTransform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0f, 1f);
            var v = UIBuilder.VLayout(bg, 0, 10);
            v.childControlWidth = v.childControlHeight = true;
            var fitter = bg.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _text = UIBuilder.Label(_panel, "", 14, TextAnchor.UpperLeft);
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _layout = _text.gameObject.AddComponent<LayoutElement>();
            _panel.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        internal static void Show(TooltipTarget owner, string text)
        {
            var t = s_instance;
            if (t == null || string.IsNullOrEmpty(text)) return;
            t._owner = owner;
            t._text.text = text;
            // Wrap long text at MaxWidth, keep short text tight.
            float preferred = t._text.cachedTextGeneratorForLayout.GetPreferredWidth(text, t._text.GetGenerationSettings(Vector2.zero)) / t._text.pixelsPerUnit;
            t._layout.preferredWidth = Mathf.Min(preferred + 2f, MaxWidth);
            t._panel.gameObject.SetActive(true);
            t._panel.SetAsLastSibling();
            t.Follow();
        }

        internal static void Hide(TooltipTarget owner)
        {
            var t = s_instance;
            if (t == null || (owner != null && t._owner != owner)) return;
            t._owner = null;
            t._panel.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (!_panel.gameObject.activeSelf) return;
            // Owner hidden or destroyed (window closed) without a pointer-exit.
            if (_owner == null || !_owner.isActiveAndEnabled) { Hide(null); return; }
            Follow();
        }

        void Follow()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvas, mouse.position.ReadValue(), null, out var local);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panel);
            var size = _panel.rect.size;
            var half = _canvas.rect.size * 0.5f;
            var p = local + Offset;
            // Keep inside the screen: flip to the left / above the cursor near the edges.
            if (p.x + size.x > half.x) p.x = local.x - Offset.x - size.x;
            if (p.y - size.y < -half.y) p.y = local.y - Offset.y + size.y;
            _panel.anchoredPosition = p;
        }
    }

    /// <summary>Shows a tooltip while the pointer is over this element (needs a raycast-target graphic).</summary>
    public class TooltipTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Func<string> Text;

        public void OnPointerEnter(PointerEventData e) => UITooltip.Show(this, Text?.Invoke());
        public void OnPointerExit(PointerEventData e) => UITooltip.Hide(this);
        void OnDisable() => UITooltip.Hide(this);
    }
}
