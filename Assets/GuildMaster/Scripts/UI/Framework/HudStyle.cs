using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The HUD cards' look (2026-11-13, user picked "card + gauges + icons" and asked that it not look AI-made): flat fills
    /// with a thick dark outline like the chore art and the class icons, no gloss or glow. Sprites in Resources/UI/Hud are
    /// 2x art (pixels-per-unit multiplier 2). Without them the cards keep the plain panel look.
    /// </summary>
    public static class HudStyle
    {
        public const float Ppu = 2f;
        public static readonly Color Fore = new(0.96f, 0.94f, 0.89f, 1f);
        public static readonly Color Muted = new(0.72f, 0.68f, 0.78f, 1f);
        public static readonly Color Gold = new(1f, 0.84f, 0.42f, 1f);
        public static readonly Color NoteText = new(0.22f, 0.14f, 0.07f, 1f);
        public static readonly Color NoteUrgent = new(0.66f, 0.18f, 0.08f, 1f);
        public static readonly Color Hp = new(0.91f, 0.30f, 0.33f, 1f);
        public static readonly Color Tired = new(0.45f, 0.67f, 0.93f, 1f);
        public static readonly Color Stress = new(0.68f, 0.45f, 0.87f, 1f);
        public static readonly Color GoodTag = new(0.60f, 0.86f, 0.52f, 1f);
        public static readonly Color BadTag = new(0.97f, 0.56f, 0.50f, 1f);
        public static readonly Color StateTag = new(1f, 0.82f, 0.38f, 1f);
        public static readonly Color TagText = new(0.13f, 0.09f, 0.16f, 1f);

        static readonly Dictionary<string, Sprite> s_cache = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => s_cache.Clear();

        public static Sprite Get(string name)
        {
            if (s_cache.TryGetValue(name, out var s)) return s;
            s = Resources.Load<Sprite>("UI/Hud/" + name);
            s_cache[name] = s;
            return s;
        }

        public static bool Present => Get("hud_card") != null;

        /// <summary>Turns a panel into an outlined HUD card.</summary>
        public static void Card(Image img)
        {
            // 2026-11-13: the pack's stone slab when "Fantasy Wooden GUI" is imported.
            if (img != null && WoodSkin.CardBoard != null)
            {
                img.sprite = WoodSkin.CardBoard;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = WoodSkin.BoardPpu;
                img.color = WoodSkin.BoardTint;
                return;
            }
            Slice(img, "hud_card");
        }

        public static void Slice(Image img, string sprite, Color? tint = null)
        {
            var sp = Get(sprite);
            if (img == null || sp == null) return;
            img.sprite = sp;
            img.type = sp.border == Vector4.zero ? Image.Type.Simple : Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = Ppu;
            img.color = tint ?? Color.white;
        }

        public static Image NewImage(Transform parent, string sprite, string name, Color? tint = null)
        {
            var rt = UIBuilder.Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            Slice(img, sprite, tint);
            if (img.sprite == null) img.color = tint ?? new Color(0f, 0f, 0f, 0f);
            return img;
        }

        /// <summary>Top-left anchored child at (x, -y) of a given size.</summary>
        public static RectTransform Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static Text Label(Transform parent, string text, int size, Color color, FontStyle style = FontStyle.Normal, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var t = UIBuilder.Label(parent, text, size, anchor, color, style);
            t.raycastTarget = false;
            return t;
        }

        /// <summary>A small coloured tag with dark text (trait, state). Its width follows the text.</summary>
        public class Tag
        {
            public RectTransform Root;
            public Image Back;
            public Text Label;

            public static Tag Create(Transform parent)
            {
                var back = NewImage(parent, "hud_tag", "Tag");
                var label = HudStyle.Label(back.transform, "", 13, TagText, FontStyle.Bold, TextAnchor.MiddleCenter);
                label.rectTransform.Stretch(0, 0, 0, 1);
                return new Tag { Root = back.rectTransform, Back = back, Label = label };
            }

            /// <summary>Sets it and returns its width.</summary>
            public float Set(string text, Color color)
            {
                Label.text = text;
                Back.color = color;
                float w = Mathf.Ceil(Label.preferredWidth) + 18f;
                Root.sizeDelta = new Vector2(w, Root.sizeDelta.y);
                return w;
            }
        }

        /// <summary>An outlined gauge with a flat fill and its number beside it.</summary>
        public class Gauge
        {
            public RectTransform Root;
            RectTransform _fill;
            Text _value;

            public static Gauge Create(Transform parent, string icon, Color color, float width)
            {
                var root = UIBuilder.Rect(parent, "Gauge:" + icon);
                var ic = NewImage(root, "hud_icon_" + icon, "Icon");
                Place(ic.rectTransform, 0, 0, 20, 20);
                var back = NewImage(root, "hud_bar_back", "Back");
                Place(back.rectTransform, 26, 3, width, 14);
                if (back.sprite == null) back.color = new Color(0.1f, 0.08f, 0.13f, 1f);
                var fill = NewImage(back.transform, "hud_bar_fill", "Fill", color);
                if (fill.sprite == null) fill.color = color;
                var f = fill.rectTransform;
                f.anchorMin = new Vector2(0f, 0f);
                f.anchorMax = new Vector2(1f, 1f);
                f.offsetMin = new Vector2(2.5f, 2.5f);
                f.offsetMax = new Vector2(-2.5f, -2.5f);
                var value = Label(root, "", 13, Fore, FontStyle.Bold);
                Place(value.rectTransform, 26 + width + 8, 0, 64, 20);
                UISkin.Outline(value);
                return new Gauge { Root = root, _fill = f, _value = value };
            }

            /// <summary>Slides to the new value (2026-11-13, DOTween); <paramref name="snap"/> for a different unit.</summary>
            public void Set(float value, float max, string text, bool snap = false)
            {
                float r = max > 0f ? Mathf.Clamp01(value / max) : 0f;
                UIMotion.Fill(_fill, r, snap);
                _fill.gameObject.SetActive(r > 0.04f); // the sliced fill needs about 9 px
                _value.text = text;
            }
        }
    }
}
