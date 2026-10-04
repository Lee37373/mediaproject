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
    public class DisasterService : MonoBehaviour
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

        // ---------------- Setting one off ----------------

        /// <summary>A monster's hazard skill struck the ground in a fight: by chance, a disaster starts there.</summary>
        void OnStrike(HazardStrikeEvent e)
        {
            if (_clock == null) return;
            if (!FindGround(e.Kind, GridMap.WorldToCell(e.Point), out var cell)) return;
            if (!CanStartAt(cell)) return;
            float chance = ChanceOverride >= 0f ? ChanceOverride : DisasterRules.TriggerChance;
            if (!GameRandom.Shared.Chance(chance)) return;
            Begin(e.Kind, cell);
        }

        /// <summary>Room for another one: not too many going, none too close.</summary>
        bool CanStartAt(Vector2Int cell)
        {
            if (_active.Count >= DisasterRules.MaxActive) return false;
            var p = GridMap.CellToWorld(cell);
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

        /// <summary>Snow or a crack opens under someone: hurt and thrown clear.</summary>
        void Bury(Vector2Int c)
        {
            UnitRegistry.Collect(GridMap.CellToWorld(c), 1f, u => u.IsAlive && GridMap.WorldToCell(u.Position) == c, s_units);
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
                if (!Grid.InBounds(cell) || cell.x < 4 || cell.y < 4 || cell.x > Grid.Width - 5 || cell.y > Grid.Height - 5)
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
                if (u == null || !u.IsAlive) continue;
                var cell = GridMap.WorldToCell(u.Position);
                var k = Grid.HazardAt(cell);
                if (!Hazards.Hurts(k)) continue;
                float share = k == HazardKind.Fire ? DisasterRules.FireDamagePerHour : DisasterRules.PoisonDamagePerHour;
                float dmg = u.Stats.MaxHp * share * hours;
                if (dmg > 0f) u.ReceiveDamage(dmg, null, false);
                if (u.IsAlive && u.Mover != null && !u.Mover.IsMoving && SafeCellNear(cell, out var safe))
                    u.Mover.SetDestination(GridMap.CellToWorld(safe), 0.2f);
            }
        }

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
