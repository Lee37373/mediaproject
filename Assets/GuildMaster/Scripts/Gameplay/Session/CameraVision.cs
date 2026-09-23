using GuildMaster.Core;
using GuildMaster.Fog;
using UnityEngine;

namespace GuildMaster.Session
{
    /// <summary>
    /// Testing only (user request 2026-09-23): a vision source that sits on the camera, so whatever is
    /// on screen is in view while the rest of the map keeps its fog. It lets a session watch the
    /// simulation — monsters fighting each other, a pack migrating — where no guild member is standing,
    /// without lifting the fog everywhere the way F3 does.
    ///
    /// Nothing creates this on its own: <see cref="DebugCommands.SeeAtCamera"/> does, and it is gone
    /// again when play stops, so a normal run never has it and information hiding is untouched.
    /// </summary>
    public class CameraVision : MonoBehaviour, IVisionSource
    {
        public static CameraVision Instance { get; private set; }

        /// <summary>Sight radius in cells; 0 tracks the camera's own view instead.</summary>
        public float Radius { get; set; }

        Camera _cam;

        public static CameraVision Ensure()
        {
            if (Instance != null) return Instance;
            Instance = new GameObject("CameraVision").AddComponent<CameraVision>();
            return Instance;
        }

        public static void Stop()
        {
            if (Instance != null) Destroy(Instance.gameObject);
        }

        void OnEnable()
        {
            _cam = Services.TryGet<CameraControl.CameraController>(out var c) ? c.Camera : Camera.main;
            FogOfWar.Register(this);
        }

        void OnDisable()
        {
            FogOfWar.Unregister(this);
            if (Instance == this) Instance = null;
        }

        public Vector2 VisionPosition => _cam != null ? (Vector2)_cam.transform.position : Vector2.zero;

        /// <summary>The half-diagonal of the view, so the corners are lit too, unless a radius was given.</summary>
        public float SightRadius
        {
            get
            {
                if (Radius > 0f) return Radius;
                if (_cam == null) return 12f;
                float halfH = _cam.orthographicSize;
                return Mathf.Sqrt(halfH * halfH * (1f + _cam.aspect * _cam.aspect));
            }
        }

        /// <summary>A camera looks down on the map, so walls and buildings do not block it.</summary>
        public bool SeesOverWalls => true;
    }
}
