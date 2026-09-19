using System.Collections.Generic;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Session
{
    /// <summary>
    /// Debug-only combat AI probe (P2.5-V): samples unit overlap, ranged kiting and telegraph
    /// dodging, and counts console errors. Created on demand by <see cref="DebugCommands"/>.
    /// </summary>
    public class SkirmishMonitor : MonoBehaviour
    {
        const float SampleInterval = 0.2f;
        /// <summary>Pairs closer than this fraction of their combined radii count as overlapping.</summary>
        const float OverlapFactor = 0.5f;
        const float PersistentOverlapSeconds = 2f;
        const float KiteThreatRadius = 2.5f;

        readonly Dictionary<long, float> _overlapSince = new();
        readonly HashSet<long> _seen = new();
        readonly HashSet<long> _persistentPairs = new();
        readonly Dictionary<Unit, bool> _wasKiting = new();
        readonly List<string> _errors = new();
        float _sampleTimer;
        float _startTime;

        int _samples;
        float _maxOverlapSeconds;
        int _threatSamples, _kitingSamples, _kiteEpisodes;
        int _telegraphs, _advInsideAtStart, _advInsideAtResolve;
        int _errorCount;
        readonly Dictionary<Unit, int> _targetCounts = new();
        readonly Dictionary<Unit, Unit> _lastTarget = new();
        int _spreadSamples, _allOnOneSamples, _monsterSwitches, _advSwitches, _dropped;
        float _focusShareSum;

        public static SkirmishMonitor Instance { get; private set; }

        public static SkirmishMonitor Ensure()
        {
            if (Instance != null) return Instance;
            Instance = new GameObject("SkirmishMonitor").AddComponent<SkirmishMonitor>();
            return Instance;
        }

        void OnEnable()
        {
            Application.logMessageReceived += OnLog;
            EventBus.Subscribe<TelegraphStartedEvent>(OnTelegraphStarted);
            EventBus.Subscribe<TelegraphResolvedEvent>(OnTelegraphResolved);
            ResetStats();
        }

        void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
            EventBus.Unsubscribe<TelegraphStartedEvent>(OnTelegraphStarted);
            EventBus.Unsubscribe<TelegraphResolvedEvent>(OnTelegraphResolved);
            if (Instance == this) Instance = null;
        }

        public void ResetStats()
        {
            _overlapSince.Clear();
            _persistentPairs.Clear();
            _wasKiting.Clear();
            _errors.Clear();
            _startTime = Time.unscaledTime;
            _samples = 0;
            _maxOverlapSeconds = 0f;
            _threatSamples = _kitingSamples = _kiteEpisodes = 0;
            _telegraphs = _advInsideAtStart = _advInsideAtResolve = 0;
            _errorCount = 0;
            _targetCounts.Clear();
            _lastTarget.Clear();
            _spreadSamples = _allOnOneSamples = _monsterSwitches = _advSwitches = _dropped = 0;
            _focusShareSum = 0f;
        }

        void OnLog(string message, string stackTrace, LogType type)
        {
            if (type is not (LogType.Error or LogType.Exception or LogType.Assert)) return;
            _errorCount++;
            if (_errors.Count < 5) _errors.Add(message.Length > 160 ? message.Substring(0, 160) : message);
        }

        static int AdventurersInside(Unit owner, Vector2 center, float radius)
        {
            if (owner == null) return 0;
            return UnitRegistry.CountInRadius(center, radius, u => u is Adventurer && (owner.IsHostileTo(u) || u.IsHostileTo(owner)));
        }

        void OnTelegraphStarted(TelegraphStartedEvent e)
        {
            _telegraphs++;
            _advInsideAtStart += AdventurersInside(e.Owner, e.Center, e.Radius);
        }

        void OnTelegraphResolved(TelegraphResolvedEvent e)
        {
            if (!e.Cancelled) _advInsideAtResolve += AdventurersInside(e.Owner, e.Center, e.Radius);
        }

        void Update()
        {
            _sampleTimer -= Time.deltaTime;
            if (_sampleTimer > 0f) return;
            _sampleTimer = SampleInterval;
            _samples++;
            SampleOverlap();
            SampleKiting();
            SampleTargets();
        }

        /// <summary>
        /// Monster target spread (how much the pack piles onto one adventurer, only while at least two
        /// adventurers are in the fight) and target switches / dropped chases for both sides.
        /// </summary>
        void SampleTargets()
        {
            _targetCounts.Clear();
            int engaged = 0;
            var fighters = new HashSet<Unit>();
            foreach (var u in UnitRegistry.All)
            {
                if (!u.IsAlive) continue;
                var t = u.CurrentTarget;
                _lastTarget.TryGetValue(u, out var prev);
                if (prev != null && t != prev)
                {
                    if (t == null && prev.IsAlive) _dropped++;
                    else if (t != null) { if (u is Adventurer) _advSwitches++; else _monsterSwitches++; }
                }
                _lastTarget[u] = t;
                if (u is Adventurer && t != null) fighters.Add(u);
                if (u is not Monsters.Monster || t is not Adventurer || !t.IsAlive) continue;
                engaged++;
                fighters.Add(t);
                _targetCounts[t] = _targetCounts.TryGetValue(t, out var n) ? n + 1 : 1;
            }
            if (engaged < 2 || fighters.Count < 2) return;
            int top = 0;
            foreach (var c in _targetCounts.Values) top = Mathf.Max(top, c);
            _spreadSamples++;
            _focusShareSum += top / (float)engaged;
            if (_targetCounts.Count == 1) _allOnOneSamples++;
        }

        void SampleOverlap()
        {
            var units = UnitRegistry.All;
            float now = Time.time;
            _seen.Clear();
            for (int i = 0; i < units.Count; i++)
            {
                var a = units[i];
                if (!a.IsAlive) continue;
                for (int j = i + 1; j < units.Count; j++)
                {
                    var b = units[j];
                    if (!b.IsAlive) continue;
                    float min = (a.Radius + b.Radius) * OverlapFactor;
                    if ((a.Position - b.Position).sqrMagnitude >= min * min) continue;
                    int ia = a.GetInstanceID(), ib = b.GetInstanceID();
                    long key = ia < ib ? ((long)ia << 32) ^ (uint)ib : ((long)ib << 32) ^ (uint)ia;
                    _seen.Add(key);
                    if (!_overlapSince.TryGetValue(key, out var since)) _overlapSince[key] = since = now;
                    float dur = now - since;
                    if (dur > _maxOverlapSeconds) _maxOverlapSeconds = dur;
                    if (dur >= PersistentOverlapSeconds) _persistentPairs.Add(key);
                }
            }
            if (_overlapSince.Count == _seen.Count) return;
            var stale = new List<long>();
            foreach (var k in _overlapSince.Keys) if (!_seen.Contains(k)) stale.Add(k);
            foreach (var k in stale) _overlapSince.Remove(k);
        }

        void SampleKiting()
        {
            foreach (var u in UnitRegistry.All)
            {
                if (!u.IsAlive || !u.Stats.IsRanged || u is not Adventurer a) continue;
                bool threatened = Tactics.NearestMeleeThreat(a, KiteThreatRadius + a.Radius) != null;
                bool kiting = a.Engage.IsKiting;
                if (threatened) _threatSamples++;
                if (threatened && kiting) _kitingSamples++;
                _wasKiting.TryGetValue(a, out var was);
                if (kiting && !was) _kiteEpisodes++;
                _wasKiting[a] = kiting;
            }
        }

        public string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[Skirmish] {Time.unscaledTime - _startTime:0}s real, {_samples} samples, errors={_errorCount}");
            foreach (var e in _errors) sb.AppendLine("  ! " + e);
            sb.AppendLine($"  overlap: maxContinuous={_maxOverlapSeconds:0.0}s pairs>{PersistentOverlapSeconds:0}s={_persistentPairs.Count} activeNow={_overlapSince.Count}");
            float kitePct = _threatSamples > 0 ? 100f * _kitingSamples / _threatSamples : 0f;
            sb.AppendLine($"  kiting: threatSamples={_threatSamples} kitingWhileThreatened={kitePct:0}% episodes={_kiteEpisodes}");
            sb.AppendLine($"  telegraph: count={_telegraphs} advInsideAtStart={_advInsideAtStart} advInsideAtResolve={_advInsideAtResolve}");
            float share = _spreadSamples > 0 ? 100f * _focusShareSum / _spreadSamples : 0f;
            float allOne = _spreadSamples > 0 ? 100f * _allOnOneSamples / _spreadSamples : 0f;
            sb.AppendLine($"  targets: spreadSamples={_spreadSamples} avgTopShare={share:0}% allOnOne={allOne:0}% monsterSwitches={_monsterSwitches} advSwitches={_advSwitches} dropped={_dropped}");
            return sb.ToString();
        }
    }
}
