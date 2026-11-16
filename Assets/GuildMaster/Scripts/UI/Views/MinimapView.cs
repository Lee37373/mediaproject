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
    /// 2026-11-15: zoom ×1–×4 (wheel over the minimap, or the +/− buttons) — zoomed in it shows the land round
    /// the camera and follows it, holding still while the player drags on it; and hover names — the pointer
    /// over a marker shows who or what it is (up to five near it), over bare known land the zone's name.
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
        static readonly Color ZoomButtonColor = new(0.16f, 0.13f, 0.11f, 0.9f);

        /// <summary>Zoom steps (2026-11-15). ×1 is the whole surface.</summary>
        static readonly float[] Zooms = { 1f, 1.5f, 2f, 3f, 4f };
        /// <summary>Most markers listed in one hover tooltip.</summary>
        const int HoverMax = 5;
        const float HoverRefresh = 0.15f;

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

        // Zoom (2026-11-15).
        int _zoom;
        float Zoom => Zooms[_zoom];
        RawImage _raw;
        /// <summary>The map cells shown, in world units (the whole surface at ×1).</summary>
        Rect _view;
        Vector2 _viewCentre;
        bool _dragging;
        Button _zoomIn, _zoomOut;
        Text _zoomLabel;

        // Hover names (2026-11-15): what each dot stands for, and where it is in the world.
        readonly List<Vector2> _dotWorld = new();
        readonly List<object> _dotWhat = new();
        TooltipTarget _tip;
        bool _hovering;
        Vector2 _hoverScreen;
        string _hoverText;
        float _hoverTimer;

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
            _raw = raw;
            _map = raw.rectTransform;
            _map.Stretch(4, 4, 4, 4);
            raw.gameObject.AddComponent<MinimapInput>().Owner = this;
            raw.gameObject.AddComponent<RectMask2D>(); // zoomed in, markers past the edge are cut off
            _tip = raw.gameObject.AddComponent<TooltipTarget>(); // shown and updated by Hover, not on enter
            _view = new Rect(0, 0, _mapData.Width, _mapData.SurfaceHeight);

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

            BuildZoomButtons();
            Refresh();
        }

        /// <summary>+ / zoom level / − in the minimap's top-left corner (2026-11-15).</summary>
        void BuildZoomButtons()
        {
            const float b = 24f;
            var box = UIBuilder.Rect(_root, "Zoom");
            box.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -8), new Vector2(b, b * 2 + 18));
            _zoomIn = UIBuilder.Button(box, "+", () => SetZoom(_zoom + 1), b, b, ZoomButtonColor, 18);
            ((RectTransform)_zoomIn.transform).Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(b, b));
            _zoomIn.Tooltip(() => "미니맵 확대 (미니맵 위에서 마우스 휠)");
            _zoomOut = UIBuilder.Button(box, "−", () => SetZoom(_zoom - 1), b, b, ZoomButtonColor, 18);
            ((RectTransform)_zoomOut.transform).Anchor(new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(b, b));
            _zoomOut.Tooltip(() => "미니맵 축소");
            foreach (var btn in new[] { _zoomIn, _zoomOut })
            {
                // The button's label is 16 tall; at 18 points the Text would cut the glyph away.
                var glyph = btn.GetComponentInChildren<Text>();
                glyph.rectTransform.Stretch();
                glyph.horizontalOverflow = HorizontalWrapMode.Overflow;
                glyph.verticalOverflow = VerticalWrapMode.Overflow;
                glyph.fontStyle = FontStyle.Bold;
            }
            var tag = UIBuilder.Panel(box, "Level", new Color(0f, 0f, 0f, 0.55f), raycast: false);
            tag.rectTransform.Anchor(new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4, 16));
            _zoomLabel = UIBuilder.Label(tag.transform, "", 11, TextAnchor.MiddleCenter, UITheme.Text);
            _zoomLabel.rectTransform.Stretch();
            _zoomLabel.raycastTarget = false;
            ShowZoom();
        }

        void ShowZoom()
        {
            if (_zoomLabel == null) return;
            _zoomLabel.text = "×" + Zoom.ToString("0.#");
            _zoomIn.interactable = _zoom < Zooms.Length - 1;
            _zoomOut.interactable = _zoom > 0;
        }

        /// <summary>Wheel over the minimap: one zoom step per notch.</summary>
        internal void Scroll(float delta)
        {
            if (Mathf.Abs(delta) < 0.01f) return;
            SetZoom(_zoom + (delta > 0 ? 1 : -1));
        }

        void SetZoom(int index)
        {
            index = Mathf.Clamp(index, 0, Zooms.Length - 1);
            if (index == _zoom) return;
            _zoom = index;
            ShowZoom();
            UpdateView(snap: true);
            Refresh(); // incident circles are drawn to the map's scale
            _hoverTimer = 0f;
        }

        /// <summary>
        /// Which cells the minimap shows: all of them at ×1; zoomed in, a window round the camera that eases
        /// after it — but not while the player is dragging on the minimap, or the land would slide under the cursor.
        /// </summary>
        void UpdateView(bool snap = false)
        {
            float w = _mapData.Width, h = _mapData.SurfaceHeight;
            var old = _view;
            if (_zoom == 0) _view = new Rect(0, 0, w, h);
            else
            {
                if (!_dragging && Services.TryGet<CameraController>(out var camCtl) && camCtl.Camera != null)
                {
                    var target = OnSurface(camCtl.Camera.transform.position);
                    _viewCentre = snap ? target : Vector2.Lerp(_viewCentre, target, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
                }
                float vw = w / Zoom, vh = h / Zoom;
                float cx = Mathf.Clamp(_viewCentre.x, vw * 0.5f, w - vw * 0.5f), cy = Mathf.Clamp(_viewCentre.y, vh * 0.5f, h - vh * 0.5f);
                _view = new Rect(cx - vw * 0.5f, cy - vh * 0.5f, vw, vh);
            }
            if (_view == old && !snap) return;
            _raw.uvRect = new Rect(_view.x / w, _view.y / h, _view.width / w, _view.height / h);
            PlaceDots();
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
            TileType.TownFloor => new Color32(0x86, 0xD4, 0x58, 255),   // the town's kept grass (P4-114)
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
            UpdateView();
            UpdateFrame();   // every frame: follows camera pans smoothly
            UpdateHover();
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
                    if (intel.KnownLordAt(n) != null) { Dot(n.WorldPosition, LairColor, 11f, square: true, what: n); continue; }
                    Dot(n.WorldPosition, info.Observed && info.LastCount == 0 ? EmptyNestColor : NestColor, 7f, square: true, what: n);
                }

            // Places worth going to the guild knows of (P4-105), small and faint beside the dens.
            if (Services.TryGet<Places.PlaceService>(out var places))
                foreach (var p in places.Known())
                    Dot(p.WorldPosition, PlaceColor(p.Kind), 6f, what: p);

            // Other villages the guild has seen (P4-84), as last seen: ruins greyed out.
            if (Services.TryGet<Villages.VillageService>(out var villages))
                foreach (var v in villages.Known)
                {
                    bool ruined = fog != null && villages.IsVisible(v) ? v.IsDestroyed : v.SeenRuined;
                    Dot(v.WorldPosition, ruined ? VillageRuinColor : VillageColor, 10f, square: true, what: v);
                }

            // Sudden incidents (P4-85): the rough circle of every open rumour (its quest's area), faint, under the units.
            if (Services.TryGet<Incidents.IncidentService>(out var incidents))
                foreach (var i in incidents.Active)
                    if (i.Rumoured && i.Quest != null && i.Quest.IsActive)
                        Dot(i.Quest.Point, RumourColor, Mathf.Max(8f, i.Quest.Radius * 2f / _view.width * _map.rect.width), what: i);

            // Monsters only where the guild currently sees; raiders drawn larger in their own colour.
            var raiders = Services.TryGet<WaveManager>(out var waves) ? waves.Raiders : null;
            foreach (var u in UnitRegistry.All)
            {
                if (u is not Monster m || !m.IsAlive) continue;
                if (fog != null && !fog.IsVisible(m.Position)) continue;
                bool raider = raiders != null && Contains(raiders, m);
                Dot(m.Position, raider ? RaiderColor : MonsterColor, raider ? 6f : 4f, what: m);
            }

            // Where the fog took a monster: a grey dot for the last sighting, not the real position.
            if (Services.TryGet<FogGhostService>(out var ghosts))
                foreach (var g in ghosts.Ghosts)
                    if (g != null && g.IsActive && g.HpRatio >= 0f) Dot(g.Position, GhostColor, 4f, what: g);

            if (Services.TryGet<GuildRoster>(out var roster))
                foreach (var a in roster.Members)
                    if (a != null && a.IsAlive) Dot(a.Position, MemberColor, 5f, what: a);

            if (Services.TryGet<PlayerAvatar>(out var master) && master.IsAlive) Dot(master.Position, MasterColor, 7f, what: master);

            for (int i = _dotsUsed; i < _dotPool.Count; i++) { _dotPool[i].gameObject.SetActive(false); _dotWhat[i] = null; }
            PlaceDots();
        }

        static bool Contains(IReadOnlyList<Monster> list, Monster m)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == m) return true;
            return false;
        }

        void Dot(Vector2 world, Color color, float size, bool square = false, object what = null)
        {
            Image img;
            if (_dotsUsed < _dotPool.Count) img = _dotPool[_dotsUsed];
            else
            {
                img = UIBuilder.Panel(_dots, "Dot", color, raycast: false);
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
                img.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                _dotPool.Add(img);
                _dotWorld.Add(default);
                _dotWhat.Add(null);
            }
            _dotWorld[_dotsUsed] = world;
            _dotWhat[_dotsUsed] = what;
            _dotsUsed++;
            img.gameObject.SetActive(true);
            img.color = color;
            // Rounded sprite for units, plain square for nests.
            img.sprite = square ? null : UIBuilder.Rounded;
            img.type = Image.Type.Simple;
            img.rectTransform.sizeDelta = new Vector2(size, size);
        }

        /// <summary>Puts the markers where the current view has them; ones off the zoomed view are hidden.</summary>
        void PlaceDots()
        {
            var r = _map.rect;
            for (int i = 0; i < _dotsUsed; i++)
            {
                var img = _dotPool[i];
                var p = WorldToMap(_dotWorld[i]);
                float half = img.rectTransform.sizeDelta.x * 0.5f;
                img.gameObject.SetActive(p.x >= -half && p.y >= -half && p.x <= r.width + half && p.y <= r.height + half);
                img.rectTransform.anchoredPosition = p;
            }
        }

        /// <summary>Down in a ruin's dungeon (P4-85): shown at its way down (the minimap is the surface).</summary>
        Vector2 OnSurface(Vector2 world) =>
            world.y >= _mapData.SurfaceHeight && _mapData.RuinAt(GridMap.WorldToCell(world)) is { } ruin ? GridMap.CellToWorld(ruin.Entrance) : world;

        Vector2 WorldToMap(Vector2 world)
        {
            world = OnSurface(world);
            var r = _map.rect;
            return new Vector2((world.x - _view.x) / _view.width * r.width, (world.y - _view.y) / _view.height * r.height);
        }

        Vector2 MapToWorld(Vector2 local)
        {
            var r = _map.rect;
            return new Vector2(_view.x + (local.x - r.xMin) / r.width * _view.width, _view.y + (local.y - r.yMin) / r.height * _view.height);
        }

        // ---- Hover names (2026-11-15) ----

        internal void Hover(bool inside, Vector2 screen)
        {
            _hoverScreen = screen;
            if (inside && !_hovering) _hoverTimer = 0f;
            _hovering = inside;
            if (!inside) { _hoverText = null; UITooltip.Hide(_tip); }
        }

        void UpdateHover()
        {
            if (!_hovering) return;
            if (_dragging) { if (_hoverText != null) { _hoverText = null; UITooltip.Hide(_tip); } return; }
            _hoverTimer -= Time.unscaledDeltaTime;
            if (_hoverTimer > 0f) return;
            _hoverTimer = HoverRefresh;
            var text = HoverText(_hoverScreen);
            if (text == _hoverText) return;
            _hoverText = text;
            if (string.IsNullOrEmpty(text)) UITooltip.Hide(_tip);
            else UITooltip.Show(_tip, text);
        }

        readonly List<(float d, int i)> _near = new();

        /// <summary>The markers under the pointer, nearest first (incident circles after the rest); bare known land gives its zone.</summary>
        string HoverText(Vector2 screen)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_map, screen, null, out var local)) return null;
            var r = _map.rect;
            var at = local - r.min;
            _near.Clear();
            for (int i = 0; i < _dotsUsed; i++)
            {
                if (_dotWhat[i] == null || !_dotPool[i].gameObject.activeSelf) continue;
                var rt = _dotPool[i].rectTransform;
                float d = Vector2.Distance(at, rt.anchoredPosition);
                bool circle = _dotWhat[i] is Incidents.Incident;
                float reach = circle ? rt.sizeDelta.x * 0.5f : Mathf.Max(6f, rt.sizeDelta.x * 0.5f + 3f);
                if (d <= reach) _near.Add((circle ? d + 1000f : d, i));
            }
            if (_near.Count == 0) return ZoneText(MapToWorld(local));
            _near.Sort((a, b) => a.d.CompareTo(b.d));
            var sb = new System.Text.StringBuilder();
            int shown = 0;
            foreach (var (_, i) in _near)
            {
                if (shown == HoverMax) break;
                var line = Describe(_dotWhat[i]);
                if (string.IsNullOrEmpty(line)) continue;
                if (shown > 0) sb.Append('\n');
                sb.Append(line);
                shown++;
            }
            if (_near.Count > shown && shown == HoverMax) sb.Append('\n').Append(Muted($"…외 {_near.Count - shown}"));
            return shown == 0 ? null : sb.ToString();
        }

        string ZoneText(Vector2 world)
        {
            var c = GridMap.WorldToCell(world);
            var zone = _mapData.ZoneAt(c.x, c.y);
            if (zone == null || !Services.TryGet<ZoneDiscovery>(out var zones) || !zones.IsKnown(zone)) return null;
            return $"{zone.Name}  {Muted(ZoneDiscovery.Describe(zone))}";
        }

        static string Muted(string s) => UIBuilder.Colored(s, UITheme.TextMuted);
        static string Pct(float ratio) => Mathf.RoundToInt(Mathf.Clamp01(ratio) * 100f) + "%";
        static string Hp(float ratio) => ratio < 0.35f ? UIBuilder.Colored("HP " + Pct(ratio), UITheme.Bad) : Muted("HP " + Pct(ratio));

        /// <summary>One line for a marker — only what the guild knows (the same names as the inspector).</summary>
        string Describe(object what)
        {
            switch (what)
            {
                case Adventurer a when a != null && a.Data != null:
                    return $"{UIBuilder.Colored(a.Data.name, MemberColor)} {Muted($"{AdventurerData.ClassLabel(a.Data.cls)} Lv.{a.Data.level}")} · {a.StateLabel} · {Hp(a.HpRatio)}";
                case PlayerAvatar m when m != null:
                    return $"{UIBuilder.Colored("길드 마스터", MasterColor)} · {Hp(m.HpRatio)}";
                case Monster m when m != null && m.IsAlive:
                {
                    bool raider = Services.TryGet<WaveManager>(out var waves) && waves.Raiders != null && Contains(waves.Raiders, m);
                    string tag = m.IsNemesis ? "[원수] " : m.IsRegionLord ? "[지대 군주] " : m.IsBoss ? "[보스] " : raider ? "[습격] " : "";
                    return $"{UIBuilder.Colored(tag + m.DisplayName, raider ? RaiderColor : MonsterColor)} · {UIBuilder.Colored("위험 " + Quests.Quest.DangerLabel(m.Danger), UITheme.Danger(m.Danger))} · {Hp(m.HpRatio)}";
                }
                case FogGhost g when g != null && g.IsActive:
                {
                    string ago = Services.TryGet<GuildIntel>(out var intel) ? " " + intel.AgoText(g.SeenAtHour) : "";
                    return $"{UIBuilder.Colored(g.Label, GhostColor)} {Muted("· 마지막 목격" + ago)}";
                }
                case Nest n when n != null:
                {
                    if (!Services.TryGet<GuildIntel>(out var intel)) return null;
                    return $"{UIBuilder.Colored(intel.NameOf(n), intel.KnownLordAt(n) != null ? LairColor : NestColor)} {Muted("· " + intel.DescribeNest(n))}";
                }
                case PlaceSite p:
                    return UIBuilder.Colored(p.Name(), PlaceColor(p.Kind)) + " " + Muted("(장소)");
                case Villages.Village v when v != null:
                {
                    bool ruined = Services.TryGet<Villages.VillageService>(out var villages) && villages.IsVisible(v) ? v.IsDestroyed : v.SeenRuined;
                    return ruined ? $"{UIBuilder.Colored(v.DisplayName, VillageRuinColor)} {Muted("(폐허)")}" : UIBuilder.Colored(v.DisplayName, VillageColor);
                }
                case Incidents.Incident i when i.Quest != null:
                    return $"{UIBuilder.Colored("소문", new Color(RumourColor.r, RumourColor.g, RumourColor.b))} · {i.Quest.Title}";
            }
            return null;
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
            var world = MapToWorld(local);
            if (!Services.TryGet<CameraController>(out var cam)) return;
            cam.Follow(null);
            cam.JumpTo(world);
            UpdateFrame();
        }

        static bool IsTyping() => Session.TextFocus.Typing; // P6-2: a field with the caret, not just a selected one

        void OnDestroy()
        {
            EventBus.Unsubscribe<TownGroundChangedEvent>(OnGroundChanged);
            if (_texture != null) Destroy(_texture);
        }

        /// <summary>Click / drag on the minimap image.</summary>
        class MinimapInput : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler,
            IScrollHandler, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
        {
            public MinimapView Owner;
            public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) { Owner._dragging = true; Owner.JumpTo(e); } }
            public void OnDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Owner.JumpTo(e); }
            public void OnPointerUp(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Owner._dragging = false; }
            public void OnScroll(PointerEventData e) => Owner.Scroll(e.scrollDelta.y);
            public void OnPointerEnter(PointerEventData e) => Owner.Hover(true, e.position);
            public void OnPointerMove(PointerEventData e) => Owner.Hover(true, e.position);
            public void OnPointerExit(PointerEventData e) => Owner.Hover(false, e.position);
            void OnDisable() { if (Owner != null) { Owner._dragging = false; Owner.Hover(false, default); } }
        }
    }
}
