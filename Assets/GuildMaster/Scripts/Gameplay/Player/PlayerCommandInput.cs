using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Visuals;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace GuildMaster.Player
{
    /// <summary>Right-click moves the guild master avatar and shows a pooled move marker.</summary>
    public class PlayerCommandInput : MonoBehaviour
    {
        const string MarkerPool = "fx.moveMarker";

        PlayerAvatar _avatar;
        CameraController _camera;
        PoolService _pool;

        public void Init(PlayerAvatar avatar, CameraController cam, PoolService pool)
        {
            _avatar = avatar;
            _camera = cam;
            _pool = pool;
            _pool.Register(MarkerPool, () =>
            {
                var go = new GameObject("MoveMarker");
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = PixelArt.MoveMarker();
                sr.sortingOrder = 50;
                go.AddComponent<MoveMarker>();
                return go;
            }, prewarm: 2);
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || _avatar == null) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (Services.TryGet<MapPicker>(out var picker) && picker.IsPicking) return;

            if (mouse.rightButton.wasPressedThisFrame)
            {
                var target = _camera.MouseWorldPosition();
                if (_avatar.MoveTo(target))
                    _pool.Spawn(MarkerPool, target);
            }
        }

        /// <summary>Shrinking ring that returns itself to the pool.</summary>
        class MoveMarker : MonoBehaviour, IPoolable
        {
            float _t;
            public void OnSpawned() { _t = 0f; transform.localScale = Vector3.one * 1.4f; }
            public void OnDespawned() { }

            void Update()
            {
                _t += Time.unscaledDeltaTime;
                transform.localScale = Vector3.one * Mathf.Lerp(1.4f, 0.4f, _t / 0.45f);
                if (_t >= 0.45f && Services.TryGet<PoolService>(out var pool)) pool.Despawn(gameObject);
            }
        }
    }
}
