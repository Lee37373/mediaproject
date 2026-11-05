using GuildMaster.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace GuildMaster.CameraControl
{
    /// <summary>
    /// Top-down RTS camera: WASD/arrow and edge panning, left/middle-drag panning,
    /// cursor-anchored wheel zoom, map bounds clamp, follow target, and trauma-based shake.
    /// A left press that moves less than <see cref="ClickDragThreshold"/> stays a click:
    /// map-click consumers should use <see cref="MapClickReleased"/> instead of raw mouse state.
    /// Uses unscaled time so it works while the game is paused.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraController : MonoBehaviour
    {
        [SerializeField] float panSpeed = 22f;
        [SerializeField] float minZoom = 4f;
        [SerializeField] float maxZoom = 34f;
        [SerializeField] float zoomStep = 0.12f;
        [SerializeField] float zoomSmoothing = 12f;
        [SerializeField] bool edgePan = false;
        [SerializeField] float edgeSize = 12f;

        Camera _cam;
        Bounds _bounds;
        bool _hasBounds;
        float _targetZoom;
        Vector3 _basePosition;
        const float ClickDragThreshold = 6f;
        const float FocusZoom = 9f;

        Vector3 _dragOriginWorld;
        bool _dragging;
        Vector2 _leftPressScreen;
        bool _leftPressOnMap;
        bool _leftDragged;
        Transform _follow;
        bool _zoomAtCursor;
        float _trauma;
        float _shakeTime;

        public Camera Camera => _cam;
        public Transform FollowTarget => _follow;
        /// <summary>Real time the player last moved the camera by hand (keys or drag) — the spectator director waits after it (P4-129).</summary>
        public float LastManualInput { get; private set; } = -999f;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            _cam.orthographic = true;
            _targetZoom = _cam.orthographicSize;
            _basePosition = transform.position;
        }

        public void Configure(Bounds bounds, float min, float max, float speed)
        {
            _bounds = bounds;
            _hasBounds = true;
            minZoom = min;
            maxZoom = max;
            panSpeed = speed;
            _targetZoom = Mathf.Clamp(_cam.orthographicSize, minZoom, maxZoom);
        }

        public void JumpTo(Vector2 pos, bool byPlayer = true)
        {
            _basePosition = new Vector3(pos.x, pos.y, transform.position.z);
            if (byPlayer) LastManualInput = Time.unscaledTime; // the minimap, a notification: the player chose where to look
            Clamp();
        }

        public void Follow(Transform target) => _follow = target;

        static bool IsHiddenByFog(Units.Unit u)
            => u.Faction != Units.Faction.Guild && Services.TryGet<Fog.FogOfWar>(out var fog) && !fog.IsVisible(u.Position);

        /// <summary>Follows a target and zooms in if the view is wide.</summary>
        public void Focus(Transform target)
        {
            _follow = target;
            if (target != null && _targetZoom > FocusZoom) SetZoom(FocusZoom);
        }

        /// <summary>
        /// True on the frame the left button is released after a press that started on the map
        /// (not over UI) and did not turn into a drag-pan.
        /// </summary>
        public bool MapClickReleased
        {
            get
            {
                var mouse = Mouse.current;
                if (mouse == null || !mouse.leftButton.wasReleasedThisFrame || !_leftPressOnMap) return false;
                return !_leftDragged && (mouse.position.ReadValue() - _leftPressScreen).magnitude < ClickDragThreshold;
            }
        }

        public void SetZoom(float size)
        {
            _targetZoom = Mathf.Clamp(size, minZoom, maxZoom);
            _zoomAtCursor = false;
        }

        /// <summary>Adds screen shake (0..1). Shake strength is trauma squared.</summary>
        public void Shake(float trauma) => _trauma = Mathf.Clamp01(_trauma + trauma);

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool pointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            bool typing = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null
                          && EventSystem.current.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>() != null;

            // Keyboard / edge pan.
            Vector2 move = Vector2.zero;
            if (kb != null && !typing)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1;
            }
            if (edgePan && mouse != null && Application.isFocused)
            {
                var mp = mouse.position.ReadValue();
                if (mp.x < edgeSize) move.x -= 1;
                if (mp.x > Screen.width - edgeSize) move.x += 1;
                if (mp.y < edgeSize) move.y -= 1;
                if (mp.y > Screen.height - edgeSize) move.y += 1;
            }
            if (move != Vector2.zero)
            {
                _follow = null;
                LastManualInput = Time.unscaledTime;
                float zoomFactor = _cam.orthographicSize / 10f;
                _basePosition += (Vector3)(move.normalized * panSpeed * zoomFactor * dt);
            }

            if (mouse != null)
            {
                // Left drag pan (short presses remain clicks).
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    _leftPressScreen = mouse.position.ReadValue();
                    _leftPressOnMap = !pointerOverUI;
                    _leftDragged = false;
                    _dragOriginWorld = ScreenToWorld(_leftPressScreen);
                }
                if (mouse.leftButton.isPressed && _leftPressOnMap)
                {
                    var screen = mouse.position.ReadValue();
                    if (!_leftDragged && (screen - _leftPressScreen).magnitude >= ClickDragThreshold)
                    {
                        _leftDragged = true;
                        LastManualInput = Time.unscaledTime;
                        _follow = null;
                    }
                    if (_leftDragged)
                    {
                        _basePosition += _dragOriginWorld - ScreenToWorld(screen);
                        LastManualInput = Time.unscaledTime;
                    }
                }

                // Middle-mouse drag pan.
                if (mouse.middleButton.wasPressedThisFrame && !pointerOverUI)
                {
                    _dragging = true;
                    LastManualInput = Time.unscaledTime;
                    _follow = null;
                    _dragOriginWorld = ScreenToWorld(mouse.position.ReadValue());
                }
                if (!mouse.middleButton.isPressed) _dragging = false;
                if (_dragging)
                {
                    var now = ScreenToWorld(mouse.position.ReadValue());
                    _basePosition += _dragOriginWorld - now;
                    LastManualInput = Time.unscaledTime;
                }

                // Zoom toward cursor.
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f && !pointerOverUI)
                {
                    _targetZoom = Mathf.Clamp(_targetZoom * (1f - Mathf.Sign(scroll) * zoomStep), minZoom, maxZoom);
                    _zoomAtCursor = true;
                }
            }

            float prevZoom = _cam.orthographicSize;
            float newZoom = Mathf.Lerp(prevZoom, _targetZoom, 1f - Mathf.Exp(-zoomSmoothing * dt));
            if (Mathf.Abs(newZoom - prevZoom) > 0.0001f)
            {
                if (_zoomAtCursor && mouse != null && !pointerOverUI && _follow == null)
                {
                    var before = ScreenToWorld(mouse.position.ReadValue());
                    _cam.orthographicSize = newZoom;
                    var after = ScreenToWorld(mouse.position.ReadValue());
                    _basePosition += before - after;
                }
                else _cam.orthographicSize = newZoom;
            }

            // Drop dead or pooled-away targets (pooled units get reused for other monsters), and
            // non-guild units that walked into the fog (the camera must not track the unseen).
            if (_follow && (!_follow.gameObject.activeInHierarchy
                            || (_follow.TryGetComponent<Units.Unit>(out var followed) && (!followed.IsAlive || IsHiddenByFog(followed)))))
                _follow = null;

            if (_follow)
            {
                var target = new Vector3(_follow.position.x, _follow.position.y, _basePosition.z);
                _basePosition = Vector3.Lerp(_basePosition, target, 1f - Mathf.Exp(-8f * dt));
            }

            Clamp();

            // Shake.
            Vector3 offset = Vector3.zero;
            if (_trauma > 0f)
            {
                _shakeTime += dt * 25f;
                float s = _trauma * _trauma * 0.6f;
                offset = new Vector3(
                    (Mathf.PerlinNoise(_shakeTime, 0.1f) - 0.5f) * 2f * s,
                    (Mathf.PerlinNoise(0.1f, _shakeTime) - 0.5f) * 2f * s, 0f);
                _trauma = Mathf.Max(0f, _trauma - dt * 1.5f);
            }
            transform.position = _basePosition + offset;
        }

        /// <summary>
        /// Smaller bounds where the camera looks at a place of its own (P4-85: a ruin's dungeon in the underground band),
        /// or null for the map's bounds.
        /// </summary>
        public System.Func<Vector2, Bounds?> AreaBounds { get; set; }

        void Clamp()
        {
            if (!_hasBounds) return;
            var bounds = AreaBounds?.Invoke(_basePosition) ?? _bounds;
            float halfH = _cam.orthographicSize;
            float halfW = halfH * _cam.aspect;
            var min = bounds.min;
            var max = bounds.max;
            float x = halfW * 2 >= bounds.size.x ? bounds.center.x : Mathf.Clamp(_basePosition.x, min.x + halfW, max.x - halfW);
            float y = halfH * 2 >= bounds.size.y ? bounds.center.y : Mathf.Clamp(_basePosition.y, min.y + halfH, max.y - halfH);
            _basePosition = new Vector3(x, y, _basePosition.z);
        }

        Vector3 ScreenToWorld(Vector2 screen)
        {
            var p = _cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_cam.transform.position.z));
            p.z = 0f;
            return p - (transform.position - _basePosition); // ignore shake offset
        }

        public Vector2 MouseWorldPosition()
        {
            var mouse = Mouse.current;
            if (mouse == null) return Vector2.zero;
            var p = _cam.ScreenToWorldPoint(mouse.position.ReadValue());
            return new Vector2(p.x, p.y);
        }
    }
}
