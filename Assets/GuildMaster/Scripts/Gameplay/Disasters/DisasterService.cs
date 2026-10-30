using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Ecosystem;
using GuildMaster.Economy;
using GuildMaster.Fog;
using GuildMaster.Quests;
using GuildMaster.Town;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Disasters
{
    public struct DisasterStartedEvent { public Disaster Disaster; }
    /// <summary>The guild has seen it (the town posts a quest).</summary>
    public struct DisasterSightedEvent { public Disaster Disaster; }
    /// <summary>It has come up to the town walls: the town takes damage while it lasts.</summary>
    public struct DisasterReachedTownEvent { public Disaster Disaster; }
    /// <summary>Dealt with by the guild, or died down by itself; <see cref="Known"/>: the guild ever saw it.</summary>
    public struct DisasterEndedEvent { public Disaster Disaster; public bool DealtWith; public bool Known; }
    /// <summary>A cell's hazard changed (overlay, minimap).</summary>
    public struct HazardChangedEvent { public Vector2Int Cell; public HazardKind Kind; }

    /// <summary>
    /// Runs the natural disasters (P4-72, user request 2026-10-03: "define natural disasters like a
    /// wildfire, set off while monsters fight, solved through quests"). A hazard skill striking the
    /// right ground (<see cref="HazardStrikeEvent"/>) may start one; it spreads, hurts whoever stands
    /// in it, drives dens off, and hurts the town if it reaches the walls; the town posts a quest
    /// once the guild has seen it; the quest's workers work it away. Left alone it dies down when its
    /// time is up. The numbers are in <see cref="DisasterRules"/>.
    /// </summary>
    public class DisasterService : MonoBehaviour, ISaveable, ISaveLink
    {
        /// <summary>Tests and debug: overrides <see cref="DisasterRules.TriggerChance"/> when 0..1.</summary>
        public static float ChanceOverride = -1f;
        /// <summary>Tests: scales how fast disasters spread (0 = they stay where they started).</summary>
        public static float SpreadScale = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ChanceOverride = -1f;
            SpreadScale = 1f;
        }

        const float HurtInterval = 0.25f;
        const float SightInterval = 0.5f;
        static readonly Vector2Int[] Neighbours8 =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
        };

        readonly List<Disaster> _active = new();
        readonly List<Disaster> _history = new();
        /// <summary>Burnt forest and the hour it burnt (it grows back).</summary>
        readonly Dictionary<Vector2Int, float> _burnt = new();
        static readonly List<Unit> s_units = new();
        static readonly List<Vector2Int> s_cells = new();

        GameClock _clock;
        WorldService _world;
        MapRenderer _renderer;
        QuestBoard _board;
        GuildTreasury _treasury;
        float _lastHour;
        float _hurtTimer, _hurtHours;
        float _sightTimer;
        int _nextId;

        public IReadOnlyList<Disaster> Active => _active;
        public IReadOnlyList<Disaster> History => _history;
        /// <summary>Forest cells burnt and not yet grown back.</summary>
        public int BurntCells => _burnt.Count;

        public void Init(GameClock clock, WorldService world, MapRenderer renderer, QuestBoard board, GuildTreasury treasury)
        {
            _clock = clock;
            _world = world;
            _renderer = renderer;
            _board = board;
            _treasury = treasury;
            _lastHour = clock.TotalHours;
        }

        void OnEnable()
        {
            EventBus.Subscribe<HazardStrikeEvent>(OnStrike);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<HazardStrikeEvent>(OnStrike);
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
        }

        float Now => _clock.TotalHours;
        GridMap Grid => _world.Grid;

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SaveData
        {
            public int next_id;
            public List<DisasterSave> active = new();
            public List<DisasterSave> history = new();
            public List<Vector2Int> burnt = new();
            public List<float> burnt_at = new();
        }

        SaveData _restoring;
        readonly List<(Disaster d, DisasterSave s)> _restoredDisasters = new();

        public string SaveKey => "disasters";

        static int CompareCells(Vector2Int a, Vector2Int b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x);

        DisasterSave CaptureDisaster(Disaster d, IReadOnlyList<Nest> nests)
        {
            var s = new DisasterSave
            {
                id = d.Id, kind = d.Kind, origin = d.Origin, started = d.StartedHour, growth_owed = d.GrowthOwed, drift = d.Drift,
                next_storm_refresh = d.NextStormRefresh, core = d.Core, core_work_left = d.CoreWorkLeft, known = d.Known,
                sighted = d.SightedHour, quest = d.Quest?.Id, ending = d.Ending, ending_since = d.EndingSinceHour,
                dealt_with = d.DealtWith, over = d.Over, reached_town = d.ReachedTown,
            };
            s.cells.AddRange(d.Cells);
            s.cells.Sort(CompareCells);
            var lit = new List<Vector2Int>(d.LitAt.Keys);
            lit.Sort(CompareCells);
            foreach (var c in lit) { s.lit_cells.Add(c); s.lit_at.Add(d.LitAt[c]); }
            var worked = new List<Vector2Int>(d.WorkDone.Keys);
            worked.Sort(CompareCells);
            foreach (var c in worked) { s.work_cells.Add(c); s.work_done.Add(d.WorkDone[c]); }
            for (int i = 0; i < nests.Count; i++) if (d.Fled.Contains(nests[i])) s.fled.Add(i);
            return s;
        }

        public string Capture()
        {
            var nests = Services.TryGet<EcosystemManager>(out var eco) ? new List<Nest>(eco.Nests) : new List<Nest>();
            var s = new SaveData { next_id = _nextId };
            foreach (var d in _active) s.active.Add(CaptureDisaster(d, nests));
            foreach (var d in _history) s.history.Add(CaptureDisaster(d, nests));
            var burnt = new List<Vector2Int>(_burnt.Keys);
            burnt.Sort(CompareCells);
            foreach (var c in burnt) { s.burnt.Add(c); s.burnt_at.Add(_burnt[c]); }
            return JsonUtility.ToJson(s);
        }

        /// <summary>The disasters, and what they did to the map: their cells' hazards, and the forest burnt away.</summary>
        public void Restore(string json, SaveContext ctx)
        {
            _restoring = JsonUtility.FromJson<SaveData>(json);
            _nextId = _restoring.next_id;
            _lastHour = Now;
            var nests = Services.TryGet<EcosystemManager>(out var eco) ? new List<Nest>(eco.Nests) : new List<Nest>();
            _restoredDisasters.Clear();
            for (int i = 0; i < _restoring.burnt.Count && i < _restoring.burnt_at.Count; i++)
            {
                var c = _restoring.burnt[i];
                _burnt[c] = _restoring.burnt_at[i];
                Grid.SetHazard(c, HazardKind.Burnt);
                if (_renderer != null) _renderer.SetTreeBurnt(c, true);
                _world.Risk.SetForestCleared(c.x, c.y, true);
                EventBus.Publish(new HazardChangedEvent { Cell = c, Kind = HazardKind.Burnt });
            }
            foreach (var s in _restoring.active) _active.Add(RestoreDisaster(s, nests, ctx, applyCells: true));
            foreach (var s in _restoring.history) _history.Add(RestoreDisaster(s, nests, ctx, applyCells: false));
        }

        Disaster RestoreDisaster(DisasterSave s, IReadOnlyList<Nest> nests, SaveContext ctx, bool applyCells)
        {
            var d = new Disaster
            {
                Id = s.id, Kind = s.kind, Origin = s.origin, StartedHour = s.started, GrowthOwed = s.growth_owed, Drift = s.drift,
                NextStormRefresh = s.next_storm_refresh, Core = s.core, CoreWorkLeft = s.core_work_left, Known = s.known,
                SightedHour = s.sighted, Ending = s.ending, EndingSinceHour = s.ending_since, DealtWith = s.dealt_with, Over = s.over,
                ReachedTown = s.reached_town,
            };
            foreach (var c in s.cells)
            {
                d.Cells.Add(c);
                if (!applyCells) continue;
                Grid.SetHazard(c, d.Hazard);
                EventBus.Publish(new HazardChangedEvent { Cell = c, Kind = d.Hazard });
            }
            for (int i = 0; i < s.lit_cells.Count && i < s.lit_at.Count; i++) d.LitAt[s.lit_cells[i]] = s.lit_at[i];
            for (int i = 0; i < s.work_cells.Count && i < s.work_done.Count; i++) d.WorkDone[s.work_cells[i]] = s.work_done[i];
            foreach (var i in s.fled) if (i >= 0 && i < nests.Count) d.Fled.Add(nests[i]);
            ctx.Register(Disaster.SaveKeyOf(d.Id), d);
            _restoredDisasters.Add((d, s));
            return d;
        }

        public void Link(SaveContext ctx)
        {
            foreach (var (d, s) in _restoredDisasters) d.Quest = ctx.Find<Quest>(s.quest);
            _restoredDisasters.Clear();
            _restoring = null;
        }

        // ---------------- Setting one off ----------------

        /// <summary>A monster's hazard skill struck the ground in a fight: by chance, a disaster starts there.</summary>
        void OnStrike(HazardStrikeEvent e)
        {
            if (_clock == null) return;
            if (!FindGround(e.Kind, GridMap.WorldToCell(e.Point), out var cell)) return;
            if (!CanStartAt(cell)) return;
            // The storyteller's breather quiets them too (P4-111); a test's own chance stands as it is.
            float chance = ChanceOverride >= 0f ? ChanceOverride : DisasterRules.TriggerChance * Storytelling.StorytellerService.EcoFactor;
            if (!GameRandom.Shared.Chance(chance)) return;
            Begin(e.Kind, cell);
        }

        /// <summary>
        /// The storyteller sets one off (P4-111): on ground that suits it within <paramref name="radius"/> of
        /// <paramref name="point"/> (a disaster monster's den), if there is room for another. Null when none fits.
        /// </summary>
        public Disaster StartNear(DisasterKind kind, Vector2 point, float radius, GameRandom rng)
        {
            if (_clock == null) return null;
            var at = GridMap.WorldToCell(point);
            for (int attempt = 0; attempt < 16; attempt++)
            {
                var off = attempt == 0 ? Vector2.zero : rng.InsideUnitCircle() * radius;
                var c = new Vector2Int(at.x + Mathf.RoundToInt(off.x), at.y + Mathf.RoundToInt(off.y));
                if (!Grid.InBounds(c) || Grid.IsInTown(c) || !FindGround(kind, c, out var cell) || !CanStartAt(cell)) continue;
                var d = Begin(kind, cell);
                if (d != null) return d;
            }
            return null;
        }

        /// <summary>
        /// Dragon fire (P4-93, user decision 2026-10-20: "숲에 옮겨 붙어 산불"): forest within <paramref name="radius"/> of
        /// <paramref name="point"/> catches. A wildfire already burning nearby takes the new flames; otherwise one starts if
        /// there is room for another (the usual limits). No chance roll: fire on a forest always takes.
        /// </summary>
        public void Kindle(Vector2 point, float radius)
        {
            if (_clock == null) return;
            var at = GridMap.WorldToCell(point);
            var fire = _active.FirstOrDefault(d => d.Kind == DisasterKind.Wildfire && !d.Over && !d.Ending
                                                   && Vector2.Distance(d.WorldOrigin, point) < DisasterRules.MinSpacing);
            int r = Mathf.CeilToInt(radius);
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > radius * radius) continue;
                var c = new Vector2Int(at.x + dx, at.y + dy);
                if (!Eligible(DisasterKind.Wildfire, c) || Grid.Data.IsUnderground(c)) continue;
                if (fire == null)
                {
                    if (!CanStartAt(c) || (fire = Begin(DisasterKind.Wildfire, c)) == null) return;
                    continue;
                }
                Add(fire, c);
            }
        }

        /// <summary>Room for another one: not too many going, none too close.</summary>
        bool CanStartAt(Vector2Int cell)
        {
            if (_active.Count >= DisasterRules.MaxActive) return false;
            // Not down in a ruin (P4-85): a guardian's quake there brought the town's workers underground.
            if (Grid.Data.IsUnderground(cell)) return false;
            var p = GridMap.CellToWorld(cell);
            // Cleansed land (P4-83): the region's disasters went with its lord.
            if (Services.TryGet<Ecosystem.RegionLordService>(out var lords) && lords.IsSealedAt(p)) return false;
            return _active.All(d => Vector2.Distance(d.WorldOrigin, p) >= DisasterRules.MinSpacing);
        }

        /// <summary>The cell by the strike whose ground suits the kind (a fire needs the forest edge, not the grass in front of it).</summary>
        bool FindGround(DisasterKind kind, Vector2Int at, out Vector2Int cell)
        {
            for (int r = 0; r <= 2; r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                var c = new Vector2Int(at.x + dx, at.y + dy);
                if (!Grid.InBounds(c) || Grid.IsInTown(c)) continue;
                if (!DisasterRules.GroundFits(kind, _world.Map[c], _world.Map.RegionOf(c.x, c.y), NearMountain(c))) continue;
                if (!DisasterRules.CanSpreadTo(kind, _world.Map[c], _world.Map.RegionOf(c.x, c.y), Grid.HazardAt(c), false, NearMountain(c))) continue;
                cell = c;
                return true;
            }
            cell = at;
            return false;
        }

        bool NearMountain(Vector2Int c)
        {
            for (int dy = -4; dy <= 4; dy++)
            for (int dx = -4; dx <= 4; dx++)
            {
                int x = c.x + dx, y = c.y + dy;
                if (_world.Map.InBounds(x, y) && _world.Map[x, y] == TileType.Mountain) return true;
            }
            return false;
        }

        bool Eligible(DisasterKind kind, Vector2Int c)
            => Grid.InBounds(c) && DisasterRules.CanSpreadTo(kind, _world.Map[c], _world.Map.RegionOf(c.x, c.y), Grid.HazardAt(c),
                   Grid.IsInTown(c), kind == DisasterKind.Avalanche && NearMountain(c));

        /// <summary>Starts one right here (debug and tests call this too). Null when the ground does not suit it.</summary>
        public Disaster Begin(DisasterKind kind, Vector2Int cell)
        {
            if (!FindGround(kind, cell, out cell)) return null;
            var d = new Disaster
            {
                Id = ++_nextId,
                Kind = kind,
                Origin = cell,
                StartedHour = Now,
                Core = GridMap.CellToWorld(cell),
                CoreWorkLeft = DisasterRules.WorkHours(kind),
            };
            _active.Add(d);
            switch (kind)
            {
                case DisasterKind.Avalanche:
                    Burst(d, DisasterRules.AvalancheBurst);
                    break;
                case DisasterKind.Quake:
                    Burst(d, 6);
                    break;
                case DisasterKind.Sandstorm:
                    float a = GameRandom.Shared.Range(0f, Mathf.PI * 2f);
                    d.Drift = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    RefreshStorm(d);
                    break;
                default:
                    Add(d, cell);
                    break;
            }
            Debug.Log($"[Disaster] {d.Name} at {cell} ({QuestBoard.DescribeArea(d.WorldOrigin)})");
            EventBus.Publish(new DisasterStartedEvent { Disaster = d });
            return d;
        }

        /// <summary>A sudden one (avalanche, quake): this many cells at once, outward from the origin.</summary>
        void Burst(Disaster d, int count)
        {
            var queue = new Queue<Vector2Int>();
            var seen = new HashSet<Vector2Int> { d.Origin };
            queue.Enqueue(d.Origin);
            while (queue.Count > 0 && d.Cells.Count < count)
            {
                var c = queue.Dequeue();
                if (Eligible(d.Kind, c)) Add(d, c);
                foreach (var n in Neighbours8.OrderBy(_ => GameRandom.Shared.Value))
                    if (seen.Add(c + n) && (c + n - d.Origin).sqrMagnitude <= count * 2) queue.Enqueue(c + n);
            }
        }

        // ---------------- Cells ----------------

        void Add(Disaster d, Vector2Int c)
        {
            if (!d.Cells.Add(c)) return;
            Grid.SetHazard(c, d.Hazard);
            if (d.Kind == DisasterKind.Wildfire) d.LitAt[c] = Now;
            if (Hazards.Blocks(d.Hazard)) Bury(c);
            EventBus.Publish(new HazardChangedEvent { Cell = c, Kind = d.Hazard });
            DriveOffDens(d, c);
        }

        /// <summary>A cell leaves the disaster: burnt out (fire), or simply cleared.</summary>
        void Remove(Disaster d, Vector2Int c, bool burntOut = false)
        {
            if (!d.Cells.Remove(c)) return;
            d.LitAt.Remove(c);
            d.WorkDone.Remove(c);
            var after = burntOut ? HazardKind.Burnt : HazardKind.None;
            Grid.SetHazard(c, after);
            if (burntOut)
            {
                _burnt[c] = Now;
                if (_renderer != null) _renderer.SetTreeBurnt(c, true);
                _world.Risk.SetForestCleared(c.x, c.y, true);
            }
            EventBus.Publish(new HazardChangedEvent { Cell = c, Kind = after });
        }

        /// <summary>A disaster the guild knows of with ground here (P4-86: the walls cannot be pushed out over it), or null.</summary>
        public Disaster KnownOn(System.Func<Vector2Int, bool> area)
            => _active.FirstOrDefault(d => d.Known && !d.Over && (d.Cells.Any(area) || d.HasCore && area(GridMap.WorldToCell(d.Core))));

        /// <summary>
        /// The town's walls moved out over this ground (P4-86): whatever burns or lies here goes, as do burnt cells
        /// waiting to grow back (the woods were cleared). A disaster left with nothing ends as if it had died down.
        /// </summary>
        public void ClearGround(System.Func<Vector2Int, bool> cleared)
        {
            foreach (var d in _active.ToList())
            {
                foreach (var c in d.Cells.Where(cleared).ToList()) Remove(d, c);
                if (d.HasCore && cleared(GridMap.WorldToCell(d.Core)) || d.Cells.Count == 0) BeginEnding(d, false);
            }
            foreach (var c in _burnt.Keys.Where(cleared).ToList())
            {
                _burnt.Remove(c);
                if (Grid.HazardAt(c) != HazardKind.Burnt) continue;
                Grid.SetHazard(c, HazardKind.None);
                EventBus.Publish(new HazardChangedEvent { Cell = c, Kind = HazardKind.None });
            }
        }

        /// <summary>Snow or a crack opens under someone: hurt and thrown clear.</summary>
        void Bury(Vector2Int c)
        {
            UnitRegistry.Collect(GridMap.CellToWorld(c), 1f, u => u.IsAlive && !Unharmed(u) && GridMap.WorldToCell(u.Position) == c, s_units);
            foreach (var u in s_units)
            {
                u.ReceiveDamage(Mathf.Round(u.Stats.MaxHp * DisasterRules.BuriedDamage), null, false);
                if (!u.IsAlive) continue;
                if (FreeCellNear(c, out var free))
                {
                    u.Mover.Stop();
                    u.transform.position = GridMap.CellToWorld(free);
                }
            }
        }

        /// <summary>The nearest walkable cell within 4 that is not walled in by snow or a crack.</summary>
        bool FreeCellNear(Vector2Int from, out Vector2Int free)
        {
            for (int r = 1; r <= 4; r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                var c = new Vector2Int(from.x + dx, from.y + dy);
                if (Grid.IsWalkable(c) && !Hazards.Blocks(Grid.HazardAt(c))) { free = c; return true; }
            }
            free = from;
            return false;
        }

        /// <summary>A den in the way is abandoned: its pack moves out or takes to the road (P4-72, the wild changes).</summary>
        void DriveOffDens(Disaster d, Vector2Int c)
        {
            // Only what harms or walls them in: a passing sandstorm does neither (P4-73: one drifting storm
            // emptied every den on its way).
            if (d.Kind == DisasterKind.Sandstorm) return;
            if (!Services.TryGet<EcosystemManager>(out var eco) || !Services.TryGet<TerritoryService>(out var territory)) return;
            var p = GridMap.CellToWorld(c);
            foreach (var nest in eco.Nests)
            {
                if (nest.IsEmpty || d.Fled.Contains(nest)) continue;
                if (Vector2.Distance(nest.WorldPosition, p) > DisasterRules.NestFleeDistance) continue;
                d.Fled.Add(nest);
                territory.FleeDisaster(nest, p, d.Name);
            }
        }

        // ---------------- Running ----------------

        void Update()
        {
            if (_clock == null) return;
            float now = Now;
            float dh = Mathf.Clamp(now - _lastHour, 0f, 1f);
            _lastHour = now;
            if (dh > 0f)
            {
                for (int i = _active.Count - 1; i >= 0; i--) Step(_active[i], dh);
                _hurtHours += dh;
            }
            _hurtTimer -= Time.deltaTime;
            if (_hurtTimer <= 0f)
            {
                _hurtTimer = HurtInterval;
                if (_hurtHours > 0f) HurtUnits(_hurtHours);
                _hurtHours = 0f;
            }
            _sightTimer -= Time.unscaledDeltaTime;
            if (_sightTimer <= 0f)
            {
                _sightTimer = SightInterval;
                CheckSighting();
            }
        }

        void Step(Disaster d, float dh)
        {
            if (!d.Ending && Now - d.StartedHour >= DisasterRules.LifetimeHours(d.Kind)) BeginEnding(d, dealtWith: false);
            switch (d.Kind)
            {
                case DisasterKind.Wildfire: StepFire(d, dh); break;
                case DisasterKind.Sandstorm: StepStorm(d, dh); break;
                default: StepGrowth(d, dh); break;
            }
            if (d.Ending && d.Kind != DisasterKind.Wildfire) Recede(d, dh);
            // A storm over a lake or a range covers nothing for a while but is not over (P4-73).
            if (d.Cells.Count == 0 && (d.Ending || d.Kind != DisasterKind.Sandstorm)) Finish(d);
        }

        /// <summary>Burning cells set their forest neighbours alight, and burn out after a while.</summary>
        void StepFire(Disaster d, float dh)
        {
            s_cells.Clear();
            s_cells.AddRange(d.Cells);
            float p = 1f - Mathf.Pow(1f - DisasterRules.FireSpreadPerHour, dh * SpreadScale);
            foreach (var c in s_cells)
            {
                if (Now - d.LitAt[c] >= DisasterRules.BurnHours) { Remove(d, c, burntOut: true); continue; }
                if (d.Ending) continue;
                foreach (var n in Neighbours8)
                {
                    if (d.Cells.Count >= DisasterRules.MaxCells(d.Kind)) break;
                    var to = c + n;
                    if (!d.Cells.Contains(to) && Eligible(d.Kind, to) && GameRandom.Shared.Chance(p)) Add(d, to);
                }
            }
        }

        /// <summary>Poison and cracks creep outward; an avalanche keeps sliding for a few hours.</summary>
        void StepGrowth(Disaster d, float dh)
        {
            if (d.Ending || d.Cells.Count >= DisasterRules.MaxCells(d.Kind) || d.Cells.Count == 0) return;
            float rate = d.Kind == DisasterKind.Avalanche
                ? (Now - d.StartedHour < DisasterRules.SlideHours ? DisasterRules.GrowCellsPerHour * 0.5f : 0f)
                : DisasterRules.GrowCellsPerHour;
            d.GrowthOwed += rate * dh * SpreadScale;
            var cells = d.Cells.ToList();
            int tries = 0;
            while (d.GrowthOwed >= 1f && tries++ < 40 && d.Cells.Count < DisasterRules.MaxCells(d.Kind))
            {
                // Cracks run on from their ends, the rest swell anywhere along the edge.
                var from = cells[GameRandom.Shared.Range(0, cells.Count)];
                var to = from + Neighbours8[GameRandom.Shared.Range(0, d.Kind == DisasterKind.Quake ? 4 : 8)];
                if (d.Cells.Contains(to) || !Eligible(d.Kind, to)) continue;
                Add(d, to);
                cells.Add(to);
                d.GrowthOwed -= 1f;
            }
            if (tries >= 40) d.GrowthOwed = Mathf.Min(d.GrowthOwed, 1f); // boxed in: no debt piling up
        }

        /// <summary>The storm drifts and grows; its cells are whatever lies under it.</summary>
        void StepStorm(Disaster d, float dh)
        {
            if (!d.Ending)
            {
                var next = d.Core + d.Drift * DisasterRules.StormDriftPerHour * dh;
                var cell = GridMap.WorldToCell(next);
                // Turns away from the map's edge (and wanders a little).
                if (!Grid.InBounds(cell) || cell.x < 4 || cell.y < 4 || cell.x > Grid.Width - 5 || cell.y > Grid.Data.SurfaceHeight - 5)
                    d.Drift = -d.Drift;
                else d.Core = next;
                d.Drift = Quaternion.Euler(0f, 0f, GameRandom.Shared.Range(-20f, 20f) * dh) * d.Drift;
            }
            if (d.Ending || Now < d.NextStormRefresh) return;
            d.NextStormRefresh = Now + 0.25f;
            RefreshStorm(d);
        }

        void RefreshStorm(Disaster d)
        {
            float r = DisasterRules.StormRadius(Now - d.StartedHour);
            var centre = GridMap.WorldToCell(d.Core);
            var inside = new HashSet<Vector2Int>();
            int ri = Mathf.CeilToInt(r);
            for (int dy = -ri; dy <= ri; dy++)
            for (int dx = -ri; dx <= ri; dx++)
            {
                if (dx * dx + dy * dy > r * r) continue;
                var c = new Vector2Int(centre.x + dx, centre.y + dy);
                if (inside.Count < DisasterRules.MaxCells(d.Kind) && Eligible(d.Kind, c)) inside.Add(c);
            }
            foreach (var c in d.Cells.Where(c => !inside.Contains(c)).ToList()) Remove(d, c);
            foreach (var c in inside) if (!d.Cells.Contains(c)) Add(d, c);
        }

        /// <summary>Ended: what is left clears away over <see cref="DisasterRules.RecedeHours"/>.</summary>
        void Recede(Disaster d, float dh)
        {
            // Evenly over the time left: this step takes its share of what remains.
            float left = DisasterRules.RecedeHours - (Now - d.EndingSinceHour);
            int remove = left <= 0f ? d.Cells.Count : Mathf.CeilToInt(d.Cells.Count * Mathf.Clamp01(dh / (left + dh)));
            // Outermost first: the furthest from the core goes.
            foreach (var c in d.Cells.OrderByDescending(c => (GridMap.CellToWorld(c) - d.Core).sqrMagnitude).Take(remove).ToList())
                Remove(d, c);
        }

        void BeginEnding(Disaster d, bool dealtWith)
        {
            if (d.Ending) return;
            d.Ending = true;
            d.DealtWith = dealtWith;
            d.EndingSinceHour = Now;
        }

        void Finish(Disaster d)
        {
            if (d.Over) return;
            d.Over = true;
            d.Ending = true;
            _active.Remove(d);
            _history.Add(d);
            if (d.Quest != null && _board != null) _board.SettleTownQuest(d.Quest, d.DealtWith ? null : DiedDownReason);
            Debug.Log($"[Disaster] {d.Name} over ({(d.DealtWith ? "dealt with" : "died down")})");
            EventBus.Publish(new DisasterEndedEvent { Disaster = d, DealtWith = d.DealtWith, Known = d.Known });
        }

        /// <summary>Why the town took a disaster quest down by itself.</summary>
        public const string DiedDownReason = "재해가 저절로 사그라들었습니다";

        // ---------------- Harm ----------------

        /// <summary>
        /// Fire and poison hurt whoever stands in them. Anyone standing still in one steps out to
        /// the nearest safe cell (the guild master too: a fire that spread under someone idle used
        /// to burn them where they stood); those on the move keep their road, which goes round it
        /// when it can (<see cref="Hazards.CostFactor"/>).
        /// </summary>
        void HurtUnits(float hours)
        {
            s_units.Clear();
            s_units.AddRange(UnitRegistry.All);
            foreach (var u in s_units)
            {
                if (u == null || !u.IsAlive || Unharmed(u)) continue;
                var cell = GridMap.WorldToCell(u.Position);
                var k = Grid.HazardAt(cell);
                if (!Hazards.Hurts(k, u is Monsters.Monster)) continue;
                float share = k switch
                {
                    HazardKind.Fire => DisasterRules.FireDamagePerHour,
                    HazardKind.Miasma => DeepWoodRules.MiasmaDamagePerHour, // the deep woods' (P4-103), people only
                    _ => DisasterRules.PoisonDamagePerHour,
                };
                float dmg = u.Stats.MaxHp * share * hours;
                if (dmg > 0f) u.ReceiveDamage(dmg, null, false);
                if (u.IsAlive && u.Mover != null && !u.Mover.IsMoving && SafeCellNear(cell, out var safe))
                    u.Mover.SetDestination(GridMap.CellToWorld(safe), 0.2f);
            }
        }

        /// <summary>
        /// Untouched by any disaster: a region lord is at home in its region's (P4-83, it sets them off itself), and the
        /// dragon (P4-93) is fireproof and flies over the rest.
        /// </summary>
        static bool Unharmed(Unit u) => u.IsAirborne || u is Monsters.Monster { IsRegionLord: true } or Monsters.Monster { IsDragon: true };

        /// <summary>The nearest walkable cell within 4 that neither burns nor poisons.</summary>
        bool SafeCellNear(Vector2Int from, out Vector2Int safe)
        {
            for (int r = 1; r <= 4; r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                var c = new Vector2Int(from.x + dx, from.y + dy);
                if (Grid.IsWalkable(c) && !Hazards.Hurts(Grid.HazardAt(c))) { safe = c; return true; }
            }
            safe = from;
            return false;
        }

        void OnHour(HourPassedEvent e)
        {
            DamageTheTown();
            GrowBack();
        }

        /// <summary>
        /// A disaster at the walls (P4-72, user decision): gates and buildings in reach lose
        /// durability every hour, and the day's tax is cut.
        /// </summary>
        void DamageTheTown()
        {
            if (!Services.TryGet<TownService>(out var town)) return;
            var walls = town.TownRect;
            foreach (var d in _active)
            {
                if (d.Ending || !d.Cells.Any(c => DistanceToRect(c, walls) <= DisasterRules.TownReach)) continue;
                if (!d.ReachedTown)
                {
                    d.ReachedTown = true;
                    EventBus.Publish(new DisasterReachedTownEvent { Disaster = d });
                }
                _treasury?.CutTodaysTax(DisasterRules.TaxCut);
                if (Services.TryGet<GateService>(out var gates))
                    foreach (var g in gates.Gates)
                        if (!g.IsBroken && InReach(d, g.ClosestPoint)) g.Damage(g.MaxHp * DisasterRules.TownDamagePerHour);
                foreach (var b in town.Buildings)
                    if (!b.IsDestroyed && InReach(d, b.ClosestPoint)) b.Damage(b.MaxHp * DisasterRules.TownDamagePerHour);
            }
        }

        /// <summary>Some cell of the disaster lies within <see cref="DisasterRules.TownDamageReach"/> of the thing (its closest point to the cell).</summary>
        static bool InReach(Disaster d, System.Func<Vector2, Vector2> closestPoint)
        {
            float r2 = DisasterRules.TownDamageReach * DisasterRules.TownDamageReach;
            foreach (var c in d.Cells)
            {
                var p = GridMap.CellToWorld(c);
                if ((closestPoint(p) - p).sqrMagnitude <= r2) return true;
            }
            return false;
        }

        static float DistanceToRect(Vector2Int c, RectInt r)
        {
            int dx = Mathf.Max(r.xMin - c.x, 0, c.x - (r.xMax - 1));
            int dy = Mathf.Max(r.yMin - c.y, 0, c.y - (r.yMax - 1));
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Burnt forest grows back after a few days.</summary>
        void GrowBack()
        {
            foreach (var kv in _burnt.Where(kv => Now - kv.Value >= DisasterRules.RegrowHours).ToList())
            {
                _burnt.Remove(kv.Key);
                if (Grid.HazardAt(kv.Key) != HazardKind.Burnt) continue;
                Grid.SetHazard(kv.Key, HazardKind.None);
                if (_renderer != null) _renderer.SetTreeBurnt(kv.Key, false);
                _world.Risk.SetForestCleared(kv.Key.x, kv.Key.y, false);
                EventBus.Publish(new HazardChangedEvent { Cell = kv.Key, Kind = HazardKind.None });
            }
        }

        // ---------------- What the guild sees ----------------

        /// <summary>
        /// The guild learns of a disaster only by seeing it (user decision 2026-10-03: no smoke seen
        /// from afar); the town then posts a quest for it.
        /// </summary>
        void CheckSighting()
        {
            if (!Services.TryGet<FogOfWar>(out var fog)) return;
            foreach (var d in _active)
            {
                if (d.Known || d.Ending) continue;
                bool seen = d.HasCore && fog.IsVisibleRaw(d.Core) || d.Cells.Any(c => fog.IsVisibleRaw(c));
                if (!seen) continue;
                d.Known = true;
                d.SightedHour = Now;
                EventBus.Publish(new DisasterSightedEvent { Disaster = d });
                PostQuest(d);
            }
        }

        void PostQuest(Disaster d)
        {
            if (_board == null) return;
            float dist = Vector2.Distance(d.WorldOrigin, _world.TownCenterWorld);
            var (gold, exp) = DisasterRules.Reward(d.Kind, dist);
            d.Quest = _board.PostTownQuest(new QuestDraft
            {
                Type = QuestType.Disaster,
                Point = d.HasCore ? d.Core : d.WorldOrigin,
                HasPoint = true,
                Radius = 10f,
                RewardGold = gold,
                RewardExp = exp,
                MaxParty = 4,
            }, d);
        }

        // ---------------- Dealing with it ----------------

        /// <summary>
        /// Where a worker standing at <paramref name="from"/> should work next: the core, or the
        /// nearest cell. False when nothing is left.
        /// </summary>
        public bool NextWork(Disaster d, Vector2 from, out Vector2Int cell, out Vector2 spot)
        {
            cell = default;
            spot = from;
            if (d == null || d.Over || d.Ending) return false;
            if (d.HasCore)
            {
                cell = GridMap.WorldToCell(d.Core);
                spot = d.Core;
                return true;
            }
            float best = float.MaxValue;
            bool found = false;
            foreach (var c in d.Cells)
            {
                float dist = (GridMap.CellToWorld(c) - from).sqrMagnitude;
                if (dist >= best) continue;
                best = dist;
                cell = c;
                found = true;
            }
            spot = GridMap.CellToWorld(cell);
            return found;
        }

        /// <summary>
        /// A worker works the cell (or the core) for <paramref name="hours"/>. True when that piece
        /// is done. The last piece ends the disaster as dealt with.
        /// </summary>
        public bool Work(Disaster d, Vector2Int cell, float hours, IQuestTaker worker)
        {
            if (d == null || d.Over || d.Ending || hours <= 0f) return false;
            if (d.HasCore)
            {
                d.CoreWorkLeft -= hours;
                if (d.CoreWorkLeft > 0f) return false;
                if (worker != null) d.Quest?.AddContribution(worker, 5);
                if (d.Quest != null) d.Quest.Worked++;
                BeginEnding(d, dealtWith: true);
                return true;
            }
            if (!d.Cells.Contains(cell)) return false;
            d.WorkDone.TryGetValue(cell, out var done);
            done += hours;
            if (done < DisasterRules.WorkHours(d.Kind)) { d.WorkDone[cell] = done; return false; }
            Remove(d, cell);
            if (worker != null) d.Quest?.AddContribution(worker, 1);
            if (d.Quest != null) d.Quest.Worked++;
            if (d.Cells.Count == 0) BeginEnding(d, dealtWith: true);
            return true;
        }

        /// <summary>The disaster a quest is about, or null.</summary>
        public Disaster Of(Quest q) => q == null ? null : _active.FirstOrDefault(d => d.Quest == q) ?? _history.FirstOrDefault(d => d.Quest == q);
    }
}
