using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>Places the town's buildings, blocks their footprints, and answers town queries.</summary>
    public class TownService : MonoBehaviour, ISaveable
    {
        readonly List<Building> _buildings = new();
        WorldService _world;
        GameDatabase _db;

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SavedBuilding
        {
            public BuildingType type;
            public RectInt rect;
            public int level;
            public float max_hp;
            public float hp;
            /// <summary>Put up by the player (a watchtower): rebuilt on load; the starting town is already there.</summary>
            public bool placed;
        }

        [System.Serializable]
        class SaveData { public List<SavedBuilding> buildings = new(); }

        public string SaveKey => "town";

        public string Capture()
        {
            var s = new SaveData();
            foreach (var b in _buildings)
                s.buildings.Add(new SavedBuilding
                {
                    type = b.Type, rect = b.Footprint, level = b.Level, max_hp = b.MaxHp, hp = b.Hp,
                    placed = System.Array.IndexOf(ConstructionService.Placeable, b.Type) >= 0,
                });
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            foreach (var saved in s.buildings)
            {
                if (saved.placed)
                {
                    if (Services.TryGet<ConstructionService>(out var construction))
                        construction.RestorePlaced(saved.type, saved.rect, saved.level, saved.max_hp, saved.hp);
                    continue;
                }
                var b = _buildings.Find(x => x.Type == saved.type);
                if (b == null) { ctx.Warnings.Add("town: no building " + saved.type); continue; }
                b.RestoreState(saved.level, saved.max_hp, saved.hp);
            }
        }

        public ListView<Building> Buildings => new(_buildings);

        /// <summary>Goes up whenever a building is put up or taken away (a restore included, which raises no events).</summary>
        public int Version { get; private set; }

        /// <summary>How many of this kind stand or lie in rubble (P4-87: the next one costs more).</summary>
        public int CountOf(BuildingType type)
        {
            int n = 0;
            foreach (var b in _buildings) if (b.Type == type) n++;
            return n;
        }

        /// <summary>How many of its kind were put up before this one (its place in the build order, P4-87).</summary>
        public int EarlierOfKind(Building building)
        {
            int n = 0;
            foreach (var b in _buildings)
            {
                if (b == building) return n;
                if (b.Type == building.Type) n++;
            }
            return n;
        }
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
            // The guild hall people walk into (P4-115): its "door" is the middle of the room, its walls and furniture block.
            var hallRect = GuildHallLayout.FootprintAt(c);
            var hall = Place(BuildingType.GuildHall, hallRect, GuildHallLayout.ToMap(hallRect, GuildHallLayout.Lobby), false, 400f, mapRenderer);
            HallInside = hall.gameObject.AddComponent<GuildHallInterior>();
            HallInside.Init(world, hall);
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
            Version++;
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
            Version++;
            Destroy(b.gameObject);
        }

        public Building Get(BuildingType type) => _buildings.Find(b => b.Type == type);

        /// <summary>The inside of the guild hall: its places, and who is at them (P4-115).</summary>
        public GuildHallInterior HallInside { get; private set; }

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

        // ---------------- Repairs (P4-74) ----------------

        /// <summary>
        /// Gold for putting a building back to full durability (P4-74, user decision 2026-10-04: like
        /// the gates, P4-39). A fallen one is repaired the same way and works again: crafting,
        /// upgrades, a watchtower's sight.
        /// </summary>
        public int RepairCost(Building b)
        {
            float perHp = Core.Services.TryGet<Session.GameBalance>(out var balance) ? balance.buildingRepairGoldPerHp : 0.5f;
            // The smith's favour (P4-134): −10% a step.
            return b == null ? 0 : Social.ResidentRequestRules.Cut(Mathf.CeilToInt((b.MaxHp - b.Hp) * perHp), Social.ResidentPerks.BuildingRepairCut);
        }

        /// <summary>Why this building cannot be repaired now (shown on the button), or null.</summary>
        public string CannotRepairReason(Building b)
        {
            if (b == null) return "건물이 없습니다";
            if (b.Hp >= b.MaxHp) return "손상이 없습니다";
            float reach = Mathf.Max(b.Footprint.width, b.Footprint.height) * 0.5f + GateService.RepairSafeDistance;
            if (Units.UnitRegistry.FindNearest(b.Center, reach, u => u.Faction == Units.Faction.Monster) != null)
                return "적이 가까이 있어 수리할 수 없습니다";
            if (Core.Services.TryGet<Economy.GuildTreasury>(out var treasury) && !treasury.CanAfford(RepairCost(b)))
                return $"골드가 부족합니다 ({treasury.Gold}/{RepairCost(b)}G)";
            return null;
        }

        public bool TryRepair(Building b)
        {
            if (CannotRepairReason(b) != null) return false;
            int cost = RepairCost(b);
            if (Core.Services.TryGet<Economy.GuildTreasury>(out var treasury) && !treasury.TrySpendGold(cost, Economy.GoldReason.Repair)) return false;
            bool wasDown = b.IsDestroyed;
            b.Repair(b.MaxHp);
            Core.EventBus.Publish(new BuildingRepairedEvent { Building = b, WasDestroyed = wasDown });
            if (Core.Services.TryGet<Session.GameLog>(out var log))
                log.Add($"{b.DisplayName}{Adventurers.MemoryRecorder.Josa(b.DisplayName, "을", "를")} {(wasDown ? "다시 세웠습니다" : "수리했습니다")}(-{cost}G).",
                        Session.LogCategory.Economy);
            return true;
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
