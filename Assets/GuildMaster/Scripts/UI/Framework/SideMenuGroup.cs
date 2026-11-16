using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// A side-menu button that opens a small column of window buttons beside it (2026-11-12, user: the left side had
    /// too many buttons — fifteen became four plus two of these). The windows inside keep their hotkeys. Picking a
    /// window, the group button again, ESC or a press anywhere else closes the column.
    /// </summary>
    public class SideMenuGroup : MonoBehaviour
    {
        public const float Width = 170f, ButtonHeight = 40f, Spacing = 6f, Padding = 6f, Gap = 6f;
        static readonly Color ColumnBg = new(0.07f, 0.05f, 0.1f, 0.96f);

        static readonly List<SideMenuGroup> s_all = new();
        static readonly Vector3[] s_corners = new Vector3[4];

        RectTransform _layer, _flyout, _buttonRect;
        Button _button;

        /// <summary>Where the windows' buttons go: pass it to <see cref="UIRoot.AddWindow{T}"/> as the menu.</summary>
        public RectTransform Items => _flyout;
        public Button Button => _button;
        public bool IsOpen => _flyout != null && _flyout.gameObject.activeSelf;

        /// <summary>The group button in <paramref name="menu"/>, its column on <paramref name="layer"/> (above the windows).</summary>
        public static SideMenuGroup Create(RectTransform menu, RectTransform layer, string label)
        {
            var group = layer.gameObject.AddComponent<SideMenuGroup>();
            group._layer = layer;
            group._button = UIBuilder.Button(menu, label, group.Toggle, -1, ButtonHeight);
            group._buttonRect = (RectTransform)group._button.transform;
            var panel = UIBuilder.Panel(layer, "MenuGroup:" + label, ColumnBg); // nearly opaque: it opens over the goal panel
            HudStyle.Card(panel); // 2026-11-13: stone slab with the wooden skin
            group._flyout = panel.rectTransform;
            group._flyout.anchorMin = group._flyout.anchorMax = group._flyout.pivot = new Vector2(0f, 1f);
            UIBuilder.VLayout(panel, Spacing, (int)Padding);
            group._flyout.gameObject.SetActive(false);
            group._button.Tooltip(() => string.Join("\n", group.ItemLabels()));
            s_all.Add(group);
            return group;
        }

        IEnumerable<string> ItemLabels() =>
            _flyout.GetComponentsInChildren<Button>(true).Select(b => b.GetComponentInChildren<Text>(true).text);

        /// <summary>After its windows are added: picking one also closes the column.</summary>
        public void Seal()
        {
            foreach (var b in _flyout.GetComponentsInChildren<Button>(true)) b.onClick.AddListener(Close);
        }

        void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            foreach (var g in s_all)
                if (g != this) g.Close();
            int n = _flyout.GetComponentsInChildren<Button>(true).Length;
            _flyout.sizeDelta = new Vector2(Width, n * ButtonHeight + Mathf.Max(0, n - 1) * Spacing + 2f * Padding);
            // Beside the group button, its top level with the button's top.
            _buttonRect.GetWorldCorners(s_corners);
            Vector2 local = _layer.InverseTransformPoint(s_corners[2]); // top right
            var r = _layer.rect;
            var pos = local - new Vector2(r.xMin, r.yMax) + new Vector2(Gap, 0f);
            pos.y = Mathf.Max(pos.y, -r.height + _flyout.sizeDelta.y + 8f); // kept above the bottom edge
            _flyout.anchoredPosition = pos;
            _flyout.gameObject.SetActive(true); // made right after the menu: above the windows, under tooltips and menus
            UIMotion.PopIn(_flyout, 0.85f, 0.18f, fade: false); // 2026-11-13: unfolds from the group button
        }

        public void Close()
        {
            if (_flyout != null && _flyout.gameObject.activeSelf) _flyout.gameObject.SetActive(false);
        }

        /// <summary>ESC: closes an open column; true if there was one.</summary>
        public static bool CloseOpen()
        {
            bool any = false;
            foreach (var g in s_all)
                if (g != null && g.IsOpen) { g.Close(); any = true; }
            return any;
        }

        void Update()
        {
            if (!IsOpen) return;
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            var p = mouse.position.ReadValue();
            if (!RectTransformUtility.RectangleContainsScreenPoint(_flyout, p, null)
                && !RectTransformUtility.RectangleContainsScreenPoint(_buttonRect, p, null)) Close();
        }

        void OnDestroy() => s_all.Remove(this);
    }
}
