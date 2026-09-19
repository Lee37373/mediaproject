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
            Refresh();
        }

        void BuildTexture()
        {
            int w = _mapData.Width, h = _mapData.Height;
            _texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _base = new Color32[w * h];
            _pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                _base[i] = TileColor(_mapData.Tiles[i]);
                if (_mapData.Trees[i]) _base[i] = Color32.Lerp(_base[i], new Color32(0x2F, 0x6E, 0x2A, 255), 0.5f);
            }
        }

        static Color32 TileColor(TileType t) => t switch
        {
            TileType.Grass => new Color32(0x7C, 0xCF, 0x4F, 255),
            TileType.Forest => new Color32(0x4E, 0x9A, 0x35, 255),
            TileType.Sand => new Color32(0xF2, 0xD8, 0x8F, 255),
            TileType.Water => new Color32(0x4B, 0xA9, 0xE9, 255),
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
            DrawFog();
            DrawMarkers();
            UpdateFrame();
        }

        void DrawFog()
        {
            Services.TryGet<FogOfWar>(out var fog);
            int w = _mapData.Width, h = _mapData.Height;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
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
            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
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
                    Dot(n.WorldPosition, info.Observed && info.LastCount == 0 ? EmptyNestColor : NestColor, 7f, square: true);
                }

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
            var r = _map.rect;
            return new Vector2(world.x / _mapData.Width * r.width, world.y / _mapData.Height * r.height);
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
            var world = new Vector2((local.x - r.xMin) / r.width * _mapData.Width, (local.y - r.yMin) / r.height * _mapData.Height);
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
