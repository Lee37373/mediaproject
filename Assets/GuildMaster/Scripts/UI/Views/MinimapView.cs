using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Ecosystem;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Player;
using GuildMaster.Town;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Top-right minimap (P4-5): one pixel per map cell, darkened by the fog (unexplored = black,
    /// explored but out of sight = dim). Every 0.5 s it redraws the fog and the markers — guild
    /// members, the guild master, monsters the guild sees, known nests, sighted raiders — and the
    /// camera frame. Clicking or dragging on it moves the camera. M toggles it.
    /// Only what the guild knows is shown (information rule: GuildIntel for nests, fog for monsters).
    /// </summary>
    public class MinimapView : MonoBehaviour
    {
        const float Size = 240f;
        const float RefreshInterval = 0.5f;
        const float ExploredDim = 0.5f;

        static readonly Color MemberColor = new(0.3f, 0.95f, 0.35f);
        static readonly Color MasterColor = new(1f, 0.92f, 0.3f);
        static readonly Color MonsterColor = new(0.95f, 0.25f, 0.2f);
        static readonly Color RaiderColor = new(1f, 0.2f, 0.85f);
        static readonly Color NestColor = new(0.85f, 0.45f, 0.15f);
        static readonly Color EmptyNestColor = new(0.55f, 0.5f, 0.45f);
        static readonly Color LairColor = new(0.85f, 0.2f, 0.6f);
        static readonly Color VillageColor = new(0.95f, 0.9f, 0.6f);

        /// <summary>A place's colour on the minimap (P4-105).</summary>
        static Color PlaceColor(World.PlaceKind k) => k switch
        {
            World.PlaceKind.HotSpring => new Color(0.45f, 0.85f, 0.9f),
            World.PlaceKind.Shrine => new Color(1f, 0.9f, 0.5f),
            World.PlaceKind.OreVein => new Color(0.85f, 0.55f, 0.3f),
            World.PlaceKind.HerbPatch => new Color(0.75f, 0.6f, 0.95f),
            World.PlaceKind.OldCamp => new Color(0.75f, 0.65f, 0.5f),
            _ => new Color(0.85f, 0.85f, 0.85f),
        };
        static readonly Color VillageRuinColor = new(0.4f, 0.38f, 0.36f);
        static readonly Color RumourColor = new(0.85f, 0.45f, 1f, 0.35f);
        /// <summary>Last-seen monsters under the fog (P4-23), grey so they read as memory.</summary>
        static readonly Color GhostColor = new(0.62f, 0.66f, 0.74f);
        static readonly Color FrameColor = new(1f, 1f, 1f, 0.85f);

        RectTransform _root, _map, _dots;

        /// <summary>False while hidden with M.</summary>
        public bool IsShown => _root != null && _root.gameObject.activeSelf;
        Texture2D _texture;
        Color32[] _base, _pixels;
        MapData _mapData;
        readonly List<Image> _dotPool = new();
        int _dotsUsed;
        readonly RectTransform[] _frame = new RectTransform[4];
        float _timer;

        public void Build(RectTransform canvas)
        {
            if (!Services.TryGet<WorldService>(out var world)) return;
            _mapData = world.Map;

            var bg = UIBuilder.Panel(canvas, "Minimap", UITheme.PanelBg);
            _root = bg.rectTransform;
            _root.Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-8, -60), new Vector2(Size + 8, Size + 8));

            BuildTexture();
            var raw = new GameObject("Map", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            raw.transform.SetParent(_root, false);
            raw.texture = _texture;
            _map = raw.rectTransform;
            _map.Stretch(4, 4, 4, 4);
            raw.gameObject.AddComponent<MinimapInput>().Owner = this;

            // Markers and the camera frame sit on top and never catch clicks.
            _dots = UIBuilder.Rect(_map, "Dots").Stretch();
            for (int i = 0; i < 4; i++)
            {
                var img = UIBuilder.Panel(_map, "Frame", FrameColor, raycast: false);
                _frame[i] = img.rectTransform;
                _frame[i].anchorMin = _frame[i].anchorMax = Vector2.zero;
                _frame[i].pivot = Vector2.zero;
            }

            // P4-102: the zone the camera looks at, by name once the guild knows it.
            var bar = UIBuilder.Panel(_root, "Zone", UITheme.PanelBg, raycast: false);
            bar.rectTransform.Anchor(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1), new Vector2(0, -2), new Vector2(0, 22));
            _zoneLabel = UIBuilder.Label(bar.transform, "", 13, TextAnchor.MiddleCenter, UITheme.Text);
            _zoneLabel.rectTransform.Stretch(4, 0, 4, 0);
            _zoneLabel.raycastTarget = false;
            Refresh();
        }

        Text _zoneLabel;

        void UpdateZoneLabel()
        {
            if (_zoneLabel == null || !Services.TryGet<CameraController>(out var camCtl) || camCtl.Camera == null) return;
            var c = GridMap.WorldToCell(camCtl.Camera.transform.position);
            var zone = _mapData.ZoneAt(c.x, c.y);
            if (zone == null) { _zoneLabel.text = ""; return; }
            bool known = Services.TryGet<ZoneDiscovery>(out var zones) && zones.IsKnown(zone);
            _zoneLabel.text = known ? $"{zone.Name} · {ZoneDiscovery.Describe(zone)}" : "미지의 땅";
            _zoneLabel.color = known && zone.Kind == ZoneKind.Pocket ? UITheme.Warn : known ? UITheme.Text : UITheme.TextMuted;
        }

        void BuildTexture()
        {
            int w = _mapData.Width, h = _mapData.SurfaceHeight;
            _texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _base = new Color32[w * h];
            _pixels = new Color32[w * h];
            BaseColours(new RectInt(0, 0, w, h));
            EventBus.Subscribe<TownGroundChangedEvent>(OnGroundChanged);
        }

        void BaseColours(RectInt cells)
        {
            int w = _mapData.Width;
            for (int y = Mathf.Max(0, cells.yMin); y < Mathf.Min(_mapData.SurfaceHeight, cells.yMax); y++)
            for (int x = Mathf.Max(0, cells.xMin); x < Mathf.Min(w, cells.xMax); x++)
            {
                int i = y * w + x;
                _base[i] = TileColor(_mapData.Tiles[i], _mapData.Regions[i]);
                // The deep woods darker (P4-103).
                if (_mapData.Deep[i] && _mapData.Tiles[i] == TileType.Forest) _base[i] = Color32.Lerp(_base[i], new Color32(0x10, 0x1C, 0x12, 255), 0.45f);
                if (_mapData.Trees[i]) _base[i] = Color32.Lerp(_base[i], new Color32(0x2F, 0x6E, 0x2A, 255), 0.5f);
            }
        }

        /// <summary>The town walls moved out (P4-86): those cells take their new colours at the next redraw.</summary>
        void OnGroundChanged(TownGroundChangedEvent e)
        {
            if (_mapData == null || _base == null) return;
            BaseColours(e.Area);
            _paintedVersion = null; // the whole map once (rare)
        }

        // Natural ground in the region's colours (P4-55), the rest as before.
        static Color32 TileColor(TileType t, Region region) => t switch
        {
            TileType.Grass => CartoonGroundPainter.PaletteOf(region).Grass,
            TileType.Forest => CartoonGroundPainter.PaletteOf(region).ForestEdge,
            TileType.Sand => CartoonGroundPainter.PaletteOf(region).Sand,
            TileType.Water => CartoonGroundPainter.PaletteOf(region).Water,
            TileType.Mountain => new Color32(0x80, 0x7F, 0x8C, 255),
            TileType.Road => new Color32(0xDE, 0xB4, 0x72, 255),
            TileType.TownFloor => new Color32(0xD6, 0xCE, 0xC0, 255),
            TileType.Wall => new Color32(0x55, 0x5A, 0x64, 255),
            TileType.Gate => new Color32(0xB5, 0x7C, 0x45, 255),
            _ => new Color32(0, 0, 0, 255),
        };

        void Update()
        {
            if (_root == null) return;
            var kb = Keyboard.current;
            if (kb != null && kb.mKey.wasPressedThisFrame && !IsTyping()) _root.gameObject.SetActive(!_root.gameObject.activeSelf);
            if (!_root.gameObject.activeSelf) return;
            UpdateFrame();   // every frame: follows camera pans smoothly
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = RefreshInterval;
            Refresh();
        }

        void Refresh()
        {
            if (_mapData == null) return; // before the world is built, or while a scene unloads
            DrawFog();
            DrawMarkers();
            UpdateFrame();
            UpdateZoneLabel();
        }

        /// <summary>Fog block versions last painted (P4-81: only the blocks the fog redrew are painted again).</summary>
        int[] _paintedVersion;
        FogOfWar _paintedFog;

        void DrawFog()
        {
            Services.TryGet<FogOfWar>(out var fog);
            int w = _mapData.Width, h = _mapData.SurfaceHeight;
            if (fog == null || fog.Width != w || fog.Height != _mapData.Height)
            {
                PaintCells(null, 0, 0, w, h);
            }
            else
            {
                // The whole 512 map took about 10 ms twice a second; the fog knows what changed.
                int bs = FogOfWar.BlockSize, bw = fog.BlocksWide, bh = fog.BlocksHigh;
                if (_paintedFog != fog || _paintedVersion == null || _paintedVersion.Length != bw * bh)
                {
                    _paintedFog = fog;
                    _paintedVersion = new int[bw * bh];
                    for (int b = 0; b < _paintedVersion.Length; b++) _paintedVersion[b] = -1;
                }
                bool changed = false;
                for (int b = 0; b < _paintedVersion.Length; b++)
                {
                    int v = fog.BlockVersion(b);
                    if (v == _paintedVersion[b]) continue;
                    _paintedVersion[b] = v;
                    changed = true;
                    int x0 = b % bw * bs, y0 = b / bw * bs;
                    PaintCells(fog, x0, y0, Mathf.Min(w, x0 + bs), Mathf.Min(h, y0 + bs));
                }
                if (!changed) return;
            }
            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }

        void PaintCells(FogOfWar fog, int x0, int y0, int x1, int y1)
        {
            int w = _mapData.Width;
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int i = y * w + x;
                var c = new Vector2Int(x, y);
                if (fog == null || fog.IsVisible(c)) _pixels[i] = _base[i];
                else if (fog.IsExplored(c))
                {
                    var b = _base[i];
                    _pixels[i] = new Color32((byte)(b.r * ExploredDim), (byte)(b.g * ExploredDim), (byte)(b.b * ExploredDim), 255);
                }
                else _pixels[i] = new Color32(12, 10, 16, 255);
            }
        }

        void DrawMarkers()
        {
            _dotsUsed = 0;
            Services.TryGet<FogOfWar>(out var fog);

            // Nests the guild has found, as last observed (empty ones greyed out).
            if (Services.TryGet<GuildIntel>(out var intel))
                foreach (var n in intel.KnownNests)
                {
                    var info = intel.Get(n);
                    // A region lord's lair (P4-83), once the guild has seen its lord there: bigger, in its own colour.
                    if (intel.KnownLordAt(n) != null) { Dot(n.WorldPosition, LairColor, 11f, square: true); continue; }
                    Dot(n.WorldPosition, info.Observed && info.LastCount == 0 ? EmptyNestColor : NestColor, 7f, square: true);
                }

            // Places worth going to the guild knows of (P4-105), small and faint beside the dens.
            if (Services.TryGet<Places.PlaceService>(out var places))
                foreach (var p in places.Known())
                    Dot(p.WorldPosition, PlaceColor(p.Kind), 6f);

            // Other villages the guild has seen (P4-84), as last seen: ruins greyed out.
            if (Services.TryGet<Villages.VillageService>(out var villages))
                foreach (var v in villages.Known)
                {
                    bool ruined = fog != null && villages.IsVisible(v) ? v.IsDestroyed : v.SeenRuined;
                    Dot(v.WorldPosition, ruined ? VillageRuinColor : VillageColor, 10f, square: true);
                }

            // Sudden incidents (P4-85): the rough circle of every open rumour (its quest's area), faint, under the units.
            if (Services.TryGet<Incidents.IncidentService>(out var incidents))
                foreach (var i in incidents.Active)
                    if (i.Rumoured && i.Quest != null && i.Quest.IsActive)
                        Dot(i.Quest.Point, RumourColor, Mathf.Max(8f, i.Quest.Radius * 2f / _mapData.Width * _map.rect.width));

            // Monsters only where the guild currently sees; raiders drawn larger in their own colour.
            var raiders = Services.TryGet<WaveManager>(out var waves) ? waves.Raiders : null;
            foreach (var u in UnitRegistry.All)
            {
                if (u is not Monster m || !m.IsAlive) continue;
                if (fog != null && !fog.IsVisible(m.Position)) continue;
                bool raider = raiders != null && Contains(raiders, m);
                Dot(m.Position, raider ? RaiderColor : MonsterColor, raider ? 6f : 4f);
            }

            // Where the fog took a monster: a grey dot for the last sighting, not the real position.
            if (Services.TryGet<FogGhostService>(out var ghosts))
                foreach (var g in ghosts.Ghosts)
                    if (g != null && g.IsActive && g.HpRatio >= 0f) Dot(g.Position, GhostColor, 4f);

            if (Services.TryGet<GuildRoster>(out var roster))
                foreach (var a in roster.Members)
                    if (a != null && a.IsAlive) Dot(a.Position, MemberColor, 5f);

            if (Services.TryGet<PlayerAvatar>(out var master) && master.IsAlive) Dot(master.Position, MasterColor, 7f);

            for (int i = _dotsUsed; i < _dotPool.Count; i++) _dotPool[i].gameObject.SetActive(false);
        }

        static bool Contains(IReadOnlyList<Monster> list, Monster m)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == m) return true;
            return false;
        }

        void Dot(Vector2 world, Color color, float size, bool square = false)
        {
            Image img;
            if (_dotsUsed < _dotPool.Count) img = _dotPool[_dotsUsed];
            else
            {
                img = UIBuilder.Panel(_dots, "Dot", color, raycast: false);
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
                img.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                _dotPool.Add(img);
            }
            _dotsUsed++;
            img.gameObject.SetActive(true);
            img.color = color;
            // Rounded sprite for units, plain square for nests.
            img.sprite = square ? null : UIBuilder.Rounded;
            img.type = Image.Type.Simple;
            img.rectTransform.sizeDelta = new Vector2(size, size);
            img.rectTransform.anchoredPosition = WorldToMap(world);
        }

        Vector2 WorldToMap(Vector2 world)
        {
            // Down in a ruin's dungeon (P4-85): shown at its way down (the minimap is the surface).
            if (world.y >= _mapData.SurfaceHeight && _mapData.RuinAt(GridMap.WorldToCell(world)) is { } ruin) world = GridMap.CellToWorld(ruin.Entrance);
            var r = _map.rect;
            return new Vector2(world.x / _mapData.Width * r.width, world.y / _mapData.SurfaceHeight * r.height);
        }

        void UpdateFrame()
        {
            if (!Services.TryGet<CameraController>(out var camCtl) || camCtl.Camera == null) return;
            var cam = camCtl.Camera;
            float hh = cam.orthographicSize, hw = hh * cam.aspect;
            Vector2 c = cam.transform.position;
            var min = WorldToMap(c - new Vector2(hw, hh));
            var max = WorldToMap(c + new Vector2(hw, hh));
            var r = _map.rect;
            min = Vector2.Max(min, Vector2.zero);
            max = Vector2.Min(max, new Vector2(r.width, r.height));
            var size = Vector2.Max(max - min, Vector2.one);
            const float t = 1.5f;
            SetFrame(0, min, new Vector2(size.x, t));                          // bottom
            SetFrame(1, new Vector2(min.x, max.y - t), new Vector2(size.x, t)); // top
            SetFrame(2, min, new Vector2(t, size.y));                          // left
            SetFrame(3, new Vector2(max.x - t, min.y), new Vector2(t, size.y)); // right
        }

        void SetFrame(int i, Vector2 pos, Vector2 size)
        {
            _frame[i].anchoredPosition = pos;
            _frame[i].sizeDelta = size;
        }

        /// <summary>Screen point on the minimap → camera jump (stops following a unit).</summary>
        internal void JumpTo(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_map, e.position, e.pressEventCamera, out var local)) return;
            var r = _map.rect;
            var world = new Vector2((local.x - r.xMin) / r.width * _mapData.Width, (local.y - r.yMin) / r.height * _mapData.SurfaceHeight);
            if (!Services.TryGet<CameraController>(out var cam)) return;
            cam.Follow(null);
            cam.JumpTo(world);
            UpdateFrame();
        }

        static bool IsTyping()
        {
            var es = EventSystem.current;
            return es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.GetComponent<InputField>() != null;
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<TownGroundChangedEvent>(OnGroundChanged);
            if (_texture != null) Destroy(_texture);
        }

        /// <summary>Click / drag on the minimap image.</summary>
        class MinimapInput : MonoBehaviour, IPointerDownHandler, IDragHandler
        {
            public MinimapView Owner;
            public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Owner.JumpTo(e); }
            public void OnDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Owner.JumpTo(e); }
        }
    }
}
