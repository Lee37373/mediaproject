using GuildMaster.Core;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    public enum BuildingType { TownHall, GuildHall, Blacksmith, Apothecary, TrainingGround }

    public struct BuildingDamagedEvent { public Building Building; public float Amount; }
    public struct BuildingDestroyedEvent { public Building Building; }
    public struct BuildingLevelChangedEvent { public Building Building; public int Level; }

    /// <summary>A town structure occupying a rectangular footprint, entered via a door cell.</summary>
    public class Building : MonoBehaviour
    {
        SpriteRenderer _renderer;

        public BuildingType Type { get; private set; }
        public RectInt Footprint { get; private set; }
        public Vector2Int DoorCell { get; private set; }
        public bool BlocksMovement { get; private set; }
        public int Level { get; private set; } = 1;
        public float MaxHp { get; private set; }
        public float Hp { get; private set; }
        public bool IsDestroyed => Hp <= 0f;

        public Vector2 DoorPosition => GridMap.CellToWorld(DoorCell);
        public Vector2 Center => Footprint.center;

        public string DisplayName => Type switch
        {
            BuildingType.TownHall => "마을회관",
            BuildingType.GuildHall => "길드 홀",
            BuildingType.Blacksmith => "대장간",
            BuildingType.Apothecary => "약방",
            BuildingType.TrainingGround => "훈련소",
            _ => Type.ToString(),
        };

        public void Init(BuildingType type, RectInt footprint, Vector2Int door, bool blocks, float maxHp)
        {
            Type = type;
            Footprint = footprint;
            DoorCell = door;
            BlocksMovement = blocks;
            MaxHp = Hp = maxHp;
            name = $"Building:{type}";
            transform.position = new Vector3(footprint.center.x, footprint.center.y, 0f);

            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = PixelArt.Building(type, footprint.width, footprint.height);
            _renderer.sortingOrder = CharacterVisual.BaseSortingOrder - Mathf.RoundToInt(footprint.yMin * 8f) + 4;
        }

        public void SetLevel(int level)
        {
            Level = level;
            EventBus.Publish(new BuildingLevelChangedEvent { Building = this, Level = level });
        }

        public void SetMaxHp(float maxHp, bool heal)
        {
            float ratio = MaxHp > 0 ? Hp / MaxHp : 1f;
            MaxHp = maxHp;
            Hp = heal ? maxHp : maxHp * ratio;
        }

        public void Repair(float amount) => Hp = Mathf.Min(MaxHp, Hp + amount);

        public void Damage(float amount)
        {
            if (IsDestroyed) return;
            Hp = Mathf.Max(0f, Hp - amount);
            EventBus.Publish(new BuildingDamagedEvent { Building = this, Amount = amount });
            if (_renderer) _renderer.color = Color.Lerp(new Color(0.45f, 0.3f, 0.3f), Color.white, Hp / MaxHp);
            if (IsDestroyed) EventBus.Publish(new BuildingDestroyedEvent { Building = this });
        }

        /// <summary>Closest point on the footprint edge (for melee attackers).</summary>
        public Vector2 ClosestPoint(Vector2 from)
        {
            var r = Footprint;
            return new Vector2(Mathf.Clamp(from.x, r.xMin, r.xMax), Mathf.Clamp(from.y, r.yMin, r.yMax));
        }
    }
}
