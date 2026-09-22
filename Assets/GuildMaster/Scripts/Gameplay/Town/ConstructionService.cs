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
    /// price back, and monsters can knock it over like any other building.
    /// </summary>
    public class ConstructionService : MonoBehaviour
    {
        /// <summary>Types the player can place (the build window lists these).</summary>
        public static readonly BuildingType[] Placeable = { BuildingType.Watchtower };

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
        }

        void OnDestroy() => EventBus.Unsubscribe<BuildingDestroyedEvent>(OnBuildingDestroyed);

        /// <summary>A toppled tower stops watching; the rubble has to be cleared and rebuilt.</summary>
        void OnBuildingDestroyed(BuildingDestroyedEvent e)
        {
            if (e.Building == null) return;
            var vision = e.Building.GetComponent<VisionSource>();
            if (vision != null) vision.enabled = false;
        }

        // ---------------- Costs ----------------

        public BuildingLevel CostOf(BuildingType type) => _db.Building(type)?.Level(1);

        public int FootprintOf(BuildingType type) => Mathf.Max(1, CostOf(type)?.footprint ?? 2);

        /// <summary>Why the guild cannot afford this building, or null when it can.</summary>
        public string CannotAffordReason(BuildingType type)
        {
            var cost = CostOf(type);
            if (cost == null) return "지을 수 없는 건물입니다.";
            if (_treasury.Gold < cost.goldCost) return $"골드가 부족합니다 ({_treasury.Gold}/{cost.goldCost}G).";
            foreach (var c in cost.itemCosts)
                if (_treasury.GetStock(c.itemId) < c.count)
                    return $"{_db.Item(c.itemId)?.displayName ?? c.itemId}이(가) 부족합니다 ({_treasury.GetStock(c.itemId)}/{c.count}).";
            return null;
        }

        // ---------------- Placing ----------------

        /// <summary>Starts "click where it goes"; a preview follows the cursor until the click.</summary>
        public bool BeginPlacement(BuildingType type, out string error)
        {
            error = CannotAffordReason(type);
            if (error != null) return false;
            if (!Services.TryGet<MapPicker>(out var picker)) { error = "지금은 배치할 수 없습니다."; return false; }
            _placing = type;
            _isPlacing = true;
            ShowPreview(type);
            EventBus.Publish(new PlacementChangedEvent { Active = true, Type = type });
            picker.Begin($"{BuildingNames.Of(type)}을(를) 지을 자리를 클릭하세요 (우클릭·ESC 취소)",
                p => { EndPlacement(); TryBuild(type, p, out _); },
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
            => ConstructionRules.CannotPlace(_world.Grid, _town.Buildings, _world.Map.Gates,
                                             ConstructionRules.FootprintAt(point, FootprintOf(type)));

        public bool TryBuild(BuildingType type, Vector2 point, out string error)
        {
            error = CannotAffordReason(type) ?? CannotPlaceAt(type, point);
            if (error != null) return false;
            var cost = CostOf(type);
            if (!_treasury.TrySpendGold(cost.goldCost)) { error = "골드가 부족합니다."; return false; }
            foreach (var c in cost.itemCosts) _treasury.TryRemoveStock(c.itemId, c.count);

            var foot = ConstructionRules.FootprintAt(point, FootprintOf(type));
            var b = _town.Build(type, foot, cost.maxHp, _mapRenderer);
            if (type == BuildingType.Watchtower) AttachVision(b, cost.sightRadius);
            EventBus.Publish(new BuildingBuiltEvent { Building = b });
            return true;
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

        /// <summary>Removes a player-placed building and returns half of what it cost.</summary>
        public bool TryDemolish(Building b, out string error)
        {
            if (!CanDemolish(b)) { error = "철거할 수 없는 건물입니다."; return false; }
            // Rubble is cleared for nothing; only a standing building is worth half its price.
            var cost = b.IsDestroyed ? null : CostOf(b.Type);
            if (cost != null)
            {
                _treasury.AddGold(ConstructionRules.Refund(cost.goldCost));
                foreach (var c in cost.itemCosts)
                {
                    int back = ConstructionRules.Refund(c.count);
                    if (back > 0) _treasury.AddStock(c.itemId, back);
                }
            }
            EventBus.Publish(new BuildingDemolishedEvent { Building = b });
            _town.Remove(b);
            error = null;
            return true;
        }

        /// <summary>Half of the build cost, as "75G · 돌조각 4" for the demolish button.</summary>
        public string RefundText(BuildingType type)
        {
            var cost = CostOf(type);
            if (cost == null) return "";
            var parts = new List<string> { $"{ConstructionRules.Refund(cost.goldCost)}G" };
            foreach (var c in cost.itemCosts)
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
