using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>Someone of the guild stepped on a hidden trap in the deep woods (P4-103).</summary>
    public struct TrapSprungEvent { public Unit Victim; public Vector2Int Cell; public bool Seen; }

    /// <summary>The guild has seen the deep woods for the first time (P4-103): word of what lurks in them.</summary>
    public struct DeepWoodsSightedEvent { public Vector2Int Cell; }

    /// <summary>
    /// The deep woods' hazards (P4-103, user decision 2026-10-24): miasma in patches (<see cref="HazardKind.Miasma"/> — laid
    /// at the start, the disasters' harm tick hurts people standing in it) and hidden traps that spring on whoever of the
    /// guild treads on one (damage, held fast a moment, set again a day later). Both only out past
    /// <see cref="DeepWoodRules.HazardMinDistance"/>, never on a trail. The woods themselves, their sight and going, and
    /// their dens are the map's (<see cref="RegionBarriers"/>, <see cref="DeepWoodRules"/>). Nothing is saved: the miasma
    /// and the traps are drawn again from the seed, a sprung trap is set again on a load.
    /// </summary>
    public class DeepWoods : MonoBehaviour
    {
        const float CheckInterval = 0.2f;
        const int SortingOrder = 5;

        WorldService _world;
        Fog.FogOfWar _fog;
        GameClock _clock;
        /// <summary>Trap cells (map index) and the game hour each is set again (0: set).</summary>
        readonly Dictionary<int, float> _traps = new();
        readonly List<(SpriteRenderer sr, float until)> _shown = new();
        readonly List<Unit> _victims = new();
        bool _sighted;
        float _timer;

        public int TrapCount => _traps.Count;
        public IEnumerable<int> TrapCells => _traps.Keys;
        public bool IsTrapSet(Vector2Int c) => _traps.TryGetValue(_world.Map.Index(c.x, c.y), out float at) && at <= _clock.TotalHours;

        public void Init(WorldService world, Fog.FogOfWar fog, GameClock clock, int seed)
        {
            _world = world;
            _fog = fog;
            _clock = clock;
            var map = world.Map;
            var town = map.TownCenter;
            float min2 = DeepWoodRules.HazardMinDistance * DeepWoodRules.HazardMinDistance;
            var rng = new GameRandom(seed ^ 0x7A11);
            float nx = rng.Range(0f, 5000f), ny = rng.Range(0f, 5000f);
            var open = new List<int>();
            for (int y = 0; y < map.SurfaceHeight; y++)
            for (int x = 0; x < map.Width; x++)
            {
                int i = y * map.Width + x;
                if (!map.Deep[i] || (x - town.x) * (x - town.x) + (y - town.y) * (y - town.y) < min2) continue;
                // Miasma in patches (about MiasmaCover of the woods).
                if (Mathf.PerlinNoise(x * 0.09f + nx, y * 0.09f + ny) > MiasmaThreshold)
                {
                    var c = new Vector2Int(x, y);
                    world.Grid.SetHazard(c, HazardKind.Miasma);
                    EventBus.Publish(new Disasters.HazardChangedEvent { Cell = c, Kind = HazardKind.Miasma }); // drawn by the hazard overlay
                }
                else open.Add(i);
            }
            // Traps where the air is clear (one caught in the miasma would never be told from it), a few cells apart.
            rng.Shuffle(open);
            int want = (open.Count + CountMiasma(map)) / DeepWoodRules.CellsPerTrap;
            var placed = new List<Vector2Int>();
            foreach (int i in open)
            {
                if (placed.Count >= want) break;
                var c = new Vector2Int(i % map.Width, i / map.Width);
                bool close = false;
                foreach (var p in placed) if ((p - c).sqrMagnitude < 36) { close = true; break; }
                if (close) continue;
                placed.Add(c);
                _traps[i] = 0f;
            }
            EventBus.Subscribe<Fog.FogRevealedEvent>(OnFogRevealed);
        }

        /// <summary>Perlin above this is miasma: about <see cref="DeepWoodRules.MiasmaCover"/> of the woods.</summary>
        const float MiasmaThreshold = 0.58f;

        int CountMiasma(MapData map)
        {
            int n = 0;
            for (int i = 0; i < map.Width * map.SurfaceHeight; i++) if (map.Deep[i] && _world.Grid.HazardAt(i % map.Width, i / map.Width) == HazardKind.Miasma) n++;
            return n;
        }

        void OnDestroy() => EventBus.Unsubscribe<Fog.FogRevealedEvent>(OnFogRevealed);

        /// <summary>The first deep woods the guild sees: said once (a loaded game that has seen them says it again — harmless).</summary>
        void OnFogRevealed(Fog.FogRevealedEvent e)
        {
            if (_sighted) return;
            var map = _world.Map;
            for (int y = e.Cells.yMin; y < e.Cells.yMax; y++)
            for (int x = e.Cells.xMin; x < e.Cells.xMax; x++)
            {
                if (!map.IsDeep(x, y) || !_fog.IsExploredRaw(new Vector2Int(x, y))) continue;
                _sighted = true;
                EventBus.Publish(new DeepWoodsSightedEvent { Cell = new Vector2Int(x, y) });
                return;
            }
        }

        void Update()
        {
            if (_world == null) return;
            for (int k = _shown.Count - 1; k >= 0; k--)
            {
                if (Time.time < _shown[k].until) continue;
                if (_shown[k].sr != null) Destroy(_shown[k].sr.gameObject);
                _shown.RemoveAt(k);
            }
            _timer -= Time.deltaTime;
            if (_timer > 0f || _traps.Count == 0) return;
            _timer = CheckInterval;
            float now = _clock.TotalHours;
            _victims.Clear();
            foreach (var u in UnitRegistry.All)
                if (u != null && u.IsAlive && u.Faction == Faction.Guild && !u.IsAirborne) _victims.Add(u);
            foreach (var u in _victims)
            {
                var c = GridMap.WorldToCell(u.Position);
                if (!_world.Map.InBounds(c)) continue;
                int i = _world.Map.Index(c.x, c.y);
                if (!_traps.TryGetValue(i, out float setAt) || setAt > now) continue;
                Spring(u, c, i, now);
            }
        }

        void Spring(Unit victim, Vector2Int cell, int index, float now)
        {
            _traps[index] = now + DeepWoodRules.TrapRearmHours;
            bool seen = _fog == null || _fog.IsVisible(cell);
            victim.ReceiveDamage(victim.Stats.MaxHp * DeepWoodRules.TrapDamage, null, false);
            if (victim.IsAlive) victim.Stun(DeepWoodRules.TrapHoldSeconds);
            if (victim is Adventurers.Adventurer a && a.IsAlive) a.Say("으악, 덫이다!");
            if (seen) Show(cell);
            EventBus.Publish(new TrapSprungEvent { Victim = victim, Cell = cell, Seen = seen });
        }

        /// <summary>The sprung trap, drawn where it caught someone for a while.</summary>
        void Show(Vector2Int cell)
        {
            var go = new GameObject("SprungTrap");
            go.transform.SetParent(transform, false);
            var p = GridMap.CellToWorld(cell);
            go.transform.position = new Vector3(p.x, p.y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = TrapSprite;
            sr.sortingOrder = SortingOrder;
            _shown.Add((sr, Time.time + DeepWoodRules.TrapShownSeconds));
        }

        static Sprite s_trap;

        /// <summary>A sprung jaw trap: iron jaws shut on a rusted plate.</summary>
        static Sprite TrapSprite => s_trap ??= SpriteFactory.FromTemplate("deepwoods_trap", new[]
        {
            "x.x.x.x.x.x",
            "xxxxxxxxxxx",
            "x...ooo...x",
            "x..ooooo..x",
            "x...ooo...x",
            "xxxxxxxxxxx",
            "x.x.x.x.x.x",
        }, new Dictionary<char, Color> { ['x'] = new Color(0.22f, 0.2f, 0.19f), ['o'] = new Color(0.58f, 0.32f, 0.16f) }, new Vector2(0.5f, 0.5f));
    }
}
