using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Cartoon UI skin (2026-10-31): sprites in Resources/UI/Skin, drawn for the game (outlined, glossy, 2x art; import
    /// borders set by UiSkinImport). <see cref="UIBuilder"/> and <see cref="UIPanel"/> call in here, so every window and
    /// button picks it up without changes to the views. With the folder empty everything keeps the old flat look.
    /// - Windows: purple frame with rivets, transparent title bar, round close button.
    /// - Buttons: one of six coloured sprites, chosen by the colour the view asked for (green stays green, danger red).
    /// - Theme-coloured panels (window/row/header/input backgrounds): a white outlined sprite tinted by that colour.
    ///   Other panels (bars, fills, overlays) are left alone.
    /// </summary>
    public static class UISkin
    {
        /// <summary>Sprites are 2x art; these shrink their borders to the 1920x1080 canvas.</summary>
        const float WindowPpu = 2f, PanelPpu = 2f, ButtonPpu = 3f;
        static readonly Color Ink = new(0.16f, 0.12f, 0.2f, 0.9f);

        static Dictionary<string, Sprite> s_sprites;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => s_sprites = null;

        static Dictionary<string, Sprite> Sprites
        {
            get
            {
                if (s_sprites != null) return s_sprites;
                s_sprites = new Dictionary<string, Sprite>();
                foreach (var sp in Resources.LoadAll<Sprite>("UI/Skin")) s_sprites[sp.name] = sp;
                return s_sprites;
            }
        }

        public static Sprite Get(string name) => Sprites.TryGetValue(name, out var s) ? s : null;

        public static Sprite Icon(string name) => Get("icon_" + name);

        static bool IsThemePanel(Color c) =>
            c == UITheme.PanelBg || c == UITheme.PanelBgLight || c == UITheme.RowBg || c == UITheme.RowBgAlt
            || c == UITheme.Header || c == UITheme.InputBg;

        /// <summary>Outlined, softly shaded panel in the colour asked for (theme colours only).</summary>
        public static void Panel(Image img)
        {
            if (!IsThemePanel(img.color)) return;
            var sp = Get("panel_tint");
            if (sp == null) return;
            img.sprite = sp;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = PanelPpu;
        }

        /// <summary>The button sprite nearest a colour: green, blue, red, yellow, purple or gray.</summary>
        public static string ButtonStyle(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s < 0.18f || v < 0.12f) return "gray";
            float deg = h * 360f;
            return deg < 18f || deg >= 330f ? "red"
                 : deg < 70f ? "yellow"
                 : deg < 170f ? "green"
                 : deg < 245f ? "blue"
                 : "purple";
        }

        public static void Button(Button button, Image img, Color baseColor, Text label)
        {
            var sp = Get("button_" + ButtonStyle(baseColor));
            if (sp == null) return;
            img.sprite = sp;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = ButtonPpu;
            img.color = Color.white;
            var colors = button.colors;
            colors.normalColor = new Color(0.94f, 0.94f, 0.94f, 1f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.58f, 0.75f);
            button.colors = colors;
            if (label != null)
            {
                var rt = label.rectTransform;
                rt.offsetMin = new Vector2(rt.offsetMin.x, 3f); // sit above the button's lip
                Outline(label);
            }
        }

        /// <summary>Dark outline for text over the skin (cartoon look, readable on any button colour).</summary>
        public static void Outline(Text t)
        {
            if (t == null || Get("panel_tint") == null || t.GetComponent<Outline>() != null) return;
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = Ink;
            o.effectDistance = new Vector2(1.2f, -1.2f);
        }

        /// <summary>Window frame, see-through title bar (still drags), round close button.</summary>
        public static void Window(Image frame, Image header, Button close, Text title)
        {
            var sp = Get("panel_window_purple");
            if (sp == null) return;
            frame.sprite = sp;
            frame.type = Image.Type.Sliced;
            frame.pixelsPerUnitMultiplier = WindowPpu;
            frame.color = Color.white;
            if (header != null) header.color = new Color(0f, 0f, 0f, 0f);
            Outline(title);
            var round = Get("button_close");
            if (close == null || round == null) return;
            var img = (Image)close.targetGraphic;
            img.sprite = round;
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.color = Color.white;
            var colors = close.colors;
            colors.normalColor = new Color(0.94f, 0.94f, 0.94f, 1f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.selectedColor = colors.normalColor;
            close.colors = colors;
            foreach (var t in close.GetComponentsInChildren<Text>(true)) t.enabled = false;
            ((RectTransform)close.transform).sizeDelta = new Vector2(34, 34);
        }

        /// <summary>A small icon in a layout row (HUD); nothing without the skin.</summary>
        public static Image LayoutIcon(Transform parent, string icon, float size = 28)
        {
            var sp = Icon(icon);
            if (sp == null) return null;
            var rt = UIBuilder.Rect(parent, "Icon:" + icon);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sp;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.Size(size, size);
            return img;
        }
    }
}
