using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Monsters;
using GuildMaster.Town;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Session
{
    /// <summary>
    /// Debug: the ecosystem with no guild in it (user request 2026-09-22). The members are taken off
    /// the map, the guild master cannot be targeted, buildings and gates take blows without falling;
    /// then a snapshot per game day by distance ring (inner ≤ 45 cells, i.e. the raid ring; middle;
    /// outer): who holds the nests, their levels and danger, how long sites stay empty, and what
    /// happened (migrations, newcomers, raids, monster-on-monster kills). Read with <see cref="Report"/>.
    /// </summary>
    public class WildlifeProbe : MonoBehaviour
    {
        public static WildlifeProbe Instance { get; private set; }

        public static WildlifeProbe Ensure()
        {
            if (Instance != null) return Instance;
            Instance = new GameObject("WildlifeProbe").AddComponent<WildlifeProbe>();
            return Instance;
        }

        const float InnerRing = 45f;   // = WaveManager.RaidRadius
        const float MiddleRing = 100f;
        const float OuterRing = EcosystemManager.InnerEdge; // beyond it the 512 map's far land (P4-81)

        public bool Running { get; private set; }
        int _days, _startDay;
        float _startHour;
        int _settled, _newcomers, _monsterKills, _cleared, _routs, _pushedOut;
        int _raidsAtStart;
        int _maxEmptyHours;
        readonly List<string> _rows = new();
        string _first;

        public void Begin(int days)
        {
            var clock = Services.Get<GameClock>();
            _days = days;
            _startDay = clock.Day;
            _startHour = clock.TotalHours;
            _settled = _newcomers = _monsterKills = _cleared = _routs = _pushedOut = _maxEmptyHours = 0;
            _raidsAtStart = Services.Get<WaveManager>().RaidCount;
            _rows.Clear();
            _first = null;

            // No guild: members off the map, the master out of reach, the town standing.
            foreach (var a in Services.Get<GuildRoster>().Members.ToList()) a.gameObject.SetActive(false);
            Unit.GuildInvulnerable = true;
            Unit.GuildUntargetable = true;
            Building.DebugIndestructible = true;

            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<BandSettledEvent>(OnSettled);
            EventBus.Subscribe<NestRecolonizedEvent>(OnNewcomers);
            EventBus.Subscribe<UnitDiedEvent>(OnDied);
            EventBus.Subscribe<NestClearedEvent>(OnCleared);
            EventBus.Subscribe<IntrudersRoutedEvent>(OnRouted);
            EventBus.Subscribe<NestAbandonedEvent>(OnPushedOut);
            Running = true;
            _first = Snapshot("start");
        }

        void OnDestroy() => Stop();

        void Stop()
        {
            if (!Running) return;
            Running = false;
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            EventBus.Unsubscribe<BandSettledEvent>(OnSettled);
            EventBus.Unsubscribe<NestRecolonizedEvent>(OnNewcomers);
            EventBus.Unsubscribe<UnitDiedEvent>(OnDied);
            EventBus.Unsubscribe<NestClearedEvent>(OnCleared);
            EventBus.Unsubscribe<IntrudersRoutedEvent>(OnRouted);
            EventBus.Unsubscribe<NestAbandonedEvent>(OnPushedOut);
        }

        void OnPushedOut(NestAbandonedEvent e) => _pushedOut++;
        void OnSettled(BandSettledEvent e) => _settled++;
        void OnNewcomers(NestRecolonizedEvent e) => _newcomers++;
        void OnCleared(NestClearedEvent e) => _cleared++;
        void OnRouted(IntrudersRoutedEvent e) => _routs++;
        void OnDied(UnitDiedEvent e) { if (e.Victim is Monster && e.Killer is Monster) _monsterKills++; }

        void OnHour(HourPassedEvent e)
        {
            var clock = Services.Get<GameClock>();
            foreach (var n in Services.Get<EcosystemManager>().Nests) _maxEmptyHours = Mathf.Max(_maxEmptyHours, n.EmptyHours);
            if (clock.Hour == 0) _rows.Add(Snapshot($"day {clock.Day}"));
            if (clock.TotalHours - _startHour >= _days * 24f)
            {
                _rows.Add(Snapshot("end"));
                Stop();
            }
        }

        /// <summary>0 낮음 .. 2 높음, by the monsters' own danger (P4-58: their level, not their species).</summary>
        static int DangerRank(Monsters.Monster m) => (int)m.Danger;

        string Snapshot(string label)
        {
            var eco = Services.Get<EcosystemManager>();
            var town = Services.Get<World.WorldService>().TownCenterWorld;
            var sb = new StringBuilder(label.PadRight(7));
            (string name, float lo, float hi)[] rings = { ("in", 0f, InnerRing), ("mid", InnerRing, MiddleRing), ("out", MiddleRing, OuterRing), ("far", OuterRing, 999f) };
            foreach (var (name, lo, hi) in rings)
            {
                var nests = eco.Nests.Where(n => { float d = Vector2.Distance(n.WorldPosition, town); return d > lo && d <= hi; }).ToList();
                var held = nests.Where(n => !n.IsEmpty).ToList();
                var alive = held.SelectMany(n => n.Members).Where(m => m != null && m.IsAlive).ToList();
                float danger = alive.Count == 0 ? 0f : (float)alive.Average(m => DangerRank(m));
                float level = alive.Count == 0 ? 0f : (float)alive.Average(m => m.Level);
                int skilled = alive.Count(m => m.Tier >= CombatTier.Skilled);
                string kinds = string.Join(",", held.GroupBy(n => n.Species.id).OrderByDescending(g => g.Count()).Select(g => $"{Short(g.Key)}{g.Count()}"));
                sb.Append($" | {name} {held.Count}/{nests.Count} pop{alive.Count} lv{level:0.0} dng{danger:0.00} sk{skilled} [{kinds}]");
            }
            int total = eco.Nests.Sum(n => n.Members.Count(m => m != null && m.IsAlive));
            var roaming = Services.Get<RoamingPackService>().Packs;
            sb.Append($" | total{total} empty{eco.Nests.Count(n => n.IsEmpty)} roam{roaming.Count}(bound{roaming.Count(p => p.Bound != null)},{roaming.Sum(p => p.Members.Count)})");
            return sb.ToString();
        }

        static string Short(string id) => id switch
        {
            "slime" => "sl", "stone_slime" => "ss", "skull" => "sk", "goblin" => "gb", "goblin_warrior" => "gw", "slime_king" => "kg", _ => id,
        };

        public string Report()
        {
            var clock = Services.Get<GameClock>();
            var eco = Services.Get<EcosystemManager>();
            var sb = new StringBuilder($"[wildlife] day {_startDay}..{clock.Day} running={Running}\n");
            sb.AppendLine($"  migrations settled={_settled} newcomers settled={_newcomers} raids={Services.Get<WaveManager>().RaidCount - _raidsAtStart} "
                          + $"monster-on-monster kills={_monsterKills} nests cleared={_cleared} routs={_routs} pushed out={_pushedOut} longest empty={_maxEmptyHours}h");
            var levels = eco.Nests.SelectMany(n => n.Members).Where(m => m != null && m.IsAlive).GroupBy(m => m.Level).OrderBy(g => g.Key);
            sb.AppendLine("  levels now: " + string.Join(" ", levels.Select(g => $"Lv{g.Key}:{g.Count()}")));
            sb.AppendLine("  species now: " + string.Join(" ", eco.Species.Select(s => $"{s.id}={eco.GetPopulation(s)}/{eco.Nests.Count(n => n.Species == s && !n.IsEmpty)}n")));
            sb.AppendLine("  (per ring: held/sites, pop, mean level, mean danger 0-2, skilled+ count, [kind nests])");
            if (_first != null) sb.AppendLine("  " + _first);
            foreach (var r in _rows) sb.AppendLine("  " + r);
            return sb.ToString();
        }
    }
}
