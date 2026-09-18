using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Composition root for the UI layer. Runs after GameBootstrap (which registers all
    /// gameplay services) and builds canvases, HUD, windows and world overlays.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class UIRoot : MonoBehaviour
    {
        readonly List<(Key key, UIPanel panel)> _hotkeys = new();
        readonly List<UIPanel> _panels = new();
        RectTransform _banner;
        Text _bannerText;

        public RectTransform WindowLayer { get; private set; }
        public RectTransform WorldLayer { get; private set; }
        public RectTransform HudLayer { get; private set; }

        void Awake()
        {
            if (!Services.TryGet<GameClock>(out _))
            {
                Debug.LogError("[UIRoot] GameBootstrap must run before UIRoot.");
                enabled = false;
                return;
            }
            Services.Register(this);

            // Map picker lives with gameplay input but is created here so UI owns its lifetime.
            var picker = new GameObject("MapPicker").AddComponent<MapPicker>();
            picker.transform.SetParent(transform, false);
            Services.Register(picker);

            // World overlay (bubbles, floating text) below windows.
            var worldCanvas = UIBuilder.CreateCanvas("WorldOverlayCanvas", 0);
            worldCanvas.transform.SetParent(transform, false);
            worldCanvas.GetComponent<GraphicRaycaster>().enabled = false;
            WorldLayer = (RectTransform)worldCanvas.transform;
            var floating = WorldLayer.gameObject.AddComponent<FloatingTextLayer>();
            floating.Build(UIBuilder.Rect(WorldLayer, "Floating").Stretch());
            var bubbles = WorldLayer.gameObject.AddComponent<SpeechBubbleLayer>();
            bubbles.Build(UIBuilder.Rect(WorldLayer, "Bubbles").Stretch());
            Services.Register(floating);
            Services.Register(bubbles);

            // HUD + windows.
            var canvas = UIBuilder.CreateCanvas("UICanvas", 10);
            canvas.transform.SetParent(transform, false);
            HudLayer = (RectTransform)canvas.transform;

            if (Services.TryGet<LLM.LlmGateway>(out var llm)) HudView.ExtraStatus = () => llm ? llm.StatusText : "";
            var hud = HudLayer.gameObject.AddComponent<HudView>();
            hud.Build(HudLayer);
            var log = HudLayer.gameObject.AddComponent<LogView>();
            log.Build(HudLayer);
            var inspector = HudLayer.gameObject.AddComponent<InspectorView>();
            inspector.Build(HudLayer);
            Services.Register(inspector);

            WindowLayer = UIBuilder.Rect(HudLayer, "Windows").Stretch();

            var menu = UIBuilder.Rect(HudLayer, "SideMenu");
            menu.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -60), new Vector2(150, 300));
            UIBuilder.VLayout(menu, 6, 0);

            AddWindow<QuestBoardView>(menu, "의뢰 게시판 (Q)", Key.Q, new Vector2(1180, 680));
            AddWindow<RosterView>(menu, "길드원 (R)", Key.R, new Vector2(1000, 560));
            AddWindow<StockView>(menu, "창고 (I)", Key.I, new Vector2(620, 560));
            AddWindow<EcologyView>(menu, "생태계 (E)", Key.E, new Vector2(900, 560));

            BuildBanner();
            EventBus.Subscribe<MapPickingChangedEvent>(OnPicking);
        }

        void OnDestroy() => EventBus.Unsubscribe<MapPickingChangedEvent>(OnPicking);

        /// <summary>Adds a window with a side-menu button and hotkey. Later phases call this to add their panels.</summary>
        public T AddWindow<T>(RectTransform menu, string label, Key hotkey, Vector2 size) where T : UIPanel
        {
            var go = new GameObject(typeof(T).Name);
            var panel = go.AddComponent<T>();
            panel.Create(WindowLayer, size, new Vector2(0, -10));
            _panels.Add(panel);
            if (menu != null) UIBuilder.Button(menu, label, () => Focus(panel), -1, 40);
            _hotkeys.Add((hotkey, panel));
            Services.Register(panel);
            return panel;
        }

        void Focus(UIPanel panel)
        {
            bool wasOpen = panel.Visible;
            foreach (var p in _panels) if (p != panel) p.Close();
            if (wasOpen) panel.Close();
            else panel.Open();
        }

        void BuildBanner()
        {
            var img = UIBuilder.Panel(HudLayer, "PickBanner", new Color(0.1f, 0.08f, 0.14f, 0.92f), raycast: false);
            _banner = img.rectTransform;
            _banner.Anchor(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -62), new Vector2(560, 40));
            _bannerText = UIBuilder.Label(_banner, "", 17, TextAnchor.MiddleCenter, UITheme.Accent);
            _bannerText.rectTransform.Stretch();
            _banner.gameObject.SetActive(false);
        }

        void OnPicking(MapPickingChangedEvent e)
        {
            _banner.gameObject.SetActive(e.Active);
            _bannerText.text = e.Prompt ?? "";
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            var es = EventSystem.current;
            bool typing = es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.GetComponent<InputField>() != null;
            if (typing) return;

            foreach (var (key, panel) in _hotkeys)
                if (kb[key].wasPressedThisFrame) Focus(panel);

            if (kb.escapeKey.wasPressedThisFrame && !(Services.TryGet<MapPicker>(out var picker) && picker.IsPicking))
            {
                bool closedAny = false;
                foreach (var p in _panels) if (p.Visible) { p.Close(); closedAny = true; }
                if (!closedAny && Services.TryGet<InspectorView>(out var inspector)) inspector.Clear();
            }
        }
    }
}
