using GuildMaster.Core;
using GuildMaster.Session;
using UnityEngine;

namespace GuildMaster.UI
{
    /// <summary>
    /// ESC pause menu (P4-10): holds the game clock while open. Continue, settings, back to the
    /// title (a fresh world — there is no save yet, P5-4), or quit.
    /// </summary>
    public class PauseMenuView : MonoBehaviour
    {
        RectTransform _root;
        SettingsView _settings;
        bool _holding;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        public void Build(RectTransform canvas, SettingsView settings)
        {
            _settings = settings;
            var dim = UIBuilder.Panel(canvas, "PauseMenu", new Color(0f, 0f, 0f, 0.55f));
            _root = dim.rectTransform;
            _root.Stretch();
            var box = UIBuilder.Panel(_root, "Box", UITheme.PanelBg);
            box.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360, 330));
            UIBuilder.VLayout(box, 10, 24);
            UIBuilder.Label(box.transform, "일시정지", 26, TextAnchor.MiddleCenter, UITheme.Accent, FontStyle.Bold).Size(-1, 44);
            UIBuilder.Button(box.transform, "계속하기", Hide, -1, 44, new Color(0.25f, 0.45f, 0.3f), 18);
            UIBuilder.Button(box.transform, "설정", () => _settings.Show(), -1, 44, null, 18);
            UIBuilder.Button(box.transform, "타이틀로", () => GameSettings.ReloadScene(showTitle: true), -1, 44, null, 18)
                .GetComponent<RectTransform>().Tooltip(() => "현재 게임은 저장되지 않습니다(세이브는 P5-4에서 추가).");
            UIBuilder.Button(box.transform, "종료", GameSettings.Quit, -1, 44, UITheme.ButtonDanger, 18);
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
