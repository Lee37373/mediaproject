using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Fog
{
    /// <summary>
    /// A frozen grey copy of something the guild last saw here before the fog closed over it
    /// (P4-23). It never follows the real object: sprite, facing, size and health bar are the
    /// values captured at that moment. Pooled under <see cref="PoolKey"/>.
    /// </summary>
    public class FogGhost : MonoBehaviour, IPoolable
    {
        public const string PoolKey = "fog.ghost";
        /// <summary>Drawn over the fog overlay (<see cref="FogOfWar.SortingOrder"/>).</summary>
        public const int SortingOrder = FogOfWar.SortingOrder + 10;

        /// <summary>
        /// Multiplied over the captured sprite. The default sprite material cannot desaturate, so
        /// this cold, dim tint stands in for "grey memory" against the explored-fog blue.
        /// </summary>
        static readonly Color GhostTint = new(0.62f, 0.68f, 0.80f, 0.7f);
        const float BarWidth = 0.8f;
        const float BarHeight = 0.09f;

        SpriteRenderer _body;
        Transform _bar;
        Transform _barFill;

        /// <summary>What was seen, e.g. "고블린 전사" or "녹슨 단검 x2".</summary>
        public string Label { get; private set; }
        /// <summary>Health ratio at the sighting, or negative when the subject has no bar.</summary>
        public float HpRatio { get; private set; }
        /// <summary>Game hour (TotalHours) of the sighting.</summary>
        public float SeenAtHour { get; private set; }
        public Vector2 Position => transform.position;
        public bool IsActive => gameObject.activeSelf;

        public static GameObject CreatePooledInstance()
        {
            var go = new GameObject("FogGhost");
            go.AddComponent<FogGhost>().Build();
            return go;
        }

        void Build()
        {
            var body = new GameObject("Body");
            body.transform.SetParent(transform, false);
            _body = body.AddComponent<SpriteRenderer>();
            _body.sortingOrder = SortingOrder;

            _bar = new GameObject("Bar").transform;
            _bar.SetParent(transform, false);
            Bar("Bg", new Color(0f, 0f, 0f, 0.45f), SortingOrder + 1, BarWidth + 0.06f, BarHeight + 0.06f);
            _barFill = Bar("Fill", new Color(0.62f, 0.66f, 0.72f, 0.75f), SortingOrder + 2, BarWidth, BarHeight);
        }

        Transform Bar(string barName, Color c, int order, float w, float h)
        {
            var go = new GameObject(barName);
            go.transform.SetParent(_bar, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.White;
            sr.color = c;
            sr.sortingOrder = order;
            go.transform.localScale = new Vector3(w, h, 1f);
            return go.transform;
        }

        /// <summary>Copies how the subject looked right now; nothing here is read again afterwards.</summary>
        public void Capture(IFogGhostSubject subject, float hour)
        {
            var src = subject.GhostBody;
            Label = subject.GhostLabel;
            HpRatio = subject.GhostHpRatio;
            SeenAtHour = hour;

            transform.position = src.transform.position;
            _body.sprite = src.sprite;
            _body.flipX = src.flipX;
            _body.color = src.color * GhostTint;
            var scale = src.transform.lossyScale;
            _body.transform.localScale = scale;
            // The bar sits above the sprite, at the height the subject's own bar would use.
            float top = src.sprite != null ? src.sprite.bounds.extents.y * Mathf.Abs(scale.y) + 0.12f : 0.5f;
            _bar.localPosition = new Vector3(0f, top, 0f);

            bool showBar = HpRatio >= 0f && HpRatio < 0.999f;
            _bar.gameObject.SetActive(showBar);
            if (showBar)
            {
                float r = Mathf.Clamp01(HpRatio);
                _barFill.localScale = new Vector3(BarWidth * r, BarHeight, 1f);
                _barFill.localPosition = new Vector3(-BarWidth * (1f - r) * 0.5f, 0f, 0f);
            }
        }

        public void OnSpawned() { }

        public void OnDespawned()
        {
            Label = null;
            _body.sprite = null;
        }
    }
}
