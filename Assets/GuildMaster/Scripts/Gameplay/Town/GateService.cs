using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>
    /// The town gates (P4-39): puts one <see cref="TownGate"/> on each gap in the wall, keeps the
    /// navigation grid shut to monsters where a gate stands, and repairs broken or battered gates
    /// for gold (user decision 2026-09-21).
    /// </summary>
    public class GateService : MonoBehaviour
    {
        /// <summary>A monster this close to a gate keeps the carpenters away.</summary>
        public const float RepairSafeDistance = 4f;

        readonly List<TownGate> _gates = new();
        WorldService _world;
        GuildTreasury _treasury;

        public IReadOnlyList<TownGate> Gates => _gates;
        public float RepairGoldPerHp { get; private set; }

        public void Init(WorldService world, GuildTreasury treasury, float maxHp, float repairGoldPerHp)
        {
            _world = world;
            _treasury = treasury;
            RepairGoldPerHp = repairGoldPerHp;
            var centre = world.Map.TownCenter;
            foreach (var cell in world.Map.Gates)
            {
                var d = cell - centre;
                var outward = Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? new Vector2Int(d.x >= 0 ? 1 : -1, 0) : new Vector2Int(0, d.y >= 0 ? 1 : -1);
                var go = new GameObject();
                go.transform.SetParent(transform, false);
                var gate = go.AddComponent<TownGate>();
                gate.Init(this, cell, outward, maxHp);
                _gates.Add(gate);
                world.Grid.SetGateClosed(gate.Cells, true);
            }
            UpdateSealed();
        }

        internal void OnGateChanged(TownGate gate)
        {
            _world.Grid.SetGateClosed(gate.Cells, !gate.IsBroken);
            UpdateSealed();
        }

        void UpdateSealed()
        {
            bool sealedTown = _gates.Count > 0;
            foreach (var g in _gates) if (g.IsBroken) sealedTown = false;
            _world.Grid.TownSealed = sealedTown;
        }

        /// <summary>The standing gate closest to a point, or null when all are broken.</summary>
        public TownGate NearestStanding(Vector2 from)
        {
            TownGate best = null;
            float bestD = float.MaxValue;
            foreach (var g in _gates)
            {
                if (g.IsBroken) continue;
                float d = (g.Center - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = g; }
            }
            return best;
        }

        /// <summary>The gate a world point falls on (for clicks), or null.</summary>
        public TownGate GateAt(Vector2 p)
        {
            foreach (var g in _gates) if (g.Contains(p)) return g;
            return null;
        }

        public int RepairCost(TownGate g) => g == null ? 0 : Mathf.CeilToInt((g.MaxHp - g.Hp) * RepairGoldPerHp);

        /// <summary>Why this gate cannot be repaired right now (shown on the button), or null.</summary>
        public string CannotRepairReason(TownGate g)
        {
            if (g == null) return "성문이 없습니다";
            if (g.Hp >= g.MaxHp) return "손상이 없습니다";
            if (UnitRegistry.FindNearest(g.Center, RepairSafeDistance, u => u.Faction == Faction.Monster) != null)
                return "적이 가까이 있어 수리할 수 없습니다";
            if (_treasury != null && !_treasury.CanAfford(RepairCost(g))) return "골드가 부족합니다";
            return null;
        }

        public bool TryRepair(TownGate g)
        {
            if (CannotRepairReason(g) != null) return false;
            int cost = RepairCost(g);
            if (_treasury != null && !_treasury.TrySpendGold(cost)) return false;
            g.RepairFull();
            if (Services.TryGet<Session.GameLog>(out var log))
                log.Add($"{g.DisplayName}을 수리했습니다(-{cost}G).", Session.LogCategory.Economy);
            return true;
        }
    }
}
