using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Ecosystem
{
    /// <summary>The guild saw a pack boss for the first time (P4-57).</summary>
    public struct BossSightedEvent { public Monster Boss; }

    /// <summary>
    /// What the guild actually knows: species and nests it has seen, and the last observed
    /// nest populations. UI, adventurer navigation, logs and LLM prompts read this instead of
    /// the real simulation (<see cref="EcosystemManager"/>), so nothing outside vision leaks.
    /// </summary>
    public class GuildIntel : MonoBehaviour, ISaveable, ISaveLink
    {
        const float ScanInterval = 0.5f;

        public class NestIntel
        {
            public bool Observed;
            public int LastCount;
            public float LastSeenHour;
            public bool VisibleNow;
            /// <summary>Species the guild last saw living here (owners can change out of sight).</summary>
            public MonsterDefinition Species;
            /// <summary>
            /// Most steps of scavenged smarts seen on one of its residents (2026-09-23). A pack that
            /// has eaten its way over battlefields fights above its level, and the only way the guild
            /// can know is by having watched one of them do it.
            /// </summary>
            public int ScavengedSteps;
        }

        /// <summary>
        /// One monster's gear as the guild last saw it (P4-22). The record outlives the monster
        /// leaving vision (or dying out of sight), so it stores the species and nest it was seen
        /// with instead of reading the live monster.
        /// </summary>
        public class GearSighting
        {
            public MonsterDefinition Def;
            public Nest Home;
            public ItemDefinition[] Worn;
            public float AbsorbedHp;
            public float Hour;

            public bool HasCrown
            {
                get
                {
                    if (Worn == null) return false;
                    foreach (var i in Worn) if (i != null && i.IsUnique) return true;
                    return false;
                }
            }
        }

        /// <summary>What the guild observed about one group's gear (a nest, or a species).</summary>
        public class GearIntel
        {
            /// <summary>Monsters last seen wearing at least one item.</summary>
            public int GearedCount;
            public bool HasCrown;
            /// <summary>Highest swallowed HP seen on a single monster (slimes).</summary>
            public float AbsorbedHp;
            public float LastSeenHour;
            /// <summary>Observed items with how many wearers, most common first.</summary>
            public readonly List<(ItemDefinition item, int count)> Items = new();

            public bool Any => GearedCount > 0 || AbsorbedHp > 0f;
        }

        /// <summary>Oldest sightings are forgotten past this many records.</summary>
        const int MaxGearSightings = 80;

        readonly HashSet<MonsterDefinition> _known = new();
        readonly Dictionary<Nest, NestIntel> _nests = new();
        readonly Dictionary<Monster, GearSighting> _gear = new();
        /// <summary>Bosses the guild has seen (P4-57); dead ones leave on <see cref="OnUnitDied"/>.</summary>
        readonly HashSet<Monster> _bossesSeen = new();
        static readonly GearIntel s_emptyGear = new();
        EcosystemManager _eco;
        FogOfWar _fog;
        GameClock _clock;
        WorldService _world;
        float _timer;

        /// <summary>Living monsters currently inside the guild's vision.</summary>
        public int VisibleMonsters { get; private set; }

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SavedNest
        {
            public int nest;
            public bool observed;
            public int last_count;
            public float last_seen;
            public string species;
            public int scavenged;
        }

        [System.Serializable]
        class SavedGear
        {
            public string monster;
            public string species;
            public int home = -1;
            public List<string> worn = new();
            public float absorbed;
            public float hour;
        }

        [System.Serializable]
        class SaveData
        {
            public List<string> known = new();
            public List<SavedNest> nests = new();
            /// <summary>Sightings of monsters still alive (a dead one's record goes with it).</summary>
            public List<SavedGear> gear = new();
            public List<string> bosses = new();
        }

        SaveData _restoring;

        public string SaveKey => "intel";

        public string Capture()
        {
            var s = new SaveData();
            foreach (var d in _known) s.known.Add(d.id);
            s.known.Sort(System.StringComparer.Ordinal);
            for (int i = 0; i < _eco.Nests.Count; i++)
            {
                if (!_nests.TryGetValue(_eco.Nests[i], out var ni)) continue;
                s.nests.Add(new SavedNest
                {
                    nest = i, observed = ni.Observed, last_count = ni.LastCount, last_seen = ni.LastSeenHour,
                    species = ni.Species != null ? ni.Species.id : null, scavenged = ni.ScavengedSteps,
                });
            }
            foreach (var kv in _gear)
            {
                if (kv.Key == null || !kv.Key.IsAlive) continue;
                var g = new SavedGear
                {
                    monster = kv.Key.UnitId, species = kv.Value.Def != null ? kv.Value.Def.id : null,
                    home = kv.Value.Home != null ? _eco.Nests.IndexOf(kv.Value.Home) : -1, absorbed = kv.Value.AbsorbedHp, hour = kv.Value.Hour,
                };
                if (kv.Value.Worn != null) foreach (var item in kv.Value.Worn) if (item != null) g.worn.Add(item.id);
                s.gear.Add(g);
            }
            s.gear.Sort((a, b) => string.CompareOrdinal(a.monster, b.monster));
            foreach (var b in _bossesSeen) if (b != null && b.IsAlive) s.bosses.Add(b.UnitId);
            s.bosses.Sort(System.StringComparer.Ordinal);
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            _restoring = JsonUtility.FromJson<SaveData>(json);
            var db = Services.Get<GameDatabase>();
            _known.Clear();
            foreach (var id in _restoring.known) if (db.Monster(id) is { } d) _known.Add(d);
            _nests.Clear();
            foreach (var sn in _restoring.nests)
            {
                if (sn.nest < 0 || sn.nest >= _eco.Nests.Count) continue;
                var nest = _eco.Nests[sn.nest];
                var ni = Get(nest);
                ni.Observed = sn.observed;
                ni.LastCount = sn.last_count;
                ni.LastSeenHour = sn.last_seen;
                ni.Species = db.Monster(sn.species);
                ni.ScavengedSteps = sn.scavenged;
                if (ni.Species != null) nest.ShowAs(ni.Species);
            }
        }

        /// <summary>Gear sightings and seen bosses point at monsters: those are all back by now.</summary>
        public void Link(SaveContext ctx)
        {
            var db = Services.Get<GameDatabase>();
            _gear.Clear();
            foreach (var g in _restoring.gear)
            {
                if (ctx.Find<Monster>(g.monster) is not { } m) continue;
                var worn = new List<ItemDefinition>();
                foreach (var id in g.worn) if (db.Item(id) is { } item) worn.Add(item);
                _gear[m] = new GearSighting
                {
                    Def = db.Monster(g.species), Home = g.home >= 0 && g.home < _eco.Nests.Count ? _eco.Nests[g.home] : null,
                    Worn = worn.ToArray(), AbsorbedHp = g.absorbed, Hour = g.hour,
                };
            }
            _bossesSeen.Clear();
            foreach (var id in _restoring.bosses) if (ctx.Find<Monster>(id) is { } boss) _bossesSeen.Add(boss);
            _restoring = null;
        }

        public void Init(EcosystemManager eco, FogOfWar fog, GameClock clock, WorldService world)
        {
            _eco = eco;
            _fog = fog;
            _clock = clock;
            _world = world;
            EventBus.Subscribe<UnitDiedEvent>(OnUnitDied);
        }

        void OnDestroy() => EventBus.Unsubscribe<UnitDiedEvent>(OnUnitDied);

        /// <summary>A monster the guild watched die drops everything, so its sighting is forgotten.</summary>
        void OnUnitDied(UnitDiedEvent e)
        {
            if (e.Victim is Monster m && _fog != null && _fog.IsVisibleRaw(m.Position)) _gear.Remove(m);
            // Pooled monsters come back as someone else: never keep a dead boss.
            if (e.Victim is Monster dead) _bossesSeen.Remove(dead);
        }

        void Update()
        {
            if (_eco == null) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = ScanInterval;
            Scan();
        }

        void Scan()
        {
            int visible = 0;
            foreach (var u in UnitRegistry.All)
            {
                if (u is not Monster m || !m.IsAlive || !_fog.IsVisibleRaw(m.Position)) continue;
                visible++;
                _known.Add(m.Def);
                RecordGear(m);
                // Smarts beyond its level show in how it fights, so seeing one is knowing it.
                if (m.Home != null && m.ScavengedTierSteps > 0)
                {
                    var hi = Get(m.Home);
                    if (m.ScavengedTierSteps > hi.ScavengedSteps) hi.ScavengedSteps = m.ScavengedTierSteps;
                }
                if (m.IsBoss && _bossesSeen.Add(m)) EventBus.Publish(new BossSightedEvent { Boss = m });
            }
            VisibleMonsters = visible;

            foreach (var n in _eco.Nests)
            {
                if (!n.Discovered) continue;
                var ni = Get(n);
                ni.VisibleNow = _fog.IsVisibleRaw(n.Cell);
                // Who lives here is learned by seeing them, not by having walked past the den: the den
                // has to be in view with a resident in view alongside it. A nest whose cell was explored
                // but whose owners were never seen stays unknown, and so does one standing empty.
                if (ni.VisibleNow && ni.Species != n.Species
                    && n.Members.Any(m => m != null && m.IsAlive && _fog.IsVisibleRaw(m.Position)))
                {
                    ni.Species = n.Species;
                    n.ShowAs(n.Species);
                }
                if (ni.Species != null) _known.Add(ni.Species);
                if (!ni.VisibleNow) continue;
                ni.Observed = true;
                ni.LastCount = n.Population;
                ni.LastSeenHour = _clock.TotalHours;
            }
        }

        // ---------------- Observed gear (P4-22) ----------------

        /// <summary>
        /// Stores what this monster is wearing right now; monsters seen carrying nothing are
        /// forgotten. Only called for monsters inside guild vision.
        /// </summary>
        void RecordGear(Monster m)
        {
            bool bare = m.Equipment.WornCount == 0 && m.Equipment.AbsorbedHp <= 0f;
            if (bare) { _gear.Remove(m); return; }
            if (!_gear.TryGetValue(m, out var g)) _gear[m] = g = new GearSighting();
            g.Def = m.Def;
            g.Home = m.Home;
            g.Worn = m.Equipment.Worn.ToArray();
            g.AbsorbedHp = m.Equipment.AbsorbedHp;
            g.Hour = _clock.TotalHours;
            if (_gear.Count > MaxGearSightings) ForgetOldestGear();
        }

        void ForgetOldestGear()
        {
            Monster oldest = null;
            float oldestHour = float.MaxValue;
            foreach (var kv in _gear)
                if (kv.Value.Hour < oldestHour) { oldestHour = kv.Value.Hour; oldest = kv.Key; }
            if (oldest != null) _gear.Remove(oldest);
        }

        /// <summary>Gear the guild saw on the monsters of this nest.</summary>
        public GearIntel GearAt(Nest n)
            => n == null ? s_emptyGear : Aggregate(_gear.Values.Where(g => g.Home == n));

        /// <summary>Gear the guild saw on this species, wherever it lives.</summary>
        public GearIntel GearOf(MonsterDefinition species)
            => species == null ? s_emptyGear : Aggregate(_gear.Values.Where(g => g.Def == species));

        /// <summary>Gear seen on monsters with no nest (raiders, migrants) of this species.</summary>
        public GearIntel GearOfHomeless(MonsterDefinition species)
            => species == null ? s_emptyGear : Aggregate(_gear.Values.Where(g => g.Def == species && g.Home == null));

        /// <summary>Folds sightings into one group summary. Pure, so the rules can be tested.</summary>
        public static GearIntel Aggregate(IEnumerable<GearSighting> sightings)
        {
            var result = new GearIntel();
            var counts = new Dictionary<ItemDefinition, int>();
            foreach (var g in sightings)
            {
                if (g == null) continue;
                bool wearsSomething = false;
                if (g.Worn != null)
                    foreach (var i in g.Worn)
                    {
                        if (i == null) continue;
                        wearsSomething = true;
                        counts.TryGetValue(i, out int c);
                        counts[i] = c + 1;
                        if (i.IsUnique) result.HasCrown = true;
                    }
                if (wearsSomething) result.GearedCount++;
                result.AbsorbedHp = Mathf.Max(result.AbsorbedHp, g.AbsorbedHp);
                result.LastSeenHour = Mathf.Max(result.LastSeenHour, g.Hour);
            }
            foreach (var kv in counts.OrderByDescending(kv => kv.Value)) result.Items.Add((kv.Key, kv.Value));
            return result;
        }

        /// <summary>
        /// Gear makes a group notably more dangerous once two of its members are armed, or when
        /// one of them wears a unique item (the crown). Pure rule, shared with the quest board.
        /// </summary>
        public static bool RaisesDanger(GearIntel gear)
            => gear != null && (gear.GearedCount >= 2 || gear.HasCrown);

        /// <summary>"관측된 장비: 고블린 전사 투구 2, 녹슨 단검 1 (3시간 전)" — empty when nothing was seen.</summary>
        public string DescribeGear(GearIntel gear)
        {
            if (gear == null || !gear.Any) return "";
            var parts = gear.Items.Select(e => e.count > 1 ? $"{e.item.displayName} {e.count}" : e.item.displayName).ToList();
            if (gear.AbsorbedHp > 0f) parts.Add($"삼킨 아이템 HP +{gear.AbsorbedHp:0}");
            return $"관측된 장비: {string.Join(", ", parts)} ({AgoText(gear.LastSeenHour)})";
        }

        /// <summary>"3시간 전" / "방금" for an observation time in total game hours.</summary>
        public string AgoText(float hour) => _clock.AgoText(hour);

        /// <summary>
        /// Short sentences about armed monsters the guild has actually seen, worst first, for the
        /// LLM decision prompt. Empty when nothing was observed.
        /// </summary>
        public List<string> ThreatNotes(int max = 3)
        {
            var notes = new List<(float weight, string text)>();
            foreach (var n in KnownNests)
            {
                var gear = GearAt(n);
                if (!gear.Any) continue;
                var owners = SpeciesOf(n);
                if (owners == null) continue; // nobody has seen who lives there, so there is nothing to say about them
                notes.Add((Weight(gear), NoteFor(NameOf(n), owners, gear)));
            }
            foreach (var species in KnownSpecies)
            {
                var gear = GearOfHomeless(species);
                if (!gear.Any) continue;
                notes.Add((Weight(gear), NoteFor($"떠도는 {species.displayName} 무리", species, gear)));
            }
            return notes.OrderByDescending(x => x.weight).Take(max).Select(x => x.text).ToList();

            static float Weight(GearIntel g) => (g.HasCrown ? 100f : 0f) + g.GearedCount * 10f + g.AbsorbedHp;
        }

        string NoteFor(string where, MonsterDefinition species, GearIntel gear)
        {
            string who = species != null ? species.displayName : "몬스터";
            if (gear.HasCrown) return $"{where}에서 왕관을 쓴 우두머리 관측 ({AgoText(gear.LastSeenHour)})";
            if (gear.GearedCount > 0)
            {
                string items = string.Join(", ", gear.Items.Take(2).Select(e => e.item.displayName));
                return $"{where}에서 장비를 착용한 {who} {gear.GearedCount}마리 관측 ({items}, {AgoText(gear.LastSeenHour)})";
            }
            return $"{where}에서 아이템을 삼켜 튼튼해진 {who} 관측 (HP +{gear.AbsorbedHp:0}, {AgoText(gear.LastSeenHour)})";
        }

        public bool IsKnown(MonsterDefinition species) => species != null && _known.Contains(species);

        /// <summary>Debug/test only: the guild has seen this species (quests for it can be posted).</summary>
        public void MarkKnown(MonsterDefinition species) { if (species != null) _known.Add(species); }

        /// <summary>Known species in database order.</summary>
        public IEnumerable<MonsterDefinition> KnownSpecies => _eco.Species.Where(_known.Contains);

        public IEnumerable<Nest> KnownNests => _eco.Nests.Where(n => n.Discovered);

        public NestIntel Get(Nest n)
        {
            if (!_nests.TryGetValue(n, out var ni)) _nests[n] = ni = new NestIntel();
            return ni;
        }

        /// <summary>
        /// Species the guild believes lives in the nest, by the last sighting of its residents — null
        /// while nobody has seen who lives there. Never falls back to the real owners: a den the guild
        /// has only walked past tells it nothing.
        /// </summary>
        public MonsterDefinition SpeciesOf(Nest n) => n == null ? null : Get(n).Species;

        /// <summary>Guild-facing nest name, e.g. "고블린 둥지 #04", by the last seen owners.</summary>
        public string NameOf(Nest n)
        {
            if (n == null) return "";
            // A region lord's lair is named for its lord once the guild has seen the lord (P4-83).
            if (n.IsLair && n.Lord != null && _known.Contains(n.Lord)) return $"{n.Lord.displayName}의 거처";
            return n.NameFor(SpeciesOf(n));
        }

        /// <summary>
        /// The region lord of this lair as the guild knows it: seen, and not seen dying since (P4-83). Null when the
        /// guild has not seen it, or it is dead.
        /// </summary>
        public Monster KnownLordAt(Nest n)
        {
            if (n == null || !n.IsLair) return null;
            foreach (var b in _bossesSeen)
                if (b != null && b.IsAlive && b.IsRegionLord && b.Home == n) return b;
            return null;
        }

        /// <summary>Sum of the last observed counts of the species' discovered nests.</summary>
        public int ObservedPopulation(MonsterDefinition species)
            => KnownNests.Where(n => SpeciesOf(n) == species).Sum(n => Get(n).LastCount);

        public int KnownNestCount(MonsterDefinition species) => KnownNests.Count(n => SpeciesOf(n) == species);

        /// <summary>Observed count shown to the player: live when in view, otherwise the last sighting.</summary>
        public string DescribeNest(Nest n)
        {
            var ni = Get(n);
            if (!ni.Observed) return "관측 기록 없음";
            string count = ni.LastCount > 0 ? $"{ni.LastCount}마리" : "비어 있음";
            if (ni.VisibleNow) return $"{count} (지금 보임)";
            int day = Mathf.FloorToInt(ni.LastSeenHour / 24f) + 1;
            return $"마지막 관측 {count} ({day}일 {Mathf.FloorToInt(ni.LastSeenHour % 24f)}시)";
        }

        /// <summary>
        /// Nearest discovered nest of the species that was not last seen empty (never-observed
        /// discovered nests count as possible). Null when the guild knows none.
        /// </summary>
        public Nest FindNearestKnownNest(Vector2 from, MonsterDefinition species)
        {
            Nest best = null;
            float bestSq = float.MaxValue;
            foreach (var n in KnownNests)
            {
                if (SpeciesOf(n) != species) continue;
                var ni = Get(n);
                if (ni.Observed && ni.LastCount <= 0) continue;
                float sq = (n.WorldPosition - from).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = n; }
            }
            return best;
        }

        /// <summary>
        /// A walkable spot worth searching: prefers unexplored ground outside town at a moderate
        /// distance. Used when the guild does not know where a quest target lives.
        /// </summary>
        public Vector2 FindSearchPoint(Vector2 from, GameRandom rng)
        {
            var grid = _world.Grid;
            Vector2 best = from;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < 24; i++)
            {
                var p = from + rng.InsideUnitCircle().normalized * rng.Range(10f, 36f);
                var cell = GridMap.WorldToCell(p);
                if (!grid.InBounds(cell) || !grid.IsWalkable(cell) || grid.IsInTown(cell)) continue;
                float unexplored = 1f - _fog.ExploredFraction(p, 4f);
                float score = unexplored * 10f - Vector2.Distance(from, p) * 0.05f + rng.Range(0f, 1f);
                if (score > bestScore) { bestScore = score; best = GridMap.CellToWorld(cell); }
            }
            return best;
        }
    }
}
