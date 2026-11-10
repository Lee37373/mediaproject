using System.Collections.Generic;
using System.Linq;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.LLM;
using GuildMaster.Session;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Settings overlay (P4-10), opened from the title and pause menus. Values persist through <see cref="GameSettings"/>.
    /// P6-3 (user decisions 2026-11-10): five tabs — 게임 (LLM, window spots), 화면 (full screen, resolution), 소리 (volumes),
    /// 연출 (shake strength, hit-stop, calmer flashes, reward effects) and 사건 (which windows open by themselves, which
    /// moments pause the game). P6-4 adds to 화면, P6-5 a 조작 tab.
    /// </summary>
    public class SettingsView : MonoBehaviour
    {
        public enum Tab { Game, Screen, Sound, Effects, Events }
        static readonly string[] TabNames = { "게임", "화면", "소리", "연출", "사건" };

        const float BoxWidth = 600f, BoxHeight = 640f;
        const float ControlWidth = 240f;
        /// <summary>Real seconds between two shake previews while the strength slider moves.</summary>
        const float ShakePreviewGap = 0.4f;

        RectTransform _root;
        readonly Button[] _tabButtons = new Button[TabNames.Length];
        readonly RectTransform[] _pages = new RectTransform[TabNames.Length];
        Button _llm, _fullscreen, _rewardFx, _hitStop, _calmFlashes, _dilemmaOpen, _newsOpen;
        readonly Dictionary<PauseTrigger, Button> _pauseButtons = new();
        Slider _shake;
        Text _shakeValue, _sfxValue, _musicValue;
        Dropdown _resolution;
        Text _gameNote, _screenNote;
        readonly List<Vector2Int> _resolutions = new();
        float _nextShakePreview;

        /// <summary>The tab shown; kept while the game runs, so the window reopens where the player left it.</summary>
        public static Tab Current { get; private set; }

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        public void Build(RectTransform canvas)
        {
            var dim = UIBuilder.Panel(canvas, "Settings", new Color(0f, 0f, 0f, 0.55f));
            _root = dim.rectTransform;
            _root.Stretch();
            var box = UIBuilder.Panel(_root, "Box", UITheme.PanelBg);
            box.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(BoxWidth, BoxHeight));
            UIBuilder.VLayout(box, 10, 24);
            // As the confirm window (P6-1): the skinned panel is see-through, so an opaque backing keeps the windows and
            // the world behind from showing through the rows.
            var backing = UIBuilder.Panel(box.transform, "Backing", new Color(UITheme.PanelBg.r, UITheme.PanelBg.g, UITheme.PanelBg.b, 1f), raycast: false);
            backing.rectTransform.Stretch(5, 5, 5, 5);
            backing.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UIBuilder.Label(box.transform, "설정", 26, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 40);

            var tabs = UIBuilder.Row(box.transform, 36, 6);
            float tabWidth = (BoxWidth - 48f - 6f * (TabNames.Length - 1)) / TabNames.Length;
            for (int i = 0; i < TabNames.Length; i++)
            {
                var tab = (Tab)i;
                _tabButtons[i] = UIBuilder.Button(tabs, TabNames[i], () => ShowTab(tab), tabWidth, 34, null, 16);
                _pages[i] = UIBuilder.Rect(box.transform, "Page:" + TabNames[i]);
                _pages[i].Size(-1, -1, -1, 1);
                UIBuilder.VLayout(_pages[i], 8, 0);
            }

            BuildGame(_pages[(int)Tab.Game]);
            BuildScreen(_pages[(int)Tab.Screen]);
            BuildSound(_pages[(int)Tab.Sound]);
            BuildEffects(_pages[(int)Tab.Effects]);
            BuildEvents(_pages[(int)Tab.Events]);

            UIBuilder.Button(box.transform, "닫기", Hide, -1, 40, new Color(0.25f, 0.45f, 0.3f), 17);
            _root.gameObject.SetActive(false);
        }

        // ---------------- Pages ----------------

        void BuildGame(RectTransform page)
        {
            _llm = UIBuilder.Button(Row(page, "AI(LLM)"), "", ToggleLlm, ControlWidth, 34, null, 15);
            _llm.Tooltip(() => "영입·면담에서 직접 친 말과 상납 대화에 로컬 언어 모델을 씁니다.\n끄면 미리 쓴 대사와 규칙으로 답합니다.");
            // P6-2: windows open where they were last dragged, across runs; this sends them all back.
            UIBuilder.Button(Row(page, "창 위치"), "처음 자리로", ResetWindows, ControlWidth, 34, null, 15)
                .Tooltip(() => "끌어 옮긴 창들이 다음에 열릴 자리를 잊고 처음 자리로 되돌립니다.");
            _gameNote = Note(page);
        }

        void BuildScreen(RectTransform page)
        {
            _fullscreen = UIBuilder.Button(Row(page, "전체 화면"), "", ToggleFullscreen, ControlWidth, 34, null, 15);
            _resolution = UIBuilder.Dropdown(Row(page, "해상도"), new List<string>(), OnResolution, ControlWidth, 34);
            _screenNote = Note(page);
        }

        void BuildSound(RectTransform page)
        {
            var sfx = Row(page, "효과음");
            UIBuilder.Slider(sfx, 0f, 1f, GameSettings.SfxVolume, v => { GameSettings.SfxVolume = v; RefreshValues(); }, ControlWidth);
            _sfxValue = Value(sfx);
            var music = Row(page, "배경 음악");
            UIBuilder.Slider(music, 0f, 1f, GameSettings.MusicVolume, v => { GameSettings.MusicVolume = v; RefreshValues(); }, ControlWidth);
            _musicValue = Value(music);
        }

        void BuildEffects(RectTransform page)
        {
            var all = Row(page, "한꺼번에");
            UIBuilder.Button(all, "모두 줄이기", () => { GameSettings.CalmAllEffects(); Refresh(); }, (ControlWidth - 6f) / 2f, 34, null, 15)
                .Tooltip(() => "화면 흔들림 0%, 히트스톱 끔, 번쩍임 줄이기 켬, 보상 연출 끔.");
            UIBuilder.Button(all, "기본값으로", () => { GameSettings.DefaultEffects(); Refresh(); }, (ControlWidth - 6f) / 2f, 34, null, 15)
                .Tooltip(() => "화면 흔들림 100%, 히트스톱 켬, 번쩍임 줄이기 끔, 보상 연출 켬.");

            var shake = Row(page, "화면 흔들림");
            _shake = UIBuilder.Slider(shake, 0f, 1f, GameSettings.ShakeStrength, OnShake, ControlWidth);
            _shake.Tooltip(() => "큰 타격·쓰러짐·침공 때 화면이 흔들리는 세기입니다. 0%면 흔들리지 않습니다.");
            _shakeValue = Value(shake);
            _hitStop = UIBuilder.Button(Row(page, "히트스톱"), "", () => { GameSettings.HitStop = !GameSettings.HitStop; Refresh(); }, ControlWidth, 34, null, 15);
            _hitStop.Tooltip(() => "길드가 낀 치명타 때 아주 잠깐(0.05초) 시간이 거의 멈추는 효과입니다.");
            _calmFlashes = UIBuilder.Button(Row(page, "번쩍임 줄이기"), "", () => { GameSettings.CalmFlashes = !GameSettings.CalmFlashes; Refresh(); }, ControlWidth, 34, null, 15);
            _calmFlashes.Tooltip(() => "켜면 치명타 섬광이 일반 타격처럼, 맞을 때 빨간 깜빡임은 옅게(누가 맞았는지는 보임),\n"
                                       + "공격받는 마을의 붉은 번쩍임도 옅게, 관계도의 최근 바뀐 선은 깜빡이지 않고 밝게 표시합니다.");
            _rewardFx = UIBuilder.Button(Row(page, "보상 연출"), "", ToggleRewardFx, ControlWidth, 34, null, 15);
            _rewardFx.Tooltip(() => "골드로 날아가는 동전, 건물 반짝임, 등급 깃발, 레벨 업 빛기둥.");
        }

        void BuildEvents(RectTransform page)
        {
            var hint = UIBuilder.Label(page, "사건이 일어나면 게임을 어떻게 할지 고릅니다. 길드가 보거나 들은 것만 셉니다. 알림은 언제나 옵니다.",
                14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 40);
            hint.verticalOverflow = VerticalWrapMode.Overflow;
            _dilemmaOpen = UIBuilder.Button(Row(page, "결정 카드"), "", () => { GameSettings.DilemmaAutoOpen = !GameSettings.DilemmaAutoOpen; Refresh(); }, ControlWidth, 34, null, 15);
            _dilemmaOpen.Tooltip(() => "새 결정 카드가 나왔을 때. '알림만'이면 결정 창(J)이나 알림으로 엽니다. 기한이 지나면 기본 선택으로 정해집니다.");
            _newsOpen = UIBuilder.Button(Row(page, "아침 소식지"), "", () => { GameSettings.NewsEachMorning = !GameSettings.NewsEachMorning; Refresh(); }, ControlWidth, 34, null, 15);
            _newsOpen.Tooltip(() => "아침마다 마을 소식지가 나왔을 때. '알림만'이면 소식지(O)나 알림으로 엽니다.");
            foreach (PauseTrigger t in System.Enum.GetValues(typeof(PauseTrigger)))
            {
                var trigger = t;
                var b = UIBuilder.Button(Row(page, EventPause.Label(t)), "", () => { GameSettings.SetPauseOn(trigger, !GameSettings.PauseOn(trigger)); Refresh(); }, ControlWidth, 34, null, 15);
                b.Tooltip(() => EventPause.Describe(trigger) + ".\n'일시정지'면 게임이 멈춥니다. Space나 x1로 다시 시작합니다.");
                _pauseButtons[t] = b;
            }
        }

        static RectTransform Row(Transform parent, string label)
        {
            var row = UIBuilder.Row(parent, 36);
            UIBuilder.Label(row, label, 16, TextAnchor.MiddleLeft, UITheme.Text).Size(180);
            return row;
        }

        static Text Value(Transform row) => UIBuilder.Label(row, "", 15, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(60);

        static Text Note(Transform page)
        {
            var note = UIBuilder.Label(page, "", 14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 56);
            note.verticalOverflow = VerticalWrapMode.Overflow;
            return note;
        }

        // ---------------- Open / close ----------------

        public void Show()
        {
            if (IsOpen) return;
            MenuState.Push();
            BuildResolutions();
            _shake.SetValueWithoutNotify(GameSettings.ShakeStrength);
            _gameNote.text = "";
            ShowTab(Current);
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (!IsOpen) return;
            _root.gameObject.SetActive(false);
            MenuState.Pop();
        }

        /// <summary>Shows one tab (public for tests).</summary>
        public void ShowTab(Tab tab)
        {
            Current = tab;
            for (int i = 0; i < _pages.Length; i++)
            {
                bool on = i == (int)tab;
                _pages[i].gameObject.SetActive(on);
                UISkin.Recolor(_tabButtons[i], on ? UITheme.Accent : UITheme.Button);
                _tabButtons[i].GetComponentInChildren<Text>().color = !on ? UITheme.TextMuted : UISkin.HasButtons ? UITheme.Text : TabInk;
            }
            Refresh();
        }

        static readonly Color TabInk = new(0.14f, 0.11f, 0.08f);

        void BuildResolutions()
        {
            _resolutions.Clear();
            foreach (var r in Screen.resolutions.Select(r => new Vector2Int(r.width, r.height)).Distinct().OrderByDescending(v => v.x * v.y))
                if (r.x >= 1024 && _resolutions.Count < 12) _resolutions.Add(r);
            var current = GameSettings.Resolution;
            if (!_resolutions.Contains(current)) _resolutions.Insert(0, current);
            _resolution.ClearOptions();
            _resolution.AddOptions(_resolutions.Select(r => $"{r.x} × {r.y}").ToList());
            _resolution.SetValueWithoutNotify(Mathf.Max(0, _resolutions.IndexOf(current)));
        }

        void Refresh()
        {
            _llm.SetLabel(GameSettings.LlmEnabled ? "켬 (LLM 대화)" : "끔 (규칙 기반)");
            _fullscreen.SetLabel(GameSettings.Fullscreen ? "켬" : "끔");
            _screenNote.text = Application.isEditor ? "에디터에서는 화면 설정이 저장만 되고 적용되지 않습니다(빌드에서 적용)." : "";
            _rewardFx.SetLabel(GameSettings.RewardEffects ? "켬" : "끔");
            _hitStop.SetLabel(GameSettings.HitStop ? "켬" : "끔");
            _calmFlashes.SetLabel(GameSettings.CalmFlashes ? "켬" : "끔");
            _shake.SetValueWithoutNotify(GameSettings.ShakeStrength);
            _dilemmaOpen.SetLabel(GameSettings.DilemmaAutoOpen ? "창 열고 멈춤" : "알림만");
            _newsOpen.SetLabel(GameSettings.NewsEachMorning ? "창 열고 멈춤" : "알림만");
            // A moment that pauses is a yellow button (like the chosen tab), so the list reads at a glance.
            foreach (var (t, b) in _pauseButtons.Select(kv => (kv.Key, kv.Value)))
            {
                bool on = GameSettings.PauseOn(t);
                b.SetLabel(on ? "일시정지" : "그대로");
                UISkin.Recolor(b, on ? UITheme.Accent : UITheme.Button);
                b.GetComponentInChildren<Text>().color = on && !UISkin.HasButtons ? TabInk : UITheme.Text;
            }
            RefreshValues();
        }

        void RefreshValues()
        {
            _shakeValue.text = $"{Mathf.RoundToInt(GameSettings.ShakeStrength * 100f)}%";
            _sfxValue.text = $"{Mathf.RoundToInt(GameSettings.SfxVolume * 100f)}%";
            _musicValue.text = $"{Mathf.RoundToInt(GameSettings.MusicVolume * 100f)}%";
        }

        // ---------------- Actions ----------------

        void ToggleLlm()
        {
            GameSettings.LlmEnabled = !GameSettings.LlmEnabled;
            if (Services.TryGet<LlmGateway>(out var llm)) llm.Enabled = GameSettings.LlmEnabled;
            Refresh();
        }

        void ToggleRewardFx()
        {
            GameSettings.RewardEffects = !GameSettings.RewardEffects;
            Refresh();
        }

        /// <summary>The strength changes at once, and the camera gives a small shake now and then to feel it (in a game).</summary>
        void OnShake(float v)
        {
            GameSettings.ShakeStrength = v;
            RefreshValues();
            if (Time.unscaledTime < _nextShakePreview || !Services.TryGet<CameraController>(out var cam)) return;
            _nextShakePreview = Time.unscaledTime + ShakePreviewGap;
            cam.Shake(0.35f);
        }

        void ResetWindows()
        {
            if (Services.TryGet<UIRoot>(out var root)) root.ResetWindowPositions();
            _gameNote.text = "창 위치를 처음 자리로 되돌렸습니다.";
        }

        void ToggleFullscreen()
        {
            GameSettings.Fullscreen = !GameSettings.Fullscreen;
            Refresh();
        }

        void OnResolution(int index)
        {
            if (index < 0 || index >= _resolutions.Count) return;
            GameSettings.Resolution = _resolutions[index];
            Refresh();
        }
    }
}
