using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using GuildMaster.Town;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Villages
{
    /// <summary>The guild saw a village under attack (P4-84): the village posts its defence.</summary>
    public struct VillageRaidSightedEvent { public Village Village; public int Raiders; public MonsterDefinition Species; }
    /// <summary>A raid on a village is over (<see cref="Sighted"/>: the guild saw it; <see cref="Fell"/>: the village fell to it).</summary>
    public struct VillageRaidEndedEvent { public Village Village; public int Killed; public int Withdrawn; public bool Sighted; public bool Fell; }

    /// <summary>
    /// Raids on the other villages (P4-84, user decision 2026-10-18: the raid rules plus packs passing by). A den
    /// within <see cref="VillageRules.RaidRadius"/> of a village sends part of its pack against it when its raid
    /// roll comes up (<see cref="WaveManager"/> rolls, as for our town); a migrating or roaming pack of a raiding
    /// kind that passes within <see cref="VillageRules.PassingRadius"/> turns on it, then goes on its way. When the
    /// guild sees raiders at a village, the village posts a defence quest (it pays, the guild keeps its fee).
    /// </summary>
    public class VillageRaids : MonoBehaviour, ISaveable, ISaveLink
    {
        const float CheckInterval = 0.25f;
        const float PassingCheckInterval = 1f;

        /// <summary>What a raider goes back to when the raid is over.</summary>
        enum After { Nest, Migrate, Roam }

        class Raid
        {
            public int Id;
            public Village Village;
            public MonsterDefinition Species;
            public readonly List<Monster> Members = new();
            public readonly Dictionary<Monster, After> Then = new();
            public float StartedAtHour;
            public bool Sighted;
            public int Killed;
            public Quest Quest;
            public bool Passing;
        }

        RaidSettings _cfg;
        GameClock _clock;
        EcosystemManager _eco;
        VillageService _villages;
        readonly List<Raid> _raids = new();
        int _count;
        float _timer, _passingTimer;
        static readonly List<Unit> s_near = new();

        public int RaidCount => _count;
        public bool IsUnderAttack(Village v) => _raids.Any(r => r.Village == v);
        public bool IsRaiding(Nest n) => _raids.Any(r => !r.Passing && r.Members.Any(m => m != null && m.Home == n));

        public void Init(RaidSettings cfg, GameClock clock, EcosystemManager eco, VillageService villages)
        {
            _cfg = cfg;
            _clock = clock;
            _eco = eco;
            _villages = villages;
            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<IntrudersRoutedEvent>(OnRouted);
            EventBus.Subscribe<VillageFellEvent>(OnFell);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            EventBus.Unsubscribe<IntrudersRoutedEvent>(OnRouted);
            EventBus.Unsubscribe<VillageFellEvent>(OnFell);
        }

        // ---------------- Launching ----------------

        /// <summary>
        /// A den's raid roll came up away from our town (<see cref="WaveManager"/>): if a standing village lies within
        /// reach, part of the pack goes for it. False when there is none (the den migrates instead, as before).
        /// </summary>
        public bool TryLaunch(Nest nest)
        {
            if (nest == null || nest.IsLair || IsRaiding(nest)) return false;
            var village = _villages.Standing
                .Where(v => !IsUnderAttack(v) && Vector2.Distance(v.WorldPosition, nest.WorldPosition) <= VillageRules.RaidRadius)
                .OrderBy(v => Vector2.Distance(v.WorldPosition, nest.WorldPosition)).FirstOrDefault();
            if (village == null) return false;
            // Half as often as our town is raided (P4-88, user decision 2026-10-18): on the other half the den just stays home.
            if (!GameRandom.Shared.Chance(VillageRules.DenRaidShare)) return true;
            return Launch(nest, village) > 0;
        }

        /// <summary>Sends part of the den against the village now (also debug and tests). Returns raiders sent.</summary>
        public int Launch(Nest nest, Village village)
        {
            var home = nest.Members.Where(m => m != null && m.IsAlive && m.DefaultStateOverride == null).ToList();
            if (home.Count == 0 || village == null || village.IsDestroyed) return 0;
            int n = WaveManager.PartySize(home.Count, _cfg.SendFraction, _cfg.MinParty);
            var raid = Begin(village, nest.Species, false);
            foreach (var m in home.OrderBy(_ => GameRandom.Shared.Value).Take(n)) Enlist(raid, m, After.Nest);
            Debug.Log($"[Village] raid #{raid.Id}: {raid.Members.Count} {nest.Species.displayName} from {nest.DisplayName} → {village.DisplayName}");
            return raid.Members.Count;
        }

        /// <summary>Sends these monsters against the village (debug and tests: monsters with no den, or any group).</summary>
        public int LaunchGroup(IEnumerable<Monster> group, Village village)
        {
            var list = group.Where(m => m != null && m.IsAlive).ToList();
            if (list.Count == 0 || village == null || village.IsDestroyed) return 0;
            var raid = Begin(village, list[0].Def, false);
            foreach (var m in list) Enlist(raid, m, m.Roaming != null ? After.Roam : After.Nest);
            return raid.Members.Count;
        }

        Raid Begin(Village village, MonsterDefinition species, bool passing)
        {
            var raid = new Raid { Id = ++_count, Village = village, Species = species, StartedAtHour = _clock.TotalHours, Passing = passing };
            _raids.Add(raid);
            return raid;
        }

        void Enlist(Raid raid, Monster m, After then)
        {
            raid.Members.Add(m);
            raid.Then[m] = then;
            m.VillageRaidState.Target = raid.Village;
            m.DefaultStateOverride = m.VillageRaidState;
            m.Died += OnRaiderDied;
            if (m.Fsm.Current != m.EngageState) m.GoToDefaultState();
        }

        /// <summary>A migrating band or a roaming pack of a raiding kind passing a village turns on it (once in a while per village).</summary>
        void CheckPassingPacks()
        {
            Services.TryGet<MigrationService>(out var migration);
            float now = _clock.TotalHours;
            foreach (var v in _villages.Standing)
            {
                if (IsUnderAttack(v) || now - v.LastPassingRaidHour < VillageRules.PassingCooldownHours) continue;
                UnitRegistry.Collect(v.WorldPosition, VillageRules.PassingRadius + VillageRules.Size * 0.5f,
                    u => u is Monster m && m.IsAlive && m.Def.canRaid && (m.DefaultStateOverride == m.MigrateState || m.DefaultStateOverride == m.RoamState), s_near);
                if (s_near.Count == 0) continue;
                var first = (Monster)s_near[0];
                List<Monster> pack = null;
                After then;
                if (first.DefaultStateOverride == first.MigrateState && migration != null && migration.TryGetBand(first, out var band))
                {
                    pack = band.Members.Where(m => m != null && m.IsAlive && m.DefaultStateOverride == m.MigrateState).ToList();
                    then = After.Migrate;
                }
                else if (first.Roaming != null)
                {
                    pack = first.Roaming.Members.Where(m => m != null && m.IsAlive && m.DefaultStateOverride == m.RoamState).ToList();
                    then = After.Roam;
                }
                else continue;
                if (pack.Count == 0) continue;
                v.LastPassingRaidHour = now;
                var raid = Begin(v, first.Def, true);
                foreach (var m in pack) Enlist(raid, m, then);
                Debug.Log($"[Village] raid #{raid.Id}: a passing {first.Def.displayName} pack ({pack.Count}) turns on {v.DisplayName}");
            }
        }

        // ---------------- Ending ----------------

        void OnHour(HourPassedEvent e)
        {
            foreach (var raid in _raids.ToList())
                if (_clock.TotalHours - raid.StartedAtHour >= VillageRules.RaidMaxHours) End(raid, false);
        }

        /// <summary>A raid ran into a much stronger den on the way (P4-18): it turns back, as our town's raiders do.</summary>
        void OnRouted(IntrudersRoutedEvent e)
        {
            foreach (var raid in _raids.ToList())
                if (raid.Members.Any(m => m != null && m.Home == e.IntruderHome)) End(raid, false);
        }

        void OnFell(VillageFellEvent e)
        {
            foreach (var raid in _raids.ToList())
                if (raid.Village == e.Village) End(raid, true);
        }

        void OnRaiderDied(Unit u)
        {
            u.Died -= OnRaiderDied;
            if (u is not Monster m) return;
            foreach (var raid in _raids.ToList())
            {
                if (!raid.Members.Remove(m)) continue;
                raid.Then.Remove(m);
                raid.Killed++;
                if (raid.Members.Count == 0) End(raid, false);
            }
        }

        /// <summary>Survivors go back to what they were doing: their den, their band's march, their pack's road.</summary>
        void End(Raid raid, bool fell)
        {
            if (!_raids.Remove(raid)) return;
            int withdrawn = 0;
            Services.TryGet<MigrationService>(out var migration);
            foreach (var m in raid.Members)
            {
                if (m == null) continue;
                m.Died -= OnRaiderDied;
                m.VillageRaidState.Target = null;
                if (!m.IsAlive || m.DefaultStateOverride != m.VillageRaidState) continue;
                withdrawn++;
                raid.Then.TryGetValue(m, out var then);
                if (then == After.Migrate && migration != null && migration.TryGetBand(m, out _)) Resume(m, m.MigrateState);
                else if (then == After.Roam && m.Roaming != null) Resume(m, m.RoamState);
                else m.EndMarch();
            }
            if (raid.Quest != null && raid.Quest.IsActive && Services.TryGet<QuestBoard>(out var board))
                board.SettleTownQuest(raid.Quest, fell ? QuestBoard.VillageFellReason : "습격이 끝났습니다");
            EventBus.Publish(new VillageRaidEndedEvent { Village = raid.Village, Killed = raid.Killed, Withdrawn = withdrawn, Sighted = raid.Sighted, Fell = fell });
        }

        static void Resume(Monster m, AI.State<Monster> march)
        {
            m.DefaultStateOverride = march;
            if (m.Fsm.Current != m.EngageState) m.GoToDefaultState();
        }

        // ---------------- Every quarter second: who is still on it, what the guild sees ----------------

        void Update()
        {
            if (_clock == null || Time.deltaTime <= 0f) return;
            _passingTimer -= Time.deltaTime;
            if (_passingTimer <= 0f)
            {
                _passingTimer = PassingCheckInterval;
                CheckPassingPacks();
            }
            if (_raids.Count == 0) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = CheckInterval;
            Services.TryGet<FogOfWar>(out var fog);
            foreach (var raid in _raids.ToList())
            {
                // Called off by someone else meanwhile (a band giving up its march, a pack settling): no longer ours.
                foreach (var m in raid.Members.ToList())
                    if (m == null || !m.IsAlive || m.DefaultStateOverride != m.VillageRaidState) Release(raid, m);
                if (raid.Members.Count == 0) { End(raid, false); continue; }
                if (!raid.Sighted && fog != null && raid.Members.Any(m => SeenAttacking(fog, raid.Village, m))) Sight(raid);
                if (raid.Sighted && raid.Quest == null) PostDefence(raid);
            }
        }

        void Release(Raid raid, Monster m)
        {
            raid.Members.Remove(m);
            raid.Then.Remove(m);
            if (m == null) return;
            m.Died -= OnRaiderDied;
            if (m.DefaultStateOverride != m.VillageRaidState) m.VillageRaidState.Target = null;
        }

        static bool SeenAttacking(FogOfWar fog, Village v, Monster m)
            => m != null && m.IsAlive && Vector2.Distance(m.Position, v.WorldPosition) <= VillageRules.AttackSeenRadius && fog.IsVisibleRaw(m.Position);

        void Sight(Raid raid)
        {
            raid.Sighted = true;
            EventBus.Publish(new VillageRaidSightedEvent { Village = raid.Village, Raiders = raid.Members.Count, Species = raid.Species });
        }

        /// <summary>
        /// The village asks the guild to drive the raiders off: a hunt of the raiders seen, where the village is, priced as
        /// an urgent hunt (<see cref="QuestPricing.DefenceReward"/>). Tried again while the species is not yet on the
        /// guild's books (the sighting and GuildIntel's scan do not land on the same frame).
        /// </summary>
        void PostDefence(Raid raid)
        {
            if (!Services.TryGet<QuestBoard>(out var board) || !Services.TryGet<WorldService>(out var world)) return;
            var v = raid.Village;
            int count = Mathf.Clamp(raid.Members.Count(m => m != null && m.IsAlive && m.Def == raid.Species), 1, 8);
            int level = raid.Members.Where(m => m != null && m.IsAlive).Select(m => m.Level).DefaultIfEmpty(1).Max();
            var danger = QuestBoard.HuntDanger(level, count);
            int gold = QuestPricing.DefenceReward(danger, count, Vector2.Distance(v.WorldPosition, world.TownCenterWorld));
            var draft = new QuestDraft
            {
                Type = QuestType.Hunt,
                Species = raid.Species,
                Count = count,
                Point = v.WorldPosition,
                HasPoint = true,
                Radius = VillageRules.DefenceRadius,
                RewardGold = gold,
                RewardExp = QuestPricing.RewardExp(gold),
                MaxParty = QuestPricing.PartyFor(danger),
            };
            if (board.TryPostRequest(draft, QuestClient.Village, out var q, out _, null, v, villageDefence: true)) raid.Quest = q;
        }

        /// <summary>Raiders of the raids under way (debug).</summary>
        public IEnumerable<(Village village, int raiders, bool sighted, bool passing, float hours)> Report()
            => _raids.Select(r => (r.Village, r.Members.Count, r.Sighted, r.Passing, _clock.TotalHours - r.StartedAtHour));

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SavedRaid
        {
            public int id;
            public int village;
            public string species;
            public List<string> members = new();
            public List<int> then = new();
            public float started;
            public bool sighted;
            public int killed;
            public string quest;
            public bool passing;
        }

        [System.Serializable]
        class SaveData
        {
            public int count;
            public List<SavedRaid> raids = new();
        }

        SaveData _restoring;

        public string SaveKey => "village_raids";

        public string Capture()
        {
            var s = new SaveData { count = _count };
            foreach (var r in _raids)
            {
                var sr = new SavedRaid
                {
                    id = r.Id, village = r.Village.Id, species = r.Species != null ? r.Species.id : null, started = r.StartedAtHour,
                    sighted = r.Sighted, killed = r.Killed, quest = r.Quest?.Id, passing = r.Passing,
                };
                foreach (var m in r.Members)
                {
                    if (m == null || !m.IsAlive) continue;
                    sr.members.Add(m.UnitId);
                    sr.then.Add(r.Then.TryGetValue(m, out var t) ? (int)t : 0);
                }
                s.raids.Add(sr);
            }
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            _restoring = JsonUtility.FromJson<SaveData>(json);
            _count = _restoring.count;
        }

        /// <summary>The raiders (their orders come back with the monsters' own save) are told which village again.</summary>
        public void Link(SaveContext ctx)
        {
            var db = Services.Get<GameDatabase>();
            foreach (var sr in _restoring.raids)
            {
                var village = ctx.Find<Village>(Village.SaveKeyOf(sr.village));
                if (village == null) continue;
                var raid = new Raid
                {
                    Id = sr.id, Village = village, Species = db.Monster(sr.species), StartedAtHour = sr.started, Sighted = sr.sighted,
                    Killed = sr.killed, Quest = string.IsNullOrEmpty(sr.quest) ? null : ctx.Find<Quest>(sr.quest), Passing = sr.passing,
                };
                for (int i = 0; i < sr.members.Count; i++)
                {
                    if (ctx.Find<Monster>(sr.members[i]) is not { } m) continue;
                    raid.Members.Add(m);
                    raid.Then[m] = i < sr.then.Count ? (After)sr.then[i] : After.Nest;
                    m.VillageRaidState.Target = village;
                    m.Died += OnRaiderDied;
                }
                _raids.Add(raid);
            }
            _restoring = null;
        }
    }
}
