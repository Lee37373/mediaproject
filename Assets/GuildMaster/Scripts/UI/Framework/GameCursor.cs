using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Units;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The mouse pointer as a small sword (2026-11-14, user picked "4. 작은 검"): steel normally, gold over a character
    /// or a button that can be pressed. Starts itself after the first scene loads, so no scene or UIRoot change is needed.
    /// Without the textures (or if they are not Read/Write) the system pointer stays.
    /// </summary>
    public class GameCursor : MonoBehaviour
    {
        /// <summary>The sword's tip, in pixels from the image's top-left.</summary>
        static readonly Vector2 Hotspot = new(5f, 5f);

        Texture2D _normal, _hover;
        bool? _showingHover;
        readonly List<RaycastResult> _hits = new();
        PointerEventData _pointer;
        EventSystem _pointerSystem;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<GameCursor>() != null) return;
            var go = new GameObject("GameCursor");
            DontDestroyOnLoad(go);
            go.AddComponent<GameCursor>();
        }

        void Awake()
        {
            _normal = Resources.Load<Texture2D>("UI/Hud/cursor_sword");
            _hover = Resources.Load<Texture2D>("UI/Hud/cursor_sword_hover");
            if (_normal == null || !_normal.isReadable) { enabled = false; return; }
            if (_hover == null || !_hover.isReadable) _hover = _normal;
        }

        void Update()
        {
            bool hover = PointerOnSomething();
            if (_showingHover == hover) return;
            _showingHover = hover;
            Cursor.SetCursor(hover ? _hover : _normal, Hotspot, CursorMode.Auto);
        }

        void OnDisable()
        {
            _showingHover = null;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        /// <summary>Over a pressable button, or (when no window is in the way) over a visible character.</summary>
        bool PointerOnSomething()
        {
            var mouse = Mouse.current;
            if (mouse == null) return false;
            Vector2 screen = mouse.position.ReadValue();

            var es = EventSystem.current;
            if (es != null)
            {
                if (_pointer == null || _pointerSystem != es) { _pointer = new PointerEventData(es); _pointerSystem = es; }
                _pointer.position = screen;
                _hits.Clear();
                es.RaycastAll(_pointer, _hits);
                if (_hits.Count > 0)
                {
                    var sel = _hits[0].gameObject.GetComponentInParent<Selectable>();
                    return sel != null && sel.IsInteractable();
                }
            }

            var cam = Camera.main;
            if (cam == null) return false;
            Vector2 p = cam.ScreenToWorldPoint(screen);
            Services.TryGet<FogOfWar>(out var fog);
            foreach (var u in UnitRegistry.All)
            {
                if (!u.IsAlive) continue;
                float scale = 1f;
                if (u is Monster m)
                {
                    if (fog != null && !fog.IsVisible(m.Position) && !FogOfWar.ShowEverything) continue;
                    scale = m.Scale;
                }
                if (NameTagLayer.Under(p, u.Position, scale)) return true;
            }
            return false;
        }
    }
}
