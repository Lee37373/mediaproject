using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Session
{
    /// <summary>
    /// Debug camera director (P4-47, user request 2026-09-22): keeps the view on the busiest fight
    /// so tests and <see cref="DebugCommands.SpawnSkirmish"/> can be watched. Every
    /// <see cref="Interval"/> it finds the fighting unit with the most other fighters around it and
    /// follows the centre of that brawl; with no fight it follows <see cref="Subject"/> if set.
    /// Dragging the camera by hand stops the follow as usual; the next update picks it up again.
    /// Debug only: unlike the player's camera it ignores the fog.
    /// </summary>
    public class FightCamera : MonoBehaviour
    {
        const float Interval = 0.3f;
        const float ClusterRadius = 8f;

        Transform _anchor;
        float _timer;

        public static FightCamera Instance { get; private set; }

        /// <summary>Who to show when nobody is fighting (e.g. the test's guild master).</summary>
        public Unit Subject { get; set; }

        public static FightCamera Ensure()
        {
            if (Instance != null) return Instance;
            Instance = new GameObject("FightCamera").AddComponent<FightCamera>();
            return Instance;
        }

        public static void Stop()
        {
            if (Instance == null) return;
            // Lets go of the camera at once: the anchor lives until the frame's end, and the camera went on to the fight
            // it marked — a jump made right after stopping (a test, the debug command) ended up at that fight instead.
            if (Services.TryGet<CameraControl.CameraController>(out var cam) && cam.FollowTarget == Instance._anchor) cam.Follow(null);
            Destroy(Instance.gameObject);
        }

        void Awake() => _anchor = new GameObject("FightCameraAnchor").transform;

        void OnDestroy()
        {
            if (_anchor != null) Destroy(_anchor.gameObject);
            if (Instance == this) Instance = null;
        }

        static bool Fighting(Unit u) => u.IsAlive && u.CurrentTarget != null && u.CurrentTarget.IsAlive;

        void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = Interval;
            if (!Services.TryGet<CameraController>(out var cam)) return;

            Unit best = null;
            int bestCount = 0;
            foreach (var u in UnitRegistry.All)
            {
                if (!Fighting(u)) continue;
                int n = UnitRegistry.CountInRadius(u.Position, ClusterRadius, Fighting);
                if (n > bestCount) { bestCount = n; best = u; }
            }

            Vector2 centre;
            if (best != null)
            {
                centre = Vector2.zero;
                int n = 0;
                foreach (var u in UnitRegistry.All)
                    if (Fighting(u) && (u.Position - best.Position).sqrMagnitude <= ClusterRadius * ClusterRadius) { centre += u.Position; n++; }
                centre /= n;
            }
            else if (Subject != null && Subject.IsAlive) centre = Subject.Position;
            else return;

            _anchor.position = new Vector3(centre.x, centre.y, 0f);
            if (cam.FollowTarget != _anchor) cam.Focus(_anchor);
        }
    }
}
