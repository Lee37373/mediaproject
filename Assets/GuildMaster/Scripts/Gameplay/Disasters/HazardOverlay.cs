using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Disasters
{
    /// <summary>
    /// Draws the natural disasters' cells (P4-72): flames, ash, poison mist, snow, sand and cracks.
    /// A cell shows what the guild last saw there (information rule): a change out of sight is drawn
    /// once someone sees the cell, so a fire nobody watched still looks like it did when they left.
    /// Placeholder shapes until real art (user: blank images are fine for now).
    /// </summary>
    public class HazardOverlay : MonoBehaviour
    {
        const float RefreshInterval = 0.25f;
        const int SortingOrder = 4;

        WorldService _world;
        FogOfWar _fog;
        Transform _root;
        readonly HashSet<Vector2Int> _pending = new();
        readonly Dictionary<Vector2Int, (HazardKind kind, SpriteRenderer sr)> _shown = new();
        readonly Stack<SpriteRenderer> _free = new();
        readonly List<SpriteRenderer> _flames = new();
        static readonly List<Vector2Int> s_seen = new();
        float _timer;
        Sprite _soft, _flame, _mound, _crack;

        public void Init(WorldService world, FogOfWar fog)
        {
            _world = world;
            _fog = fog;
            _root = new GameObject("Hazards").transform;
            _root.SetParent(transform, false);
            _soft = SpriteFactory.SoftCircle(64);
            _flame = SpriteFactory.Circle(20, new Color(1f, 0.55f, 0.15f), new Color(1f, 0.9f, 0.35f));
            _mound = SpriteFactory.Circle(24, new Color(0.96f, 0.98f, 1f), new Color(0.75f, 0.82f, 0.9f));
            _crack = SpriteFactory.Box(28, 6, new Color(0.12f, 0.08f, 0.06f), new Color(0.3f, 0.22f, 0.18f));
        }

        void OnEnable() => EventBus.Subscribe<HazardChangedEvent>(OnChanged);
        void OnDisable() => EventBus.Unsubscribe<HazardChangedEvent>(OnChanged);

        void OnChanged(HazardChangedEvent e) => _pending.Add(e.Cell);

        /// <summary>What the guild believes is on the cell (debug, tests).</summary>
        public HazardKind ShownAt(Vector2Int c) => _shown.TryGetValue(c, out var s) ? s.kind : HazardKind.None;

        void Update()
        {
            if (_world == null) return;
            Flicker();
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f || _pending.Count == 0) return;
            _timer = RefreshInterval;
            s_seen.Clear();
            // A disaster is shown as seen; the deep woods' miasma (P4-103) is always there, so like the ground it shows once explored.
            foreach (var c in _pending)
                if (_fog == null || _fog.IsVisible(c) || _world.Grid.HazardAt(c) == HazardKind.Miasma && _fog.IsExplored(c)) s_seen.Add(c);
            foreach (var c in s_seen)
            {
                _pending.Remove(c);
                Show(c, _world.Grid.HazardAt(c));
            }
        }

        void Show(Vector2Int c, HazardKind kind)
        {
            if (_shown.TryGetValue(c, out var old))
            {
                if (old.kind == kind) return;
                Release(old.sr);
                _shown.Remove(c);
            }
            if (kind == HazardKind.None) return;
            var sr = Take();
            var p = GridMap.CellToWorld(c);
            sr.transform.position = new Vector3(p.x, p.y, 0f);
            sr.transform.rotation = Quaternion.identity;
            sr.sortingOrder = SortingOrder;
            float scale = 1f;
            switch (kind)
            {
                case HazardKind.Fire:
                    sr.sprite = _flame; sr.color = Color.white; scale = 0.8f;
                    _flames.Add(sr);
                    break;
                case HazardKind.Burnt:
                    sr.sprite = _soft; sr.color = new Color(0.1f, 0.08f, 0.06f, 0.8f); scale = 1.8f;
                    sr.sortingOrder = SortingOrder - 2; // under the stumps
                    break;
                case HazardKind.Poison:
                    sr.sprite = _soft; sr.color = new Color(0.6f, 0.25f, 0.85f, 0.7f); scale = 2f;
                    break;
                case HazardKind.Miasma:
                    // The deep woods' miasma (P4-103): a sickly green haze under the canopy.
                    sr.sprite = _soft; sr.color = new Color(0.45f, 0.62f, 0.2f, 0.5f); scale = 2.3f;
                    break;
                case HazardKind.Snow:
                    sr.sprite = _mound; sr.color = Color.white; scale = 1.05f;
                    break;
                case HazardKind.Sandstorm:
                    sr.sprite = _soft; sr.color = new Color(0.95f, 0.8f, 0.45f, 0.6f); scale = 2.2f;
                    break;
                case HazardKind.Fissure:
                    sr.sprite = _crack; sr.color = Color.white; scale = 1f;
                    sr.transform.rotation = Quaternion.Euler(0f, 0f, (c.x * 37 + c.y * 61) % 180);
                    break;
            }
            sr.transform.localScale = Vector3.one * scale;
            _shown[c] = (kind, sr);
        }

        void Flicker()
        {
            float t = Time.time;
            for (int i = _flames.Count - 1; i >= 0; i--)
            {
                var sr = _flames[i];
                if (sr == null || !sr.gameObject.activeSelf) { _flames.RemoveAt(i); continue; }
                float k = Mathf.Sin(t * 9f + sr.transform.position.x * 3.1f + sr.transform.position.y * 1.7f);
                sr.transform.localScale = Vector3.one * (0.8f + 0.12f * k);
                sr.color = new Color(1f, 0.85f + 0.15f * k, 0.8f + 0.2f * k, 1f);
            }
        }

        SpriteRenderer Take()
        {
            if (_free.Count > 0)
            {
                var sr = _free.Pop();
                sr.gameObject.SetActive(true);
                return sr;
            }
            var go = new GameObject("Hazard");
            go.transform.SetParent(_root, false);
            return go.AddComponent<SpriteRenderer>();
        }

        void Release(SpriteRenderer sr)
        {
            if (sr == null) return;
            _flames.Remove(sr);
            sr.gameObject.SetActive(false);
            _free.Push(sr);
        }
    }
}
