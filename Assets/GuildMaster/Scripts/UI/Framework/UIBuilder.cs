using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Code-first uGUI construction helpers. Layout is driven by LayoutGroups so views only
    /// describe structure. All widgets use the Korean-capable UIFonts.Main font.
    /// </summary>
    public static class UIBuilder
    {
        static Sprite s_rounded;
        static Sprite s_arrow;
        static Sprite s_check;

        /// <summary>White 9-sliced rounded rectangle (tinted by Image.color).</summary>
        public static Sprite Rounded
        {
            get
            {
                if (s_rounded != null) return s_rounded;
                const int n = 16, r = 4;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, n - r), cy = Mathf.Clamp(y + 0.5f, r, n - r);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(r - d + 0.5f));
                }
                tex.SetPixels32(px);
                tex.Apply();
                s_rounded = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(r + 1, r + 1, r + 1, r + 1));
                return s_rounded;
            }
        }

        static Sprite Arrow
        {
            get
            {
                if (s_arrow != null) return s_arrow;
                var rows = new[] { "ooooooo", ".ooooo.", "..ooo..", "...o..." };
                s_arrow = Core.SpriteFactory.FromTemplate("ui.arrow", rows, new Dictionary<char, Color> { ['o'] = Color.white }, new Vector2(0.5f, 0.5f));
                return s_arrow;
            }
        }

        static Sprite Check
        {
            get
            {
                if (s_check != null) return s_check;
                s_check = Core.SpriteFactory.Circle(8, Color.white, Color.white);
                return s_check;
            }
        }

        // ---------------- Roots ----------------

        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image Panel(Transform parent, string name, Color color, bool raycast = true)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Rounded;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        // ---------------- Anchoring ----------------

        public static RectTransform Anchor(this RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        // ---------------- Layout ----------------

        public static VerticalLayoutGroup VLayout(Component c, float spacing = 6, int padding = 8, bool expandWidth = true, bool controlHeight = true)
        {
            var g = c.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(padding, padding, padding, padding);
            g.childControlWidth = true;
            g.childControlHeight = controlHeight;
            g.childForceExpandWidth = expandWidth;
            g.childForceExpandHeight = false;
            return g;
        }

        public static HorizontalLayoutGroup HLayout(Component c, float spacing = 6, int padding = 0, bool expandWidth = false)
        {
            var g = c.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(padding, padding, padding, padding);
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = expandWidth;
            g.childForceExpandHeight = true;
            g.childAlignment = TextAnchor.MiddleLeft;
            return g;
        }

        public static T Size<T>(this T c, float width = -1, float height = -1, float flexWidth = -1, float flexHeight = -1) where T : Component
        {
            if (!c.TryGetComponent<LayoutElement>(out var le)) le = c.gameObject.AddComponent<LayoutElement>();
            if (width >= 0) { le.preferredWidth = width; le.minWidth = width; }
            if (height >= 0) { le.preferredHeight = height; le.minHeight = height; }
            if (flexWidth >= 0) le.flexibleWidth = flexWidth;
            if (flexHeight >= 0) le.flexibleHeight = flexHeight;
            return c;
        }

        public static RectTransform Row(Transform parent, float height = 32, float spacing = 6)
        {
            var rt = Rect(parent, "Row");
            HLayout(rt, spacing);
            // Explicit 0 flex: a layout group with childForceExpand would otherwise report itself as flexible.
            rt.Size(-1, height, -1, 0);
            return rt;
        }

        public static void FitContent(Component c, bool vertical = true, bool horizontal = false)
        {
            var f = c.gameObject.AddComponent<ContentSizeFitter>();
            f.verticalFit = vertical ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            f.horizontalFit = horizontal ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
        }

        // ---------------- Widgets ----------------

        public static Text Label(Transform parent, string text, int size = 16, TextAnchor align = TextAnchor.MiddleLeft, Color? color = null, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(parent, "Label");
            var t = rt.gameObject.AddComponent<Text>();
            t.font = UIFonts.Main;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color ?? UITheme.Text;
            t.fontStyle = style;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        public static Button Button(Transform parent, string label, UnityAction onClick, float width = -1, float height = 32, Color? color = null, int fontSize = 16)
        {
            var img = Panel(parent, $"Button:{label}", Color.white);
            var btn = img.gameObject.AddComponent<Button>();
            var baseColor = color ?? UITheme.Button;
            var colors = btn.colors;
            colors.normalColor = baseColor;
            colors.highlightedColor = Color.Lerp(baseColor, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(baseColor, Color.black, 0.25f);
            colors.selectedColor = baseColor;
            colors.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.6f);
            btn.colors = colors;
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(onClick);
            var text = Label(img.transform, label, fontSize, TextAnchor.MiddleCenter);
            text.rectTransform.Stretch(4, 4, 0, 0);
            img.Size(width, height);
            return btn;
        }

        public static void SetLabel(this Button b, string label) => b.GetComponentInChildren<Text>().text = label;

        public static InputField Input(Transform parent, string placeholder, InputField.ContentType type = InputField.ContentType.Standard, float width = -1, float height = 30)
        {
            var img = Panel(parent, "Input", UITheme.InputBg);
            var input = img.gameObject.AddComponent<InputField>();
            var text = Label(img.transform, "", 16);
            text.rectTransform.Stretch(8, 8, 2, 2);
            text.supportRichText = false;
            var ph = Label(img.transform, placeholder, 16, TextAnchor.MiddleLeft, UITheme.TextMuted, FontStyle.Italic);
            ph.rectTransform.Stretch(8, 8, 2, 2);
            input.textComponent = text;
            input.placeholder = ph;
            input.contentType = type;
            input.targetGraphic = img;
            img.Size(width, height, width < 0 ? 1 : 0);
            return input;
        }

        public static Dropdown Dropdown(Transform parent, IList<string> options, UnityAction<int> onChanged, float width = -1, float height = 30)
        {
            var resources = new DefaultControls.Resources
            {
                standard = Rounded, background = Rounded, inputField = Rounded, knob = Rounded,
                dropdown = Arrow, checkmark = Check, mask = Rounded,
            };
            var go = DefaultControls.CreateDropdown(resources);
            go.transform.SetParent(parent, false);
            var dd = go.GetComponent<Dropdown>();
            ApplyFont(go.transform);
            go.GetComponent<Image>().color = UITheme.InputBg;
            foreach (var img in go.GetComponentsInChildren<Image>(true))
            {
                if (img.gameObject == go) continue;
                if (img.name == "Template" || img.name == "Viewport") img.color = UITheme.PanelBgLight;
                else if (img.name == "Item Background") img.color = UITheme.RowBg;
                else if (img.name == "Arrow") img.color = UITheme.Text;
            }
            var template = dd.template;
            template.sizeDelta = new Vector2(template.sizeDelta.x, 220);
            // Item height a bit taller for Korean glyphs.
            var item = template.GetComponentInChildren<Toggle>(true).GetComponent<RectTransform>();
            item.sizeDelta = new Vector2(item.sizeDelta.x, 26);
            dd.ClearOptions();
            dd.AddOptions(new List<string>(options));
            if (onChanged != null) dd.onValueChanged.AddListener(onChanged);
            ((RectTransform)go.transform).Size(width, height, width < 0 ? 1 : 0);
            return dd;
        }

        public static Slider Slider(Transform parent, float min, float max, float value, UnityAction<float> onChanged, float width = -1)
        {
            var resources = new DefaultControls.Resources { standard = Rounded, background = Rounded, knob = Rounded };
            var go = DefaultControls.CreateSlider(resources);
            go.transform.SetParent(parent, false);
            var s = go.GetComponent<Slider>();
            s.minValue = min;
            s.maxValue = max;
            s.value = value;
            foreach (var img in go.GetComponentsInChildren<Image>(true))
                img.color = img.name switch { "Fill" => UITheme.Accent, "Handle" => UITheme.Text, _ => UITheme.InputBg };
            if (onChanged != null) s.onValueChanged.AddListener(onChanged);
            ((RectTransform)go.transform).Size(width, 20, width < 0 ? 1 : 0);
            return s;
        }

        /// <summary>Vertical scroll list; returns the content transform (add rows to it).</summary>
        public static RectTransform ScrollList(Transform parent, out ScrollRect scroll, float spacing = 4)
        {
            var root = Panel(parent, "Scroll", new Color(0, 0, 0, 0.25f));
            scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = Rect(root.transform, "Viewport");
            viewport.Stretch(2, 2, 2, 2);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect(viewport, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            var v = VLayout(content, spacing, 4);
            v.childControlHeight = true;
            FitContent(content);
            scroll.viewport = viewport;
            scroll.content = content;
            return content;
        }

        public static void ApplyFont(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                t.font = UIFonts.Main;
                t.fontSize = Mathf.Max(t.fontSize, 15);
                t.color = UITheme.Text;
            }
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        public static string Colored(string s, Color c) => $"<color=#{UITheme.Hex(c)}>{s}</color>";

        /// <summary>Hover tooltip for this element (text read on hover, so it can change). See UITooltip.</summary>
        public static T Tooltip<T>(this T c, Func<string> text) where T : Component
        {
            // No '??' here: a missing Unity component is a fake null in the editor.
            var target = c.GetComponent<TooltipTarget>();
            if (target == null) target = c.gameObject.AddComponent<TooltipTarget>();
            target.Text = text;
            var graphic = c.GetComponent<Graphic>();
            if (graphic != null) graphic.raycastTarget = true;
            return c;
        }
    }
}
