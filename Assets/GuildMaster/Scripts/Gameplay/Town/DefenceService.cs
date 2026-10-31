using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.Player;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>A trap of the guild's caught a monster (P4-113); <see cref="Seen"/>: the guild saw it.</summary>
    public struct DefenceTrapSprungEvent { public Unit Victim; public Vector2Int Cell; public bool Seen; public int ChargesLeft; }

    /// <summary>
    /// The guild's traps (P4-113, H10; numbers in <see cref="DefenceRules"/>). Set one cell at a time beyond the walls, up to
    /// <see cref="DefenceRules.Reach"/> out, for gold and iron; only monsters spring them — hurt and held a moment — and the
    /// skilled and better sometimes step round. Each springs <see cref="DefenceRules.TrapCharges"/> times, then lies spent
    /// until set again for gold. Walls moving out over one take it up and give back what it cost. Saved as "defences".
    /// The arrow towers are buildings (<see cref="ArrowTower"/>).
    /// </summary>
    public class DefenceService : MonoBehaviour, ISaveable
    {
        const int SortingOrder = 5; // on the ground, under everyone (the deep woods' traps lie as low)

        WorldService _world;
        GuildTreasury _treasury;
        Fog.FogOfWar _fog;
        float _timer;

        /// <summary>Trap cells (map index) and the times each will still spring.</summary>
        readonly Dictionary<int, int> _traps = new();
        readonly Dictionary<int, SpriteRenderer> _sprites = new();
        /// <summary>
        /// Monsters a trap has had its go at — caught, or stepped round — and the cell: not again until they leave it (one held
        /// fast on the plank was caught three times over in a second).
        /// </summary>
        readonly Dictionary<Unit, int> _dealtWith = new();
        readonly List<Unit> _monsters = new();

        public int TrapCount => _traps.Count;
        public int SpentCount => _traps.Values.Count(c => c <= 0);
        public IEnumerable<(Vector2Int cell, int charges)> Traps => _traps.Select(kv => (Cell(kv.Key), kv.Value));
        public bool IsPlacingTrap { get; private set; }

        public void Init(WorldService world, GuildTreasury treasury, Fog.FogOfWar fog)
        {
            _world = world;
            _treasury = treasury;
            _fog = fog;
            EventBus.Subscribe<TownGroundChangedEvent>(OnTownGround);
        }

        void OnDestroy() => EventBus.Unsubscribe<TownGroundChangedEvent>(OnTownGround);

        Vector2Int Cell(int index) => new(index % _world.Map.Width, index / _world.Map.Width);

        public int ChargesAt(Vector2Int cell) => _world.Map.InBounds(cell) && _traps.TryGetValue(_world.Map.Index(cell.x, cell.y), out int c) ? c : -1;

        // ---------------- Setting traps ----------------

        /// <summary>Why the guild cannot pay for another trap, or null.</summary>
        public string CannotAffordTrap()
        {
            if (_treasury.Gold < DefenceRules.TrapGold) return $"골드가 부족합니다 ({_treasury.Gold}/{DefenceRules.TrapGold}G).";
            if (_treasury.GetStock(DefenceRules.TrapIronItem) < DefenceRules.TrapIron)
                return $"철광석이 부족합니다 ({_treasury.GetStock(DefenceRules.TrapIronItem)}/{DefenceRules.TrapIron}).";
            return null;
        }

        /// <summary>Why a trap cannot lie here, or null: open ground beyond the walls and within reach, with no trap on it.</summary>
        public string CannotSetTrapAt(Vector2 point)
        {
            var c = GridMap.WorldToCell(point);
            var map = _world.Map;
            if (!map.InBounds(c) || map.IsUnderground(c)) return "지도 밖입니다.";
            if (DefenceRules.OutOfReach(new RectInt(c, Vector2Int.one), map.TownRect) is { } far) return far;
            if (!_world.Grid.IsWalkable(c)) return "여기에는 놓을 수 없습니다 (걸을 수 없는 땅이거나 건물이 있습니다).";
            if (_traps.ContainsKey(map.Index(c.x, c.y))) return "이미 함정이 있습니다.";
            return null;
        }

        /// <summary>Pays for a trap and sets it here. False (and why) when it cannot.</summary>
        public bool TrySetTrap(Vector2 point, out string error)
        {
            error = CannotAffordTrap() ?? CannotSetTrapAt(point);
            if (error != null) return false;
            if (!_treasury.TrySpendGold(DefenceRules.TrapGold, GoldReason.Build)) { error = "골드가 부족합니다."; return false; }
            _treasury.TryRemoveStock(DefenceRules.TrapIronItem, DefenceRules.TrapIron);
            var c = GridMap.WorldToCell(point);
            Set(_world.Map.Index(c.x, c.y), DefenceRules.TrapCharges);
            return true;
        }

        /// <summary>
        /// "Click where it goes", again and again until cancelled or the money runs out (traps are laid in rows); the prompt
        /// says how the last click went (<paramref name="note"/>).
        /// </summary>
        public bool BeginTrapPlacement(out string error, string note = null)
        {
            error = CannotAffordTrap();
            if (error != null) return false;
            if (!Services.TryGet<MapPicker>(out var picker)) { error = "지금은 놓을 수 없습니다."; return false; }
            IsPlacingTrap = true;
            picker.Begin($"{(note != null ? note + " — " : "")}함정을 놓을 자리를 클릭하세요: 성벽 밖 {DefenceRules.Reach}칸 안 (우클릭·ESC 끝)", p =>
            {
                IsPlacingTrap = false;
                bool set = TrySetTrap(p, out var why);
                BeginTrapPlacement(out _, set ? $"함정 {TrapCount}개" : why);
            }, () => IsPlacingTrap = false);
            return true;
        }

        /// <summary>Gold to set every spent trap again.</summary>
        public int RearmCost => SpentCount * DefenceRules.TrapRearmGold;

        /// <summary>Sets every spent trap again for gold. False (and why) when there are none or the gold is short.</summary>
        public bool TryRearmAll(out string error)
        {
            int spent = SpentCount;
            error = spent == 0 ? "다 쓴 함정이 없습니다." : _treasury.Gold < RearmCost ? $"골드가 부족합니다 ({_treasury.Gold}/{RearmCost}G)." : null;
            if (error != null) return false;
            if (!_treasury.TrySpendGold(RearmCost, GoldReason.Repair)) { error = "골드가 부족합니다."; return false; }
            foreach (var cell in _traps.Where(kv => kv.Value <= 0).Select(kv => kv.Key).ToList()) Set(cell, DefenceRules.TrapCharges);
            return true;
        }

        void Set(int index, int charges)
        {
            _traps[index] = charges;
            if (!_sprites.TryGetValue(index, out var sr) || sr == null)
            {
                var go = new GameObject("DefenceTrap");
                go.transform.SetParent(transform, false);
                var p = GridMap.CellToWorld(Cell(index));
                go.transform.position = new Vector3(p.x, p.y, 0f);
                sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = SortingOrder;
                _sprites[index] = sr;
            }
            sr.sprite = charges > 0 ? ArmedSprite : SpentSprite;
        }

        void Take(int index)
        {
            _traps.Remove(index);
            if (_sprites.TryGetValue(index, out var sr) && sr != null) Destroy(sr.gameObject);
            _sprites.Remove(index);
        }

        /// <summary>The walls moved out (P4-86): traps now inside them or under them come up, and what they cost comes back.</summary>
        void OnTownGround(TownGroundChangedEvent e)
        {
            var town = _world.Map.TownRect;
            var taken = _traps.Keys.Where(i => DefenceRules.CellsBeyondWalls(Cell(i), town) <= 0).ToList();
            if (taken.Count == 0) return;
            foreach (var i in taken) Take(i);
            _treasury.AddGold(taken.Count * DefenceRules.TrapGold, GoldReason.Build);
            _treasury.AddStock(DefenceRules.TrapIronItem, taken.Count * DefenceRules.TrapIron);
            if (Services.TryGet<Session.GameLog>(out var log))
                log.Add($"[방어] 넓어진 성벽 안의 함정 {taken.Count}개를 거두고 {taken.Count * DefenceRules.TrapGold}G·철광석 {taken.Count * DefenceRules.TrapIron}개를 돌려받았습니다.",
                        Session.LogCategory.Economy);
        }

        // ---------------- Springing ----------------

        void Update()
        {
            if (_world == null) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = DefenceRules.CheckSeconds;
            if (_traps.Count == 0 || _traps.Values.All(c => c <= 0)) return;
            _monsters.Clear();
            foreach (var u in UnitRegistry.All)
                if (u != null && u.IsAlive && u.Faction == Faction.Monster && !u.IsAirborne) _monsters.Add(u);
            foreach (var m in _monsters)
            {
                var c = GridMap.WorldToCell(m.Position);
                if (!_world.Map.InBounds(c)) continue;
                int i = _world.Map.Index(c.x, c.y);
                // Still on the trap that had its go at it: nothing; off it: free to be caught by the next.
                if (_dealtWith.TryGetValue(m, out int was))
                {
                    if (was == i) continue;
                    _dealtWith.Remove(m);
                }
                if (!_traps.TryGetValue(i, out int charges) || charges <= 0) continue;
                _dealtWith[m] = i;
                if (GameRandom.Shared.Chance(DefenceRules.DodgeChance(m.Tier))) continue; // saw it in time and stepped round
                Spring(m, c, i);
            }
            if (_dealtWith.Count > 64)
                foreach (var gone in _dealtWith.Keys.Where(u => u == null || !u.IsAlive).ToList()) _dealtWith.Remove(gone);
        }

        void Spring(Unit victim, Vector2Int cell, int index)
        {
            int left = _traps[index] - 1;
            Set(index, left);
            victim.ReceiveDamage(DefenceRules.TrapHit(victim.Stats.MaxHp), null, false);
            if (victim.IsAlive) victim.Stun(DefenceRules.TrapHoldSeconds);
            bool seen = _fog == null || _fog.IsVisible(cell);
            EventBus.Publish(new DefenceTrapSprungEvent { Victim = victim, Cell = cell, Seen = seen, ChargesLeft = left });
        }

        // ---------------- Pictures ----------------

        static Sprite s_armed, s_spent;

        /// <summary>A set trap: iron spikes on a plank.</summary>
        static Sprite ArmedSprite => s_armed ??= SpriteFactory.FromTemplate("defence_trap_armed", new[]
        {
            ".x...x...x.",
            ".x...x...x.",
            "xox.xox.xox",
            "ppppppppppp",
            "pbbbbbbbbbp",
            "ppppppppppp",
        }, new Dictionary<char, Color>
        {
            ['x'] = new Color(0.78f, 0.8f, 0.84f), ['o'] = new Color(0.45f, 0.47f, 0.52f),
            ['p'] = new Color(0.36f, 0.24f, 0.13f), ['b'] = new Color(0.62f, 0.42f, 0.24f),
        }, new Vector2(0.5f, 0.5f));

        /// <summary>A spent one: the spikes bent flat.</summary>
        static Sprite SpentSprite => s_spent ??= SpriteFactory.FromTemplate("defence_trap_spent", new[]
        {
            "...........",
            "..ooo.ooo..",
            "ppppppppppp",
            "pbbbbbbbbbp",
            "ppppppppppp",
        }, new Dictionary<char, Color>
        {
            ['o'] = new Color(0.45f, 0.4f, 0.36f), ['p'] = new Color(0.3f, 0.22f, 0.14f), ['b'] = new Color(0.48f, 0.36f, 0.24f),
        }, new Vector2(0.5f, 0.5f));

        // ---------------- Save ----------------

        [Serializable]
        class SaveData
        {
            public List<int> cells = new();
            public List<int> charges = new();
        }

        public string SaveKey => "defences";

        public string Capture()
        {
            var s = new SaveData();
            foreach (var kv in _traps.OrderBy(kv => kv.Key))
            {
                s.cells.Add(kv.Key);
                s.charges.Add(kv.Value);
            }
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            foreach (var i in _traps.Keys.ToList()) Take(i);
            int cells = _world.Map.Width * _world.Map.Height;
            for (int k = 0; k < s.cells.Count && k < s.charges.Count; k++)
                if (s.cells[k] >= 0 && s.cells[k] < cells) Set(s.cells[k], Mathf.Clamp(s.charges[k], 0, DefenceRules.TrapCharges));
        }
    }
}
