using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.LLM;
using GuildMaster.Session;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Settings overlay (P4-10), opened from the title and pause menus: LLM on/off, full screen and
    /// resolution, and (P4-9) effects and music volume. Values persist through <see cref="GameSettings"/>.
    /// </summary>
    public class SettingsView : MonoBehaviour
    {
        RectTransform _root;
        Button _llm, _fullscreen, _rewardFx;
        Dropdown _resolution;
        Text _note;
        readonly List<Vector2Int> _resolutions = new();

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        public void Build(RectTransform canvas)
        {
            var dim = UIBuilder.Panel(canvas, "Settings", new Color(0f, 0f, 0f, 0.55f));
            _root = dim.rectTransform;
            _root.Stretch();
            var box = UIBuilder.Panel(_root, "Box", UITheme.PanelBg);
            box.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 532)); // 486 → 532: 창 위치 (P6-2)
            UIBuilder.VLayout(box, 10, 24);
            UIBuilder.Label(box.transform, "설정", 26, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 40);

            var llmRow = Row(box.transform, "AI(LLM)");
            _llm = UIBuilder.Button(llmRow, "", ToggleLlm, 200, 34, null, 15);
            var fsRow = Row(box.transform, "전체 화면");
            _fullscreen = UIBuilder.Button(fsRow, "", ToggleFullscreen, 200, 34, null, 15);
            var resRow = Row(box.transform, "해상도");
            _resolution = UIBuilder.Dropdown(resRow, new List<string>(), OnResolution, 200, 34);
            var sfxRow = Row(box.transform, "효과음");
            UIBuilder.Slider(sfxRow, 0f, 1f, GameSettings.SfxVolume, v => GameSettings.SfxVolume = v, 200);
            var musicRow = Row(box.transform, "배경 음악");
            UIBuilder.Slider(musicRow, 0f, 1f, GameSettings.MusicVolume, v => GameSettings.MusicVolume = v, 200);
            var fxRow = Row(box.transform, "보상 연출");
            _rewardFx = UIBuilder.Button(fxRow, "", ToggleRewardFx, 200, 34, null, 15);
            // P6-2: windows open where they were last dragged, across runs; this sends them all back.
            var windowRow = Row(box.transform, "창 위치");
            UIBuilder.Button(windowRow, "처음 자리로", ResetWindows, 200, 34, null, 15)
                .Tooltip(() => "끌어 옮긴 창들이 다음에 열릴 자리를 잊고 처음 자리로 되돌립니다.");

            _note = UIBuilder.Label(box.transform, "", 14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 56);
            _note.verticalOverflow = VerticalWrapMode.Overflow;
            UIBuilder.Button(box.transform, "닫기", Hide, -1, 40, new Color(0.25f, 0.45f, 0.3f), 17);
            _root.gameObject.SetActive(false);
        }

        static RectTransform Row(Transform parent, string label)
        {
            var row = UIBuilder.Row(parent, 36);
            UIBuilder.Label(row, label, 16, TextAnchor.MiddleLeft, UITheme.Text).Size(160);
            return row;
        }

        public void Show()
        {
            if (IsOpen) return;
            MenuState.Push();
            BuildResolutions();
            Refresh();
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (!IsOpen) return;
            _root.gameObject.SetActive(false);
            MenuState.Pop();
        }

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
            _llm.SetLabel(GameSettings.LlmEnabled ? "켬 (LLM 대화·제안)" : "끔 (규칙 기반)");
            _fullscreen.SetLabel(GameSettings.Fullscreen ? "켬" : "끔");
            _rewardFx.SetLabel(GameSettings.RewardEffects ? "켬" : "끔");
            _note.text = Application.isEditor
                ? "에디터에서는 화면 설정이 저장만 되고 적용되지 않습니다(빌드에서 적용)."
                : "";
        }

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

        void ResetWindows()
        {
            if (Services.TryGet<UIRoot>(out var root)) root.ResetWindowPositions();
            Refresh();
            _note.text = "창 위치를 처음 자리로 되돌렸습니다." + (_note.text.Length > 0 ? "\n" + _note.text : "");
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
