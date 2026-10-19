using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    public struct GateDamagedEvent { public TownGate Gate; public float Amount; public Units.Unit Attacker; }
    public struct GateBrokenEvent { public TownGate Gate; }
    public struct GateRepairedEvent { public TownGate Gate; }

    /// <summary>
    /// One of the town's gates (P4-39, user request 2026-09-21): it opens for the guild and its
    /// guests and stays shut for monsters, who have to break it to get in. Walls cannot be broken
    /// (user decision), so the gates are where every attack on the town lands. A broken gate lets
    /// anything through until the player pays for its repair.
    /// </summary>
    public class TownGate : MonoBehaviour
    {
        /// <summary>Guild units this close swing the door open (it is only a picture, they pass anyway).</summary>
        const float OpenDistance = 1.8f;

        readonly List<Vector2Int> _cells = new();
        GateService _service;
        SpriteRenderer _door;
        Color _tint = Color.white;
        float _openAlpha = 1f;
        float _checkTimer;
        bool _open;

        public Vector2Int CenterCell { get; private set; }
        /// <summary>Unit step from the town out through this gate.</summary>
        public Vector2Int Outward { get; private set; }
        public IReadOnlyList<Vector2Int> Cells => _cells;
        public float MaxHp { get; private set; }
        public float Hp { get; private set; }
        public bool IsBroken => Hp <= 0f;

        public Vector2 Center => GridMap.CellToWorld(CenterCell);
        /// <summary>Where an attacker from outside stands (the road leading up to it).</summary>
        public Vector2 OutsidePoint => GridMap.CellToWorld(CenterCell + Outward);
        public Vector2 InsidePoint => GridMap.CellToWorld(CenterCell - Outward);
        bool AlongX => Outward.y != 0;

        public string DisplayName => Outward == Vector2Int.up ? "북문"
            : Outward == Vector2Int.down ? "남문"
            : Outward == Vector2Int.left ? "서문" : "동문";

        public void Init(GateService service, Vector2Int center, Vector2Int outward, float maxHp)
        {
            _service = service;
            Outward = outward;
            MaxHp = Hp = Mathf.Max(1f, maxHp);
            name = $"Gate:{DisplayName}";
            _door = gameObject.AddComponent<SpriteRenderer>();
            Place(center);
            RefreshSprite();
        }

        /// <summary>
        /// The gate now stands in the walls pushed out (P4-86), the same gate (saves, raiders breaking it and the
        /// gate window go by it), as battered as it was. The caller updates the navigation grid.
        /// </summary>
        internal void MoveTo(Vector2Int center) => Place(center);

        void Place(Vector2Int center)
        {
            CenterCell = center;
            var side = new Vector2Int(Outward.y, Outward.x);
            _cells.Clear();
            for (int k = -1; k <= 1; k++) _cells.Add(center + side * k);
            var pos = GridMap.CellToWorld(center);
            // Front view stands on the sill (bottom of the cell); the top-down strip is centred.
            transform.position = AlongX ? new Vector3(pos.x, center.y, 0f) : new Vector3(pos.x, pos.y, 0f);
            _door.sortingOrder = CharacterVisual.BaseSortingOrder - Mathf.RoundToInt(center.y * 8f) + 4;
        }

        public void Damage(float amount, Units.Unit attacker = null)
        {
            if (IsBroken || amount <= 0f) return;
            if (!Building.DebugIndestructible) Hp = Mathf.Max(0f, Hp - amount);
            EventBus.Publish(new GateDamagedEvent { Gate = this, Amount = amount, Attacker = attacker });
            _tint = Color.Lerp(new Color(0.55f, 0.4f, 0.35f), Color.white, Hp / MaxHp);
            if (IsBroken)
            {
                RefreshSprite();
                _service.OnGateChanged(this);
                EventBus.Publish(new GateBrokenEvent { Gate = this });
            }
            ApplyColor();
        }

        /// <summary>Back to full strength (and shut again if it was broken).</summary>
        public void RepairFull()
        {
            bool wasBroken = IsBroken;
            Hp = MaxHp;
            _tint = Color.white;
            if (wasBroken)
            {
                RefreshSprite();
                _service.OnGateChanged(this);
            }
            ApplyColor();
            EventBus.Publish(new GateRepairedEvent { Gate = this });
        }

        /// <summary>Puts saved durability back quietly (P5-4); a broken gate opens the walls as it did.</summary>
        public void RestoreHp(float hp)
        {
            bool wasBroken = IsBroken;
            Hp = Mathf.Clamp(hp, 0f, MaxHp);
            _tint = Color.Lerp(new Color(0.55f, 0.4f, 0.35f), Color.white, Hp / MaxHp);
            if (wasBroken != IsBroken)
            {
                RefreshSprite();
                _service.OnGateChanged(this);
            }
            ApplyColor();
        }

        /// <summary>Closest point of the gate's span, for attackers measuring their reach.</summary>
        public Vector2 ClosestPoint(Vector2 from)
        {
            if (_cells.Count == 0) return transform.position;
            var a = GridMap.CellToWorld(_cells[0]);
            var b = GridMap.CellToWorld(_cells[_cells.Count - 1]);
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(from - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            return a + ab * t;
        }

        /// <summary>A world point on one of the gate's cells.</summary>
        public bool Contains(Vector2 p) => _cells.Contains(GridMap.WorldToCell(p));

        void RefreshSprite()
        {
            if (_door) _door.sprite = CartoonArt.GateDoor(AlongX, _cells.Count, IsBroken);
        }

        void ApplyColor()
        {
            if (_door) _door.color = new Color(_tint.r, _tint.g, _tint.b, _openAlpha);
        }

        /// <summary>The door swings open (fades) for guild units passing through.</summary>
        void Update()
        {
            _checkTimer -= Time.unscaledDeltaTime;
            if (_checkTimer <= 0f)
            {
                _checkTimer = 0.15f;
                _open = !IsBroken && FriendNear();
            }
            float target = _open ? 0.3f : 1f;
            if (Mathf.Approximately(_openAlpha, target)) return;
            _openAlpha = Mathf.MoveTowards(_openAlpha, target, Time.unscaledDeltaTime * 5f);
            ApplyColor();
        }

        bool FriendNear()
        {
            // Update can run before Init has laid the cells (the editor log held thousands of
            // index errors from ClosestPoint on an empty span).
            if (_cells.Count == 0) return false;
            var c = Center;
            foreach (var u in Units.UnitRegistry.All)
                if (u != null && u.IsAlive && u.PassesGates && (u.Position - ClosestPoint(u.Position)).sqrMagnitude < OpenDistance * OpenDistance
                    && (u.Position - c).sqrMagnitude < 9f)
                    return true;
            return false;
        }
    }
}
