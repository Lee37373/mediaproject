using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Economy;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using GuildMaster.Town;
using UnityEngine;

namespace GuildMaster.Session
{
    /// <summary>
    /// Balance telemetry (P5-1): samples the economy, the roster and the ecosystem once per
    /// game hour and writes the run to a CSV so the numbers can be compared between passes.
    /// Created on demand by <see cref="DebugCommands"/>; it never runs in a normal session.
    /// </summary>
    public class BalanceProbe : MonoBehaviour
    {
        public static BalanceProbe Instance { get; private set; }

        public static BalanceProbe Ensure()
        {
            if (Instance != null) return Instance;
            Instance = new GameObject("BalanceProbe").AddComponent<BalanceProbe>();
            return Instance;
        }

        readonly List<string> _rows = new();
        readonly StringBuilder _sb = new();

        GameClock _clock;
        GuildTreasury _treasury;
        GuildRoster _roster;
        EcosystemManager _eco;
        QuestBoard _board;
        TownService _town;
        GuildIntel _intel;
        Player.PlayerAvatar _master;
        MonsterDefinition[] _species;

        /// <summary>Game day the run stops at (inclusive). 0 = run until stopped by hand.</summary>
        public int StopAfterDays { get; set; }
        public bool Running { get; private set; }
        public string CsvPath { get; private set; }
        public int Samples => _rows.Count;

        // Cumulative counters, all collected from events so nothing is polled per frame.
        int _deaths, _joins, _levelUps, _questsPosted, _questsCompleted, _questsFailed, _questsCancelled;
        int _raidsSighted, _raidersKilled, _gearBought, _extinctions, _nestsCleared, _recolonized;
        int _startDay;
        float _startHour;

        public void Begin(int days, string csvPath)
        {
            _clock = Services.Get<GameClock>();
            _treasury = Services.Get<GuildTreasury>();
            _roster = Services.Get<GuildRoster>();
            _eco = Services.Get<EcosystemManager>();
            _board = Services.Get<QuestBoard>();
            Services.TryGet(out _town);
            Services.TryGet(out _intel);
            Services.TryGet(out _master);
            _species = _eco.Species.ToArray();

            _rows.Clear();
            _deaths = _joins = _levelUps = 0;
            _questsPosted = _questsCompleted = _questsFailed = _questsCancelled = 0;
            _raidsSighted = _raidersKilled = _gearBought = _extinctions = _nestsCleared = _recolonized = 0;
            StopAfterDays = days;
            CsvPath = csvPath;
            _startDay = _clock.Day;
            _startHour = _clock.TotalHours;
            Running = true;

            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Subscribe<AdventurerJoinedEvent>(OnJoined);
            EventBus.Subscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Subscribe<AdventurerGearUpgradedEvent>(OnGear);
            EventBus.Subscribe<QuestPostedEvent>(OnPosted);
            EventBus.Subscribe<QuestClosedEvent>(OnClosed);
            EventBus.Subscribe<RaidSightedEvent>(OnRaidSighted);
            EventBus.Subscribe<RaidEndedEvent>(OnRaidEnded);
            EventBus.Subscribe<SpeciesExtinctEvent>(OnExtinct);
            EventBus.Subscribe<NestClearedEvent>(OnNestCleared);
            EventBus.Subscribe<NestRecolonizedEvent>(OnRecolonized);

            Sample(); // day 0 baseline
        }

        public void Stop()
        {
            if (!Running) return;
            Running = false;
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            EventBus.Unsubscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Unsubscribe<AdventurerJoinedEvent>(OnJoined);
            EventBus.Unsubscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Unsubscribe<AdventurerGearUpgradedEvent>(OnGear);
            EventBus.Unsubscribe<QuestPostedEvent>(OnPosted);
            EventBus.Unsubscribe<QuestClosedEvent>(OnClosed);
            EventBus.Unsubscribe<RaidSightedEvent>(OnRaidSighted);
            EventBus.Unsubscribe<RaidEndedEvent>(OnRaidEnded);
            EventBus.Unsubscribe<SpeciesExtinctEvent>(OnExtinct);
            EventBus.Unsubscribe<NestClearedEvent>(OnNestCleared);
            EventBus.Unsubscribe<NestRecolonizedEvent>(OnRecolonized);
            Flush();
        }

        void OnDestroy()
        {
            if (Running) Stop();
            if (Instance == this) Instance = null;
        }

        void OnDied(AdventurerDiedEvent e) => _deaths++;
        void OnJoined(AdventurerJoinedEvent e) => _joins++;
        void OnLevelUp(AdventurerLevelUpEvent e) => _levelUps++;
        void OnGear(AdventurerGearUpgradedEvent e) => _gearBought++;
        void OnPosted(QuestPostedEvent e) => _questsPosted++;
        void OnRaidSighted(RaidSightedEvent e) => _raidsSighted++;
        void OnRaidEnded(RaidEndedEvent e) => _raidersKilled += e.Killed;
        void OnExtinct(SpeciesExtinctEvent e) => _extinctions++;
        void OnNestCleared(NestClearedEvent e) => _nestsCleared++;
        void OnRecolonized(NestRecolonizedEvent e) => _recolonized++;

        void OnClosed(QuestClosedEvent e)
        {
            switch (e.Status)
            {
                case QuestStatus.TurnedIn:
                case QuestStatus.Completed: _questsCompleted++; break;
                case QuestStatus.Failed: _questsFailed++; break;
                case QuestStatus.Cancelled: _questsCancelled++; break;
            }
        }

