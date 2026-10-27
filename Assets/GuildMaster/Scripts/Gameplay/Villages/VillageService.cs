using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Ecosystem;
using GuildMaster.Fog;
using GuildMaster.Player;
using GuildMaster.Quests;
using GuildMaster.Session;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Villages
{
    /// <summary>The guild saw a village for the first time: it shows on the map from now on.</summary>
    public struct VillageDiscoveredEvent { public Village Village; }
    /// <summary>The guild learned a village fell: it saw it happen, or came upon the ruins later.</summary>
    public struct VillageRuinsSeenEvent { public Village Village; public bool SawItFall; }
    /// <summary>A new village was founded somewhere (P4-84); the guild only hears which region.</summary>
    public struct VillageFoundedEvent { public Village Village; }
    /// <summary>One of a village's candidates joined the guild there and sets off for our town.</summary>
    public struct VillagerJoinedEvent { public Adventurer Adventurer; public Village Village; }
    /// <summary>A village's candidates fled it for our town (<see cref="Seen"/>: the guild saw them go).</summary>
    public struct VillageRefugeesFledEvent { public Village Village; public int Count; public bool Seen; }

    /// <summary>
    /// The other villages (P4-84, user decisions 2026-10-18): placed with the map, found by sight, worn down by raids
    /// (<see cref="VillageRaids"/>) and mended when left alone, gone for good when they fall — and now and then a new
    /// one founded elsewhere. The guild master recruits there in person: a village's two candidates come out while
    /// the guild master stands by it, and one who joins walks to our town on their own. When a village is about to
    /// fall its candidates flee to our town and become visitors. No sight of its own: the guild knows what it sees.
    /// </summary>
    public class VillageService : MonoBehaviour, ISaveable
    {
        const float CheckInterval = 0.5f;
        /// <summary>Hours a candidate "visitor" never runs out of while at its village (it leaves by rotation, not by the clock).</summary>
        const float NeverLeaves = 1e6f;

        WorldService _world;
        EcosystemManager _eco;
        GameClock _clock;

        readonly List<Village> _villages = new();
        /// <summary>Candidates out in front of their village right now (not saved: they come out again when the guild master is back).</summary>
        readonly Dictionary<Adventurer, Village> _out = new();
        int _nextId = 1;
        /// <summary>How many villages the world started with: new ones never take it above this.</summary>
        int _initialCount;
        float _timer;

        public IReadOnlyList<Village> Villages => _villages;
        public IEnumerable<Village> Standing => _villages.Where(v => !v.IsDestroyed);
        public IEnumerable<Village> Known => _villages.Where(v => v.Known);
        public int InitialCount => _initialCount;

        public void Init(WorldService world, EcosystemManager eco, GameClock clock, int mapSeed)
        {
            _world = world;
            _eco = eco;
            _clock = clock;
            // A loaded game brings its villages back as they were (P5-4): new ones, ruins and all.
            if (!GameSettings.RestoresSection(SaveKey))
            {
                foreach (var site in VillagePlacement.PlaceInitial(world.Map, VillagePlacement.StreamFor(mapSeed))) Add(site);
                _initialCount = _villages.Count;
            }
            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<DayPassedEvent>(OnDay);
            EventBus.Subscribe<VillageDamagedEvent>(OnDamaged);
            EventBus.Subscribe<VillageFellEvent>(OnFell);
            EventBus.Subscribe<AdventurerJoinedEvent>(OnJoined);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            EventBus.Unsubscribe<DayPassedEvent>(OnDay);
            EventBus.Unsubscribe<VillageDamagedEvent>(OnDamaged);
            EventBus.Unsubscribe<VillageFellEvent>(OnFell);
            EventBus.Unsubscribe<AdventurerJoinedEvent>(OnJoined);
        }

        Village Add(VillageSite site, int id = 0)
        {
            var v = new GameObject("Village").AddComponent<Village>();
            v.Init(id > 0 ? id : _nextId, site, transform);
            _nextId = Mathf.Max(_nextId, v.Id + 1);
            _villages.Add(v);
            _world.Grid.SetBlocked(v.Footprint, true);
            return v;
        }

        public Village Get(int id) => _villages.FirstOrDefault(v => v.Id == id);

        /// <summary>The village's candidates out in front of it right now (the guild master is there).</summary>
        public IEnumerable<Adventurer> CandidatesOut(Village v) => _out.Where(p => p.Value == v && p.Key != null).Select(p => p.Key);

        /// <summary>The village a quest on a den at <paramref name="den"/> is for: a standing one the guild knows within raiding reach, nearer to it than our town.</summary>
        public Village ClientFor(Vector2 den, Vector2 town)
        {
            Village best = null;
            float bestD = Mathf.Min(VillageRules.RaidRadius, Vector2.Distance(den, town));
            foreach (var v in _villages)
            {
                if (!v.Known || v.IsDestroyed || v.SeenRuined) continue;
                float d = Vector2.Distance(den, v.WorldPosition);
                if (d < bestD) { bestD = d; best = v; }
            }
            return best;
        }

        /// <summary>The guild sees (part of) the village right now.</summary>
        public bool IsVisible(Village v) => AnyCell(v, (fog, c) => fog.IsVisibleRaw(c));

        bool Explored(Village v) => AnyCell(v, (fog, c) => fog.IsExploredRaw(c));

        /// <summary>The icon and the ring round it: the icon itself blocks sight, so its middle is never seen.</summary>
        static bool AnyCell(Village v, System.Func<FogOfWar, Vector2Int, bool> test)
        {
            if (!Services.TryGet<FogOfWar>(out var fog)) return false;
            var r = v.Footprint;
            for (int y = r.yMin - 1; y <= r.yMax; y++)
            for (int x = r.xMin - 1; x <= r.xMax; x++)
                if (test(fog, new Vector2Int(x, y))) return true;
            return false;
        }

        // ---------------- Every half second: sight, the guild master at the door ----------------

        void Update()
        {
            if (_clock == null || Time.deltaTime <= 0f) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = CheckInterval;
            foreach (var v in _villages) Look(v);
            TendCandidates();
        }

        void Look(Village v)
        {
            bool visible = IsVisible(v);
            if (!v.Known && (visible || Explored(v)))
            {
                v.Known = true;
                Saw(v);
                EventBus.Publish(new VillageDiscoveredEvent { Village = v });
                if (v.IsDestroyed) EventBus.Publish(new VillageRuinsSeenEvent { Village = v, SawItFall = false });
            }
            else if (visible)
            {
                bool news = v.IsDestroyed && !v.SeenRuined;
                Saw(v);
                if (news) EventBus.Publish(new VillageRuinsSeenEvent { Village = v, SawItFall = false });
            }
            v.RefreshLook(visible);
        }

        void Saw(Village v)
        {
            v.SeenHpRatio = v.HpRatio;
            v.SeenRuined = v.IsDestroyed;
            v.SeenAtHour = _clock.TotalHours;
        }

        /// <summary>Candidates come out to meet the guild master, and go back in when they leave or a raid comes.</summary>
        void TendCandidates()
        {
            // Out already: joined (handled by OnJoined), turned the guild master down, or gone.
            foreach (var (a, v) in _out.ToList())
            {
                if (a == null || !a.IsAlive) { _out.Remove(a); continue; }
                if (a.IsGuildMember) continue;
                if (_clock.TotalHours >= a.VisitorLeaveAtHour)
                {
                    // Turned down the offer: moves on for good.
                    v.Candidates.RemoveAll(c => c.data == a.Data);
                    TakeIn(a);
                }
            }
            if (!Services.TryGet<PlayerAvatar>(out var master) || !master.IsAlive) return;
            Services.TryGet<VillageRaids>(out var raids);
            foreach (var v in _villages)
            {
                float d = Vector2.Distance(master.Position, v.WorldPosition);
                bool attacked = raids != null && raids.IsUnderAttack(v);
                if (!v.IsDestroyed && !attacked && d <= VillageRules.MeetRadius) ComeOut(v);
                else if (v.IsDestroyed || attacked || d > VillageRules.PartRadius) GoBackIn(v);
            }
        }

        void ComeOut(Village v)
        {
            EnsureCandidates(v);
            int i = 0;
            foreach (var c in v.Candidates)
            {
                i++;
                if (_out.Keys.Any(a => a != null && a.Data == c.data)) continue;
                var spot = v.Doorstep + new Vector2((i - 1.5f) * 1.6f, -0.6f);
                if (!_world.Grid.TryFindNearestWalkable(GridMap.WorldToCell(spot), 3, out var cell)) continue;
                var a = SpawnVillager(c.data, GridMap.CellToWorld(cell));
                a.VillageCandidateState.Spot = GridMap.CellToWorld(cell);
                a.ChangeState(a.VillageCandidateState);
                _out[a] = v;
            }
        }

        void GoBackIn(Village v)
        {
            foreach (var (a, home) in _out.ToList())
                if (home == v && a != null && !a.InConversation && !a.IsGuildMember) TakeIn(a);
        }

        void TakeIn(Adventurer a)
        {
            _out.Remove(a);
            if (a == null) return;
            a.Vanish();
            Destroy(a.gameObject);
        }

        /// <summary>
        /// One of a village's people as a unit: a visitor (not the guild's, not attacked in front of its village), hidden in
        /// the fog like a monster, and with no sight of its own — the guild does not see through its eyes.
        /// </summary>
        Adventurer SpawnVillager(AdventurerData data, Vector2 at)
        {
            var a = Adventurer.Create(data, at, transform);
            a.BecomeVisitor(_clock.TotalHours + NeverLeaves);
            Recruitment.VisitorService.Unsighted(a, true);
            return a;
        }

        /// <summary>The first time anyone needs them, the village's candidates are drawn (the guild cannot know them before).</summary>
        void EnsureCandidates(Village v)
        {
            if (v.CandidatesDrawn) return;
            v.CandidatesDrawn = true;
            v.NextRotationHour = _clock.TotalHours + VillageRules.RotateHours;
            // Staggered: the one who came first moves on at the next rotation.
            for (int i = 0; i < VillageRules.CandidateCount; i++) AddCandidate(v, _clock.TotalHours - VillageRules.RotateHours * (VillageRules.CandidateCount - 1 - i) / VillageRules.CandidateCount);
        }

        void AddCandidate(Village v, float arrived)
        {
            var rng = GameRandom.Shared;
            // As our own town's visitors (VisitorService) but born to the land round the village.
            // The guild hall's and the guild's rank's (P4-106, user decision: both).
            int bonus = (Services.TryGet<Town.UpgradeService>(out var upgrades) ? upgrades.VisitorLevelBonus : 0) + Guild.GuildRankService.RankVisitorBonus;
            int townLevel = Mathf.Max(1, 1 + (_clock.Day - 1) / 4 + bonus);
            int level = VillageRules.CandidateLevel(_eco.BirthLevelAt(v.WorldPosition), townLevel, rng.Range(-2, 2));
            v.Candidates.Add(new VillageCandidate { data = AdventurerGenerator.Generate(rng, level), arrived = arrived });
        }

        void OnJoined(AdventurerJoinedEvent e)
        {
            var a = e.Adventurer;
            if (a == null || !_out.TryGetValue(a, out var v)) return;
            _out.Remove(a);
            v.Candidates.RemoveAll(c => c.data == a.Data);
            // One of the guild now: its own eyes, always on the map, and home on its own (user decision 2026-10-18).
            Recruitment.VisitorService.Unsighted(a, false);
            a.ChangeState(a.ReturnState);
            EventBus.Publish(new VillagerJoinedEvent { Adventurer = a, Village = v });
        }

        // ---------------- Hourly: mending, the candidates moving on ----------------

        void OnHour(HourPassedEvent e)
        {
            float now = _clock.TotalHours;
            foreach (var v in _villages)
            {
                if (v.IsDestroyed) continue;
                v.Mend(now, 1f);
                if (!v.CandidatesDrawn || now < v.NextRotationHour) continue;
                v.NextRotationHour += VillageRules.RotateHours;
                // The oldest moves on (unless talking with the guild master), and newcomers fill the places.
                if (v.Candidates.Count >= VillageRules.CandidateCount)
                {
                    var oldest = v.Candidates.OrderBy(c => c.arrived).First();
                    var unit = _out.Keys.FirstOrDefault(a => a != null && a.Data == oldest.data);
                    if (VillageRules.MovesOn(oldest.arrived, now) && (unit == null || !unit.InConversation))
                    {
                        v.Candidates.Remove(oldest);
                        if (unit != null) TakeIn(unit);
                    }
                }
                while (v.Candidates.Count < VillageRules.CandidateCount) AddCandidate(v, now);
            }
        }

        // ---------------- Under attack, falling ----------------

        void OnDamaged(VillageDamagedEvent e)
        {
            var v = e.Village;
            if (v.RefugeesSent || v.HpRatio >= VillageRules.RefugeeBelow) return;
            SendRefugees(v);
        }

        /// <summary>The village is about to fall: its candidates take to the road for our town (user decision 2026-10-18).</summary>
        public int SendRefugees(Village v)
        {
            v.RefugeesSent = true;
            EnsureCandidates(v);
            int sent = 0;
            foreach (var c in v.Candidates.ToList())
            {
                var unit = _out.Keys.FirstOrDefault(a => a != null && a.Data == c.data);
                if (unit != null) _out.Remove(unit);
                else unit = SpawnVillager(c.data, GridMap.CellToWorld(_world.Grid.TryFindNearestWalkable(GridMap.WorldToCell(v.Doorstep), 3, out var cell) ? cell : v.Cell));
                unit.InConversation = false;
                unit.ConversationPartner = null;
                unit.RefugeeState.From = v.DisplayName;
                unit.ChangeState(unit.RefugeeState);
                if (Services.TryGet<Recruitment.VisitorService>(out var visitors)) visitors.Adopt(unit);
                sent++;
            }
            v.Candidates.Clear();
            if (sent > 0) EventBus.Publish(new VillageRefugeesFledEvent { Village = v, Count = sent, Seen = IsVisible(v) });
            return sent;
        }

        void OnFell(VillageFellEvent e)
        {
            var v = e.Village;
            _world.Grid.SetBlocked(v.Footprint, false);
            GoBackIn(v);
            if (Services.TryGet<QuestBoard>(out var board)) board.WithdrawFromVillage(v);
            Debug.Log($"[Village] {v.DisplayName} fell (day {_clock.Day})");
            if (!IsVisible(v)) return;
            v.Known = true;
            Saw(v);
            v.RefreshLook(true);
            EventBus.Publish(new VillageRuinsSeenEvent { Village = v, SawItFall = true });
        }

        // ---------------- Daily: new villages ----------------

        void OnDay(DayPassedEvent e)
        {
            int missing = _initialCount - Standing.Count();
            for (int i = 0; i < missing; i++)
                if (GameRandom.Shared.Chance(VillageRules.FoundChancePerDay)) TryFound(out _);
        }

        /// <summary>Founds a new village at a random place (a cleansed region draws more, P4-84). Also for debug and tests.</summary>
        public bool TryFound(out Village village)
        {
            village = null;
            // Apart from the ruins too (P4-102: the kept village sites made the ruins' own site the likeliest pick — built on top of them).
            var standing = _villages.Select(v => new VillageSite { Cell = v.Cell, Region = v.Region }).ToList();
            var names = _villages.Select(v => v.Name).ToList();
            Services.TryGet<RegionLordService>(out var lords);
            float Weight(Region r) => lords != null && lords.IsCleansed(r) ? VillageRules.CleansedWeight : 1f;
            if (!VillagePlacement.TryPickNew(_world.Map, standing, names, Weight, GameRandom.Shared, out var site)) return false;
            // Anyone standing where the palisade goes up just steps out (PathFollower lets a walker leave a blocked cell).
            village = Add(site);
            Debug.Log($"[Village] {village.DisplayName} founded at {site.Cell} ({site.Region}, day {_clock.Day})");
            EventBus.Publish(new VillageFoundedEvent { Village = village });
            return true;
        }

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SavedVillage
        {
            public int id;
            public string name;
            public Vector2Int cell;
            public Region region;
            public bool inner;
            public float hp;
            public float last_hit;
            public float fell_at;
            public float last_passing;
            public bool known;
            public bool seen_ruined;
            public float seen_hp;
            public float seen_at;
            public bool drawn;
            public float next_rotation;
            public bool refugees_sent;
            public List<VillageCandidate> candidates = new();
        }

        [System.Serializable]
        class SaveData
        {
            public int next_id;
            public int initial;
            public List<SavedVillage> villages = new();
        }

        public string SaveKey => "villages";

        public string Capture()
        {
            var s = new SaveData { next_id = _nextId, initial = _initialCount };
            foreach (var v in _villages)
            {
                var sv = new SavedVillage
                {
                    id = v.Id, name = v.Name, cell = v.Cell, region = v.Region, inner = v.Inner, hp = SaveTime.Hundredths(v.Hp),
                    last_hit = v.LastHitHour, fell_at = v.FellAtHour, last_passing = v.LastPassingRaidHour, known = v.Known,
                    seen_ruined = v.SeenRuined, seen_hp = SaveTime.Hundredths(v.SeenHpRatio), seen_at = v.SeenAtHour, drawn = v.CandidatesDrawn,
                    next_rotation = v.NextRotationHour, refugees_sent = v.RefugeesSent,
                };
                sv.candidates.AddRange(v.Candidates);
                s.villages.Add(sv);
            }
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            foreach (var v in _villages) if (v != null) Destroy(v.gameObject);
            _villages.Clear();
            _out.Clear();
            _initialCount = s.initial;
            foreach (var sv in s.villages)
            {
                var v = Add(new VillageSite { Cell = sv.cell, Region = sv.region, Inner = sv.inner, Name = sv.name }, sv.id);
                v.RestoreState(sv.hp, sv.last_hit, sv.fell_at, sv.last_passing);
                if (v.IsDestroyed) _world.Grid.SetBlocked(v.Footprint, false);
                v.Known = sv.known;
                v.SeenRuined = sv.seen_ruined;
                v.SeenHpRatio = sv.seen_hp;
                v.SeenAtHour = sv.seen_at;
                v.CandidatesDrawn = sv.drawn;
                v.NextRotationHour = sv.next_rotation;
                v.RefugeesSent = sv.refugees_sent;
                foreach (var c in sv.candidates)
                {
                    if (c?.data == null) continue;
                    AdventurerGenerator.NoteId(c.data.id);
                    v.Candidates.Add(c);
                }
                v.RefreshLook(false);
                ctx.Register(Village.SaveKeyOf(v.Id), v);
            }
            _nextId = Mathf.Max(_nextId, s.next_id);
        }
    }
}
