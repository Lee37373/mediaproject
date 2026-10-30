using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Disasters;
using GuildMaster.Dragons;
using GuildMaster.Ecosystem;
using GuildMaster.Incidents;
using GuildMaster.Recruitment;
using GuildMaster.Town;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Storytelling
{
    /// <summary>The storyteller set off a big event (P4-111a). Nothing tells the player so: the event tells itself, as it always did.</summary>
    public struct StoryBeatEvent { public StoryBeat Beat; public string Detail; public float Tension; }

    /// <summary>
    /// The storyteller (P4-111a, G5 — user decisions 2026-10-30; rules and numbers in <see cref="StorytellerRules"/>). It
    /// keeps a tension of what the guild has been through and, by it and the guild's strength, times and sizes the big
    /// events itself — a town raid, a horde, the dragon's flight, a disaster — with a breather after each, and holds them
    /// all off for a guild near its end. What the wilds do of themselves (dens raiding, nests moving in, newcomers
    /// crowding the far land, a fight setting off a disaster) goes on as before, times <see cref="EcoFactor"/>. Unseen:
    /// a director, not a character. Off (<see cref="Enabled"/>), every system rolls its own as before. Saved as "storyteller".
    /// </summary>
    public class StorytellerService : MonoBehaviour, ISaveable
    {
        /// <summary>Tests: off leaves every system to its own rolls (factor 1), as before P4-111.</summary>
        public static bool Enabled = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Enabled = true;

        /// <summary>The storyteller times the big events: the horde's hourly roll and the dragon's daily one stand down.</summary>
        public static bool Directs => Enabled && Services.TryGet<StorytellerService>(out _);

        /// <summary>What the wilds' own rolls are multiplied by now (1 without a storyteller).</summary>
        public static float EcoFactor => Enabled && Services.TryGet<StorytellerService>(out var s) ? StorytellerRules.EcoFactor(s.Phase) : 1f;

        /// <summary>Quiet enough that a homeless pack waits before it comes at the town.</summary>
        public static bool HoldsOff => EcoFactor < StorytellerRules.HoldOffBelow;

        public struct Record
        {
            public StoryBeat Beat;
            public float Hour;
            public string Detail;
        }

        const int MaxHistory = 24;
        static readonly StoryBeat[] Beats = { StoryBeat.TownRaid, StoryBeat.Horde, StoryBeat.DragonFlight, StoryBeat.Disaster };

        GameClock _clock;
        WorldService _world;
        EcosystemManager _eco;
        GuildRoster _roster;
        int _seed;

        float _tension;
        float _nextBeatHour;
        float _relaxUntil = -1f, _recoverUntil = -1f;
        /// <summary>Tension went past <see cref="StorytellerRules.RelaxAtTension"/>: a breather until it falls back.</summary>
        bool _tense;
        /// <summary>This recovery has brought a visitor forward already.</summary>
        bool _visitorCalled;
        int _last = -1;
        readonly List<float> _deaths = new();
        readonly List<Record> _history = new();

        public float Tension => _tension;
        public float NextBeatHour => _nextBeatHour;
        public IReadOnlyList<Record> History => _history;

        float Now => _clock != null ? _clock.TotalHours : 0f;

        public StoryPhase Phase
        {
            get
            {
                float now = Now;
                if (now < _recoverUntil) return StoryPhase.Recovery;
                return now < _relaxUntil || _tense ? StoryPhase.Relax : StoryPhase.Calm;
            }
        }

        /// <summary>The guild's strength: its living members' power (level and gear, the inner buildings' scale).</summary>
        public float GuildPower => Members().Sum(m => AdventurerStats.Power(m.Data));

        IEnumerable<Adventurer> Members()
            => _roster == null ? Enumerable.Empty<Adventurer>() : _roster.Members.Where(m => m != null && m.IsAlive && m.IsGuildMember && !m.IsVisitor);

        public void Init(GameClock clock, WorldService world, EcosystemManager eco, GuildRoster roster, int seed)
        {
            _clock = clock;
            _world = world;
            _eco = eco;
            _roster = roster;
            _seed = seed;
            _nextBeatHour = StorytellerRules.FirstBeatHour(StorytellerRules.Stream(seed, 0, 1).Value);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<AdventurerDiedEvent>(OnMemberDied);
            EventBus.Subscribe<AdventurerRetreatedEvent>(OnRetreated);
            EventBus.Subscribe<BuildingDamagedEvent>(OnBuildingDamaged);
            EventBus.Subscribe<BuildingDestroyedEvent>(OnBuildingDestroyed);
            EventBus.Subscribe<GateBrokenEvent>(OnGateBroken);
            EventBus.Subscribe<RaidSightedEvent>(OnRaidSighted);
            EventBus.Subscribe<DragonSightedEvent>(OnDragonSighted);
            EventBus.Subscribe<DragonPlunderedEvent>(OnPlundered);
            EventBus.Subscribe<DisasterSightedEvent>(OnDisasterSighted);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            EventBus.Unsubscribe<AdventurerDiedEvent>(OnMemberDied);
            EventBus.Unsubscribe<AdventurerRetreatedEvent>(OnRetreated);
            EventBus.Unsubscribe<BuildingDamagedEvent>(OnBuildingDamaged);
            EventBus.Unsubscribe<BuildingDestroyedEvent>(OnBuildingDestroyed);
            EventBus.Unsubscribe<GateBrokenEvent>(OnGateBroken);
            EventBus.Unsubscribe<RaidSightedEvent>(OnRaidSighted);
            EventBus.Unsubscribe<DragonSightedEvent>(OnDragonSighted);
            EventBus.Unsubscribe<DragonPlunderedEvent>(OnPlundered);
            EventBus.Unsubscribe<DisasterSightedEvent>(OnDisasterSighted);
        }

        // ---------------- Tension ----------------

        void Feel(float amount)
        {
            if (!Enabled || amount <= 0f) return;
            _tension = StorytellerRules.Add(_tension, amount);
            if (_tension >= StorytellerRules.RelaxAtTension) _tense = true;
        }

        void OnMemberDied(AdventurerDiedEvent e)
        {
            if (e.Adventurer == null || !e.Adventurer.IsGuildMember || e.Adventurer.IsVisitor) return;
            _deaths.Add(Now);
            Feel(StorytellerRules.MemberDied);
            CheckRecovery();
        }

        void OnRetreated(AdventurerRetreatedEvent e)
        {
            if (e.Adventurer != null && e.Adventurer.IsGuildMember && !e.Adventurer.IsVisitor) Feel(StorytellerRules.NearDeathRetreat);
        }

        void OnBuildingDamaged(BuildingDamagedEvent e)
        {
            if (e.Building != null && e.Building.MaxHp > 0f) Feel(StorytellerRules.BuildingDamagePerWhole * e.Amount / e.Building.MaxHp);
        }

        void OnBuildingDestroyed(BuildingDestroyedEvent e) => Feel(StorytellerRules.BuildingDestroyed);
        void OnGateBroken(GateBrokenEvent e) => Feel(StorytellerRules.GateBroken);
        void OnRaidSighted(RaidSightedEvent e) => Feel(StorytellerRules.RaidSighted);
        void OnDragonSighted(DragonSightedEvent e) => Feel(StorytellerRules.DragonSighted);
        void OnPlundered(DragonPlunderedEvent e) => Feel(StorytellerRules.Plundered);
        void OnDisasterSighted(DisasterSightedEvent e) => Feel(StorytellerRules.DisasterSighted);

        /// <summary>Debug: sets the tension.</summary>
        public void SetTension(float value)
        {
            _tension = Mathf.Clamp(value, 0f, StorytellerRules.MaxTension);
            _tense = _tension >= StorytellerRules.RelaxAtTension;
        }

        // ---------------- The round ----------------

        void OnHour(HourPassedEvent e)
        {
            if (!Enabled) return;
            float now = Now;
            _tension = StorytellerRules.Decay(_tension, 1f);
            if (_tense && _tension < StorytellerRules.RelaxEndsBelow) _tense = false;
            CheckRecovery();
            if (Phase == StoryPhase.Recovery)
            {
                _nextBeatHour = Mathf.Max(_nextBeatHour, _recoverUntil + StorytellerRules.AfterRecoveryHours);
                return;
            }
            _visitorCalled = false;
            if (now < _nextBeatHour) return;
            if (_tension >= StorytellerRules.HoldAtTension) { _nextBeatHour = now + StorytellerRules.HoldHours; return; }
            if (Busy()) { _nextBeatHour = now + StorytellerRules.BusyHoldHours; return; }
            int hour = Mathf.FloorToInt(now);
            if (!Fire(hour, null)) { _nextBeatHour = now + StorytellerRules.HoldHours; return; }
            _nextBeatHour = now + StorytellerRules.Gap(_clock.Day, StorytellerRules.Stream(_seed, hour, 3).Value);
        }

        /// <summary>A guild near its end: nothing big for a while, the wilds quieter, and someone comes to the door soon.</summary>
        void CheckRecovery()
        {
            float now = Now;
            _deaths.RemoveAll(h => now - h > StorytellerRules.DeathWindowHours);
            if (!StorytellerRules.NearTheEnd(Members().Count(), _deaths.Count)) return;
            _recoverUntil = now + StorytellerRules.RecoveryHours;
            if (_visitorCalled) return;
            _visitorCalled = true;
            if (Services.TryGet<VisitorService>(out var visitors)) visitors.CallSoon(StorytellerRules.VisitorWithinHours);
        }

        /// <summary>A raid or a horde is still on, or the dragon is up: the next one waits rather than piles on.</summary>
        bool Busy()
        {
            if (Services.TryGet<WaveManager>(out var waves) && waves.RaidActive) return true;
            if (Services.TryGet<IncidentService>(out var incidents) && incidents.Active.Any(i => i.Kind == IncidentKind.Horde)) return true;
            return Services.TryGet<DragonService>(out var dragon) && dragon.Dragon != null && dragon.Dragon.IsAlive && !dragon.Asleep;
        }

        /// <summary>
        /// Draws a big event by weight and sets it off; one that cannot happen now (no den near, no site, the dragon away)
        /// drops out of the draw. <paramref name="only"/>: that one (debug). False when none could.
        /// </summary>
        public bool Fire(int hour, StoryBeat? only)
        {
            int day = _clock.Day;
            var rng = StorytellerRules.Stream(_seed, hour, 2);
            var tried = new HashSet<StoryBeat>();
            StoryBeat? last = _last >= 0 ? (StoryBeat)_last : null;
            for (int attempt = 0; attempt < Beats.Length; attempt++)
            {
                StoryBeat pick;
                if (only.HasValue)
                {
                    if (attempt > 0) return false;
                    pick = only.Value;
                }
                else
                {
                    float total = 0f;
                    foreach (var b in Beats) if (!tried.Contains(b)) total += StorytellerRules.Weight(b, day, last, SeasonWeight(b));
                    if (total <= 0f) return false;
                    float roll = rng.Range(0f, total);
                    pick = Beats[^1];
                    foreach (var b in Beats)
                    {
                        if (tried.Contains(b)) continue;
                        float w = StorytellerRules.Weight(b, day, last, SeasonWeight(b));
                        if (w <= 0f) continue;
                        pick = b;
                        if ((roll -= w) <= 0f) break;
                    }
                }
                tried.Add(pick);
                if (!TrySet(pick, rng, out string detail)) continue;
                _last = (int)pick;
                _relaxUntil = Now + StorytellerRules.RelaxHours;
                _history.Add(new Record { Beat = pick, Hour = Now, Detail = detail });
                if (_history.Count > MaxHistory) _history.RemoveAt(0);
                Debug.Log($"[Storyteller] {StorytellerRules.Name(pick)}: {detail} (tension {_tension:0}, power {GuildPower:0})");
                EventBus.Publish(new StoryBeatEvent { Beat = pick, Detail = detail, Tension = _tension });
                return true;
            }
            return false;
        }

        /// <summary>The season's say in the draw (P4-111b).</summary>
        static float SeasonWeight(StoryBeat b) => 1f;

        bool TrySet(StoryBeat beat, GameRandom rng, out string detail)
        {
            detail = null;
            switch (beat)
            {
                case StoryBeat.TownRaid: return SendRaid(rng, out detail);
                case StoryBeat.Horde:
                {
                    if (!Services.TryGet<IncidentService>(out var incidents)) return false;
                    int size = StorytellerRules.HordeSize(GuildPower);
                    var horde = incidents.StartHorde(size: size);
                    if (horde == null) return false;
                    detail = $"{horde.SpeciesName} {horde.HordeSize}마리 Lv{horde.HordeLevel} → {horde.Site?.DisplayName}";
                    return true;
                }
                case StoryBeat.DragonFlight:
                {
                    if (!Services.TryGet<DragonService>(out var dragon) || !dragon.WakeForStory(_clock.Day)) return false;
                    detail = "깨어나 날아오름";
                    return true;
                }
                default: return StartDisaster(rng, out detail);
            }
        }

        /// <summary>A raiding kind's den near the town sends its band, as many as the guild's strength calls for (and it has).</summary>
        bool SendRaid(GameRandom rng, out string detail)
        {
            detail = null;
            if (!Services.TryGet<WaveManager>(out var waves)) return false;
            Services.TryGet<MigrationService>(out var migration);
            Services.TryGet<Villages.VillageRaids>(out var villageRaids);
            var town = _world.TownCenterWorld;
            float range = StorytellerRules.RaidRange + _world.TownGrowth;
            var dens = _eco.Nests.Where(n => n.Species.canRaid && !n.IsEmpty && !n.IsLair && !n.Sealed && !waves.IsRaiding(n)
                                             && (migration == null || !migration.IsMigrating(n))
                                             && (villageRaids == null || !villageRaids.IsRaiding(n))
                                             && Vector2.Distance(n.WorldPosition, town) <= range
                                             && waves.AtHome(n) >= StorytellerRules.RaidMin)
                                 .ToList();
            if (dens.Count == 0) return false;
            float total = dens.Sum(n => waves.AtHome(n));
            float roll = rng.Range(0f, total);
            var den = dens[^1];
            foreach (var n in dens)
                if ((roll -= waves.AtHome(n)) <= 0f) { den = n; break; }
            int sent = waves.Launch(den, StorytellerRules.RaidSize(GuildPower));
            if (sent <= 0) return false;
            detail = $"{den.DisplayName} {den.Species.displayName} {sent}마리 ({Vector2.Distance(den.WorldPosition, town):0}칸)";
            return true;
        }

        /// <summary>A disaster monster's den on the right ground sets one off — the cause is there to see.</summary>
        bool StartDisaster(GameRandom rng, out string detail)
        {
            detail = null;
            if (!Services.TryGet<DisasterService>(out var disasters)) return false;
            var town = _world.TownCenterWorld;
            var dens = new List<(Nest nest, DisasterKind kind)>();
            foreach (var n in _eco.Nests)
            {
                if (n.IsEmpty || n.IsLair || n.Sealed) continue;
                float d = Vector2.Distance(n.WorldPosition, town);
                if (d < StorytellerRules.DisasterMinDistance || d > StorytellerRules.DisasterMaxDistance) continue;
                foreach (var id in n.Species.skillIds)
                    if (DisasterRules.KindOfSkill(id) is { } kind) { dens.Add((n, kind)); break; }
            }
            rng.Shuffle(dens);
            foreach (var (nest, kind) in dens)
            {
                var d = disasters.StartNear(kind, nest.WorldPosition, StorytellerRules.DisasterSearch, rng);
                if (d == null) continue;
                detail = $"{DisasterRules.NameOf(kind)} — {nest.DisplayName} 옆 ({Vector2.Distance(nest.WorldPosition, town):0}칸)";
                return true;
            }
            return false;
        }

        // ---------------- Debug ----------------

        public string Describe()
        {
            var s = new StringBuilder();
            float now = Now;
            s.AppendLine($"스토리텔러 {(Enabled ? "켬" : "끔")}: {StorytellerRules.Name(Phase)}, 긴장도 {_tension:0.0}, 길드 힘 {GuildPower:0.0} ({Members().Count()}명), 생태 배율 {EcoFactor:0.00}");
            s.AppendLine($"다음 큰 사건: {_nextBeatHour - now:0.0}시간 뒤 ({_nextBeatHour:0.0}시), 완화 끝 {_relaxUntil:0.0}, 회복기 끝 {_recoverUntil:0.0}, 48시간 사망 {_deaths.Count}");
            s.AppendLine($"크기: 침공 {StorytellerRules.RaidSize(GuildPower)} · 대규모 무리 {StorytellerRules.HordeSize(GuildPower)}");
            foreach (var r in _history) s.AppendLine($"  {r.Hour:0.0}시 {StorytellerRules.Name(r.Beat)}: {r.Detail}");
            return s.ToString();
        }

        // ---------------- Save ----------------

        [System.Serializable]
        class SavedRecord
        {
            public int beat;
            public float hour;
            public string detail;
        }

        [System.Serializable]
        class SaveData
        {
            public float tension, next_beat, relax_until = -1f, recover_until = -1f;
            public bool tense, visitor_called;
            public int last = -1;
            public List<float> deaths = new();
            public List<SavedRecord> history = new();
        }

        public string SaveKey => "storyteller";

        public string Capture()
        {
            var s = new SaveData
            {
                tension = _tension, next_beat = _nextBeatHour, relax_until = _relaxUntil, recover_until = _recoverUntil,
                tense = _tense, visitor_called = _visitorCalled, last = _last, deaths = new List<float>(_deaths),
            };
            foreach (var r in _history) s.history.Add(new SavedRecord { beat = (int)r.Beat, hour = r.Hour, detail = r.Detail });
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            _tension = s.tension;
            _nextBeatHour = s.next_beat;
            _relaxUntil = s.relax_until;
            _recoverUntil = s.recover_until;
            _tense = s.tense;
            _visitorCalled = s.visitor_called;
            _last = s.last;
            _deaths.Clear();
            _deaths.AddRange(s.deaths);
            _history.Clear();
            foreach (var r in s.history) _history.Add(new Record { Beat = (StoryBeat)r.beat, Hour = r.hour, Detail = r.detail });
        }
    }
}
