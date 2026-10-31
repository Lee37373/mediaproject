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
            // Party work is gathered for up to half a day (P4-97); an ordinary gathering ends the moment it begins.
            if (s == a.GatherState) return PartyService.RecruitHours + 1f;
            if (s == a.DecidingState) return 3f;
            if (s == a.CampState) return 13f;
            if (s == a.DefendState) return 12f;
            if (s == a.TownGuardState) return 4f;
            // Lying low until no monster is at it (P4-100); a newcomer thinks again after two hours' training (P4-99).
            if (s == a.TakeCoverState) return 4f;
            if (s == a.TrainState) return NewcomerRules.TrainHours + 1f;
            if (s == a.LureState) return 1f;
            // P4-108: the later states — a walk to a place (its give-up walk and a stay; a night camp is a state of its own),
            // a mood break (a spree's walk home and hangover the longest), trailing a party out (its outing).
            if (s == a.PlaceVisitState) return Places.PlaceRules.GiveUpHours + 6f;
            if (s == a.BreakState) return 6f;
            if (s == a.TagAlongState) return 16f;
            // Idling decides every hour and a half; a follower waits for its leader, but not for half a
            // day (P4-76: an outing that never settled held a whole party in town for days).
            if (s == a.IdleState) return 12f;
            return float.PositiveInfinity;
        }

        /// <summary>States in which a member should be getting somewhere.</summary>
        static bool Walking(Adventurer a, State<Adventurer> s) => s == a.TravelState || s == a.ReturnState || s == a.FreeHuntState;

        readonly List<string> _findings = new();
        readonly HashSet<string> _reported = new();
        readonly Dictionary<Adventurer, (Vector2 pos, float hour, State<Adventurer> state)> _lastMove = new();
        readonly Dictionary<Quest, float> _unworkedSince = new();
        readonly Dictionary<Party, float> _splitSince = new();
        readonly Dictionary<Party, float> _openOutingSince = new();
        const float SplitDistance = 25f;

        /// <summary>Out of town on the party's business (not at home, not camped).</summary>
        static bool Outside(Adventurer a)
        {
            var s = a.Fsm.Current;
            return s == a.TravelState || s == a.HuntState || s == a.FreeHuntState || s == a.ReturnState || s == a.LureState;
        }
        GameClock _clock;

        public IReadOnlyList<string> Findings => _findings;

        void OnEnable()
        {
            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<UnitHealedEvent>(OnHealed);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            EventBus.Unsubscribe<UnitHealedEvent>(OnHealed);
        }

        // ---- What a member's fighting comes to in its current state (P4-80: a healer and two goblin
        // archers, ten hours on the same spot — does it not strike, miss, or heal off what it takes?) ----

        class Tally
        {
            public State<Adventurer> State;
            public Unit Target;
            public float NextAttack, TargetHitAt, Since;
            public int Strikes, Hits, Heals;
        }

        readonly Dictionary<Adventurer, Tally> _tally = new();

        void OnHealed(UnitHealedEvent e)
        {
            if (e.Healer is Adventurer a && _tally.TryGetValue(a, out var t)) t.Heals++;
        }

        void Count(Adventurer a)
        {
            if (!_tally.TryGetValue(a, out var t)) _tally[a] = t = new Tally();
            var target = a.Engage.Target;
            if (t.State != a.Fsm.Current)
            {
                t.State = a.Fsm.Current;
                t.Strikes = t.Hits = t.Heals = 0;
                t.Since = _clock.TotalHours;
                t.NextAttack = a.NextAttackTime;
            }
            if (a.NextAttackTime != t.NextAttack) { t.Strikes++; t.NextAttack = a.NextAttackTime; }
            if (target != t.Target) { t.Target = target; t.TargetHitAt = target != null ? target.LastAttackedTime : 0f; }
            else if (target != null && target.LastAttackedTime != t.TargetHitAt)
            {
                if (target.LastAttacker == a) t.Hits++;
                t.TargetHitAt = target.LastAttackedTime;
            }
        }

        string TallyOf(Adventurer a)
            => _tally.TryGetValue(a, out var t) && t.State == a.Fsm.Current
                ? $" tally[{_clock.TotalHours - t.Since:0.0}h strikes={t.Strikes} hits={t.Hits} heals={t.Heals}]" : "";

        // ---- What each member did lately (P4-80: a stuck fight, a quest walked forty cells the wrong
        // way, half a day idle in town — each needed what came before it, long gone by the report) ----

        const int HistoryLength = 12;
        /// <summary>Same target this long (game hours) is a fight that is not going anywhere.</summary>
        const float LongFightHours = 3f;
        readonly Dictionary<Adventurer, Queue<string>> _history = new();
        readonly Dictionary<Adventurer, (State<Adventurer> state, Unit target)> _seen = new();
        readonly Dictionary<Adventurer, (Unit target, float hour)> _fightingSince = new();

        void Update()
        {
            if (_clock == null && !Services.TryGet<GameClock>(out _clock)) return;
            if (!Services.TryGet<GuildRoster>(out var roster)) return;
            foreach (var a in roster.Members)
            {
                if (a == null || a.Fsm.Current == null) continue;
                Count(a);
                var now = (a.Fsm.Current, a.Engage.Target);
                if (_seen.TryGetValue(a, out var was) && was.state == now.Current && was.target == now.Target) continue;
                _seen[a] = now;
                if (!_history.TryGetValue(a, out var h)) _history[a] = h = new Queue<string>();
                h.Enqueue($"{_clock.FormatTime()} '{a.StateLabel}' {a.Position} hp={a.HpRatio:0.00}" + (now.Target != null ? $" vs {now.Target.DisplayName}" : ""));
                while (h.Count > HistoryLength) h.Dequeue();
            }
        }

        string History(Adventurer a)
            => _history.TryGetValue(a, out var h) ? "\n      before: " + string.Join("\n              ", h) : "";

        /// <summary>A member's fight as it stands: can it reach, can it shoot, when it strikes next.</summary>
        static string Fight(Unit self, Unit target)
        {
            if (target == null) return "";
            bool channeling = false;
            foreach (var s in self.Skills) channeling |= s.IsChanneling;
            return $" [{self.DisplayName}→{target.DisplayName} dist={Vector2.Distance(self.Position, target.Position):0.00} range={self.Stats.AttackRange:0.#}" +
                   $"{(self.Stats.IsRanged ? " ranged" : "")} inRange={Combat.InRange(self, target)} shot={Combat.HasShot(self, target)}" +
                   $" nextAtk={self.NextAttackTime - Time.time:0.0}s channeling={channeling} stunned={self.IsStunned}" +
                   // Why two melee fighters stand apart (P4-80: a boss and a goblin warrior, 2.6 cells, ten hours).
                   $" moving={self.Mover.IsMoving} vel={self.Mover.Velocity.magnitude:0.0} dest={self.Mover.Destination}" +
                   (self.CombatEngagement is { } e ? $" holdOff={e.IsHoldingOff} searching={e.IsSearching} steering={e.IsSteering} ignoring={e.IsIgnoring(target)}" : "") +
                   (self is Monster m ? $" state='{m.Fsm.Current?.Label}'" : "") + "]";
        }

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
                // Where and why too (P4-80: "travelling 8.6h" alone could not be traced afterwards).
                if (hours > LimitHours(a, s))
                    Report($"{a.Data.name}: '{s.Label}' {hours:0.0}h (limit {LimitHours(a, s):0}) at {a.Position} hp={a.HpRatio:0.00}{Context(a)}{TallyOf(a)}{History(a)}", $"long:{a.Data.name}:{s.Label}");

                // The same fight for hours (P4-80: a priest and a stone slime on 7 health, ten hours).
                var target = a.Engage.Target;
                if (target == null) _fightingSince.Remove(a);
                else if (!_fightingSince.TryGetValue(a, out var fight) || fight.target != target) _fightingSince[a] = (target, now);
                else if (now - fight.hour >= LongFightHours)
                    Report($"{a.Data.name}: '{s.Label}' fighting {Strength(target)} for {now - fight.hour:0.0}h at {a.Position}{Context(a)}" +
                           $"{(target.CurrentTarget is { } back ? Fight(target, back) : "")}{TallyOf(a)}{History(a)}", $"fight:{a.Data.name}:{target.GetInstanceID()}:{Mathf.RoundToInt(fight.hour)}");

                // Standing still counts from when it last moved or last changed what it was doing (P4-76:
                // three hours' rest by the guild door, then a hunt mustering on the same spot, read as a
                // muster stuck for three hours).
                if (!_lastMove.TryGetValue(a, out var last) || (a.Position - last.pos).sqrMagnitude > 1f || last.state != s)
                    _lastMove[a] = (a.Position, now, s);
                else if (Walking(a, s) && a.Engage.Target == null && now - last.hour >= 2f) // a fight of twenty seconds is two game hours
                    Report($"{a.Data.name}: '{s.Label}' but has not moved for {now - last.hour:0.0}h at {a.Position}{Context(a)}{TallyOf(a)}{History(a)}", $"still:{a.Data.name}:{s.Label}:{Mathf.RoundToInt(last.hour)}");
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
                // Both sides' strength too (P4-80: two fights went on for ten hours, one side a stone
                // slime boss; who could not hurt whom was gone with the fight by the time anyone looked).
                if (hours > limit)
                {
                    var mt = m.Engage.Target;
                    Report($"monster {Strength(m)} '{ms.Label}' {hours:0.0}h at {m.Position} (target {(mt != null ? Strength(mt) : "-")}){Fight(m, mt)}" +
                           (mt is Adventurer foe ? $"{Fight(foe, foe.Engage.Target)} '{foe.StateLabel}'{TallyOf(foe)}{History(foe)}" : mt?.CurrentTarget is { } back ? Fight(mt, back) : ""),
                        $"mlong:{m.GetInstanceID()}:{ms.Label}");
                }
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
                    // An outing still open with everyone who went out back home: its split never comes
                    // (P4-76), and its leader waits for it.
                    bool anyoneOut = p.Out.Keys.Any(m => m != null && m.IsAlive && m.IsGuildMember && !p.Arrived.Contains(m)
                                                         && !(m.Fsm.Current == m.IdleState || m.Fsm.Current == m.DecidingState || m.Fsm.Current == m.RestState));
                    if (p.OnOuting && !p.Settling && !anyoneOut)
                    {
                        if (!_openOutingSince.TryGetValue(p, out var open)) _openOutingSince[p] = open = now;
                        if (now - open >= 3f) Report($"{p.Name}: outing still open {now - open:0}h with nobody out (arrived {p.Arrived.Count}/{p.Out.Count})", $"outing:{p.Id}:{Mathf.RoundToInt(open)}");
                    }
                    else _openOutingSince.Remove(p);
                    if (spread < SplitDistance) { _splitSince.Remove(p); continue; }
                    if (!_splitSince.TryGetValue(p, out var since)) _splitSince[p] = since = now;
                    if (now - since >= 1f)
                        Report($"{p.Name} split {spread:0} cells apart for {now - since:0.0}h: " +
                               string.Join(" | ", outside.Select(m => $"{m.Data.name} '{m.StateLabel}' {m.Position}{Context(m)}")), $"split:{p.Id}:{Mathf.RoundToInt(since)}");
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
            sb.Append($" moving={a.Mover.IsMoving} steering={a.Mover.IsSteering} speed={a.Mover.Speed:0.0}x{a.Mover.SpeedMultiplier:0.00} stunned={a.IsStunned} " +
                      $"dest={a.Mover.Destination} target={a.Engage.Target?.DisplayName ?? "-"} searching={a.Engage.IsSearching}");
            // P4-81: a member stood for hours 'moving' next to rock: the walk's own state and the ground under it.
            var cell = World.GridMap.WorldToCell(a.Position);
            var grid = Services.Get<World.WorldService>().Grid;
            sb.Append($" plan[{a.Mover.DebugPlan} buff={a.Mover.BuffMultiplier:0.00} cost={(grid.InBounds(cell) ? grid.Cost(cell) : -1f):0.00} v={a.Mover.Velocity}]");
            if (Services.TryGet<PartyTactics>(out var tactics) && tactics.LeaderOf(a) is { } leader)
            {
                sb.Append($" leader={leader.Data.name}('{leader.StateLabel}' {leader.Position})");
                var party = tactics.Party(a, new List<Adventurer>());
                sb.Append(" party[" + string.Join("/", party.Select(m => $"{m.Data.name}:{m.StateLabel}@{Vector2.Distance(m.Position, a.Position):0}{(m.Engage.Target != null ? "*" : "")}")) + "]");
            }
            if (Services.TryGet<HuntBandService>(out var bands) && bands.BandOf(a) is { } band)
                sb.Append($" band[{string.Join("/", band.Members.Select(m => $"{m.Data.name}:{m.StateLabel}"))}] mustering={band.Mustering} paused={band.Paused} spot={(band.HasSpot ? band.Spot.ToString() : "-")}");
            if (a.CurrentQuest is { } q) sb.Append($" quest={q.Id}:{q.Status} point={q.Point}");
            sb.Append(Fight(a, a.Engage.Target));
            // Idle in town: whom it waits for (P4-80: a party member idle 12 hours while its leader was out).
            if (Services.TryGet<GuildParties>(out var parties) && parties.PartyOf(a) is { } standing)
                sb.Append($" standing[{standing.Name} leader={standing.Leader?.Data.name}('{standing.Leader?.StateLabel}') follows={parties.FollowsALeader(a)}" +
                          $" outing={standing.OnOuting} unfinished={parties.OutingUnfinished(a)} gettingReady={parties.MembersGettingReady(a)}]");
            // Guarding the town with no target for hours (P4-80): what it is after and why it does not fight it.
            if (a.Fsm.Current == a.TownGuardState)
            {
                var intruder = TownGuardState.FindIntruder(a);
                sb.Append(intruder == null ? " intruder=-"
                    : $" intruder={Strength(intruder)} at {intruder.Position} dist={Vector2.Distance(intruder.Position, a.Position):0.0}" +
                      $" inTown={AdvCtx.Town.IsInTown(intruder.Position)} breaking={(intruder as Monster)?.IsBreakingGate} canSee={a.CanSee(intruder)}" +
                      $" targetable={intruder.IsTargetableBy(a)} ignored={a.Engage.IsIgnoring(intruder)} state='{(intruder as Monster)?.Fsm.Current?.Label}'");
            }
            return sb.ToString();
        }

        /// <summary>Name, id, level, health and fighting numbers of a unit, for a report.</summary>
        static string Strength(Unit u)
            => $"{u.DisplayName}#{u.GetInstanceID()} Lv{(u is Monster m ? m.Level : u is Adventurer a ? a.Data.level : 0)} hp={u.Hp:0}/{u.Stats.MaxHp:0} atk={u.Stats.Attack:0.#} def={u.Stats.Defense:0.#}";

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
