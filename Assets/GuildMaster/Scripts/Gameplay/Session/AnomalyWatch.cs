using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Session
{
    /// <summary>
    /// Debug watcher for long runs (P4-73): every game hour it looks for guild members stuck in a
    /// state far longer than that state should last, or standing still where they should be walking,
    /// and for quests in progress that nobody is working any more. Findings are kept (and logged
    /// once each) for <see cref="DebugCommands.Anomalies"/>.
    /// </summary>
    public class AnomalyWatch : MonoBehaviour
    {
        public static AnomalyWatch Instance { get; private set; }

        public static AnomalyWatch Ensure()
        {
            if (Instance != null) return Instance;
            Instance = new GameObject("AnomalyWatch").AddComponent<AnomalyWatch>();
            return Instance;
        }

        /// <summary>Longest a state is expected to last, in game hours (states not listed are not watched).</summary>
        static float LimitHours(Adventurer a, State<Adventurer> s)
        {
            if (s == a.TravelState) return 8f;
            if (s == a.HuntState) return 16f;
            if (s == a.FreeHuntState) return 12f;
            if (s == a.ReturnState) return 8f;
            if (s == a.VisitState) return 4f;
            if (s == a.GatherState) return 2f;
            if (s == a.DecidingState) return 3f;
            if (s == a.CampState) return 13f;
            if (s == a.DefendState) return 12f;
            if (s == a.TownGuardState) return 4f;
            if (s == a.LureState) return 1f;
            return float.PositiveInfinity;
        }

        /// <summary>States in which a member should be getting somewhere.</summary>
        static bool Walking(Adventurer a, State<Adventurer> s) => s == a.TravelState || s == a.ReturnState || s == a.FreeHuntState;

        readonly List<string> _findings = new();
        readonly HashSet<string> _reported = new();
        readonly Dictionary<Adventurer, (Vector2 pos, float hour)> _lastMove = new();
        readonly Dictionary<Quest, float> _unworkedSince = new();
        readonly Dictionary<Party, float> _splitSince = new();
        const float SplitDistance = 25f;

        /// <summary>Out of town on the party's business (not at home, not camped).</summary>
        static bool Outside(Adventurer a)
        {
            var s = a.Fsm.Current;
            return s == a.TravelState || s == a.HuntState || s == a.FreeHuntState || s == a.ReturnState || s == a.LureState;
        }
        GameClock _clock;

        public IReadOnlyList<string> Findings => _findings;

        void OnEnable() => EventBus.Subscribe<HourPassedEvent>(OnHour);
        void OnDisable() => EventBus.Unsubscribe<HourPassedEvent>(OnHour);

        void OnHour(HourPassedEvent e)
        {
            if (_clock == null && !Services.TryGet<GameClock>(out _clock)) return;
            if (!Services.TryGet<GuildRoster>(out var roster)) return;
            float now = _clock.TotalHours;
            float hoursPerSecond = 1f / Mathf.Max(0.01f, _clock.RealSecondsPerGameHour);
            foreach (var a in roster.Members)
            {
                if (a == null || !a.IsAlive) continue;
                var s = a.Fsm.Current;
                float hours = s.Elapsed * hoursPerSecond;
                if (hours > LimitHours(a, s)) Report($"{a.Data.name}: '{s.Label}' {hours:0.0}h (limit {LimitHours(a, s):0})", $"long:{a.Data.name}:{s.Label}");

                if (!_lastMove.TryGetValue(a, out var last) || (a.Position - last.pos).sqrMagnitude > 1f) _lastMove[a] = (a.Position, now);
                else if (Walking(a, s) && now - last.hour >= 2f)
                    Report($"{a.Data.name}: '{s.Label}' but has not moved for {now - last.hour:0.0}h at {a.Position}{Context(a)}", $"still:{a.Data.name}:{s.Label}:{Mathf.RoundToInt(last.hour)}");
            }

            // Monsters: a fight, a walk home, a flight or a pickup should not last for hours.
            foreach (var u in UnitRegistry.All)
            {
                if (u is not Monster m || !m.IsAlive || m.Fsm.Current == null) continue;
                var ms = m.Fsm.Current;
                float hours = ms.Elapsed * hoursPerSecond;
                // A game hour is ten simulated seconds: these are minutes of fighting, not hours.
                float limit = ms == m.EngageState ? 10f : ms == m.ReturnState ? 4f : ms == m.FleeState || ms == m.PickupState || ms == m.RegroupState || ms == m.LureState ? 2f
                            : ms == m.GateBreakState ? 8f : float.PositiveInfinity;
                if (hours > limit) Report($"monster {m.DisplayName}#{m.GetInstanceID()} '{ms.Label}' {hours:0.0}h at {m.Position} (target {m.Engage.Target?.DisplayName ?? "-"})",
                    $"mlong:{m.GetInstanceID()}:{ms.Label}");
            }

            // A party out together that has come apart (user report 2026-10-03: "they act separately").
            if (Services.TryGet<GuildParties>(out var parties))
                foreach (var p in parties.Parties)
                {
                    var outside = p.Members.Where(m => m != null && m.IsAlive && Outside(m)).ToList();
                    float spread = 0f;
                    for (int i = 0; i < outside.Count; i++)
                    for (int j = i + 1; j < outside.Count; j++)
                        spread = Mathf.Max(spread, Vector2.Distance(outside[i].Position, outside[j].Position));
                    if (spread < SplitDistance) { _splitSince.Remove(p); continue; }
                    if (!_splitSince.TryGetValue(p, out var since)) _splitSince[p] = since = now;
                    if (now - since >= 1f)
                        Report($"{p.Name} split {spread:0} cells apart for {now - since:0.0}h: " +
                               string.Join(", ", outside.Select(m => $"{m.Data.name} '{m.StateLabel}' {m.Position}")), $"split:{p.Id}:{Mathf.RoundToInt(since)}");
                }

            if (Services.TryGet<QuestBoard>(out var board))
                foreach (var q in board.Quests)
                {
                    bool worked = q.Status != QuestStatus.InProgress
                                  || q.Assignees.Any(t => t is Adventurer m && m.IsAlive && m.CurrentQuest == q);
                    if (worked) { _unworkedSince.Remove(q); continue; }
                    if (!_unworkedSince.TryGetValue(q, out var since)) _unworkedSince[q] = since = now;
                    if (now - since >= 6f) Report($"quest {q.Id} '{q.Title}' in progress, nobody on it for {now - since:0}h", "quest:" + q.Id);
                }
        }

        /// <summary>What a stalled member is waiting on: its band or quest, its leader, where it is headed.</summary>
        static string Context(Adventurer a)
        {
            var sb = new StringBuilder();
            sb.Append($" moving={a.Mover.IsMoving} dest={a.Mover.Destination} target={a.Engage.Target?.DisplayName ?? "-"}");
            if (Services.TryGet<PartyTactics>(out var tactics) && tactics.LeaderOf(a) is { } leader)
                sb.Append($" leader={leader.Data.name}('{leader.StateLabel}' {leader.Position})");
            if (Services.TryGet<HuntBandService>(out var bands) && bands.BandOf(a) is { } band)
                sb.Append($" band[{string.Join("/", band.Members.Select(m => $"{m.Data.name}:{m.StateLabel}"))}] mustering={band.Mustering} paused={band.Paused} spot={(band.HasSpot ? band.Spot.ToString() : "-")}");
            if (a.CurrentQuest is { } q) sb.Append($" quest={q.Id}:{q.Status} point={q.Point}");
            return sb.ToString();
        }

        void Report(string text, string key)
        {
            if (!_reported.Add(key)) return;
            string line = $"[{_clock.FormatTime()}] {text}";
            _findings.Add(line);
            Debug.Log("[Anomaly] " + line);
        }

        public string Summary()
        {
            var sb = new StringBuilder($"anomalies: {_findings.Count}\n");
            foreach (var f in _findings.TakeLast(40)) sb.AppendLine("  " + f);
            return sb.ToString();
        }
    }
}
