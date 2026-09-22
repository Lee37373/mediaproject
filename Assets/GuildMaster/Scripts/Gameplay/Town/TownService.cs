using System.Collections.Generic;
using GuildMaster.Data;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>Places the town's buildings, blocks their footprints, and answers town queries.</summary>
    public class TownService : MonoBehaviour
    {
        readonly List<Building> _buildings = new();
        WorldService _world;
        GameDatabase _db;

        public IReadOnlyList<Building> Buildings => _buildings;
        public RectInt TownRect => _world.Map.TownRect;
        public Vector2 PlazaPosition => _world.TownCenterWorld;

        /// <summary>
        /// Lays out the starting town. Durability comes from the building data when it is there
        /// (P5-1: it used to be hard-coded here, so tuning the town hall in DefaultContent quietly
        /// did nothing until an upgrade); the numbers below are only the fallback.
        /// </summary>
        public void Init(WorldService world, MapRenderer mapRenderer, GameDatabase db = null)
        {
            _world = world;
            _db = db;
            var c = world.Map.TownCenter;
            // Layout relative to the plaza (roads run along c.x and c.y).
            Place(BuildingType.TownHall, new RectInt(c.x + 2, c.y + 2, 6, 5), new Vector2Int(c.x + 5, c.y + 1), true, 600f, mapRenderer);
            Place(BuildingType.GuildHall, new RectInt(c.x - 8, c.y + 2, 7, 5), new Vector2Int(c.x - 4, c.y + 1), true, 400f, mapRenderer);
            Place(BuildingType.Blacksmith, new RectInt(c.x + 2, c.y - 6, 4, 4), new Vector2Int(c.x + 4, c.y - 1), true, 250f, mapRenderer);
            Place(BuildingType.Apothecary, new RectInt(c.x + 7, c.y - 5, 3, 3), new Vector2Int(c.x + 8, c.y - 1), true, 200f, mapRenderer);
            Place(BuildingType.TrainingGround, new RectInt(c.x - 9, c.y - 8, 7, 6), new Vector2Int(c.x - 5, c.y - 5), false, 150f, mapRenderer);
        }

        /// <summary>Level-1 durability from the building data, or the given fallback.</summary>
        float StartingHp(BuildingType type, float fallback)
        {
            var def = _db != null ? _db.Building(type) : null;
            float fromData = def != null ? def.Level(1).maxHp : 0f;
            return fromData > 0f ? fromData : fallback;
        }

        Building Place(BuildingType type, RectInt rect, Vector2Int door, bool blocks, float fallbackHp, MapRenderer mapRenderer)
        {
            var go = new GameObject();
            go.transform.SetParent(transform, false);
            var b = go.AddComponent<Building>();
            b.Init(type, rect, door, blocks, StartingHp(type, fallbackHp));
            if (blocks) _world.Grid.SetBlocked(rect, true);
            mapRenderer.ClearDecor(rect);
            _buildings.Add(b);
            return b;
        }

        /// <summary>
        /// Puts up a building the player placed (P4-13). Its door is the cell below it, so NPCs
        /// walking to it stand in front rather than inside.
        /// </summary>
        public Building Build(BuildingType type, RectInt rect, float hp, MapRenderer mapRenderer)
        {
            var door = new Vector2Int(rect.xMin + rect.width / 2, rect.yMin - 1);
            return Place(type, rect, door, blocks: true, hp, mapRenderer);
        }

        /// <summary>Takes a building away again (demolished); its ground becomes walkable.</summary>
        public void Remove(Building b)
        {
            if (b == null || !_buildings.Remove(b)) return;
            if (b.BlocksMovement) _world.Grid.SetBlocked(b.Footprint, false);
            Destroy(b.gameObject);
        }

        public Building Get(BuildingType type) => _buildings.Find(b => b.Type == type);

        public bool IsInTown(Vector2 pos) => _world.Grid.IsInTown(pos);

        /// <summary>
        /// Inside the walls, or outside but within <paramref name="reach"/> cells of them (measured
        /// towards the town centre). Used for who the defenders may go for: raiders shooting into
        /// town (DefendState) and monsters fighting the guild just outside the wall (TownGuardState).
        /// </summary>
        public bool WithinReachOfTown(Vector2 pos, float reach)
        {
            var toTown = _world.TownCenterWorld - pos;
            return toTown.sqrMagnitude <= reach * reach || IsInTown(pos + toTown.normalized * reach);
        }

        /// <summary>Random walkable spot around the plaza for idling NPCs.</summary>
        public Vector2 RandomPlazaSpot(Core.GameRandom rng, int radius = 5)
        {
            var c = _world.Map.TownCenter;
            if (_world.Grid.TryRandomWalkableNear(c, radius, rng, out var cell, 20))
                return GridMap.CellToWorld(cell) + rng.InsideUnitCircle() * 0.3f;
            return PlazaPosition;
        }
    }
}