        void OnHour(HourPassedEvent e)
        {
            Sample();
            if (StopAfterDays > 0 && _clock.TotalHours - _startHour >= StopAfterDays * 24f) Stop();
        }

        static string Header(IEnumerable<MonsterDefinition> species)
        {
            var sb = new StringBuilder(
                "day,hour,gold,guild_exp,members,avg_level,max_level,avg_gear,avg_fatigue," +
                "deaths,joins,level_ups,posted,completed,failed,cancelled," +
                "raids_sighted,raiders_killed,gear_bought,extinct,nests_cleared,recolonized," +
                "open_quests,potions,townhall_hp,master_hp,known_nests,total_pop,nests");
            foreach (var s in species) sb.Append(",pop_").Append(s.id).Append(",nest_").Append(s.id);
            return sb.ToString();
        }

        void Sample()
        {
            var members = _roster.Members;
            float avgLevel = 0f, avgGear = 0f, avgFatigue = 0f;
            int maxLevel = 0;
            foreach (var a in members)
            {
                avgLevel += a.Data.level;
                avgGear += a.Data.gearTier;
                avgFatigue += a.Data.fatigue;
                if (a.Data.level > maxLevel) maxLevel = a.Data.level;
            }
            int n = Mathf.Max(1, members.Count);
            avgLevel /= n; avgGear /= n; avgFatigue /= n;

            float hallHp = 0f;
            var hall = _town != null ? _town.Get(BuildingType.TownHall) : null;
            if (hall != null) hallHp = hall.Hp;

            int openQuests = _board.Quests.Count(q => q.IsActive);
            int potions = _treasury.GetStock(DefaultContent.Potion);
            int totalPop = _eco.TotalMonsters;
            int nests = _eco.Nests.Count(x => !x.IsEmpty);

            _sb.Clear();
            var c = CultureInfo.InvariantCulture;
            _sb.Append(_clock.Day).Append(',').Append(_clock.Hour).Append(',')
               .Append(_treasury.Gold).Append(',').Append(_treasury.Exp).Append(',')
               .Append(members.Count).Append(',')
               .Append(avgLevel.ToString("0.00", c)).Append(',').Append(maxLevel).Append(',')
               .Append(avgGear.ToString("0.00", c)).Append(',').Append(avgFatigue.ToString("0.0", c)).Append(',')
               .Append(_deaths).Append(',').Append(_joins).Append(',').Append(_levelUps).Append(',')
               .Append(_questsPosted).Append(',').Append(_questsCompleted).Append(',')
               .Append(_questsFailed).Append(',').Append(_questsCancelled).Append(',')
               .Append(_raidsSighted).Append(',').Append(_raidersKilled).Append(',')
               .Append(_gearBought).Append(',').Append(_extinctions).Append(',')
               .Append(_nestsCleared).Append(',').Append(_recolonized).Append(',')
               .Append(openQuests).Append(',').Append(potions).Append(',')
               .Append(hallHp.ToString("0", c)).Append(',')
               .Append((_master != null ? _master.Hp : 0f).ToString("0", c)).Append(',')
               .Append(_intel != null ? _intel.KnownNests.Count() : 0).Append(',')
               .Append(totalPop).Append(',').Append(nests);
            foreach (var s in _species)
                _sb.Append(',').Append(_eco.GetPopulation(s)).Append(',').Append(_eco.NestsOf(s).Count(x => !x.IsEmpty));
            _rows.Add(_sb.ToString());
        }

        void Flush()
        {
            if (string.IsNullOrEmpty(CsvPath)) return;
            var lines = new List<string> { Header(_species) };
            lines.AddRange(_rows);
            var dir = Path.GetDirectoryName(CsvPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllLines(CsvPath, lines);
            Debug.Log($"[Balance] {_rows.Count} samples -> {CsvPath}");
        }

        /// <summary>One-screen summary of the run so far (also written to the log on stop).</summary>
        public string Report()
        {
            if (_clock == null) return "not started";
            var sb = new StringBuilder();
            float days = (_clock.TotalHours - _startHour) / 24f;
            sb.AppendLine($"[balance] day {_startDay}..{_clock.Day} ({days:0.0}d, {_rows.Count} samples) running={Running}");
            sb.AppendLine($"  gold={_treasury.Gold} guildExp={_treasury.Exp} potions={_treasury.GetStock(DefaultContent.Potion)}");
            sb.AppendLine($"  members={_roster.Members.Count} joins={_joins} deaths={_deaths} levelUps={_levelUps} gearBought={_gearBought}");
            sb.AppendLine($"  quests posted={_questsPosted} completed={_questsCompleted} failed={_questsFailed} cancelled={_questsCancelled}");
            sb.AppendLine($"  raids sighted={_raidsSighted} raidersKilled={_raidersKilled} nestsCleared={_nestsCleared} recolonized={_recolonized} extinct={_extinctions}");
            foreach (var a in _roster.Members)
                sb.AppendLine($"    {a.Data.name} {AdventurerData.ClassLabel(a.Data.cls)} Lv{a.Data.level} ({a.Data.exp}/{a.Data.ExpToNextLevel}) gear={a.Data.gearTier} kills={a.Data.kills} quests={a.Data.questsCompleted}");
            foreach (var s in _species)
                sb.AppendLine($"    {s.id}: pop={_eco.GetPopulation(s)} nests={_eco.NestsOf(s).Count(x => !x.IsEmpty)}{(_eco.IsExtinct(s) ? " EXTINCT" : "")}");
            if (!string.IsNullOrEmpty(CsvPath)) sb.AppendLine("  csv: " + CsvPath);
            return sb.ToString();
        }
    }
}
