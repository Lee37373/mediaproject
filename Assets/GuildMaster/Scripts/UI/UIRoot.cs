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
        /// <summary>Open windows, oldest first (last = top-most, closed first by ESC).</summary>
        readonly List<UIPanel> _openOrder = new();
        readonly List<RaycastResult> _hits = new();
        const float CascadeStep = 28f;
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
            var nameTags = WorldLayer.gameObject.AddComponent<NameTagLayer>();
            nameTags.Build(UIBuilder.Rect(WorldLayer, "NameTags").Stretch());
            var bubbles = WorldLayer.gameObject.AddComponent<SpeechBubbleLayer>();
            bubbles.Build(UIBuilder.Rect(WorldLayer, "Bubbles").Stretch());
            // Skill icons with their cooldowns, above the health bar (P4-34).
            var skillBars = WorldLayer.gameObject.AddComponent<SkillBarLayer>();
            skillBars.Build(UIBuilder.Rect(WorldLayer, "SkillBars").Stretch());
            Services.Register(floating);
            Services.Register(bubbles);
            Services.Register(nameTags); // KeyHelpView reads its on/off state

            // HUD + windows.
            var canvas = UIBuilder.CreateCanvas("UICanvas", 10);
            canvas.transform.SetParent(transform, false);
            HudLayer = (RectTransform)canvas.transform;

            if (Services.TryGet<LLM.LlmGateway>(out var llm)) HudView.ExtraStatus = () => llm ? llm.StatusText : "";
            var hud = HudLayer.gameObject.AddComponent<HudView>();
            hud.Build(HudLayer);
            var minimap = HudLayer.gameObject.AddComponent<MinimapView>();
            minimap.Build(HudLayer);
            Services.Register(minimap);
            var log = HudLayer.gameObject.AddComponent<LogView>();
            log.Build(HudLayer);
            var inspector = HudLayer.gameObject.AddComponent<InspectorView>();
            inspector.Build(HudLayer);
            Services.Register(inspector);

            WindowLayer = UIBuilder.Rect(HudLayer, "Windows").Stretch();

            var menu = UIBuilder.Rect(HudLayer, "SideMenu");
            menu.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -60), new Vector2(150, 410));
            UIBuilder.VLayout(menu, 6, 0);

            AddWindow<QuestBoardView>(menu, "의뢰 게시판 (Q)", Key.Q, new Vector2(1180, 680));
            AddWindow<RosterView>(menu, "길드원 (R)", Key.R, new Vector2(1000, 560));
            AddWindow<StockView>(menu, "창고 (I)", Key.I, new Vector2(760, 600));
            AddWindow<EcologyView>(menu, "생태계 (E)", Key.E, new Vector2(900, 560));
            AddWindow<MerchantView>(menu, "행상인 (T)", Key.T, new Vector2(760, 600));
            AddWindow<BuildView>(menu, "건설 (B)", Key.B, new Vector2(700, 300));
            // Daily accounts: income, spending and events per game day (F-3). Moved off B, which
            // construction (P4-13) already claimed.
            AddWindow<DailyReportView>(menu, "정산 (L)", Key.L, new Vector2(960, 760));
            // The wall of the fallen: relics to fetch, memorials (F-6).
            AddWindow<MemorialView>(menu, "추모의 벽 (H)", Key.H, new Vector2(880, 640));
            // Notification list (F-1); the side-menu button shows the unread count (kept up to date by ToastLayer).
            var notificationView = AddWindow<NotificationView>(null, null, Key.N, new Vector2(820, 560));
            var notificationButton = UIBuilder.Button(menu, "알림 (N)", () => Focus(notificationView), -1, 40);
            // Debug-only: LLM request/response viewer (no side-menu button).
            if (Debug.isDebugBuild) AddWindow<LlmDebugView>(null, null, Key.F2, new Vector2(1180, 680));
            // Opened from the inspector ("영입 대화"), no menu button or hotkey.
            AddWindow<RecruitDialogView>(null, null, Key.None, new Vector2(980, 600));
            // Opened from the quest board ("설득") once a quest has stayed untaken (P4-24).
            AddWindow<PersuadeDialogView>(null, null, Key.None, new Vector2(1040, 700));
            AddWindow<BuildingView>(null, null, Key.None, new Vector2(560, 380));
            AddWindow<DialogueLogView>(null, null, Key.None, new Vector2(760, 560));
            // Opened from the inspector and roster [상세] buttons (P4-6).
            AddWindow<AdventurerDetailView>(null, null, Key.None, new Vector2(1080, 700));
            AddWindow<MemberCareView>(null, null, Key.None, new Vector2(640, 700));
            // Ready-made quests to choose from (F-8); opened from the quest board and the proposal notification.
            AddWindow<QuestProposalView>(null, null, Key.None, new Vector2(920, 640));

            BuildBanner();
            HudLayer.gameObject.AddComponent<RaidBannerView>().Build(HudLayer);
            // Toasts for new notifications on the right edge, above the windows (F-1).
            var toasts = HudLayer.gameObject.AddComponent<ToastLayer>();
            toasts.MenuButton = notificationButton;
            toasts.Build(HudLayer);
            HudLayer.gameObject.AddComponent<GameOverView>().Build(HudLayer);
            // All four region lords down (P4-83): the victory; play goes on.
            HudLayer.gameObject.AddComponent<VictoryView>().Build(HudLayer);
            // Menus (P4-10): pause (ESC) and title over the world, settings above both.
            _pause = HudLayer.gameObject.AddComponent<PauseMenuView>();
            _title = HudLayer.gameObject.AddComponent<TitleView>();
            _settings = HudLayer.gameObject.AddComponent<SettingsView>();
            _saves = HudLayer.gameObject.AddComponent<SaveLoadView>(); // P5-4
            _pause.Build(HudLayer, _settings, _saves);
            _title.Build(HudLayer, _settings, _saves);
            _settings.Build(HudLayer);
            _saves.Build(HudLayer);
            // ` key: shortcut reference with toggle states (drawn above everything else).
            HudLayer.gameObject.AddComponent<KeyHelpView>().Build(HudLayer);
            // Hover tooltips sit above every window.
            HudLayer.gameObject.AddComponent<UITooltip>().Build(HudLayer);
            EventBus.Subscribe<MapPickingChangedEvent>(OnPicking);
            _title.ShowIfRequested();
        }

        PauseMenuView _pause;
        TitleView _title;
        SettingsView _settings;
        SaveLoadView _saves;

        void OnDestroy() => EventBus.Unsubscribe<MapPickingChangedEvent>(OnPicking);

        /// <summary>Adds a window with a side-menu button and hotkey. Later phases call this to add their panels.</summary>
        public T AddWindow<T>(RectTransform menu, string label, Key hotkey, Vector2 size) where T : UIPanel
        {
            var go = new GameObject(typeof(T).Name);
            var panel = go.AddComponent<T>();
            panel.Create(WindowLayer, size, new Vector2(0, -10));
            _panels.Add(panel);
            if (menu != null) UIBuilder.Button(menu, label, () => Focus(panel), -1, 40);
            if (hotkey != Key.None) _hotkeys.Add((hotkey, panel));
            Services.Register(panel);
            return panel;
        }

        /// <summary>Menu button / hotkey: open (on top), raise if buried, close if already on top.</summary>
        void Focus(UIPanel panel)
        {
            if (!panel.Visible) panel.Open();
            else if (TopWindow != panel) panel.BringToFront();
            else panel.Close();
        }

        UIPanel TopWindow => _openOrder.Count > 0 ? _openOrder[_openOrder.Count - 1] : null;

        /// <summary>Called by UIPanel.Open/BringToFront: moves the window to the top of the open order.</summary>
        internal void NotifyOpened(UIPanel panel, bool wasOpen)
        {
            _openOrder.Remove(panel);
            _openOrder.RemoveAll(p => p == null || !p.Visible);
            // A freshly opened window steps aside from any open window sitting at the same spot.
            if (!wasOpen)
            {
                var frame = panel.WindowFrame;
                if (_openOrder.Count == 0) frame.anchoredPosition = panel.HomePosition;
                for (int guard = 0; guard < 12 && _openOrder.Exists(p => (p.WindowFrame.anchoredPosition - frame.anchoredPosition).sqrMagnitude < 12f * 12f); guard++)
                    frame.anchoredPosition += new Vector2(CascadeStep, -CascadeStep);
            }
            _openOrder.Add(panel);
        }

        internal void NotifyClosed(UIPanel panel) => _openOrder.Remove(panel);

        /// <summary>A press anywhere on a window raises it above the others.</summary>
        void RaiseClickedWindow()
        {
            var mouse = Mouse.current;
            var es = EventSystem.current;
            if (mouse == null || es == null || !mouse.leftButton.wasPressedThisFrame || _openOrder.Count < 2) return;
            _hits.Clear();
            es.RaycastAll(new PointerEventData(es) { position = mouse.position.ReadValue() }, _hits);
            if (_hits.Count == 0) return;
            for (var t = _hits[0].gameObject.transform; t != null; t = t.parent)
            {
                var panel = _openOrder.Find(p => p.WindowFrame == t);
                if (panel == null) continue;
                if (panel != TopWindow) panel.BringToFront();
                return;
            }
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
            RaiseClickedWindow();
            var kb = Keyboard.current;
            if (kb == null) return;
            var es = EventSystem.current;
            bool typing = es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.GetComponent<InputField>() != null;
            if (typing) return;

            // Menus (title, pause, settings) swallow window shortcuts; ESC backs out of them.
            if (MenuState.IsOpen)
            {
                if (!kb.escapeKey.wasPressedThisFrame) return;
                if (_settings.IsOpen) _settings.Hide();
                else if (_saves.IsOpen) _saves.Hide();
                else if (_pause.IsOpen) _pause.Hide();
                return;
            }

            foreach (var (key, panel) in _hotkeys)
                if (kb[key].wasPressedThisFrame) Focus(panel);

            if (kb.escapeKey.wasPressedThisFrame && !(Services.TryGet<MapPicker>(out var picker) && picker.IsPicking))
            {
                // Newest window first, then the selection; with nothing left to close, the pause menu.
                _openOrder.RemoveAll(p => p == null || !p.Visible);
                if (TopWindow != null) TopWindow.Close();
                else if (Services.TryGet<InspectorView>(out var inspector) && inspector.Selected != null) inspector.Clear();
                else _pause.Show();
            }
        }
    }
}
