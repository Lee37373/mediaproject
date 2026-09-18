using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Visuals
{
    /// <summary>
    /// Sprite presentation for moving characters: shadow, facing flip, walk bob,
    /// hit flash, and Y-based sorting so lower units draw in front.
    /// </summary>
    public class CharacterVisual : MonoBehaviour
    {
        public const int BaseSortingOrder = 2000;

        SpriteRenderer _body;
        SpriteRenderer _shadow;
        Transform _bodyT;
        PathFollower _mover;
        float _bobPhase;
        float _flashTimer;
        Color _tint = Color.white;
        bool _facingLeftOverride;
        float _faceOverrideTimer;

        public SpriteRenderer Body => _body;

        public void Init(Sprite sprite, float scale = 1f)
        {
            if (_body == null)
            {
                var shadowGo = new GameObject("Shadow");
                shadowGo.transform.SetParent(transform, false);
                _shadow = shadowGo.AddComponent<SpriteRenderer>();
                _shadow.sprite = PixelArt.Shadow();
                shadowGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
                shadowGo.transform.localScale = new Vector3(1f, 0.45f, 1f);

                var bodyGo = new GameObject("Body");
                bodyGo.transform.SetParent(transform, false);
                _bodyT = bodyGo.transform;
                _body = bodyGo.AddComponent<SpriteRenderer>();
            }
            _body.sprite = sprite;
            _bodyT.localScale = Vector3.one * scale;
            _shadow.transform.localScale = new Vector3(scale, 0.45f * scale, 1f);
            _mover = GetComponent<PathFollower>();
            // Anchor feet slightly below the logical position (cell center).
            _bodyT.localPosition = new Vector3(0f, -0.3f, 0f);
            _shadow.transform.localPosition = new Vector3(0f, -0.28f, 0f);
            SetTint(Color.white);
        }

        public void SetTint(Color c)
        {
            _tint = c;
            if (_body) _body.color = c;
        }

        public void Flash(float duration = 0.1f) => _flashTimer = duration;

        /// <summary>Face toward a world x position for a moment (e.g. when attacking).</summary>
        public void FaceTowards(Vector2 target)
        {
            _facingLeftOverride = target.x < transform.position.x;
            _faceOverrideTimer = 0.4f;
        }

        void LateUpdate()
        {
            if (_body == null) return;
            bool moving = _mover != null && _mover.IsMoving;
            bool left = _faceOverrideTimer > 0f ? _facingLeftOverride : _mover != null && _mover.FacingLeft;
            _faceOverrideTimer -= Time.deltaTime;
            _body.flipX = left;

            if (moving) _bobPhase += Time.deltaTime * 14f;
            else _bobPhase = Mathf.MoveTowards(_bobPhase, 0f, Time.deltaTime * 10f);
            float bob = moving ? Mathf.Abs(Mathf.Sin(_bobPhase)) * 0.08f : 0f;
            _bodyT.localPosition = new Vector3(0f, -0.3f + bob, 0f);

            int order = BaseSortingOrder - Mathf.RoundToInt(transform.position.y * 8f);
            _body.sortingOrder = order;
            _shadow.sortingOrder = order - 1;

            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                _body.color = _flashTimer > 0f ? new Color(1f, 0.35f, 0.35f, _tint.a) : _tint;
            }
        }
    }
}
