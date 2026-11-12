using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Rounded chips as wide as their text, and thin fill bars (KMS UI round 2026-11-05 — first in the event card window,
    /// shared since the P4-135 merge folded that window into the decision cards: the roster's party board and the
    /// decision card window use them).
    /// </summary>
    public static class UIChips
    {
        public static readonly Color Card = new(0.17f, 0.15f, 0.21f, 1f);
        public static readonly Color CardEdge = new(0.30f, 0.26f, 0.40f, 1f);
        public static readonly Color Good = new(0.14f, 0.25f, 0.16f, 1f), GoodText = new(0.61f, 0.90f, 0.61f, 1f);
        public static readonly Color Bad = new(0.29f, 0.14f, 0.19f, 1f), BadText = new(1f, 0.70f, 0.67f, 1f);
        public static readonly Color Mid = new(0.29f, 0.23f, 0.12f, 1f), MidText = new(0.98f, 0.82f, 0.54f, 1f);
        public static readonly Color Track = new(0.23f, 0.20f, 0.28f, 1f);

        /// <summary>A rounded chip as wide as its text.</summary>
        public static (Image bg, Text text) Chip(Transform parent, int fontSize)
        {
            var bg = UIBuilder.Panel(parent, "Chip", Card, raycast: false);
            var h = UIBuilder.HLayout(bg, 0, 0);
            h.padding = new RectOffset(8, 8, 2, 2);
            h.childForceExpandWidth = false;
            var t = UIBuilder.Label(bg.transform, "", fontSize, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return (bg, t);
        }

        public static void SetChip((Image bg, Text text) chip, string text, Color bg, Color fg)
        {
            chip.bg.color = bg;
            chip.text.text = text;
            chip.text.color = fg;
        }

        /// <summary>Green for good for the guild (sign &gt; 0), red for bad, amber for neither.</summary>
        public static void SetSignChip((Image bg, Text text) chip, string text, int sign)
        {
            if (sign > 0) SetChip(chip, text, Good, GoodText);
            else if (sign < 0) SetChip(chip, text, Bad, BadText);
            else SetChip(chip, text, Mid, MidText);
        }

        /// <summary>The fill of a bar laid over <paramref name="track"/>; size it with <see cref="SetFill"/>.</summary>
        public static Image Fill(RectTransform track, Color color)
        {
            var fill = UIBuilder.Panel(track, "Fill", color, raycast: false);
            var rt = fill.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return fill;
        }

        public static void SetFill(Image fill, float ratio)
        {
            var rt = fill.rectTransform;
            rt.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
            rt.offsetMax = Vector2.zero;
        }
    }
}
