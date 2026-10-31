using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Disasters;
using GuildMaster.Ecosystem;
using GuildMaster.Economy;
using GuildMaster.Fog;
using GuildMaster.Items;
using GuildMaster.Monsters;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>The player pushed the walls out (P4-86); not raised when a save rebuilds them.</summary>
    public struct TownExpandedEvent { public int Stage; public int Width; }

    /// <summary>The tiles round the town changed (P4-86), also while a save rebuilds the town: pictures of the map redraw this area.</summary>
    public struct TownGroundChangedEvent { public RectInt Area; }

    /// <summary>
    /// Pushing the town walls out (P4-86, user decision 2026-10-19; rules in <see cref="TownExpansionRules"/>, tiles in
    /// <see cref="TownWalls"/>). It is refused while a raid is being fought off and while a den the guild knows of —
    /// or a disaster it has seen — lies on the ground to be taken. Then, at once: the walls and gates move out (whole
    /// gates), the ground round them is cleared, empty den sites there become the town's ground for good, a tower in the
    /// way comes down for all it cost, and whoever stands where a wall now is steps off it (monsters out of town).
    /// A den the guild believed empty that has been taken again unseen leaves for the road rather than being shut in.
    /// </summary>
    public class TownExpansion : MonoBehaviour, ISaveable
    {
        WorldService _world;
        TownService _town;
        GateService _gates;
        MapRenderer _renderer;
        VisionSource _vision;
        GuildTreasury _treasury;
        GameDatabase _db;
        float _baseSight;

        /// <summary>Steps taken (0 = the town as it was made, <see cref="TownExpansionRules.MaxStage"/> at most).</summary>
        public int Stage { get; private set; }
        public bool AtMax => Stage >= TownExpansionRules.MaxStage;
        public int HalfSize => _world.Map.TownHalfSize;
        public int NextHalfSize => TownExpansionRules.HalfSizeAt(_world.Map.BaseTownHalfSize, Stage + 1);
        public TownExpansionRules.Cost? NextCost => TownExpansionRules.CostToGrow(Stage);
        /// <summary>How far the town itself sees now (it grows with the walls).</summary>
        public float TownSight => _vision != null ? _vision.SightRadius : 0f;

        public void Init(WorldService world, TownService town, GateService gates, MapRenderer renderer, VisionSource townVision,
                         GuildTreasury treasury, GameDatabase db)
        {
            _world = world;
            _town = town;
            _gates = gates;
            _renderer = renderer;
            _vision = townVision;
            _treasury = treasury;
            _db = db;
            _baseSight = townVision != null ? townVision.SightRadius : 0f;
        }

        // ---------------- Save ----------------

        [System.Serializable]
        class SaveData { public int stage; }

        public string SaveKey => "expansion";

        public string Capture() => JsonUtility.ToJson(new SaveData { stage = Stage });

        /// <summary>The fresh map is grown again step by step, which lays the same tiles; the rest comes back from the save.</summary>
        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            while (Stage < Mathf.Min(s.stage, TownExpansionRules.MaxStage)) Grow(quiet: true);
        }

        // ---------------- Asking ----------------

        /// <summary>Why the walls cannot move out now (shown on the button), or null.</summary>
        public string CannotExpandReason()
        {
            if (AtMax) return "마을을 더 넓힐 수 없습니다.";
            if (Services.TryGet<WaveManager>(out var waves) && waves.RaidSighted) return "침공을 막는 동안에는 넓힐 수 없습니다.";
            int half = NextHalfSize;
            var den = KnownDenIn(half);
            if (den != null)
                return $"넓힐 자리에 {IntelName(den)}이(가) 있습니다. 먼저 토벌해야 합니다({Quests.QuestBoard.DescribeArea(den.WorldPosition)}).";
            if (Services.TryGet<DisasterService>(out var disasters))
            {
                var c = _world.Map.TownCenter;
                var d = disasters.KnownOn(cell => TownWalls.InClearing(cell.x, cell.y, c, half) || TownExpansionRules.Crowds(cell, c, half));
                if (d != null) return $"넓힐 자리에 {d.Name}이(가) 번져 있습니다. 재해가 끝나야 넓힐 수 있습니다.";
            }
            var cost = NextCost.Value;
            if (_treasury.Gold < cost.Gold) return $"골드가 부족합니다 ({_treasury.Gold}/{cost.Gold}G).";
            foreach (var item in cost.Items)
                if (_treasury.GetStock(item.itemId) < item.count)
                    return $"{ItemName(item.itemId)}이(가) 부족합니다 ({_treasury.GetStock(item.itemId)}/{item.count}).";
            return null;
        }

        /// <summary>
        /// A den on the ground walls of this size would take that the guild knows of and has not seen empty (one it has
        /// only found counts: it may be lived in). What the guild has not found does not stop it (information rule).
        /// </summary>
        Nest KnownDenIn(int half)
        {
            if (!Services.TryGet<GuildIntel>(out var intel)) return null;
            var c = _world.Map.TownCenter;
            foreach (var n in intel.KnownNests)
            {
                if (!TownExpansionRules.Crowds(n.Cell, c, half)) continue;
                var ni = intel.Get(n);
                if (!ni.Observed || ni.LastCount > 0) return n;
            }
            return null;
        }

        static string IntelName(Nest n) => Services.TryGet<GuildIntel>(out var intel) ? intel.NameOf(n) : n.DisplayName;

        string ItemName(string id) => _db.Item(id)?.displayName ?? id;

        // ---------------- Growing ----------------

        public bool TryExpand(out string error)
        {
            error = CannotExpandReason();
            if (error != null) return false;
            var cost = NextCost.Value;
            if (!_treasury.TrySpendGold(cost.Gold, GoldReason.Build)) { error = "골드가 부족합니다."; return false; }
            foreach (var item in cost.Items) _treasury.TryRemoveStock(item.itemId, item.count);

            var refunds = Grow(quiet: false);
            int width = TownExpansionRules.Width(HalfSize);
            if (Services.TryGet<Session.GameLog>(out var log))
            {
                var paid = new StringBuilder($"-{cost.Gold}G");
                foreach (var item in cost.Items) paid.Append($", {ItemName(item.itemId)} {item.count}");
                log.Add($"마을을 넓혔습니다: 성벽 안이 {width}×{width}칸이 되었습니다({paid}).", Session.LogCategory.Economy);
                foreach (var r in refunds)
                    log.Add($"새 성벽 자리의 감시탑을 헐었습니다{(r.Length > 0 ? $"(비용을 모두 돌려받음: {r})" : "")}.", Session.LogCategory.Economy);
            }
            EventBus.Publish(new TownExpandedEvent { Stage = Stage, Width = width });
            return true;
        }

        /// <summary>
        /// One step out. Quiet = rebuilding a saved town on the freshly made map: only the ground, the gates, the dens'
        /// sites and the sight change (everything else comes back from the save). Returns what towers in the way gave back.
        /// </summary>
        List<string> Grow(bool quiet)
        {
            var map = _world.Map;
            var c = map.TownCenter;
            var oldTown = map.TownRect;
            int half = TownExpansionRules.HalfSizeAt(map.BaseTownHalfSize, Stage + 1);
            var refunds = new List<string>();

            if (Services.TryGet<EcosystemManager>(out var eco))
            {
                Services.TryGet<RoamingPackService>(out var roaming);
                foreach (var n in eco.Nests)
                {
                    if (n.Retired || !TownExpansionRules.Crowds(n.Cell, c, half)) continue;
                    // Believed empty but taken again out of sight: they leave rather than being walled in.
                    if (!quiet && !n.IsEmpty && roaming != null) roaming.SendOnTheRoad(n, _world.TownCenterWorld);
                    n.Retire();
                }
            }
            if (!quiet && Services.TryGet<DisasterService>(out var disasters))
                disasters.ClearGround(cell => TownWalls.InClearing(cell.x, cell.y, c, half));

            var area = TownWalls.Grow(map, half);
            _world.Grid.TerrainChanged();
            _world.Risk.Refresh(area);
            _gates.FollowWalls(mend: !quiet);
            Stage++;

            if (!quiet)
            {
                if (Services.TryGet<ConstructionService>(out var construction))
                    foreach (var b in _town.Buildings.ToList())
                        // The outer defences (P4-113) belong beyond the walls: one now inside them comes down too.
                        if (construction.CanDemolish(b) && (ConstructionRules.InTheWayOfWalls(map, map.Gates, b.Footprint)
                                                            || DefenceRules.IsDefence(b.Type) && DefenceRules.OutOfReach(b.Footprint, map.TownRect) != null))
                            refunds.Add(construction.TakeDownForWalls(b));
                StepOffTheWalls(oldTown);
            }
            if (_renderer != null) _renderer.Repaint(area);
            if (_vision != null) _vision.SightRadius = TownExpansionRules.SightRadius(_baseSight, _world.TownGrowth);
            EventBus.Publish(new TownGroundChangedEvent { Area = area });
            Debug.Log($"[Town] walls out to half size {half} (stage {Stage}){(quiet ? " (restored)" : "")}");
            return refunds;
        }

        static readonly List<Unit> s_units = new();

        /// <summary>
        /// Whoever stands where a wall now is steps to the nearest open ground, and monsters the walls closed round
        /// step out of town. Things lying on a wall are moved off it too.
        /// </summary>
        void StepOffTheWalls(RectInt oldTown)
        {
            var grid = _world.Grid;
            var town = _world.Map.TownRect;
            s_units.Clear();
            s_units.AddRange(UnitRegistry.All);
            foreach (var u in s_units)
            {
                if (u == null || !u.IsAlive) continue;
                var cell = GridMap.WorldToCell(u.Position);
                if (_world.Map.IsUnderground(cell)) continue;
                bool shutIn = u is Monster && town.Contains(cell) && !oldTown.Contains(cell);
                if (grid.IsWalkable(cell) && !shutIn) continue;
                if (!FindOpenGround(cell, shutIn || u is Monster, out var to)) continue;
                var at = GridMap.CellToWorld(to);
                if (u.Mover != null) u.Mover.Warp(at);
                else u.transform.position = new Vector3(at.x, at.y, u.transform.position.z);
            }
            s_units.Clear();
            if (Services.TryGet<GroundItemService>(out var ground))
                foreach (var item in ground.Items)
                {
                    var cell = GridMap.WorldToCell(item.Position);
                    if (grid.IsWalkable(cell) || !FindOpenGround(cell, false, out var to)) continue;
                    var at = GridMap.CellToWorld(to);
                    item.transform.position = new Vector3(at.x, at.y, item.transform.position.z);
                }
        }

        /// <summary>The nearest walkable cell (outside the walls when <paramref name="outside"/>), searched ring by ring.</summary>
        bool FindOpenGround(Vector2Int from, bool outside, out Vector2Int found)
        {
            var grid = _world.Grid;
            var town = _world.Map.TownRect;
            int reach = HalfSize * 2 + 4;
            for (int r = 0; r <= reach; r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                var c = new Vector2Int(from.x + dx, from.y + dy);
                if (!grid.IsWalkable(c) || outside && town.Contains(c)) continue;
                found = c;
                return true;
            }
            found = from;
            return false;
        }
    }
}
