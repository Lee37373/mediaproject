using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Economy;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The odd-jobs minigames (일손 돕기, user decision 2026-10-29): 한스's quenching (timing), 마리's herb sorting (pairs)
    /// and 엘리's kneading (rhythm). Opened from the household window (살림). The game is paused while it is open and the
    /// minigames run on real time. Closing early still pays for what was done; each resident once a day.
    /// </summary>
    public class ChoreView : UIPanel
    {
        class Driver : MonoBehaviour
        {
            public ChoreView Owner;
            void Update() { if (Owner != null) Owner.DriverTick(); }
        }

        public override string Title => "일손 돕기";
        protected override float RefreshInterval => 0.1f;

        Text _header, _howTo, _status, _result;
        Button _action, _close;
        RectTransform _forgeRoot, _herbRoot, _doughRoot;
        // Forge
        RectTransform _zone, _sweet, _needle;
        // Herbs
        readonly List<Button> _cards = new();
        // Dough
        RectTransform _lane;
        readonly List<Image> _notes = new();

        ChoreKind _kind;
        ForgeGame _forge;
        HerbGame _herbs;
        DoughGame _dough;
        float _missShownAt = -1f;
        bool _running, _paid, _holdingClock;
        string _flash = "";
        float _flashUntil;

        protected override void Build(RectTransform body)
        {
            var root = body.GetComponentInParent<Canvas>().rootCanvas.gameObject;
            root.AddComponent<Driver>().Owner = this;

            UIBuilder.VLayout(body, 8, 0);
            _header = UIBuilder.Label(body, "", 19, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 28);
            _howTo = UIBuilder.Label(body, "", 14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 40);
            _howTo.verticalOverflow = VerticalWrapMode.Overflow;

            var area = UIBuilder.Panel(body, "Area", UITheme.PanelBgLight);
            area.Size(-1, 230);
            var areaRt = area.rectTransform;

            _forgeRoot = UIBuilder.Rect(areaRt, "Forge").Stretch();
            var bar = UIBuilder.Panel(_forgeRoot, "Bar", new Color(0.12f, 0.12f, 0.14f), false).rectTransform;
            Span(bar, 0.05f, 0.38f, 0.95f, 0.62f);
            _zone = UIBuilder.Panel(bar, "Zone", new Color(0.25f, 0.55f, 0.3f), false).rectTransform;
            _sweet = UIBuilder.Panel(bar, "Sweet", new Color(0.45f, 0.85f, 0.45f), false).rectTransform;
            _needle = UIBuilder.Panel(bar, "Needle", new Color(1f, 0.85f, 0.4f), false).rectTransform;

            _herbRoot = UIBuilder.Rect(areaRt, "Herbs").Stretch(10, 10, 10, 10);
            var grid = _herbRoot.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(118, 96);
            grid.spacing = new Vector2(8, 10);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 6;
            grid.childAlignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < HerbGame.Pairs * 2; i++)
            {
                int slot = i;
                _cards.Add(UIBuilder.Button(_herbRoot, "?", () => PickHerb(slot), 118, 96, null, 18));
            }

            _doughRoot = UIBuilder.Rect(areaRt, "Dough").Stretch();
            _lane = UIBuilder.Panel(_doughRoot, "Lane", new Color(0.12f, 0.12f, 0.14f), false).rectTransform;
            Span(_lane, 0.03f, 0.35f, 0.97f, 0.65f);
            var hit = UIBuilder.Panel(_lane, "HitLine", new Color(1f, 0.85f, 0.4f), false).rectTransform;
            Span(hit, HitX - 0.004f, -0.25f, HitX + 0.004f, 1.25f);
            for (int i = 0; i < DoughGame.Beats; i++)
            {
                var n = UIBuilder.Panel(_lane, "Note", new Color(0.95f, 0.88f, 0.7f), false);
                n.gameObject.SetActive(false);
                _notes.Add(n);
            }

            _status = UIBuilder.Label(body, "", 16).Size(-1, 26);
            _result = UIBuilder.Label(body, "", 15, TextAnchor.UpperLeft).Size(-1, 52);
            _result.verticalOverflow = VerticalWrapMode.Overflow;

            var row = UIBuilder.Row(body, 40, 10);
            _action = UIBuilder.Button(row, "두드리기 (F)", Action, 200, 38, UITheme.Button, 16);
            _close = UIBuilder.Button(row, "그만두기", Close, 140, 38, UITheme.ButtonDanger, 16);
        }

        const float HitX = 0.1f;

        static void Span(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>Starts a job (the household window checks <see cref="ChoreService.CanDo"/> first).</summary>
        public void Show(ChoreKind kind)
        {
            if (!Services.TryGet<ChoreService>(out var service) || !Services.TryGet<GameClock>(out var clock)) return;
            EndRun();
            _kind = kind;
            _forge = kind == ChoreKind.Forge ? new ForgeGame(GameRandom.Shared) : null;
            _herbs = kind == ChoreKind.Herbs ? new HerbGame(GameRandom.Shared) : null;
            _dough = kind == ChoreKind.Dough ? new DoughGame(GameRandom.Shared) : null;
            _forgeRoot.gameObject.SetActive(_forge != null);
            _herbRoot.gameObject.SetActive(_herbs != null);
            _doughRoot.gameObject.SetActive(_dough != null);
            _action.gameObject.SetActive(kind != ChoreKind.Herbs);
            _action.SetLabel(kind == ChoreKind.Forge ? "두드리기 (F)" : "반죽 (F)");
            _close.SetLabel("그만두기");
            _header.text = $"{ChoreRules.Resident(kind)} — {ChoreRules.Title(kind)}";
            _howTo.text = ChoreRules.HowTo(kind);
            _result.text = "";
            _flash = "";
            _missShownAt = -1f;
            service.Begin(kind, clock.TotalHours);
            _running = true;
            _paid = false;
            if (!_holdingClock) { clock.Hold(); _holdingClock = true; }
            Open();
            Refresh();
        }

        void DriverTick()
        {
            if (!Visible)
            {
                if (_running || _holdingClock) EndRun();
                return;
            }
            if (!_running) return;
            float dt = Time.unscaledDeltaTime;
            var kb = Keyboard.current;
            bool press = kb != null && kb.fKey.wasPressedThisFrame;
            if (_forge != null)
            {
                _forge.Tick(dt);
                if (press) Action();
            }
            else if (_herbs != null)
            {
                _herbs.Tick(dt);
                if (_herbs.Second != null && _missShownAt >= 0f && Time.unscaledTime - _missShownAt > 0.7f) { _herbs.HideMiss(); _missShownAt = -1f; }
            }
            else if (_dough != null)
            {
                _dough.Tick(dt);
                if (press) Action();
            }
            DrawGame();
            if (Over) Pay();
        }

        bool Over => _forge?.Over ?? _herbs?.Over ?? _dough?.Over ?? true;
        float Score => _forge?.Score ?? _herbs?.Score ?? _dough?.Score ?? 0f;

        void Action()
        {
            if (!_running) return;
            if (_forge != null)
            {
                float q = _forge.Strike();
                Flash(q >= 1f ? UIBuilder.Colored("쨍! 완벽", UITheme.Good) : q >= 0.6f ? UIBuilder.Colored("좋음", UITheme.Good)
                    : q > 0f ? UIBuilder.Colored("조금 빗나감", UITheme.Warn) : UIBuilder.Colored("헛침", UITheme.Bad));
            }
            else if (_dough != null)
            {
                float q = _dough.Press();
                Flash(q >= 1f ? UIBuilder.Colored("완벽", UITheme.Good) : q >= 0.6f ? UIBuilder.Colored("좋음", UITheme.Good)
                    : q > 0f ? UIBuilder.Colored("아슬아슬", UITheme.Warn) : UIBuilder.Colored("헛손질", UITheme.Bad));
            }
            Refresh();
        }

        void PickHerb(int slot)
        {
            if (!_running || _herbs == null) return;
            var r = _herbs.Pick(slot);
            if (r == HerbGame.Flip.Miss) { _missShownAt = Time.unscaledTime; Flash(UIBuilder.Colored("다른 약초예요", UITheme.Warn)); }
            else if (r == HerbGame.Flip.Match) Flash(UIBuilder.Colored("짝을 찾았다!", UITheme.Good));
            Refresh();
        }

        void Flash(string text)
        {
            _flash = text;
            _flashUntil = Time.unscaledTime + 0.8f;
        }

        void Pay()
        {
            if (_paid || !Services.TryGet<ChoreService>(out var service)) return;
            _paid = true;
            _running = false;
            float score = Score;
            var (pay, opinion) = service.Complete(_kind, score);
            _result.text = $"{ChoreRules.Grade(score)} ({score * 100f:0}점) · " + UIBuilder.Colored($"+{pay}G", UITheme.Good)
                           + (opinion > 0 ? $" · {ChoreRules.Resident(_kind)}의 길드 평판 +{opinion}" : "") + "\n"
                           + $"{ChoreRules.Resident(_kind)}: \"{ChoreRules.Thanks(_kind, score)}\"";
            _action.gameObject.SetActive(false);
            _close.SetLabel("닫기");
            Refresh();
        }

        /// <summary>Closing mid-game pays for what was done so far.</summary>
        void EndRun()
        {
            if (_running) Pay();
            _running = false;
            if (_holdingClock && Services.TryGet<GameClock>(out var clock)) clock.Release();
            _holdingClock = false;
        }

        void DrawGame()
        {
            if (_forge != null)
            {
                float c = _forge.ZoneCenter, w = _forge.ZoneWidth;
                Span(_zone, Mathf.Clamp01(c - w), 0f, Mathf.Clamp01(c + w), 1f);
                Span(_sweet, Mathf.Clamp01(c - w * 0.25f), 0f, Mathf.Clamp01(c + w * 0.25f), 1f);
                float n = _forge.Needle;
                Span(_needle, n - 0.005f, -0.3f, n + 0.005f, 1.3f);
            }
            else if (_dough != null)
            {
                var notes = _dough.Notes;
                for (int i = 0; i < _notes.Count; i++)
                {
                    var img = _notes[i];
                    bool show = i < notes.Count && float.IsNaN(_dough.Result(i));
                    float ahead = show ? notes[i] - _dough.Time : 0f;
                    show &= ahead <= DoughGame.Lead && ahead >= -DoughGame.Window;
                    img.gameObject.SetActive(show);
                    if (!show) continue;
                    float x = HitX + ahead / DoughGame.Lead * (1f - HitX - 0.03f);
                    Span(img.rectTransform, x - 0.022f, 0.1f, x + 0.022f, 0.9f);
                }
            }
        }

        protected override void Refresh()
        {
            if (_forge == null && _herbs == null && _dough == null) return;
            if (_herbs != null)
                for (int i = 0; i < _cards.Count; i++)
                {
                    bool up = _herbs.IsFaceUp(i);
                    _cards[i].SetLabel(up ? _herbs.HerbAt(i) : "?");
                    _cards[i].interactable = _running && !_herbs.IsMatched(i);
                    _cards[i].image.color = _herbs.IsMatched(i) ? new Color(0.35f, 0.6f, 0.35f) : up ? new Color(0.55f, 0.5f, 0.35f) : Color.white;
                }

            string progress = _forge != null ? $"{_forge.Hits.Count}/{ForgeGame.Strikes}번째"
                : _herbs != null ? $"찾은 짝 {_herbs.Found}/{HerbGame.Pairs} · 틀림 {_herbs.Misses} · 남은 시간 {Mathf.CeilToInt(_herbs.TimeLeft)}초"
                : $"반죽 {_dough.Judged}/{_dough.Notes.Count} · 헛손질 {_dough.Strays}";
            string flash = Time.unscaledTime < _flashUntil ? "   " + _flash : "";
            _status.text = (_running ? progress : "끝!") + flash;
        }
    }
}
