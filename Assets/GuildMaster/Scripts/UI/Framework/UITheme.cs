using UnityEngine;

namespace GuildMaster.UI
{
    public static class UITheme
    {
        public static readonly Color PanelBg = new(0.10f, 0.09f, 0.13f, 0.94f);
        public static readonly Color PanelBgLight = new(0.17f, 0.15f, 0.22f, 0.96f);
        public static readonly Color RowBg = new(0.20f, 0.18f, 0.26f, 0.9f);
        public static readonly Color RowBgAlt = new(0.16f, 0.14f, 0.21f, 0.9f);
        public static readonly Color Header = new(0.28f, 0.22f, 0.40f, 1f);
        public static readonly Color Accent = new(0.96f, 0.78f, 0.30f, 1f);
        public static readonly Color Text = new(0.94f, 0.92f, 0.87f, 1f);
        public static readonly Color TextMuted = new(0.68f, 0.65f, 0.72f, 1f);
        public static readonly Color Good = new(0.45f, 0.85f, 0.45f, 1f);
        public static readonly Color Bad = new(0.95f, 0.40f, 0.35f, 1f);
        public static readonly Color Warn = new(0.98f, 0.72f, 0.30f, 1f);
        public static readonly Color Exp = new(0.55f, 0.75f, 1f, 1f);
        public static readonly Color Button = new(0.30f, 0.25f, 0.42f, 1f);
        public static readonly Color ButtonHover = new(0.40f, 0.34f, 0.56f, 1f);
        public static readonly Color ButtonPressed = new(0.22f, 0.18f, 0.32f, 1f);
        public static readonly Color ButtonDanger = new(0.55f, 0.22f, 0.22f, 1f);
        public static readonly Color InputBg = new(0.06f, 0.05f, 0.08f, 1f);

        public static Color Danger(Data.DangerLevel d) => d switch
        {
            Data.DangerLevel.Low => Good,
            Data.DangerLevel.Medium => Warn,
            _ => Bad,
        };

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
    }
}
