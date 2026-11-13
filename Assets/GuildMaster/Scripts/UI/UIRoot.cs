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
            // Feelings beside heads (F-10), under the speech bubbles.
            WorldLayer.gameObject.AddComponent<EmotionIconLayer>().Build(UIBuilder.Rect(WorldLayer, "Emotions").Stretch());
            // Residents' requests (P4-134): a "!" on the other side of the head.
            WorldLayer.gameObject.AddComponent<RequestMarkLayer>().Build(UIBuilder.Rect(WorldLayer, "RequestMarks").Stretch());
            // Incidents as "?"/"!" notices, gone when clicked (2026-11-13, in place of the purple rumour circles).
            var rumourMarks = WorldLayer.gameObject.AddComponent<RumourMarkLayer>();
            rumourMarks.Build(UIBuilder.Rect(WorldLayer, "RumourMarks").Stretch());
            Services.Register(rumourMarks);
            var bubbles = WorldLayer.gameObject.AddComponent<SpeechBubbleLayer>();
            bubbles.Build(UIBuilder.Rect(WorldLayer, "Bubbles").Stretch());
            // Town talks (user request 2026-10-01): bubbles for people who are not adventurers (the merchant).
            WorldLayer.gameObject.AddComponent<TownSpeechRelay>();
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
            Services.Register(hud); // the coins fly to its gold (P4-106 H8)
            var minimap = HudLayer.gameObject.AddComponent<MinimapView>();
            minimap.Build(HudLayer);
            Services.Register(minimap);
            var log = HudLayer.gameObject.AddComponent<LogView>();
            log.Build(HudLayer);
            var inspector = HudLayer.gameObject.AddComponent<InspectorView>();
            inspector.Build(HudLayer);
            Services.Register(inspector);
            // "다음 할 일" checklist and hints (F-19), under the windows.
            HudLayer.gameObject.AddComponent<GuideView>().Build(HudLayer);
            // The guild master's orders and their gauge (P4-132), bottom centre, under the windows.
            HudLayer.gameObject.AddComponent<CommandBarView>().Build(HudLayer);

            // The next rank's conditions (P4-106), under the side menu and beneath the windows.
            // 2026-11-12 (user: too many buttons on the left): four buttons and two groups that open beside the menu.
            const float MenuHeight = 6 * 40f + 5 * 6f;
            HudLayer.gameObject.AddComponent<GoalPanelView>().Build(HudLayer, new Vector2(8, -60 - MenuHeight - 10));

            WindowLayer = UIBuilder.Rect(HudLayer, "Windows").Stretch();

            var menu = UIBuilder.Rect(HudLayer, "SideMenu");
            menu.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -60), new Vector2(150, MenuHeight));
            UIBuilder.VLayout(menu, 6, 0);
            // 2026-11-12: the less used windows sit in two groups (their hotkeys unchanged); moved below 알림 further down.
            var guildGroup = SideMenuGroup.Create(menu, HudLayer, "길드  ▶");
            var townGroup = SideMenuGroup.Create(menu, HudLayer, "마을·기록  ▶");
            // P6-2 (user decision 2026-11-10): "close all" beside the side menu's top, above the windows, only while two or
            // more are open; Shift+ESC does the same.
            _closeAll = UIBuilder.Button(HudLayer, "창 모두 닫기", CloseAllWindows, 150, 34, null, 15);
            ((RectTransform)_closeAll.transform).Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(166, -60), new Vector2(150, 34));
            _closeAll.Tooltip(() => "열린 창을 모두 닫습니다 (Shift+ESC).\n결정 카드는 '나중에'와 같아 J로 다시 열 수 있습니다.");
            _closeAll.gameObject.SetActive(false);

            AddWindow<QuestBoardView>(menu, "의뢰 게시판 (Q)", Key.Q, new Vector2(1180, 680));
            AddWindow<RosterView>(menu, "길드원 (R)", Key.R, new Vector2(1200, 680)); // 2026-11-05: party board
            AddWindow<StockView>(guildGroup.Items, "창고 (I)", Key.I, new Vector2(760, 600));
            // Food and quest payments (economy round 2, 2026-10-29).
            AddWindow<HouseholdView>(guildGroup.Items, "살림 (U)", Key.U, new Vector2(820, 900)); // P4-134: the residents' standing and requests
            // A resident's request (P4-134): opened by clicking the resident or its notification.
            AddWindow<ResidentRequestView>(null, null, Key.None, new Vector2(760, 640));
            // Residents' odd jobs as minigames (일손 돕기, 2026-10-29), opened from the household window.
            AddWindow<ChoreView>(null, null, Key.None, new Vector2(820, 500)); // 2026-11-11: the minigame scene, no empty band
            AddWindow<EcologyView>(townGroup.Items, "생태계 (E)", Key.E, new Vector2(900, 560));
            AddWindow<MerchantView>(townGroup.Items, "행상인 (T)", Key.T, new Vector2(760, 600));
            AddWindow<BuildView>(guildGroup.Items, "건설 (B)", Key.B, new Vector2(760, 620));
            // Daily accounts: income, spending and events per game day (F-3). Moved off B, which
            // construction (P4-13) already claimed.
            AddWindow<DailyReportView>(guildGroup.Items, "정산 (L)", Key.L, new Vector2(960, 760));
            // The town newspaper (P4-130): opens by itself each morning; O, the accounts window and its notification.
            AddWindow<NewspaperView>(null, null, Key.O, new Vector2(920, 800));
            // The wall of the fallen: relics to fetch, memorials (F-6).
            AddWindow<MemorialView>(townGroup.Items, "추모의 벽 (H)", Key.H, new Vector2(880, 640));
            // The guild's rank and fame (P4-106).
            AddWindow<GuildRankView>(guildGroup.Items, "길드 등급 (G)", Key.G, new Vector2(900, 720));
            // The bestiary (P4-107).
            AddWindow<BestiaryView>(townGroup.Items, "몬스터 도감 (K)", Key.K, new Vector2(1000, 640));
            // Decision cards (P4-111c).
            // P4-118: opens by itself on a new card. P4-135: as tall as what it shows (up to 820), KMS's card width.
            AddWindow<DilemmaView>(menu, "결정 (J)", Key.J, new Vector2(760, 820));
            // The guild's rules (P4-112, H2).
            AddWindow<GuildLawView>(guildGroup.Items, "길드 규칙 (P)", Key.P, new Vector2(980, 760));
            // Who gets along with whom, and how everyone feels (F-10). G went to the guild rank at the 2026-10-31 merge.
            AddWindow<RelationshipView>(townGroup.Items, "관계도 (V)", Key.V, new Vector2(1040, 720));
            // Notification list (F-1); the side-menu button shows the unread count (kept up to date by ToastLayer).
            var notificationView = AddWindow<NotificationView>(null, null, Key.N, new Vector2(820, 560));
            var notificationButton = UIBuilder.Button(menu, "알림 (N)", () => Focus(notificationView), -1, 40);
            guildGroup.Button.transform.SetAsLastSibling();
            townGroup.Button.transform.SetAsLastSibling();
            guildGroup.Seal();
            townGroup.Seal();
            // Debug-only: LLM request/response viewer (no side-menu button).
            if (Debug.isDebugBuild) AddWindow<LlmDebugView>(null, null, Key.F2, new Vector2(1180, 680));
            // Opened from the inspector ("영입 대화"), no menu button or hotkey.
            AddWindow<RecruitDialogView>(null, null, Key.None, new Vector2(980, 600));
            // Opened from the quest board ("설득") once a quest has stayed untaken (P4-24).
            AddWindow<PersuadeDialogView>(null, null, Key.None, new Vector2(1040, 700));
            AddWindow<BuildingView>(null, null, Key.None, new Vector2(560, 450));
            AddWindow<WatchRuinView>(null, null, Key.None, new Vector2(520, 300)); // the ruined watchtower's lookout (2026-11-02)
            AddWindow<DialogueLogView>(null, null, Key.None, new Vector2(760, 560));
            // Opened from the inspector and roster [상세] buttons (P4-6).
            AddWindow<AdventurerDetailView>(null, null, Key.None, new Vector2(1080, 880)); // 700 → 800: 전기 (P4-129), → 880: 궁합 (P4-131)
            AddWindow<MemberCareView>(null, null, Key.None, new Vector2(640, 760)); // 700 → 760: 상납 요구 row (2026-10-29)
            // Counselling by conversation (user request 2026-10-01), opened from the care window's 면담 button.
            AddWindow<CounselDialogView>(null, null, Key.None, new Vector2(980, 620));
            // Tribute talk (상납금, 2026-10-29), opened from the care window's 상납 요구 button.
            AddWindow<TributeDialogView>(null, null, Key.None, new Vector2(980, 640));
            // Ready-made quests to choose from (F-8); opened from the quest board and the proposal notification.
            AddWindow<QuestProposalView>(null, null, Key.None, new Vector2(920, 640));

            BuildBanner();
            HudLayer.gameObject.AddComponent<RaidBannerView>().Build(HudLayer);
            HudLayer.gameObject.AddComponent<SpectatorCardView>().Build(HudLayer); // P4-129: the spectator mode's title cards
            // Coins to the gold and the rank banner (P4-106 H8), above the windows.
            HudLayer.gameObject.AddComponent<RewardFxLayer>().Build(HudLayer);
            // Toasts for new notifications on the right edge, above the windows (F-1).
            var toasts = HudLayer.gameObject.AddComponent<ToastLayer>();
            toasts.MenuButton = notificationButton;
            toasts.Build(HudLayer);
            HudLayer.gameObject.AddComponent<GameOverView>().Build(HudLayer);
            // The guild reaching rank S — the four region lords among its conditions (P4-83, P4-106): the victory; play goes on.
            _victory = HudLayer.gameObject.AddComponent<VictoryView>();
            _victory.Build(HudLayer);
            // Menus (P4-10): pause (ESC) and title over the world, settings above both.
            _pause = HudLayer.gameObject.AddComponent<PauseMenuView>();
            _title = HudLayer.gameObject.AddComponent<TitleView>();
            _settings = HudLayer.gameObject.AddComponent<SettingsView>();
            _saves = HudLayer.gameObject.AddComponent<SaveLoadView>(); // P5-4
            _pause.Build(HudLayer, _settings, _saves);
            _title.Build(HudLayer, _settings, _saves);
            _settings.Build(HudLayer);
            _saves.Build(HudLayer);
            // P6-1: the shared question window above every menu, and the exit warning that uses it.
            _confirm = HudLayer.gameObject.AddComponent<ConfirmDialog>();
            _confirm.Build(HudLayer);
            Services.Register(_confirm);
            HudLayer.gameObject.AddComponent<ExitGuard>().Build(_saves, _title);
            // ` key: shortcut reference with toggle states (drawn above everything else).
            _keyHelp = HudLayer.gameObject.AddComponent<KeyHelpView>();
            _keyHelp.Build(HudLayer);
            // Hover tooltips sit above every window.
            HudLayer.gameObject.AddComponent<UITooltip>().Build(HudLayer);
            EventBus.Subscribe<MapPickingChangedEvent>(OnPicking);
            _title.ShowIfRequested();
        }

        PauseMenuView _pause;
        TitleView _title;
        SettingsView _settings;
        SaveLoadView _saves;
        ConfirmDialog _confirm;
        VictoryView _victory;
        KeyHelpView _keyHelp;
        Button _closeAll;
        int _closeAllCount = -1;

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
            if (!wasOpen) Place(panel, _openOrder);
            _openOrder.Add(panel);
        }

        internal void NotifyClosed(UIPanel panel) => _openOrder.Remove(panel);

        /// <summary>
        /// Puts a freshly opened window at its own spot — where it was last dragged, else its home (P6-2) — kept on
        /// screen, stepping aside from any open window sitting at the same spot.
        /// </summary>
        static void Place(UIPanel panel, List<UIPanel> others)
        {
            var frame = panel.WindowFrame;
            var area = ((RectTransform)frame.parent).rect.size;
            var size = frame.rect.size;
            var pos = WindowPlacement.Clamp(panel.OpenPosition, area, size);
            for (int guard = 0; guard < 12 && others.Exists(p => p != panel && (p.WindowFrame.anchoredPosition - pos).sqrMagnitude < 12f * 12f); guard++)
            {
                var next = WindowPlacement.Clamp(pos + new Vector2(CascadeStep, -CascadeStep), area, size);
                if (next == pos) break; // pressed against the screen edge
                pos = next;
            }
            frame.anchoredPosition = pos;
        }

        /// <summary>Open windows (not menus or overlays).</summary>
        public int OpenWindowCount
        {
            get
            {
                _openOrder.RemoveAll(p => p == null || !p.Visible);
                return _openOrder.Count;
            }
        }

        /// <summary>
        /// Shift+ESC and the button (P6-2, user decision 2026-11-10): every window, the ones that opened by themselves
        /// too (a decision card waits as with "나중에", a talk ends as with its own button), and the key help; the
        /// selection stays. One close sound for all.
        /// </summary>
        public void CloseAllWindows()
        {
            int closed = 0;
            foreach (var p in _panels)
                if (p != null && p.Visible) { p.CloseQuietly(); closed++; }
            if (_keyHelp != null && _keyHelp.IsOpen) { _keyHelp.Hide(); closed++; }
            _openOrder.Clear();
            if (closed > 0) GuildMaster.Audio.Sound.Play(GuildMaster.Audio.SoundKeys.Close);
        }

        /// <summary>Settings "창 위치" (P6-2): forgets every dragged spot; open windows go back to their homes.</summary>
        public void ResetWindowPositions()
        {
            foreach (var p in _panels) WindowPlacement.Forget(p.PlacementId);
            PlayerPrefs.Save();
            _openOrder.RemoveAll(p => p == null || !p.Visible);
            for (int i = 0; i < _openOrder.Count; i++) Place(_openOrder[i], _openOrder.GetRange(0, i));
        }

        void UpdateCloseAllButton()
        {
            int open = OpenWindowCount;
            bool show = open >= 2;
            if (_closeAll.gameObject.activeSelf != show) _closeAll.gameObject.SetActive(show);
            if (!show || open == _closeAllCount) return;
            _closeAllCount = open;
            _closeAll.SetLabel($"창 모두 닫기 ({open})");
        }

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
            UpdateCloseAllButton();
            var kb = Keyboard.current;
            if (kb == null) return;
            // An open question takes Enter (first button) and ESC (cancel) before anything else, typing or not (P6-1).
            if (_confirm != null && _confirm.IsOpen)
            {
                if (kb.escapeKey.wasPressedThisFrame) _confirm.Cancel();
                else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) _confirm.Accept();
                return;
            }
            // While a field has the caret, keys are text: ESC is the field's own (it leaves the field), and the next
            // ESC closes the window (P6-2, user decision 2026-11-10). This runs before the EventSystem (-500 < 0), so
            // the frame of that first ESC still sees the field focused.
            if (Session.TextFocus.Typing) return;

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
            if (kb.cKey.wasPressedThisFrame) Director.SpectatorCamera.Set(!Director.SpectatorCamera.On); // P4-129: spectator mode

            if (kb.escapeKey.wasPressedThisFrame && !(Services.TryGet<MapPicker>(out var picker) && picker.IsPicking))
            {
                // The top-most thing first (P6-2): the key help drawn over everything, the victory screen ("계속하기"),
                // the newest window, then the selection; with nothing left to close, the pause menu.
                // Shift+ESC closes every window and nothing else.
                _openOrder.RemoveAll(p => p == null || !p.Visible);
                if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) CloseAllWindows();
                else if (SideMenuGroup.CloseOpen()) { } // an open menu group (2026-11-12) first
                else if (_keyHelp != null && _keyHelp.IsOpen) _keyHelp.Hide();
                else if (_victory != null && _victory.IsOpen) _victory.Continue();
                else if (TopWindow != null) TopWindow.Close();
                else if (Services.TryGet<InspectorView>(out var inspector) && inspector.Selected != null) inspector.Clear();
                else _pause.Show();
            }
        }
    }
}
