using System;
using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Items
{
    public struct GroundItemDroppedEvent { public GroundItem Item; }
    /// <summary>Someone took an item off the ground. Absorbed = a slime swallowed it (it is gone).</summary>
    public struct GroundItemTakenEvent { public ItemDefinition Item; public int Count; public Unit Taker; public Vector2 Position; public bool Absorbed; }

    /// <summary>
    /// Items lying on the map (P4-21): spawns drops (scattered around the corpse), finds items for
    /// pickers, hands them over, and cleans up — items rot after <see cref="LifetimeHours"/> game hours
    /// and the oldest go first above <see cref="MaxItems"/>.
    /// </summary>
    public class GroundItemService : MonoBehaviour
    {
        public const string PoolKey = "world.groundItem";
        public const float LifetimeHours = 48f;
        public const int MaxItems = 40;
        const float Scatter = 0.6f;

        readonly List<GroundItem> _items = new();
        PoolService _pool;
        GameClock _clock;
        GameDatabase _db;

        public IReadOnlyList<GroundItem> Items => _items;

        public void Init(PoolService pool, GameClock clock, GameDatabase db)
        {
            _pool = pool;
            _clock = clock;
            _db = db;
            _pool.Register(PoolKey, () => new GameObject("GroundItem").AddComponent<GroundItem>().gameObject, 8, maxSize: MaxItems + 8);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        void OnDestroy() => EventBus.Unsubscribe<HourPassedEvent>(OnHour);

        /// <summary>Drops each stack as one item, scattered around the point.</summary>
        public void Drop(Vector2 at, IReadOnlyList<ItemStack> stacks)
        {
            if (stacks == null) return;
            var world = Services.TryGet<World.WorldService>(out var w) ? w : null;
            foreach (var s in stacks)
            {
                var item = _db.Item(s.itemId);
                if (item == null || s.count <= 0) continue;
                var p = at + GameRandom.Shared.InsideUnitCircle() * Scatter;
                // Never inside a wall or water: fall back to the drop point.
                if (world != null && !world.Grid.IsWalkable(World.GridMap.WorldToCell(p))) p = at;
                var g = _pool.Spawn<GroundItem>(PoolKey, p);
                g.Init(item, s.count, _clock.TotalHours);
                _items.Add(g);
                EventBus.Publish(new GroundItemDroppedEvent { Item = g });
            }
            while (_items.Count > MaxItems) Remove(_items[0]);
        }

        /// <summary>Nearest item within radius passing the filter, or null.</summary>
        public GroundItem FindNearest(Vector2 from, float radius, Func<GroundItem, bool> filter)
        {
            GroundItem best = null;
            float bestD = radius * radius;
            foreach (var g in _items)
            {
                if (g == null || g.Item == null) continue;
                float d = (g.Position - from).sqrMagnitude;
                if (d > bestD || (filter != null && !filter(g))) continue;
                bestD = d;
                best = g;
            }
            return best;
        }

        public bool Contains(GroundItem g) => g != null && g.Item != null && _items.Contains(g);

        /// <summary>Removes the item from the map and reports who took it; returns its stack.</summary>
        public ItemStack Take(GroundItem g, Unit taker, bool absorbed = false)
        {
            if (!Contains(g)) return default;
            var stack = new ItemStack(g.Item.id, g.Count);
            EventBus.Publish(new GroundItemTakenEvent { Item = g.Item, Count = g.Count, Taker = taker, Position = g.Position, Absorbed = absorbed });
            Remove(g);
            return stack;
        }

        void Remove(GroundItem g)
        {
            _items.Remove(g);
            if (g != null) _pool.Despawn(g.gameObject);
        }

        void OnHour(HourPassedEvent e)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
                if (_items[i] == null || _clock.TotalHours - _items[i].DroppedAtHour > LifetimeHours) Remove(_items[i]);
        }
    }
}
