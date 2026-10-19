using System.Collections.Generic;
using System.Linq;
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
        readonly int[] _tierThreat = new int[System.Enum.GetValues(typeof(CombatTier)).Length];
        readonly int[] _tierKite = new int[System.Enum.GetValues(typeof(CombatTier)).Length];
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
            EventBus.Subscribe<PartyTargetCalledEvent>(OnPartyCall);
            ResetStats();
        }

        void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
            EventBus.Unsubscribe<TelegraphStartedEvent>(OnTelegraphStarted);
            EventBus.Unsubscribe<TelegraphResolvedEvent>(OnTelegraphResolved);
            EventBus.Unsubscribe<PartyTargetCalledEvent>(OnPartyCall);
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
            System.Array.Clear(_tierThreat, 0, _tierThreat.Length);
            System.Array.Clear(_tierKite, 0, _tierKite.Length);
            _telegraphs = _advInsideAtStart = _advInsideAtResolve = 0;
            _errorCount = 0;
            _targetCounts.Clear();
            _lastTarget.Clear();
            _spreadSamples = _allOnOneSamples = _monsterSwitches = _advSwitches = _dropped = 0;
            _focusShareSum = 0f;
            _partyCalls = _focusSamples = _focusHits = _noviceFocusSamples = _noviceFocusHits = _backSamples = _backBehind = 0;
            System.Array.Clear(_tierWait, 0, _tierWait.Length);
            System.Array.Clear(_tierWaitMoving, 0, _tierWaitMoving.Length);
            System.Array.Clear(_tierCloseSamples, 0, _tierCloseSamples.Length);
            System.Array.Clear(_tierSwings, 0, _tierSwings.Length);
            _lastSwing.Clear();
            _lastTurns.Clear();
            _stateTurns.Clear();
            System.Array.Clear(_tierTurns, 0, _tierTurns.Length);
            System.Array.Clear(_tierFightSamples, 0, _tierFightSamples.Length);
        }

        /// <summary>Turns per minute of fighting by tier (scaled time).</summary>
        public float TurnsPerMinute(CombatTier tier)
            => _tierFightSamples[(int)tier] > 0 ? _tierTurns[(int)tier] * 60f / (_tierFightSamples[(int)tier] * SampleInterval) : 0f;

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
            SampleParties();
            SampleFootwork();
            SampleTurns();
        }

        // ---------------- Facing (P4-92) ----------------

        readonly Dictionary<Unit, int> _lastTurns = new();
        readonly int[] _tierTurns = new int[System.Enum.GetValues(typeof(CombatTier)).Length];
        readonly int[] _tierFightSamples = new int[System.Enum.GetValues(typeof(CombatTier)).Length];

        /// <summary>How often fighters turn round (left ↔ right) — the sprite flickered for skilled+ units side-stepping and kiting.</summary>
        void SampleTurns()
        {
            foreach (var u in UnitRegistry.All)
            {
                if (u == null || !u.IsAlive || u.Visual == null) continue;
                int turns = u.Visual.Turns;
                bool had = _lastTurns.TryGetValue(u, out var before);
                _lastTurns[u] = turns;
                if (!had || !u.IsInFight()) continue;
                int t = (int)u.Tier;
                _tierFightSamples[t]++;
                int turned = Mathf.Max(0, turns - before);
                _tierTurns[t] += turned;
                // What it was doing (state, and how it moved), to find where the turning comes from.
                var e = u.CombatEngagement;
                string how = e != null && e.IsKiting ? "kite" : u.Mover.IsSteering ? "steer" : u.Mover.IsMoving ? "walk" : "stand";
                string key = $"{(u is Adventurer ? "adv" : "mon")}:{u.DebugState}:{how}";
                _stateTurns.TryGetValue(key, out var acc);
                _stateTurns[key] = (acc.turns + turned, acc.samples + 1);
            }
        }

        readonly Dictionary<string, (int turns, int samples)> _stateTurns = new();

        // ---------------- Parties (P4-27) ----------------

        readonly List<Adventurer> _party = new();
        int _partyCalls, _focusSamples, _focusHits, _noviceFocusSamples, _noviceFocusHits, _backSamples, _backBehind;

        void OnPartyCall(PartyTargetCalledEvent e) => _partyCalls++;

        /// <summary>
        /// Focus fire (members whose target is the leader's call — skilled+ should, novices need not)
        /// and the back line (back-liners fighting near a front-liner: are they farther from the enemy?).
        /// </summary>
        void SampleParties()
        {
            if (!Services.TryGet<PartyTactics>(out var tactics) || !Services.TryGet<Quests.QuestBoard>(out var board)) return;
            foreach (var q in board.Quests)
            {
                Adventurer any = null;
                foreach (var t in q.Assignees) if (t is Adventurer a) { any = a; break; }
                if (any == null || tactics.Party(any, _party).Count < 2) continue;
                var leader = _party[0];
                foreach (var m in _party)
                {
                    var target = m.Engage.Target;
                    if (target == null) continue;
                    var called = leader.Tier >= CombatTier.Skilled ? leader.Engage.Target : null;
                    if (m != leader && called != null)
                    {
                        if (m.Tier >= CombatTier.Skilled) { _focusSamples++; if (target == called) _focusHits++; }
                        else { _noviceFocusSamples++; if (target == called) _noviceFocusHits++; }
                    }
                    if (m.Role != PartyRole.Back) continue;
                    float front = float.MaxValue;
                    foreach (var f in _party)
                        if (f.Role == PartyRole.Front) front = Mathf.Min(front, Vector2.Distance(f.Position, target.Position));
                    if (front > 6f) continue;
                    _backSamples++;
                    if (Vector2.Distance(m.Position, target.Position) >= front - 0.25f) _backBehind++;
                }
            }
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

        // ---------------- Melee footwork (P4-47) ----------------

        readonly int[] _tierWait = new int[System.Enum.GetValues(typeof(CombatTier)).Length];
        readonly int[] _tierWaitMoving = new int[System.Enum.GetValues(typeof(CombatTier)).Length];
        readonly int[] _tierCloseSamples = new int[System.Enum.GetValues(typeof(CombatTier)).Length];
        readonly int[] _tierSwings = new int[System.Enum.GetValues(typeof(CombatTier)).Length];
        readonly Dictionary<Unit, float> _lastSwing = new();

        /// <summary>
        /// Melee units with a target: while waiting on the next blow in reach (or stepping), how often
        /// they move; and swings per minute while close to the target (footwork must not cost tempo).
        /// </summary>
        void SampleFootwork()
        {
            foreach (var u in UnitRegistry.All)
            {
                if (!u.IsAlive || u.Stats.IsRanged || u.CombatEngagement?.Target is not { IsAlive: true } target) continue;
                int tier = (int)u.Tier;
                bool inRange = Combat.InRange(u, target);
                if (Time.time < u.NextAttackTime && (inRange || u.CombatEngagement.IsSteering))
                {
                    _tierWait[tier]++;
                    if (u.Mover.IsMoving || u.Mover.IsSteering) _tierWaitMoving[tier]++;
                }
                if (Combat.Distance(u, target) > u.Stats.AttackRange + Combat.ContactPadding + target.Radius + 1f) continue;
                _tierCloseSamples[tier]++;
                if (_lastSwing.TryGetValue(u, out var last) && u.NextAttackTime > last + 0.01f) _tierSwings[tier]++;
                _lastSwing[u] = u.NextAttackTime;
            }
        }

        void SampleKiting()
        {
            foreach (var u in UnitRegistry.All)
            {
                if (!u.IsAlive || !u.Stats.IsRanged || u.CombatEngagement == null) continue;
                bool threatened = Tactics.NearestMeleeThreat(u, KiteThreatRadius + u.Radius) != null;
                bool kiting = u.CombatEngagement.IsKiting;
                // Every ranged unit by tier (P4-26: only skilled and up kite).
                int tier = (int)u.Tier;
                if (threatened) _tierThreat[tier]++;
                if (threatened && kiting) _tierKite[tier]++;
                if (u is not Adventurer a) continue;
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
            sb.Append("  kitingByTier(all ranged, kite%/threatSamples):");
            for (int t = 0; t < _tierThreat.Length; t++)
                if (_tierThreat[t] > 0) sb.Append($" {CombatTiers.Label((CombatTier)t)}={100f * _tierKite[t] / _tierThreat[t]:0}%/{_tierThreat[t]}");
            sb.AppendLine();
            sb.AppendLine($"  telegraph: count={_telegraphs} advInsideAtStart={_advInsideAtStart} advInsideAtResolve={_advInsideAtResolve}");
            float share = _spreadSamples > 0 ? 100f * _focusShareSum / _spreadSamples : 0f;
            float allOne = _spreadSamples > 0 ? 100f * _allOnOneSamples / _spreadSamples : 0f;
            static string Pct(int hit, int all) => all > 0 ? $"{100f * hit / all:0}%/{all}" : "-";
            sb.AppendLine($"  party: calls={_partyCalls} focusSkilled+={Pct(_focusHits, _focusSamples)} focusNovice={Pct(_noviceFocusHits, _noviceFocusSamples)} backLineBehind={Pct(_backBehind, _backSamples)}");
            sb.Append($"  footwork: steering={(LocalSteering.Enabled ? "on" : "off")} movingWhileWaiting:");
            for (int t = 0; t < _tierWait.Length; t++)
                if (_tierWait[t] > 0) sb.Append($" {CombatTiers.Label((CombatTier)t)}={Pct(_tierWaitMoving[t], _tierWait[t])}");
            sb.Append(" swingsPerMinClose:");
            for (int t = 0; t < _tierCloseSamples.Length; t++)
                if (_tierCloseSamples[t] > 0) sb.Append($" {CombatTiers.Label((CombatTier)t)}={_tierSwings[t] * 60f / (_tierCloseSamples[t] * SampleInterval):0}");
            sb.AppendLine();
            sb.AppendLine($"  targets:spreadSamples={_spreadSamples} avgTopShare={share:0}% allOnOne={allOne:0}% monsterSwitches={_monsterSwitches} advSwitches={_advSwitches} dropped={_dropped}");
            sb.Append("  turnsPerMinFighting:");
            for (int t = 0; t < _tierFightSamples.Length; t++)
                if (_tierFightSamples[t] > 0) sb.Append($" {CombatTiers.Label((CombatTier)t)}={TurnsPerMinute((CombatTier)t):0}/{_tierFightSamples[t]}");
            sb.AppendLine();
            sb.Append("  turnsBy(state:move, per min/samples):");
            foreach (var kv in _stateTurns.OrderByDescending(kv => kv.Value.turns).Take(8))
                sb.Append($" {kv.Key}={kv.Value.turns * 60f / (kv.Value.samples * SampleInterval):0}/{kv.Value.samples}");
            sb.AppendLine();
            return sb.ToString();
        }
    }
}
