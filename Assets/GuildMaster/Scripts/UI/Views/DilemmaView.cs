using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Dilemmas;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Decision cards (P4-111c, H3 — user decisions 2026-10-30: a window of their own, the outcome shown only roughly): the
    /// open cards, each with its choices and what they roughly do, the hours left before the default is taken; below, the
    /// decisions taken lately and what came of them. Opened with J and from the "결정할 일" notification.
    /// P4-118 (user decision 2026-11-02, with KMS's event cards folded in): a dealt card opens the window by itself and the game
    /// waits while it is open (not over a menu or a talk that already holds the game — then it opens once they are done); what
    /// a choice came to shows at the top, green for a success or a good turn, red for a failure or a bad one.
    /// </summary>
    public class DilemmaView : UIPanel
    {
        /// <summary>Runs every frame even while the window is closed (the panel's own object is off then).</summary>
        class Driver : MonoBehaviour
        {
            public DilemmaView Owner;
            void Update() { if (Owner != null) Owner.DriverTick(); }
        }

        public override string Title => "결정";
        protected override float RefreshInterval => 0.25f;

        Text _status, _empty, _past, _resultHead, _result;
        Image _resultBox;
        Button _close;
        RectTransform _listContent;
        readonly List<CardRow> _rows = new();
        bool _holdingClock, _wantsOpen;
        DilemmaResolvedEvent? _last;

        protected override void Build(RectTransform body)
        {
            var root = body.GetComponentInParent<Canvas>().rootCanvas.gameObject;
            root.AddComponent<Driver>().Owner = this;
            EventBus.Subscribe<DilemmaDealtEvent>(OnDealt);
            EventBus.Subscribe<DilemmaResolvedEvent>(OnResolved);

            UIBuilder.VLayout(body, 8, 0);
            _status = UIBuilder.Label(body, "", 15, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 26);

            // What the last choice came to: a tinted box with a big ✔ 성공 / ✘ 실패 line (from the event cards).
            _resultBox = UIBuilder.Panel(body, "Result", new Color(0f, 0f, 0f, 0f));
            _resultBox.Size(-1, 92);
            _resultHead = UIBuilder.Label(_resultBox.transform, "", 20, TextAnchor.UpperLeft, UITheme.Good, FontStyle.Bold);
            _resultHead.rectTransform.Anchor(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(-24, 28));
            _result = UIBuilder.Label(_resultBox.transform, "", 15, TextAnchor.UpperLeft);
            _result.horizontalOverflow = HorizontalWrapMode.Wrap;
            _result.verticalOverflow = VerticalWrapMode.Truncate;

            _listContent = UIBuilder.ScrollList(body, out var scroll);
            scroll.GetComponent<RectTransform>().Size(-1, -1, -1, 1);
            _empty = UIBuilder.Label(_listContent, "", 15, TextAnchor.MiddleCenter, UITheme.TextMuted).Size(-1, 70);
            _empty.horizontalOverflow = HorizontalWrapMode.Wrap;
            UIBuilder.Label(body, "지난 결정", 15, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 24);
            _past = UIBuilder.Label(body, "", 13, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 150);
            _past.horizontalOverflow = HorizontalWrapMode.Wrap;
            _past.verticalOverflow = VerticalWrapMode.Truncate;
            var actions = UIBuilder.Row(body, 36, 10);
            _close = UIBuilder.Button(actions, "나중에", Close, 140, 34, UITheme.ButtonDanger);
            UIBuilder.Label(actions, "창이 열려 있는 동안 게임이 멈춥니다. 닫아도 카드는 기한까지 남습니다(J).", 13, TextAnchor.MiddleLeft, UITheme.TextMuted)
                .Size(-1, -1, 1);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<DilemmaDealtEvent>(OnDealt);
            EventBus.Unsubscribe<DilemmaResolvedEvent>(OnResolved);
            if (_holdingClock && Services.TryGet<GameClock>(out var clock)) clock.Release();
        }

        void OnDealt(DilemmaDealtEvent e)
        {
            if (!DilemmaService.AutoOpen) return;
            if (Blocked()) _wantsOpen = true;
            else Open();
        }

        void OnResolved(DilemmaResolvedEvent e)
        {
            // Shown when the guild master chose it here; one decided by its deadline is told by the notification.
            if (Visible && !e.ByDeadline) _last = e;
        }

        /// <summary>A menu is open or a talk holds the game: the card waits for them (the window does not open over them).</summary>
        bool Blocked() => MenuState.IsOpen || (Services.TryGet<GameClock>(out var clock) && clock.IsHeld && !_holdingClock);

        protected override void OnOpened() => _last = null;

        void DriverTick()
        {
            if (_wantsOpen && !Visible && DilemmaService.AutoOpen && !Blocked())
            {
                _wantsOpen = false;
                if (Services.TryGet<DilemmaService>(out var service) && service.Open.Count > 0) Open();
            }
            if (Visible == _holdingClock || !Services.TryGet<GameClock>(out var clock)) return;
            if (Visible) clock.Hold();
            else clock.Release();
            _holdingClock = Visible;
        }

        protected override void Refresh()
        {
            if (!Services.TryGet<DilemmaService>(out var service) || !Services.TryGet<GameClock>(out var clock))
            {
                _status.text = "";
                _empty.text = "결정 카드를 쓸 수 없습니다.";
                return;
            }
            var open = service.Open;
            EnsureRows(_listContent, _rows, open.Count, parent => CardRow.Create(parent, this));
            for (int i = 0; i < open.Count; i++) _rows[i].Bind(open[i], i, service, clock.TotalHours);
            _empty.gameObject.SetActive(open.Count == 0);
            _empty.transform.SetAsLastSibling();
            _empty.text = "지금 정할 일이 없습니다. 아침 9시와 저녁 5시에 새 일이 생길 수 있습니다.";
            _status.text = $"정할 일 {open.Count}건 · {DilemmaRules.DeadlineHours:0}시간 안에 안 고르면 기본 선택으로 정해집니다.";
            _close.SetLabel(open.Count > 0 ? "나중에" : "확인");
            ShowResult();

            var past = service.History.Reverse().Take(6).Select(r =>
                $"{clock.AgoText(r.Hour)} · {UIBuilder.Colored(r.Title, UITheme.Accent)} → {r.Choice}{(r.ByDeadline ? UIBuilder.Colored(" (기한 지남)", UITheme.Warn) : "")}\n   {Tinted(r.Result, r.Tone)}");
            _past.text = service.History.Count == 0 ? "아직 없습니다." : string.Join("\n", past);
        }

        void ShowResult()
        {
            _resultBox.gameObject.SetActive(_last.HasValue);
            if (!_last.HasValue) return;
            var e = _last.Value;
            var c = ToneColor(e.Tone);
            string badge = DilemmaRules.Badge(e.Tone);
            _resultHead.text = $"{(badge.Length > 0 ? badge + "  " : "")}{e.Card.Title} → {e.Card.Label(e.Choice)}";
            _resultHead.color = c;
            _result.text = e.Result;
            _result.color = e.Tone == DilemmaTone.Neutral ? UITheme.Text : Color.Lerp(c, Color.white, 0.35f);
            _result.rectTransform.Stretch(12, 12, 36, 6);
            _resultBox.color = e.Tone == DilemmaTone.Neutral ? new Color(1f, 1f, 1f, 0.05f) : new Color(c.r, c.g, c.b, 0.16f);
        }

        static Color ToneColor(DilemmaTone t) => DilemmaRules.IsGood(t) ? UITheme.Good : DilemmaRules.IsBad(t) ? UITheme.Bad : UITheme.Accent;

        static string Tinted(string text, DilemmaTone t) => t == DilemmaTone.Neutral ? text : UIBuilder.Colored(text, ToneColor(t));

        void Choose(Dilemma card, int choice)
        {
            if (!Services.TryGet<DilemmaService>(out var service)) return;
            service.Choose(card, choice);
            Refresh();
        }

        /// <summary>The card's people as the guild knows them (who is the stronger in a duel, who is worn out).</summary>
        static string CastLine(Dilemma card)
        {
            if (card.Actors.Count == 0 || !Services.TryGet<GuildRoster>(out var roster)) return "";
            return string.Join("   ", card.Actors.Select(id => roster.Find(id)).Where(a => a != null && a.Data != null)
                .Select(a => $"{a.Data.name} Lv.{a.Data.level} · 스트레스 {Mathf.RoundToInt(a.Data.stress)} · 피로 {Mathf.RoundToInt(a.Data.fatigue)}"));
        }

        /// <summary>One open card: its story, its people and its choices.</summary>
        class CardRow : MonoBehaviour
        {
            const int MaxChoices = 4;
            Image _bg;
            Text _title, _text, _cast;
            readonly Button[] _buttons = new Button[MaxChoices];
            readonly Text[] _hints = new Text[MaxChoices];
            Dilemma _card;
            DilemmaView _owner;

            public static CardRow Create(Transform parent, DilemmaView owner)
            {
                var bg = UIBuilder.Panel(parent, "DilemmaCard", UITheme.RowBg);
                bg.Size(-1, 276);
                var row = bg.gameObject.AddComponent<CardRow>();
                row._bg = bg;
                row._owner = owner;
                var v = UIBuilder.VLayout(bg, 4, 0);
                v.padding = new RectOffset(12, 12, 8, 8);
                row._title = UIBuilder.Label(bg.transform, "", 17).Size(-1, 26);
                row._text = UIBuilder.Label(bg.transform, "", 14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 48);
                row._text.horizontalOverflow = HorizontalWrapMode.Wrap;
                row._text.verticalOverflow = VerticalWrapMode.Truncate;
                row._cast = UIBuilder.Label(bg.transform, "", 13, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 20);
                for (int i = 0; i < MaxChoices; i++)
                {
                    int index = i;
                    var line = UIBuilder.Row(bg.transform, 36, 10);
                    row._buttons[i] = UIBuilder.Button(line, "", () => row._owner.Choose(row._card, index), 340, 34, new Color(0.25f, 0.35f, 0.5f), 14);
                    row._hints[i] = UIBuilder.Label(line, "", 14, TextAnchor.MiddleLeft).Size(-1, 34, 1);
                }
                return row;
            }

            public void Bind(Dilemma card, int index, DilemmaService service, float now)
            {
                _card = card;
                _bg.color = index % 2 == 0 ? UITheme.RowBg : UITheme.RowBgAlt;
                string cast = CastLine(card);
                _cast.gameObject.SetActive(cast.Length > 0);
                _cast.text = cast;
                _bg.Size(-1, 96 + (cast.Length > 0 ? 24 : 0) + 40 * card.Def.Choices.Length); // as tall as its choices
                float left = Mathf.Max(0f, card.DeadlineHour - now);
                _title.text = $"{card.Title}  {UIBuilder.Colored($"{left:0}시간 남음 · 기본: {card.Label(card.Def.Default)}", left < 6f ? UITheme.Warn : UITheme.TextMuted)}";
                _text.text = card.Text;
                for (int i = 0; i < MaxChoices; i++)
                {
                    bool has = i < card.Def.Choices.Length;
                    _buttons[i].transform.parent.gameObject.SetActive(has);
                    if (!has) continue;
                    string why = service.CannotChoose(card, i);
                    _buttons[i].GetComponentInChildren<Text>().text = card.Label(i);
                    _buttons[i].interactable = why == null;
                    _hints[i].text = why == null ? card.Hints(i) : UIBuilder.Colored(why, UITheme.Bad);
                }
            }
        }
    }
}
