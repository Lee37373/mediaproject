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
    /// <summary>
    /// What the guild actually knows: species and nests it has seen, and the last observed
    /// nest populations. UI, adventurer navigation, logs and LLM prompts read this instead of
    /// the real simulation (<see cref="EcosystemManager"/>), so nothing outside vision leaks.
    /// </summary>
    public class GuildIntel : MonoBehaviour
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
        static readonly GearIntel s_emptyGear = new();
        EcosystemManager _eco;
        FogOfWar _fog;
        GameClock _clock;
        WorldService _world;
        float _timer;

        /// <summary>Living monsters currently inside the guild's vision.</summary>
        public int VisibleMonsters { get; private set; }

        public void Init(EcosystemManager eco, FogOfWar fog, GameClock clock, WorldService world)
        {
            _eco = eco;
            _fog = fog;
            _clock = clock;
            _world = world;
            // Nests already discovered at this point are the ones the town knows about (P5-1).
            foreach (var n in _eco.Nests) SeedKnownNest(n);
            EventBus.Subscribe<UnitDiedEvent>(OnUnitDied);
        }

        void OnDestroy() => EventBus.Unsubscribe<UnitDiedEvent>(OnUnitDied);

        /// <summary>A monster the guild watched die drops everything, so its sighting is forgotten.</summary>
        void OnUnitDied(UnitDiedEvent e)
        {
            if (e.Victim is Monster m && _fog != null && _fog.IsVisibleRaw(m.Position)) _gear.Remove(m);
        }

        void Update()
        {
            if (_eco == null) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = ScanInterval;
            Scan();
        }

        /// <summary>
        /// Writes down what the town already knows about a nest on its doorstep (P5-1). Nests
        /// revealed at the start are marked discovered but were never in anyone's view, so without
        /// this their observed count stays 0 and nothing can be posted against them.
        /// </summary>
        public void SeedKnownNest(Nest nest)
        {
            if (nest == null || !nest.Discovered) return;
            var ni = Get(nest);
            ni.Species = nest.Species;
            ni.Observed = true;
            ni.LastCount = nest.Population;
            ni.LastSeenHour = _clock != null ? _clock.TotalHours : 0f;
            _known.Add(nest.Species);
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
            }
            VisibleMonsters = visible;

            foreach (var n in _eco.Nests)
            {
                if (!n.Discovered) continue;
                var ni = Get(n);
                ni.VisibleNow = _fog.IsVisibleRaw(n.Cell);
                // New owners are only noticed when the nest is in view (or on discovery).
                if (ni.Species == null || (ni.VisibleNow && ni.Species != n.Species))
                {
                    ni.Species = n.Species;
                    n.ShowAs(n.Species);
                }
                _known.Add(ni.Species);
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
        public string AgoText(float hour)
        {
            int ago = Mathf.FloorToInt(_clock.TotalHours - hour);
            return ago <= 0 ? "방금" : ago < 24 ? $"{ago}시간 전" : $"{ago / 24}일 전";
        }

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
                notes.Add((Weight(gear), NoteFor(NameOf(n), SpeciesOf(n), gear)));
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

        /// <summary>Known species in database order.</summary>
        public IEnumerable<MonsterDefinition> KnownSpecies => _eco.Species.Where(_known.Contains);

        public IEnumerable<Nest> KnownNests => _eco.Nests.Where(n => n.Discovered);

        public NestIntel Get(Nest n)
        {
            if (!_nests.TryGetValue(n, out var ni)) _nests[n] = ni = new NestIntel();
            return ni;
        }

        /// <summary>Species the guild believes lives in the nest (last seen; the real one right at discovery).</summary>
        public MonsterDefinition SpeciesOf(Nest n) => n == null ? null : Get(n).Species ?? n.Species;

        /// <summary>Guild-facing nest name, e.g. "고블린 둥지 #04", by the last seen owners.</summary>
        public string NameOf(Nest n) => n == null ? "" : n.NameFor(SpeciesOf(n));

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
