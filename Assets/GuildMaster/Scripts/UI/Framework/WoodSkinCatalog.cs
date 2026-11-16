using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Points at the wooden buttons of "Fantasy Wooden GUI : Free" (Black Hammer, Unity Asset Store; 2026-11-13, user's
    /// pick for the button UI). The pack itself is not in git (Asset Store EULA, like Layer Lab): each machine imports it,
    /// and the asset store keeps its GUIDs, so these references resolve wherever it is imported and are empty elsewhere —
    /// then the purple skin stays. Lives at Resources/UI/WoodSkin.asset.
    /// </summary>
    [CreateAssetMenu(menuName = "GuildMaster/Wood Skin Catalog")]
    public class WoodSkinCatalog : ScriptableObject
    {
        public Sprite button;
        public Sprite buttonPressed;
        public Sprite close;
        /// <summary>Windows: the dark stone slab ("UI board Large stone"), so the game's light text still reads.</summary>
        public Sprite windowBoard;
        /// <summary>HUD cards: the small stone slab.</summary>
        public Sprite cardBoard;
        /// <summary>The stone plaque a window's title sits on ("IRONY TITLE  empty").</summary>
        public Sprite titlePlate;
    }

    /// <summary>
    /// The wooden button look built from <see cref="WoodSkinCatalog"/>. The pack's sprites have no 9-slice borders (and
    /// its import settings are not in git), so sliced copies are made here at runtime with fixed borders: the rounded
    /// corners and the dark lip under the plank stay whole, the middle stretches.
    /// </summary>
    public static class WoodSkin
    {
        /// <summary>Sprite px kept whole at each edge: left, bottom (the lip), right, top.</summary>
        static readonly Vector4 Border = new(60f, 30f, 60f, 30f);
        /// <summary>The plank is 112 px tall: about a third of that in the UI (a 40 px button keeps a 10 px lip).</summary>
        public const float Ppu = 3f;
        /// <summary>UI px the label sits above the bottom (over the lip), and how far the face drops when pressed.</summary>
        public const float LabelLift = 8f, PressDrop = 6f;

        static readonly Vector4 BoardBorder = new(40f, 40f, 40f, 40f), PlateBorder = new(60f, 20f, 60f, 20f);
        /// <summary>Stone frames about 15-20 px in the UI.</summary>
        public const float BoardPpu = 2.2f, PlatePpu = 2.7f;
        /// <summary>The slab a little darker than drawn, so muted grey text keeps its contrast.</summary>
        public static readonly Color BoardTint = new(0.64f, 0.62f, 0.6f, 1f);
        /// <summary>A window's content kept clear of the stone frame (left, right, top, bottom).</summary>
        public static readonly Vector4 BodyInset = new(22f, 22f, 52f, 20f);

        static bool s_loaded;
        static Sprite s_button, s_pressed, s_close, s_window, s_card, s_plate;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { s_loaded = false; s_button = s_pressed = s_close = s_window = s_card = s_plate = null; }

        static void Load()
        {
            if (s_loaded) return;
            s_loaded = true;
            var cat = Resources.Load<WoodSkinCatalog>("UI/WoodSkin");
            if (cat == null || cat.button == null || cat.buttonPressed == null) return;
            s_button = Sliced(cat.button, Border);
            s_pressed = Sliced(cat.buttonPressed, Border);
            s_close = cat.close;
            if (cat.windowBoard != null) s_window = Sliced(cat.windowBoard, BoardBorder);
            if (cat.cardBoard != null) s_card = Sliced(cat.cardBoard, BoardBorder);
            if (cat.titlePlate != null) s_plate = Sliced(cat.titlePlate, PlateBorder);
        }

        static Sprite Sliced(Sprite src, Vector4 border)
        {
            var s = Sprite.Create(src.texture, src.rect, new Vector2(0.5f, 0.5f), src.pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
            s.name = src.name + " (sliced)";
            return s;
        }

        public static bool Present { get { Load(); return s_button != null; } }
        public static Sprite Button { get { Load(); return s_button; } }
        public static Sprite Pressed { get { Load(); return s_pressed; } }
        public static Sprite Close { get { Load(); return s_close; } }
        public static Sprite WindowBoard { get { Load(); return s_window; } }
        public static Sprite CardBoard { get { Load(); return s_card; } }
        public static Sprite TitlePlate { get { Load(); return s_plate; } }

        /// <summary>
        /// The purple theme panels as warm dark stone (2026-11-13, user: "보라색 UI를 전부 에셋으로"): same lightness, so
        /// every light label keeps its contrast; the hue moves from violet to the pack's stone and wood.
        /// </summary>
        public static Color Panel(Color theme)
        {
            if (theme == UITheme.PanelBg) return new Color(0.17f, 0.15f, 0.13f, theme.a);
            if (theme == UITheme.PanelBgLight) return new Color(0.24f, 0.21f, 0.18f, theme.a);
            if (theme == UITheme.RowBg) return new Color(0.22f, 0.19f, 0.16f, theme.a);
            if (theme == UITheme.RowBgAlt) return new Color(0.18f, 0.16f, 0.14f, theme.a);
            if (theme == UITheme.Header) return new Color(0.34f, 0.27f, 0.19f, theme.a);
            if (theme == UITheme.InputBg) return new Color(0.08f, 0.07f, 0.06f, theme.a);
            return theme;
        }

        /// <summary>Any violet the HUD paints itself (rows, columns) turned to the same stone-brown, lightness kept.</summary>
        public static Color Warm(Color c)
        {
            if (!Present) return c;
            Color.RGBToHSV(c, out _, out float s, out float v);
            var warm = Color.HSVToRGB(28f / 360f, Mathf.Min(0.45f, s * 0.9f), v);
            warm.a = c.a;
            return warm;
        }

        /// <summary>The wood is brown; a view's colour (confirm green, danger red, chosen yellow) tints it a little.</summary>
        public static Color Tint(string style) => style switch
        {
            "green" => new Color(0.74f, 0.96f, 0.62f, 1f),
            "red" => new Color(1f, 0.64f, 0.56f, 1f),
            "yellow" => new Color(1f, 0.9f, 0.5f, 1f),
            _ => Color.white,
        };
    }

    /// <summary>A wooden button: the pressed plank while held, the label dropping with it.</summary>
    public class WoodPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        Image _image;
        Selectable _selectable;
        RectTransform _label;
        Vector2 _labelMin, _labelMax;
        bool _down;

        public void Init(Image image, Text label)
        {
            _image = image;
            _selectable = GetComponent<Selectable>();
            if (label != null)
            {
                _label = label.rectTransform;
                _labelMin = _label.offsetMin;
                _labelMax = _label.offsetMax;
            }
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (_selectable != null && !_selectable.IsInteractable()) return;
            Set(true);
        }

        public void OnPointerUp(PointerEventData e) => Set(false);
        public void OnPointerExit(PointerEventData e) => Set(false);
        void OnDisable() => Set(false);

        void Set(bool down)
        {
            if (_down == down || _image == null) return;
            _down = down;
            _image.sprite = down ? WoodSkin.Pressed : WoodSkin.Button;
            if (_label == null) return;
            var drop = new Vector2(0f, down ? -WoodSkin.PressDrop : 0f);
            _label.offsetMin = _labelMin + drop;
            _label.offsetMax = _labelMax + drop;
        }
    }
}
