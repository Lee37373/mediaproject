using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using GuildMaster.Fog;
using GuildMaster.Player;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    public struct BuildingBuiltEvent { public Building Building; }
    public struct BuildingDemolishedEvent { public Building Building; }
    /// <summary>The player is choosing where to put something (UI shows a hint while true).</summary>
    public struct PlacementChangedEvent { public bool Active; public BuildingType Type; }

    /// <summary>
    /// Player-placed buildings (P4-13). The first one is the watchtower: it costs gold and
    /// materials, can stand anywhere walkable, sees over walls, can be torn down for half the
    /// price back, and monsters can knock it over like any other building. The guild's facilities
    /// (P4-87) go inside the walls only, rise in level like the town's buildings, and each further
    /// one of a kind costs more (<see cref="FacilityRules"/>).
    /// </summary>
    public class ConstructionService : MonoBehaviour
    {
        /// <summary>Types the player can place (the build window lists these).</summary>
        public static readonly BuildingType[] Placeable =
        {
            BuildingType.Watchtower, BuildingType.ArrowTower,
            BuildingType.Armory, BuildingType.ArmorWorkshop, BuildingType.DiningHall,
            BuildingType.PoisonLab, BuildingType.RuneForge, BuildingType.FrostAltar, BuildingType.Dormitory,
        };

        TownService _town;
        WorldService _world;
        MapRenderer _mapRenderer;
        GuildTreasury _treasury;
        GameDatabase _db;

        SpriteRenderer _preview;
        BuildingType _placing;
        bool _isPlacing;

        public bool IsPlacing => _isPlacing;
        public BuildingType PlacingType => _placing;

        public void Init(TownService town, WorldService world, MapRenderer mapRenderer, GuildTreasury treasury, GameDatabase db)
        {
            _town = town;
            _world = world;
            _mapRenderer = mapRenderer;
            _treasury = treasury;
            _db = db;
            EventBus.Subscribe<BuildingDestroyedEvent>(OnBuildingDestroyed);
            EventBus.Subscribe<BuildingRepairedEvent>(OnBuildingRepaired);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<BuildingDestroyedEvent>(OnBuildingDestroyed);
            EventBus.Unsubscribe<BuildingRepairedEvent>(OnBuildingRepaired);
        }

        /// <summary>A tower stood up again (P4-74) watches again.</summary>
        void OnBuildingRepaired(BuildingRepairedEvent e)
        {
            if (e.Building == null || !e.WasDestroyed) return;
            var vision = e.Building.GetComponent<VisionSource>();
            if (vision != null) vision.enabled = true;
        }

        /// <summary>A toppled tower stops watching until it is repaired (P4-74) or its rubble cleared.</summary>
        void OnBuildingDestroyed(BuildingDestroyedEvent e)
        {
            if (e.Building == null) return;
            var vision = e.Building.GetComponent<VisionSource>();
            if (vision != null) vision.enabled = false;
        }

        // ---------------- Costs ----------------

        /// <summary>Level 1 as listed (what the first one of a kind costs).</summary>
        public BuildingLevel CostOf(BuildingType type) => _db.Building(type)?.Level(1);

        public int FootprintOf(BuildingType type) => Mathf.Max(1, CostOf(type)?.footprint ?? 2);

        /// <summary>What the next one of this kind costs: facilities cost more for each one already there (P4-87), towers do not.</summary>
        public BuildingLevel PriceToBuild(BuildingType type)
            => Priced(CostOf(type), FacilityRules.IsFacility(type) ? _town.CountOf(type) : 0);

        /// <summary>A level's cost for the n-th of a kind (<paramref name="earlier"/> = how many came before it).</summary>
        public static BuildingLevel Priced(BuildingLevel lv, int earlier)
        {
            if (lv == null || earlier <= 0) return lv;
            var p = new BuildingLevel
            {
                goldCost = FacilityRules.ScaledGold(lv.goldCost, earlier), maxHp = lv.maxHp, footprint = lv.footprint,
                sightRadius = lv.sightRadius, effectText = lv.effectText,
            };
            foreach (var c in lv.itemCosts) p.itemCosts.Add(new ItemCost { itemId = c.itemId, count = FacilityRules.ScaledCount(c.count, earlier) });
            return p;
        }

        /// <summary>Why the guild cannot put up another of these now (too many of the kind, or short of the price), or null.</summary>
        public string CannotBuildReason(BuildingType type)
        {
            if (FacilityRules.IsFacility(type) && _town.CountOf(type) >= FacilityRules.MaxCopies(type))
                return $"{BuildingNames.Of(type)}{Adventurers.MemoryRecorder.Josa(BuildingNames.Of(type), "은", "는")} {FacilityRules.MaxCopies(type)}채까지 지을 수 있습니다.";
            return CannotAffordReason(type);
        }

        /// <summary>Why the guild cannot afford this building, or null when it can.</summary>
        public string CannotAffordReason(BuildingType type)
        {
            var cost = PriceToBuild(type);
            if (cost == null) return "지을 수 없는 건물입니다.";
            return ShortOf(cost);
        }

        /// <summary>What the treasury lacks for this price, or null.</summary>
        public string ShortOf(BuildingLevel cost)
        {
            if (_treasury.Gold < cost.goldCost) return $"골드가 부족합니다 ({_treasury.Gold}/{cost.goldCost}G).";
            foreach (var c in cost.itemCosts)
                if (_treasury.GetStock(c.itemId) < c.count)
                    return $"{_db.Item(c.itemId)?.displayName ?? c.itemId}이(가) 부족합니다 ({_treasury.GetStock(c.itemId)}/{c.count}).";
            return null;
        }

        // ---------------- Placing ----------------

        /// <summary>
        /// Starts "click where it goes"; a preview follows the cursor until the click. A click where it cannot go says why
        /// and asks again (<paramref name="note"/>): it used to end the placing without a word, as if the click were lost.
        /// </summary>
        public bool BeginPlacement(BuildingType type, out string error, string note = null)
        {
            error = CannotBuildReason(type);
            if (error != null) return false;
            if (!Services.TryGet<MapPicker>(out var picker)) { error = "지금은 배치할 수 없습니다."; return false; }
            _placing = type;
            _isPlacing = true;
            ShowPreview(type);
            EventBus.Publish(new PlacementChangedEvent { Active = true, Type = type });
            string name = BuildingNames.Of(type);
            picker.Begin($"{(note != null ? note + " — " : "")}{name}{Adventurers.MemoryRecorder.Josa(name, "을", "를")} 지을 자리를 클릭하세요 (우클릭·ESC 취소)",
                p =>
                {
                    if (TryBuild(type, p, out var why) || !BeginPlacement(type, out _, why)) EndPlacement();
                },
                EndPlacement);
            return true;
        }

        void EndPlacement()
        {
            if (!_isPlacing) return;
            _isPlacing = false;
            if (_preview != null) _preview.gameObject.SetActive(false);
            EventBus.Publish(new PlacementChangedEvent { Active = false, Type = _placing });
        }

        /// <summary>Why the building cannot go here, or null. Used by the preview and by TryBuild.</summary>
        public string CannotPlaceAt(BuildingType type, Vector2 point)
        {
            var foot = ConstructionRules.FootprintAt(point, FootprintOf(type));
            // The outer defences (P4-113): beyond the walls, not too far out.
            if (DefenceRules.IsDefence(type) && DefenceRules.OutOfReach(foot, _world.Map.TownRect) is { } far) return far;
            return ConstructionRules.CannotPlace(_world.Grid, _town.Buildings, _world.Map.Gates, foot, insideWalls: FacilityRules.IsFacility(type));
        }

        public bool TryBuild(BuildingType type, Vector2 point, out string error)
        {
            error = CannotBuildReason(type) ?? CannotPlaceAt(type, point);
            if (error != null) return false;
            var cost = PriceToBuild(type);
            if (!_treasury.TrySpendGold(cost.goldCost, GoldReason.Build)) { error = "골드가 부족합니다."; return false; }
            foreach (var c in cost.itemCosts) _treasury.TryRemoveStock(c.itemId, c.count);

            var foot = ConstructionRules.FootprintAt(point, FootprintOf(type));
            var b = _town.Build(type, foot, cost.maxHp, _mapRenderer);
            if (type == BuildingType.Watchtower) AttachVision(b, cost.sightRadius);
            if (type == BuildingType.ArrowTower) b.gameObject.AddComponent<ArrowTower>().Init(b);
            EventBus.Publish(new BuildingBuiltEvent { Building = b });
            return true;
        }

        /// <summary>
        /// Puts a saved player-built building back (P5-4): no cost, no event. A fallen tower keeps
        /// its eye shut, as it would have after collapsing.
        /// </summary>
        public Building RestorePlaced(BuildingType type, RectInt footprint, int level, float maxHp, float hp)
        {
            var b = _town.Build(type, footprint, maxHp, _mapRenderer);
            b.RestoreState(level, maxHp, hp);
            if (type == BuildingType.Watchtower)
            {
                AttachVision(b, CostOf(type)?.sightRadius ?? 0f);
                if (b.IsDestroyed) b.GetComponent<VisionSource>().enabled = false;
            }
            if (type == BuildingType.ArrowTower)
            {
                b.gameObject.AddComponent<ArrowTower>().Init(b);
                if (b.IsDestroyed) b.GetComponent<VisionSource>().enabled = false;
            }
            return b;
        }

        /// <summary>A watchtower is an elevated eye: it sees over walls and buildings.</summary>
        static void AttachVision(Building b, float radius)
        {
            var vision = b.gameObject.AddComponent<VisionSource>();
            vision.SightRadius = radius;
            vision.SeesOverWalls = true;
        }

        // ---------------- Taking it down ----------------

        public bool CanDemolish(Building b) => b != null && System.Array.IndexOf(Placeable, b.Type) >= 0;

        /// <summary>
        /// All a standing building cost to get where it is: its level 1 and every upgrade, at the price of its place among
        /// its kind (P4-87). Rubble is worth nothing.
        /// </summary>
        public (int gold, List<ItemCost> items) PaidFor(Building b)
        {
            var items = new List<ItemCost>();
            var def = b != null && !b.IsDestroyed ? _db.Building(b.Type) : null;
            if (def == null) return (0, items);
            int earlier = FacilityRules.IsFacility(b.Type) ? _town.EarlierOfKind(b) : 0;
            int gold = 0;
            for (int level = 1; level <= Mathf.Min(b.Level, def.MaxLevel); level++)
            {
                var lv = Priced(def.Level(level), earlier);
                gold += lv.goldCost;
                foreach (var c in lv.itemCosts)
                {
                    int i = items.FindIndex(x => x.itemId == c.itemId);
                    if (i < 0) items.Add(c);
                    else items[i] = new ItemCost { itemId = c.itemId, count = items[i].count + c.count };
                }
            }
            return (gold, items);
        }

        /// <summary>Removes a player-placed building and returns half of what it cost.</summary>
        public bool TryDemolish(Building b, out string error)
        {
            if (!CanDemolish(b)) { error = "철거할 수 없는 건물입니다."; return false; }
            // Rubble is cleared for nothing; only a standing building is worth half its price.
            var (gold, items) = PaidFor(b);
            if (gold > 0) _treasury.AddGold(ConstructionRules.Refund(gold), GoldReason.Build);
            foreach (var c in items)
            {
                int back = ConstructionRules.Refund(c.count);
                if (back > 0) _treasury.AddStock(c.itemId, back);
            }
            EventBus.Publish(new BuildingDemolishedEvent { Building = b });
            _town.Remove(b);
            error = null;
            return true;
        }

        /// <summary>
        /// The walls moved out over a placed building (P4-86, user decision 2026-10-19): it comes down and, if it was
        /// still standing, all it cost comes back. Returns what came back as "150G · 돌 슬라임 조각 8" ("" for rubble).
        /// </summary>
        public string TakeDownForWalls(Building b)
        {
            if (!CanDemolish(b)) return "";
            var (gold, items) = PaidFor(b);
            var parts = new List<string>();
            if (gold > 0)
            {
                _treasury.AddGold(gold, GoldReason.Build);
                parts.Add($"{gold}G");
            }
            foreach (var c in items)
            {
                _treasury.AddStock(c.itemId, c.count);
                parts.Add($"{_db.Item(c.itemId)?.displayName ?? c.itemId} {c.count}");
            }
            EventBus.Publish(new BuildingDemolishedEvent { Building = b });
            _town.Remove(b);
            return string.Join(" · ", parts);
        }

        /// <summary>Half of what this building cost, as "75G · 돌조각 4" for the demolish button.</summary>
        public string RefundText(Building b)
        {
            var (gold, items) = PaidFor(b);
            return RefundText(gold, items);
        }

        /// <summary>Half of what the next one of this kind costs (the build window).</summary>
        public string RefundText(BuildingType type)
        {
            var cost = PriceToBuild(type);
            return cost == null ? "" : RefundText(cost.goldCost, cost.itemCosts);
        }

        string RefundText(int gold, List<ItemCost> items)
        {
            var parts = new List<string> { $"{ConstructionRules.Refund(gold)}G" };
            foreach (var c in items)
            {
                int back = ConstructionRules.Refund(c.count);
                if (back > 0) parts.Add($"{_db.Item(c.itemId)?.displayName ?? c.itemId} {back}");
            }
            return string.Join(" · ", parts);
        }

        // ---------------- Preview ----------------

        void ShowPreview(BuildingType type)
        {
            if (_preview == null)
            {
                var go = new GameObject("BuildPreview");
                go.transform.SetParent(transform, false);
                _preview = go.AddComponent<SpriteRenderer>();
                _preview.sortingOrder = CharacterVisual.BaseSortingOrder + 500;
            }
            int size = FootprintOf(type);
            _preview.sprite = CartoonArt.Building(type, size, size);
            _preview.gameObject.SetActive(true);
        }

        void Update()
        {
            if (!_isPlacing || _preview == null) return;
            if (!Services.TryGet<CameraControl.CameraController>(out var cam)) return;
            var point = cam.MouseWorldPosition();
            var foot = ConstructionRules.FootprintAt(point, FootprintOf(_placing));
            _preview.transform.position = new Vector3(foot.center.x, foot.center.y, 0f);
            bool ok = CannotPlaceAt(_placing, point) == null;
            _preview.color = ok ? new Color(0.6f, 1f, 0.6f, 0.65f) : new Color(1f, 0.5f, 0.5f, 0.65f);
        }
    }
}
