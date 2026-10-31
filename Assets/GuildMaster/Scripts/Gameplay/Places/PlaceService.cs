using System;
using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Places
{
    /// <summary>The guild learned of a place (P4-105): the fog over it lifted.</summary>
    public struct PlaceDiscoveredEvent { public PlaceSite Place; }

    /// <summary>A gathering spot was picked or grew back, a watchtower was climbed (P4-105): its look changes.</summary>
    public struct PlaceChangedEvent { public PlaceSite Place; }

    /// <summary>
    /// The places worth going to (P4-105, G3's smart objects; user decision 2026-10-26): shows them on the map, knows which the
    /// guild has found (the fog over them lifted — information hiding, as with dens and ruins), and keeps what has happened at
    /// them: a gathering spot picked clean until it grows back, a watchtower climbed. The places themselves come from the seed
    /// (<see cref="PlacePlacement"/>). What they do for a member is <see cref="PlaceRules"/>.
    /// </summary>
    public class PlaceService : MonoBehaviour, ISaveable
    {
        const float ScanInterval = 0.5f;

        MapData _map;
        FogOfWar _fog;
        GameClock _clock;
        readonly HashSet<int> _known = new();
        readonly Dictionary<int, float> _readyAt = new();
        readonly HashSet<int> _climbed = new();
        readonly Dictionary<int, SpriteRenderer> _props = new();
        float _scan;
        /// <summary>The first look (a new game's start, a loaded game) learns what is already explored without telling.</summary>
        bool _quiet = true;

        public void Init(MapData map, FogOfWar fog, GameClock clock)
        {
            _map = map;
            _fog = fog;
            _clock = clock;
            foreach (var p in map.Places) Show(p);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        void OnDestroy() => EventBus.Unsubscribe<HourPassedEvent>(OnHour);

        public IReadOnlyList<PlaceSite> All => _map != null ? _map.Places : (IReadOnlyList<PlaceSite>)Array.Empty<PlaceSite>();

        public bool IsKnown(PlaceSite p) => p != null && _known.Contains(p.Index);

        /// <summary>The places the guild knows, of one kind or all.</summary>
        public IEnumerable<PlaceSite> Known(PlaceKind? kind = null)
        {
            foreach (var p in All)
                if (_known.Contains(p.Index) && (kind == null || p.Kind == kind)) yield return p;
        }

        float Now => _clock != null ? _clock.TotalHours : 0f;

        // ---------------- Gathering spots ----------------

        /// <summary>A gathering spot with something to take (anything else is always "ready").</summary>
        public bool IsReady(PlaceSite p) => p != null && (!_readyAt.TryGetValue(p.Index, out float t) || Now >= t);

        public float HoursUntilReady(PlaceSite p) => p != null && _readyAt.TryGetValue(p.Index, out float t) ? Mathf.Max(0f, t - Now) : 0f;

        /// <summary>Picked clean: it grows back in <paramref name="hours"/>.</summary>
        public void Spend(PlaceSite p, float hours)
        {
            if (p == null) return;
            _readyAt[p.Index] = Now + hours;
            Show(p);
            EventBus.Publish(new PlaceChangedEvent { Place = p });
        }

        // ---------------- Watchtowers ----------------

        public bool IsClimbed(PlaceSite p) => p != null && _climbed.Contains(p.Index);

        public void MarkClimbed(PlaceSite p)
        {
            if (p == null || !_climbed.Add(p.Index)) return;
            Show(p);
            EventBus.Publish(new PlaceChangedEvent { Place = p });
        }

        // ---------------- Finding them ----------------

        /// <summary>The known place under a point (the inspector's click), or null.</summary>
        public PlaceSite PickAt(Vector2 at, float radius)
        {
            PlaceSite best = null;
            float bestD = radius;
            foreach (var p in All)
            {
                if (!_known.Contains(p.Index)) continue;
                // Picked by the middle of its picture, a little above its foot.
                float d = Vector2.Distance(p.WorldPosition + new Vector2(0f, 0.6f), at);
                if (d <= bestD) { bestD = d; best = p; }
            }
            return best;
        }

        void Update()
        {
            if (_map == null || _fog == null) return;
            _scan -= Time.unscaledDeltaTime;
            if (_scan > 0f) return;
            _scan = ScanInterval;
            foreach (var p in _map.Places)
            {
                if (_known.Contains(p.Index) || !_fog.IsExploredRaw(p.Cell)) continue;
                _known.Add(p.Index);
                if (!_quiet) EventBus.Publish(new PlaceDiscoveredEvent { Place = p });
            }
            _quiet = false;
        }

        void OnHour(HourPassedEvent e)
        {
            // A picked spot that has grown back shows it.
            foreach (var p in All)
                if (_readyAt.TryGetValue(p.Index, out float t) && Now >= t)
                {
                    _readyAt.Remove(p.Index);
                    Show(p);
                    EventBus.Publish(new PlaceChangedEvent { Place = p });
                }
        }

        // ---------------- Looks ----------------

        void Show(PlaceSite p)
        {
            if (!_props.TryGetValue(p.Index, out var sr) || sr == null)
            {
                var go = new GameObject("Place_" + p.Kind + "_" + p.Index);
                go.transform.SetParent(transform, false);
                var at = p.WorldPosition + new Vector2(0f, -0.45f);
                go.transform.position = at;
                sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = CharacterVisual.BaseSortingOrder - Mathf.RoundToInt(at.y * 8f) + 2;
                _props[p.Index] = sr;
            }
            bool spent = p.Kind == PlaceKind.WatchRuin ? _climbed.Contains(p.Index) : !IsReady(p);
            sr.sprite = CartoonArt.Place(p.Kind, spent);
        }

        // ---------------- Save (P5-4) ----------------

        [Serializable]
        class SaveData
        {
            public List<int> spent_index = new();
            public List<float> spent_until = new();
            public List<int> climbed = new();
        }

        public string SaveKey => "places";

        public string Capture()
        {
            var s = new SaveData();
            var keys = new List<int>(_readyAt.Keys);
            keys.Sort();
            foreach (var k in keys)
            {
                s.spent_index.Add(k);
                s.spent_until.Add(_readyAt[k]);
            }
            s.climbed.AddRange(_climbed);
            s.climbed.Sort();
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            _readyAt.Clear();
            _climbed.Clear();
            for (int i = 0; i < s.spent_index.Count && i < s.spent_until.Count; i++) _readyAt[s.spent_index[i]] = s.spent_until[i];
            foreach (var c in s.climbed) _climbed.Add(c);
            _quiet = true;
            if (_map != null) foreach (var p in _map.Places) Show(p);
        }
    }
}
