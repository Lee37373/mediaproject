using GuildMaster.Core;
using GuildMaster.Session;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Title screen (P4-10), shown over the freshly generated world while the clock is held.
    /// "새 게임" starts this world, or reloads with the typed seed; "계속하기" waits for save/load
    /// (P5-4); settings; quit. Skipped when the scene was loaded to start a game directly.
    /// </summary>
    public class TitleView : MonoBehaviour
    {
        RectTransform _root;
        InputField _seed;
        Text _message;
        SettingsView _settings;
        bool _holding;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        public void Build(RectTransform canvas, SettingsView settings)
        {
            _settings = settings;
            var dim = UIBuilder.Panel(canvas, "Title", new Color(0.05f, 0.04f, 0.08f, 0.82f));
            _root = dim.rectTransform;
            _root.Stretch();

            var box = UIBuilder.Rect(_root, "Box");
            box.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(520, 560));
            UIBuilder.VLayout(box, 12, 0);
            UIBuilder.Label(box, "길드 마스터 타이쿤", 46, TextAnchor.MiddleCenter, UITheme.Accent, FontStyle.Bold).Size(-1, 70);
            UIBuilder.Label(box, "모험가들이 스스로 생각하는 길드 경영 시뮬레이션", 18, TextAnchor.MiddleCenter, UITheme.TextMuted).Size(-1, 30);
            UIBuilder.Rect(box, "Gap").Size(-1, 24);

            var seedRow = UIBuilder.Row(box, 38);
            UIBuilder.Label(seedRow, "월드 시드", 16, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(100);
            _seed = UIBuilder.Input(seedRow, "비우면 지금 보이는 월드", InputField.ContentType.IntegerNumber, -1, 36);
            _seed.GetComponent<RectTransform>().Size(-1, 36, 1);

            UIBuilder.Button(box, "새 게임", NewGame, -1, 50, new Color(0.25f, 0.45f, 0.3f), 20);
            var cont = UIBuilder.Button(box, "계속하기", () => { }, -1, 50, null, 20);
            cont.interactable = false;
            cont.GetComponent<RectTransform>().Tooltip(() => "세이브/로드(P5-4)가 추가되면 사용할 수 있습니다.");
            UIBuilder.Button(box, "설정", () => _settings.Show(), -1, 50, null, 20);
            UIBuilder.Button(box, "종료", GameSettings.Quit, -1, 50, UITheme.ButtonDanger, 20);
            _message = UIBuilder.Label(box, "", 14, TextAnchor.MiddleCenter, UITheme.TextMuted).Size(-1, 40);
            _root.gameObject.SetActive(false);
        }

        /// <summary>Shows the title if this scene load asked for it (first launch, "타이틀로").</summary>
        public void ShowIfRequested()
        {
            if (!GameSettings.ShowTitle) return;
            MenuState.Push();
            if (Services.TryGet<GameClock>(out var clock)) { clock.Hold(); _holding = true; }
            int seed = Services.TryGet<GameBootstrap>(out var boot) ? boot.Seed : 0;
            _message.text = $"지금 월드의 시드: {seed}";
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
        }

        void NewGame()
        {
            // A typed seed builds that world: reload straight into the game (no title).
            if (int.TryParse(_seed.text, out int seed) && seed != 0
                && !(Services.TryGet<GameBootstrap>(out var boot) && boot.Seed == seed))
            {
                GameSettings.PendingSeed = seed;
                GameSettings.ReloadScene(showTitle: false);
                return;
            }
            Hide();
        }

        void Hide()
        {
            if (!IsOpen) return;
            _root.gameObject.SetActive(false);
            MenuState.Pop();
            if (_holding && Services.TryGet<GameClock>(out var clock)) clock.Release();
            _holding = false;
        }
    }
}
