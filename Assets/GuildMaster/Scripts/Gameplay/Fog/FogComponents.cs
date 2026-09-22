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

    /// <summary>
    /// Something worth remembering where it was last seen (P4-23). The fog ghost service copies
    /// <see cref="GhostBody"/> as it looked at the moment the fog swallowed it.
    /// </summary>
    public interface IFogGhostSubject
    {
        /// <summary>False for things not worth a memory (a dead monster, an emptied stack).</summary>
        bool WantsGhost { get; }
        /// <summary>Name shown in the inspector, e.g. "고블린 전사".</summary>
        string GhostLabel { get; }
        /// <summary>The renderer to snapshot (sprite, facing, colour, size).</summary>
        SpriteRenderer GhostBody { get; }
        /// <summary>0..1 to remember a health bar; negative for things without one.</summary>
        float GhostHpRatio { get; }
    }

    /// <summary>Hides this object's renderers/canvases while its cell is not currently visible.</summary>
    public class FogHideable : MonoBehaviour, IFogHideable
    {
        /// <summary>Fog hid (false) or revealed (true) this object; the ghost service listens.</summary>
        public static event System.Action<FogHideable, bool> VisibilityChanged;
        /// <summary>This hideable is going away (pooled/destroyed); its ghost keeps standing.</summary>
        public static event System.Action<FogHideable> Released;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { VisibilityChanged = null; Released = null; }

        Renderer[] _renderers;
        Canvas[] _canvases;
        bool _visible = true;
        /// <summary>The fog has judged this object at least once; before that there is no transition.</summary>
        bool _evaluated;

        public Vector2 FogPosition => transform.position;
        public bool IsFogVisible => _visible;
        /// <summary>
        /// The fog has judged this object at least once. Until then <see cref="IsFogVisible"/> is
        /// only the default (visible), not an answer, so nothing may be read as a sighting.
        /// </summary>
        public bool IsFogEvaluated => _evaluated;

        void Awake() => Refresh();

        /// <summary>Re-collects child renderers (call after adding visuals at runtime).</summary>
        public void Refresh()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _canvases = GetComponentsInChildren<Canvas>(true);
            Apply();
        }

        void OnEnable() => FogOfWar.Register(this);

        void OnDisable()
        {
            FogOfWar.Unregister(this);
            Released?.Invoke(this);
            // A pooled object comes back visible and unjudged; its next hide is a fresh sighting.
            _visible = true;
            _evaluated = false;
        }

        public void SetFogVisible(bool visible)
        {
            // The first call only establishes the state: nobody watched this object become hidden,
            // so it must not count as a sighting (that would leak the whole map at startup).
            bool first = !_evaluated;
            _evaluated = true;
            if (visible == _visible && !first) return;
            _visible = visible;
            Apply();
            if (!first) VisibilityChanged?.Invoke(this, visible);
        }

        void Apply()
        {
            if (_renderers != null) foreach (var r in _renderers) if (r) r.enabled = _visible;
            if (_canvases != null) foreach (var c in _canvases) if (c) c.enabled = _visible;
        }
    }
}
