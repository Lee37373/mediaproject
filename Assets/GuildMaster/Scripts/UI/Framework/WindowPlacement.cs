using System.Globalization;
using UnityEngine;

namespace GuildMaster.UI
{
    /// <summary>
    /// Where the player dragged each window (P6-2, user decision 2026-11-10: kept across runs). One PlayerPrefs
    /// string per window type, in canvas units of the centre-anchored frame. Only drags are written: a window that
    /// stepped aside from another when it opened goes back to its own spot next time.
    /// </summary>
    public static class WindowPlacement
    {
        /// <summary>Px of a window that must stay inside the screen on each side when dragged or reopened.</summary>
        public const float KeepVisible = 60f;

        /// <summary>Tests point this elsewhere so they never touch the player's own layout.</summary>
        public static string KeyPrefix = "GuildMaster.Window.";

        public static bool TryGet(string id, out Vector2 pos)
        {
            pos = default;
            return PlayerPrefs.HasKey(KeyPrefix + id) && TryParse(PlayerPrefs.GetString(KeyPrefix + id), out pos);
        }

        public static void Set(string id, Vector2 pos)
        {
            PlayerPrefs.SetString(KeyPrefix + id, Format(pos));
            PlayerPrefs.Save();
        }

        public static void Forget(string id) => PlayerPrefs.DeleteKey(KeyPrefix + id);

        public static string Format(Vector2 pos) =>
            pos.x.ToString("0.#", CultureInfo.InvariantCulture) + ";" + pos.y.ToString("0.#", CultureInfo.InvariantCulture);

        public static bool TryParse(string text, out Vector2 pos)
        {
            pos = default;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Split(';');
            if (parts.Length != 2
                || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                || float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y)) return false;
            pos = new Vector2(x, y);
            return true;
        }

        /// <summary>
        /// Keeps a centre-anchored frame reachable inside its parent: <see cref="KeepVisible"/> px on screen left and
        /// right, the title bar (top edge) never above the top and at least that much above the bottom. A spot saved at
        /// another resolution (or UI size) comes back on screen this way.
        /// </summary>
        public static Vector2 Clamp(Vector2 pos, Vector2 parentSize, Vector2 frameSize)
        {
            var half = parentSize * 0.5f;
            var size = frameSize * 0.5f;
            pos.x = Mathf.Clamp(pos.x, -half.x - size.x + KeepVisible, half.x + size.x - KeepVisible);
            float top = half.y - size.y, bottom = -half.y + KeepVisible - size.y;
            pos.y = bottom > top ? top : Mathf.Clamp(pos.y, bottom, top);
            return pos;
        }
    }
}
