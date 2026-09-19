using UnityEngine;

namespace GuildMaster.Fog
{
    /// <summary>Reveals fog around this transform (player, adventurers, buildings).</summary>
    public class VisionSource : MonoBehaviour, IVisionSource
    {
        [SerializeField] float sightRadius = 6f;

        public float SightRadius { get => sightRadius; set => sightRadius = value; }
        public Vector2 VisionPosition => transform.position;
        /// <summary>Town watch only: sees over walls and buildings.</summary>
        public bool SeesOverWalls { get; set; }

        void OnEnable() => FogOfWar.Register(this);
        void OnDisable() => FogOfWar.Unregister(this);
    }

    /// <summary>Hides this object's renderers/canvases while its cell is not currently visible.</summary>
    public class FogHideable : MonoBehaviour, IFogHideable
    {
        Renderer[] _renderers;
        Canvas[] _canvases;
        bool _visible = true;

        public Vector2 FogPosition => transform.position;
        public bool IsFogVisible => _visible;

        void Awake() => Refresh();

        /// <summary>Re-collects child renderers (call after adding visuals at runtime).</summary>
        public void Refresh()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _canvases = GetComponentsInChildren<Canvas>(true);
            Apply();
        }

        void OnEnable() => FogOfWar.Register(this);
        void OnDisable() => FogOfWar.Unregister(this);

        public void SetFogVisible(bool visible)
        {
            if (visible == _visible) return;
            _visible = visible;
            Apply();
        }

        void Apply()
        {
            if (_renderers != null) foreach (var r in _renderers) if (r) r.enabled = _visible;
            if (_canvases != null) foreach (var c in _canvases) if (c) c.enabled = _visible;
        }
    }
}
