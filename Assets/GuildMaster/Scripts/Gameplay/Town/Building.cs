using GuildMaster.Core;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>Saved by number: new types go at the end. The guild's own facilities (P4-87) follow the watchtower, then the arrow tower (P4-113).</summary>
    public enum BuildingType
    {
        TownHall, GuildHall, Blacksmith, Apothecary, TrainingGround, Watchtower,
        Armory, ArmorWorkshop, DiningHall, PoisonLab, RuneForge, FrostAltar, Dormitory,
        ArrowTower,
    }

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
            BuildingType.Armory => "무기고",
            BuildingType.ArmorWorkshop => "방어구 공방",
            BuildingType.DiningHall => "식당",
            BuildingType.PoisonLab => "독 연구소",
            BuildingType.RuneForge => "마법 각인소",
            BuildingType.FrostAltar => "냉기 제단",
            BuildingType.Dormitory => "대숙소",
            BuildingType.ArrowTower => "화살탑",
            _ => type.ToString(),
        };
    }

    public struct BuildingDamagedEvent { public Building Building; public float Amount; }
    public struct BuildingDestroyedEvent { public Building Building; }
    /// <summary>Repaired for gold (P4-74); <see cref="WasDestroyed"/>: it stands again and works again.</summary>
    public struct BuildingRepairedEvent { public Building Building; public bool WasDestroyed; }
    public struct BuildingLevelChangedEvent { public Building Building; public int Level; }

    /// <summary>A town structure occupying a rectangular footprint, entered via a door cell.</summary>
    public class Building : MonoBehaviour
    {
        SpriteRenderer _renderer;
        SpriteRenderer _interior;
        /// <summary>Pictures standing on their own, sorted by their foot (the training yard's fences and dummies, P4-115).</summary>
        readonly System.Collections.Generic.List<SpriteRenderer> _parts = new();
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
            Draw();

            // Interior drawn underneath; visible when the outside fades (see Update).
            var interior = CartoonArt.BuildingInterior(type, footprint.width, footprint.height);
            if (interior != null)
            {
                var go = new GameObject("Interior");
                go.transform.SetParent(transform, false);
                _interior = go.AddComponent<SpriteRenderer>();
                _interior.sprite = interior;
                _interior.sortingOrder = World.MapRenderer.DecorOrder + 1;
                _interior.enabled = false;   // the outside does not fill its footprint (P4-114): shown only while it fades
            }
        }

        /// <summary>
        /// Puts up the pictures for the building's level (P4-115: they grow richer as it goes up). The training yard is
        /// people's ground: its sand lies under them and what stands in it (the fences, each dummy and target) is sorted
        /// among them by its foot — one picture over the whole yard hid everyone training in it.
        /// </summary>
        void Draw()
        {
            foreach (var p in _parts) if (p != null) Destroy(p.gameObject);
            _parts.Clear();
            var r = Footprint;
            int doorColumn = DoorCell.x - r.xMin;
            if (Type != BuildingType.TrainingGround)
            {
                _renderer.sprite = CartoonArt.Building(Type, r.width, r.height, doorColumn, Level);
                _renderer.sortingOrder = SortAt(r.yMin) + 3;
                ApplyColor();
                return;
            }
            _renderer.sprite = CartoonArt.TrainingYardGround(r.width, r.height, doorColumn, Level);
            _renderer.sortingOrder = MapRenderer.DecorOrder + 1;
            AddPart(CartoonArt.TrainingYardFence(r.width, r.height, doorColumn, front: false), Center, r.yMax - 0.53f);
            AddPart(CartoonArt.TrainingYardFence(r.width, r.height, doorColumn, front: true), Center, r.yMin + 0.12f);
            foreach (var s in TrainingYardLayout.Of(Level))
            {
                var at = TrainingYardLayout.WorldOf(r, s);
                AddPart(CartoonArt.TrainingStation(s.Kind), at, at.y);
            }
            ApplyColor();
        }

        /// <summary>The sorting order of something standing with its foot at <paramref name="footY"/> (as people are sorted).</summary>
        static int SortAt(float footY) => CharacterVisual.BaseSortingOrder - Mathf.RoundToInt(footY * 8f) + 1;

        void AddPart(Sprite sprite, Vector2 at, float footY)
        {
            var go = new GameObject("Part");
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = SortAt(footY);
            _parts.Add(sr);
        }

        public void SetLevel(int level)
        {
            Level = level;
            Draw();
            EventBus.Publish(new BuildingLevelChangedEvent { Building = this, Level = level });
        }

        /// <summary>Puts a saved level and durability back quietly (P5-4: no upgrade or collapse events).</summary>
        public void RestoreState(int level, float maxHp, float hp)
        {
            bool redraw = level != Level;
            Level = level;
            if (redraw) Draw();
            MaxHp = maxHp;
            Hp = Mathf.Clamp(hp, 0f, maxHp);
            _tint = Color.Lerp(new Color(0.45f, 0.3f, 0.3f), Color.white, MaxHp > 0f ? Hp / MaxHp : 1f);
            ApplyColor();
        }

        public void SetMaxHp(float maxHp, bool heal)
        {
            float ratio = MaxHp > 0 ? Hp / MaxHp : 1f;
            MaxHp = maxHp;
            Hp = heal ? maxHp : maxHp * ratio;
        }

        public void Repair(float amount)
        {
            Hp = Mathf.Min(MaxHp, Hp + amount);
            _tint = Color.Lerp(new Color(0.45f, 0.3f, 0.3f), Color.white, Hp / MaxHp);
            ApplyColor();
        }

        /// <summary>Debug (ecosystem runs without a guild): blows land and are heard, but nothing falls.</summary>
        public static bool DebugIndestructible { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetDebug() => DebugIndestructible = false;

        public void Damage(float amount)
        {
            if (IsDestroyed) return;
            if (!DebugIndestructible) Hp = Mathf.Max(0f, Hp - amount);
            EventBus.Publish(new BuildingDamagedEvent { Building = this, Amount = amount });
            _tint = Color.Lerp(new Color(0.45f, 0.3f, 0.3f), Color.white, Hp / MaxHp);
            ApplyColor();
            if (IsDestroyed) EventBus.Publish(new BuildingDestroyedEvent { Building = this });
        }

        void ApplyColor()
        {
            var color = new Color(_tint.r, _tint.g, _tint.b, _alpha);
            if (_renderer) _renderer.color = color;
            foreach (var p in _parts) if (p != null) p.color = color;
            if (_interior) _interior.enabled = _alpha < 0.999f;
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
