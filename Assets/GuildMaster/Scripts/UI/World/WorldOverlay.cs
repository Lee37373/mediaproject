using UnityEngine;

namespace GuildMaster.UI
{
    /// <summary>Converts world positions to anchored positions on a screen-space overlay rect.</summary>
    public static class WorldOverlay
    {
        public static bool TryWorldToLocal(RectTransform layer, Vector3 world, out Vector2 local)
        {
            local = default;
            var cam = Camera.main;
            if (cam == null) return false;
            var screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f || screen.x < -50 || screen.y < -50 || screen.x > Screen.width + 50 || screen.y > Screen.height + 50)
                return false;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, screen, null, out local);
        }
    }
}
