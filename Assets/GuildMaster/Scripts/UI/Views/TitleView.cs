using System.Collections.Generic;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Session;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Title screen (P4-10), shown over the freshly generated world while the clock is held.
    /// "새 게임" starts this world, or reloads with the typed seed; "불러오기" opens the saved games
    /// (P5-4); settings; quit. Skipped when the scene was loaded to start a game directly.
    /// 2026-11-14 (user: "게임 메인화면 + 로고, 우리 게임 분위기와 어울리게"): the town itself is the picture — the view
    /// slides so the town sits right of centre, a dark frame closes in from the left and the edges, the HUD is faded out,
    /// and on the left the wooden sign logo hangs on its ropes above the menu. Starting a game glides the view back to the
    /// town and fades the HUD back in.
    /// Later that day: behind it all, recorded scenes of play instead of the still town when there are any
    /// (<see cref="TitleBackdrop"/>); the live town is the fallback.
    /// Without the title art (Resources/UI/Hud/title_*) the plain dark screen with a text title stays.
    /// </summary>
    public class TitleView : MonoBehaviour
    {
        /// <summary>The menu column's centre from the left edge; the sign (2x art, ropes to the top edge) and the menu card.</summary>
        const float ColumnX = 400f, LogoWidth = 640f, LogoHeight = 428f, CardWidth = 400f;
        /// <summary>How far right of centre the town sits while the title is up, as a share of the view's width.</summary>
        const float TownShift = 0.13f;

        RectTransform _root, _logo, _card;
        InputField _seed;
        Text _message;
        SettingsView _settings;
        SaveLoadView _saves;
        Button _load;
        bool _holding, _art;
        TitleBackdrop _backdrop;
        CameraController _cam;
        Vector2 _camHome;
        bool _camMoved;
        Transform _canvas;
        /// <summary>The HUD faded out under the title, with the alpha each part had.</summary>
        readonly List<(CanvasGroup group, float alpha)> _hud = new();

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        public void Build(RectTransform canvas, SettingsView settings, SaveLoadView saves)
        {
            _settings = settings;
            _saves = saves;
            _canvas = canvas;
            _art = HudStyle.Get("title_vignette") != null && HudStyle.Get("title_logo") != null;
            // The whole screen takes the clicks; with the art the dark comes from the frame picture instead.
            var dim = UIBuilder.Panel(canvas, "Title", _art ? new Color(0.07f, 0.05f, 0.04f, 0f) : new Color(0.05f, 0.04f, 0.08f, 0.82f));
            _root = dim.rectTransform;
            _root.Stretch();
            if (_art) _backdrop = TitleBackdrop.Create(_root); // scenes of play, under the frame
            if (_art) HudStyle.NewImage(_root, "title_vignette", "Frame").rectTransform.Stretch();

            var topLeft = new Vector2(0f, 1f);
            var topCentre = new Vector2(0.5f, 1f);
            if (_art)
            {
                var logo = HudStyle.NewImage(_root, "title_logo", "Logo");
                logo.preserveAspect = true;
                _logo = logo.rectTransform;
                _logo.Anchor(topLeft, topLeft, topCentre, new Vector2(ColumnX, 0f), new Vector2(LogoWidth, LogoHeight));
            }
            else
            {
                var name = UIBuilder.Label(_root, "길드 마스터 타이쿤", 46, TextAnchor.MiddleCenter, UITheme.Accent, FontStyle.Bold);
                name.rectTransform.Anchor(topLeft, topLeft, topCentre, new Vector2(ColumnX, -(LogoHeight - 90f)), new Vector2(LogoWidth, 70f));
            }
            // The menu on a card under the sign, as tall as what it holds (2026-11-14: no subtitle line, user's call).
            var card = UIBuilder.Panel(_root, "Menu", UITheme.PanelBg);
            HudStyle.Card(card);
            card.gameObject.AddComponent<CanvasGroup>();
            _card = card.rectTransform;
            _card.Anchor(topLeft, topLeft, topCentre, new Vector2(ColumnX, -(LogoHeight + 26f)), new Vector2(CardWidth, 0f));
            UIBuilder.VLayout(_card, 10, 22);
            UIBuilder.FitContent(_card);

            UIBuilder.Button(_card, "새 게임", NewGame, -1, 52, new Color(0.25f, 0.45f, 0.3f), 20);
            _load = UIBuilder.Button(_card, "불러오기", () => _saves.Show(canSave: false), -1, 52, null, 20);
            _load.GetComponent<RectTransform>().Tooltip(() => _load.interactable ? "저장한 게임을 이어서 합니다." : "저장된 게임이 없습니다.");
            UIBuilder.Button(_card, "설정", () => _settings.Show(), -1, 52, null, 20);
            UIBuilder.Button(_card, "종료", GameSettings.Quit, -1, 52, UITheme.ButtonDanger, 20);

            var seedRow = UIBuilder.Row(_card, 36);
            UIBuilder.Label(seedRow, "월드 시드", 15, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(78);
            _seed = UIBuilder.Input(seedRow, "비우면 지금 보이는 월드", InputField.ContentType.IntegerNumber, -1, 34);
            _seed.GetComponent<RectTransform>().Size(-1, 34, 1);

            // Small print in the bottom-left corner: the version and this world's seed.
            _message = UIBuilder.Label(_root, "", 14, TextAnchor.LowerLeft, new Color(0.78f, 0.74f, 0.68f, 1f));
            _message.rectTransform.Anchor(Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(20f, 14f), new Vector2(700f, 22f));
            _message.raycastTarget = false;
            // P5-5: installed under a non-ASCII path, the local model may not load — say so before the game starts.
            if (LLM.LlamaEnvironment.InstallPathNotAscii)
            {
                var warn = UIBuilder.Label(_root, LLM.LlamaEnvironment.InstallPathWarning, 14, TextAnchor.LowerLeft, UITheme.Warn);
                warn.rectTransform.Anchor(Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(20f, 42f), new Vector2(760f, 60f));
                warn.verticalOverflow = VerticalWrapMode.Overflow;
                warn.raycastTarget = false;
            }
            _root.gameObject.SetActive(false);
        }

        /// <summary>Shows the title if this scene load asked for it (first launch, "타이틀로").</summary>
        public void ShowIfRequested()
        {
            if (!GameSettings.ShowTitle) return;
            MenuState.Push();
            if (Services.TryGet<GameClock>(out var clock)) { clock.Hold(); _holding = true; }
            int seed = Services.TryGet<GameBootstrap>(out var boot) ? boot.Seed : 0;
            _message.text = $"v{Application.version}  ·  지금 월드의 시드: {seed}";
            _load.interactable = SaveLoadView.AnyLoadable();
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
            HideHud();
            FrameTown();
            if (_logo != null) UIMotion.HangSign(_logo, new Vector2(ColumnX, 0f), LogoHeight + 40f);
            UIMotion.PopIn(_card, 0.9f, 0.35f);
        }

        /// <summary>The town a little right of centre, clear of the menu (the view goes back when the game starts).</summary>
        void FrameTown()
        {
            if (_backdrop != null) return; // the recorded scenes cover the town
            if (!_art || !Application.isPlaying || !Services.TryGet<CameraController>(out _cam) || _cam == null || _cam.Camera == null) return;
            _camHome = _cam.transform.position;
            float width = _cam.Camera.orthographicSize * 2f * _cam.Camera.aspect;
            _cam.JumpTo(_camHome - new Vector2(width * TownShift, 0f), byPlayer: false);
            _camMoved = true;
        }

        /// <summary>
        /// Everything showing on the HUD canvas goes clear (the menus that open over the title are off now and stay as
        /// they are); the world's own labels and speech bubbles are on another canvas and stay.
        /// </summary>
        void HideHud()
        {
            _hud.Clear();
            if (!_art || _canvas == null) return;
            for (int i = 0; i < _canvas.childCount; i++)
            {
                var child = _canvas.GetChild(i);
                if (child == _root || !child.gameObject.activeSelf) continue;
                if (!child.TryGetComponent<CanvasGroup>(out var group)) group = child.gameObject.AddComponent<CanvasGroup>();
                if (group.alpha <= 0f) continue;
                _hud.Add((group, group.alpha));
                group.alpha = 0f;
            }
        }

        void ShowHud()
        {
            if (_hud.Count == 0 || _canvas == null) return;
            var parts = _hud.ToArray();
            _hud.Clear();
            UIMotion.Run(_canvas.gameObject, 0.6f, t =>
            {
                foreach (var (group, alpha) in parts) if (group != null) group.alpha = alpha * t;
            });
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
            ShowHud();
            if (_camMoved && _cam != null)
            {
                var cam = _cam;
                Vector2 from = cam.transform.position, home = _camHome;
                UIMotion.Run(cam.gameObject, 0.9f, t => { if (cam != null) cam.JumpTo(Vector2.Lerp(from, home, t), byPlayer: false); });
            }
            _camMoved = false;
        }
    }
}
