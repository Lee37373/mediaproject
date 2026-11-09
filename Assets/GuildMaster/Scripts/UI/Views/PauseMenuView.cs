using GuildMaster.Core;
using GuildMaster.Session;
using UnityEngine;

namespace GuildMaster.UI
{
    /// <summary>
    /// ESC pause menu (P4-10): holds the game clock while open. Continue, save or load (P5-4),
    /// settings, back to the title (a fresh world) or quit — both ask first when there is unsaved progress (P6-1, <see cref="ExitGuard"/>).
    /// </summary>
    public class PauseMenuView : MonoBehaviour
    {
        RectTransform _root;
        SettingsView _settings;
        SaveLoadView _saves;
        bool _holding;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        public void Build(RectTransform canvas, SettingsView settings, SaveLoadView saves)
        {
            _settings = settings;
            _saves = saves;
            var dim = UIBuilder.Panel(canvas, "PauseMenu", new Color(0f, 0f, 0f, 0.55f));
            _root = dim.rectTransform;
            _root.Stretch();
            var box = UIBuilder.Panel(_root, "Box", UITheme.PanelBg);
            box.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360, 440));
            UIBuilder.VLayout(box, 10, 24);
            UIBuilder.Label(box.transform, "일시정지", 26, TextAnchor.MiddleCenter, UITheme.Accent, FontStyle.Bold).Size(-1, 44);
            UIBuilder.Button(box.transform, "계속하기", Hide, -1, 44, new Color(0.25f, 0.45f, 0.3f), 18);
            UIBuilder.Button(box.transform, "저장하기", () => _saves.Show(canSave: true), -1, 44, null, 18);
            UIBuilder.Button(box.transform, "불러오기", () => _saves.Show(canSave: false), -1, 44, null, 18);
            UIBuilder.Button(box.transform, "설정", () => _settings.Show(), -1, 44, null, 18);
            // P6-1: both ask first when there is unsaved progress (ExitGuard).
            UIBuilder.Button(box.transform, "타이틀로", () => ExitGuard.Request("타이틀로 나갈까요?", () => GameSettings.ReloadScene(showTitle: true)), -1, 44, null, 18)
                .GetComponent<RectTransform>().Tooltip(() => ExitGuard.UnsavedText() ?? "마지막 저장 뒤로 흐른 시간이 없습니다.");
            UIBuilder.Button(box.transform, "종료", () => ExitGuard.Request("게임을 끝낼까요?", GameSettings.Quit), -1, 44, UITheme.ButtonDanger, 18);
            _root.gameObject.SetActive(false);
        }

        public void Show()
        {
            if (IsOpen) return;
            MenuState.Push();
            if (Services.TryGet<GameClock>(out var clock)) { clock.Hold(); _holding = true; }
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (!IsOpen) return;
            _root.gameObject.SetActive(false);
            MenuState.Pop();
            if (_holding && Services.TryGet<GameClock>(out var clock)) clock.Release();
            _holding = false;
        }
    }
}
