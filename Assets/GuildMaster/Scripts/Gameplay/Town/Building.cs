using GuildMaster.Core;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    public enum BuildingType { TownHall, GuildHall, Blacksmith, Apothecary, TrainingGround, Watchtower }

    /// <summary>Korean names, also needed before a building exists (the build window).</summary>
    public static class BuildingNames
    {
        public static string Of(BuildingType type) => type switch
        {
            BuildingType.TownHall => "마을회관",
            BuildingType.GuildHall => "길드 홀",
            BuildingType.Blacksmith => "대장간",
            BuildingType.Apothecary => "약방",
            BuildingType.TrainingGround => "훈련소",
            BuildingType.Watchtower => "감시탑",
            _ => type.ToString(),
        };
    }

    public struct BuildingDamagedEvent { public Building Building; public float Amount; }
    public struct BuildingDestroyedEvent { public Building Building; }
    public struct BuildingLevelChangedEvent { public Building Building; public int Level; }

    /// <summary>A town structure occupying a rectangular footprint, entered via a door cell.</summary>
    public class Building : MonoBehaviour
    {
        SpriteRenderer _renderer;
        SpriteRenderer _interior;
        Color _tint = Color.white;
        float _alpha = 1f;
        float _checkTimer;
        bool _seeThrough;

        /// <summary>Opacity of the outside while someone is behind it or the cursor is over it.</summary>
        const float SeeThroughAlpha = 0.35f;

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

        public string DisplayName => BuildingNames.Of(Type);

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
            _renderer.sprite = CartoonArt.Building(type, footprint.width, footprint.height);
            _renderer.sortingOrder = CharacterVisual.BaseSortingOrder - Mathf.RoundToInt(footprint.yMin * 8f) + 4;

            // Interior drawn underneath; visible when the outside fades (see Update).
            var interior = CartoonArt.BuildingInterior(type, footprint.width, footprint.height);
            if (interior != null)
            {
                var go = new GameObject("Interior");
                go.transform.SetParent(transform, false);
                _interior = go.AddComponent<SpriteRenderer>();
                _interior.sprite = interior;
                _interior.sortingOrder = World.MapRenderer.DecorOrder + 1;
            }
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
            _tint = Color.Lerp(new Color(0.45f, 0.3f, 0.3f), Color.white, Hp / MaxHp);
            ApplyColor();
            if (IsDestroyed) EventBus.Publish(new BuildingDestroyedEvent { Building = this });
        }

        void ApplyColor()
        {
            if (_renderer) _renderer.color = new Color(_tint.r, _tint.g, _tint.b, _alpha);
        }

        /// <summary>
        /// See-through walls: the outside fades when a unit stands behind/inside the footprint or the
        /// cursor hovers it, revealing the interior sprite underneath.
        /// </summary>
        void Update()
        {
            if (_interior == null) return;
            _checkTimer -= Time.unscaledDeltaTime;
            if (_checkTimer <= 0f)
            {
                _checkTimer = 0.15f;
                _seeThrough = CursorOver() || UnitBehind();
            }
            float target = _seeThrough ? SeeThroughAlpha : 1f;
            if (Mathf.Approximately(_alpha, target)) return;
            _alpha = Mathf.MoveTowards(_alpha, target, Time.unscaledDeltaTime * 4f);
            ApplyColor();
        }

        /// <summary>Cursor over the building sprite.</summary>
        bool Covers(Vector2 p)
        {
            var r = Footprint;
            return p.x >= r.xMin && p.x <= r.xMax && p.y >= r.yMin && p.y <= r.yMax;
        }

        /// <summary>
        /// A unit hidden by the building: inside its outline and above the front edge (units at the
        /// door are drawn in front of it anyway), or just behind the roof so the head is covered.
        /// </summary>
        bool Hides(Vector2 p)
        {
            var r = Footprint;
            return p.x >= r.xMin - 0.2f && p.x <= r.xMax + 0.2f && p.y > r.yMin + 0.1f && p.y <= r.yMax + 0.6f;
        }

        bool CursorOver()
        {
            if (!Services.TryGet<CameraControl.CameraController>(out var cam)) return false;
            if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return false;
            return Covers(cam.MouseWorldPosition());
        }

        bool UnitBehind()
        {
            foreach (var u in Units.UnitRegistry.All)
                if (u != null && u.IsAlive && Hides(u.Position)) return true;
            return false;
        }

        /// <summary>Closest point on the footprint edge (for melee attackers).</summary>
        public Vector2 ClosestPoint(Vector2 from)
        {
            var r = Footprint;
            return new Vector2(Mathf.Clamp(from.x, r.xMin, r.xMax), Mathf.Clamp(from.y, r.yMin, r.yMax));
        }
    }
}
