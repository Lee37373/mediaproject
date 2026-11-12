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
    /// P4-135 (user decision 2026-11-08, KMS's event card redesign of 2026-11-05 moved here): a window as tall as what it
    /// shows; each card with its time left as a bar, who it is about as chips, the story without a box, and each choice a
    /// full-width button carrying which way it moves things as chips (the direction only — its price is in its label); the result with
    /// what it actually changed, with the numbers.
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

        const float MaxHeight = 820f, MinHeight = 300f, PastLine = 18f;

        Text _status, _empty, _past, _resultHead, _result, _footer;
        Image _resultBox;
        RectTransform _resultChips, _listContent;
        LayoutElement _listLayout;
        readonly List<(Image bg, Text text)> _changeChips = new();
        Button _close;
        readonly List<CardRow> _rows = new();
        bool _holdingClock, _wantsOpen;
        DilemmaResolvedEvent? _last;

        protected override void Build(RectTransform body)
        {
            var root = body.GetComponentInParent<Canvas>().rootCanvas.gameObject;
            root.AddComponent<Driver>().Owner = this;
            EventBus.Subscribe<DilemmaDealtEvent>(OnDealt);
            EventBus.Subscribe<DilemmaResolvedEvent>(OnResolved);

            UIBuilder.VLayout(body, 8, 4);
            _status = UIBuilder.Label(body, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 22);

            // What the last choice came to: a tinted box, a big ✔ 성공 / ✘ 실패 line, the story of it and what it changed.
            _resultBox = UIBuilder.Panel(body, "Result", new Color(0f, 0f, 0f, 0f));
            var rv = UIBuilder.VLayout(_resultBox, 6, 0);
            rv.padding = new RectOffset(14, 14, 10, 10);
            _resultHead = UIBuilder.Label(_resultBox.transform, "", 19, TextAnchor.MiddleLeft, UITheme.Good, FontStyle.Bold).Size(-1, 26);
            _result = UIBuilder.Label(_resultBox.transform, "", 15, TextAnchor.UpperLeft);
            _result.verticalOverflow = VerticalWrapMode.Overflow;
            _resultChips = UIBuilder.Row(_resultBox.transform, 22, 6);
            for (int i = 0; i < DilemmaChips.MaxChangeChips; i++) _changeChips.Add(UIChips.Chip(_resultChips, 12));
            UIBuilder.Rect(_resultChips, "Spacer").Size(-1, -1, 1);

            // The open cards: as tall as they are, scrolling only when the window is at its tallest.
            _listContent = UIBuilder.ScrollList(body, out var scroll, 10);
            _listLayout = scroll.GetComponent<RectTransform>().Size(-1, 200, -1, 1).GetComponent<LayoutElement>();
            _empty = UIBuilder.Label(_listContent, "", 15, TextAnchor.MiddleCenter, UITheme.TextMuted).Size(-1, 60);
            _empty.horizontalOverflow = HorizontalWrapMode.Wrap;

            UIBuilder.Label(body, "지난 결정", 15, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 22);
            _past = UIBuilder.Label(body, "", 13, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, PastLine);
            _past.horizontalOverflow = HorizontalWrapMode.Wrap;
            _past.verticalOverflow = VerticalWrapMode.Truncate;

            var actions = UIBuilder.Row(body, 38, 10);
            _footer = UIBuilder.Label(actions, "", 13, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 38, 1);
            _close = UIBuilder.Button(actions, "나중에", Close, 130, 36, UITheme.ButtonDanger);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<DilemmaDealtEvent>(OnDealt);
            EventBus.Unsubscribe<DilemmaResolvedEvent>(OnResolved);
            if (_holdingClock && Services.TryGet<GameClock>(out var clock)) clock.Release();
        }

        /// <summary>Tests and measuring runs turn it off in code; the player in the settings' events tab (P6-3).</summary>
        static bool OpensByItself => DilemmaService.AutoOpen && Session.GameSettings.DilemmaAutoOpen;

        void OnDealt(DilemmaDealtEvent e)
        {
            if (!OpensByItself) return;
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
            if (_wantsOpen && !Visible && OpensByItself && !Blocked())
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
            for (int i = 0; i < open.Count; i++) _rows[i].Bind(open[i], service, clock.TotalHours);
            _empty.gameObject.SetActive(open.Count == 0);
            _empty.transform.SetAsLastSibling();
            _empty.text = "지금 정할 일이 없습니다. 아침 9시와 저녁 5시에 새 일이 생길 수 있습니다.";
            _status.text = open.Count > 0
                ? $"정할 일 {open.Count}건 · {DilemmaRules.DeadlineHours:0}시간 안에 안 고르면 기본 선택으로 정해집니다."
                : "정할 일이 없습니다.";
            _close.SetLabel(open.Count > 0 ? "나중에" : "확인");
            _footer.text = open.Count > 0 ? "결과는 방향만 보여 줍니다 · ▲ 오름 ▼ 내림 · 창이 열려 있는 동안 게임이 멈춥니다 (J)" : "";
            ShowResult();

            var past = service.History.Reverse().Take(5).ToList();
            _past.text = past.Count == 0
                ? "아직 없습니다."
                : string.Join("\n", past.Select(r =>
                    $"{clock.AgoText(r.Hour)} · {UIBuilder.Colored(r.Title, UITheme.Accent)} → {r.Choice}{(r.ByDeadline ? UIBuilder.Colored(" (기한 지남)", UITheme.Warn) : "")}\n   {Tinted(r.Result, r.Tone)}"));
            _past.Size(-1, PastLine * Mathf.Max(1, past.Count * 2) + 4);
            FitHeight();
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
            _resultBox.color = e.Tone == DilemmaTone.Neutral ? new Color(1f, 1f, 1f, 0.05f) : new Color(c.r, c.g, c.b, 0.16f);
            // What it changed, with the numbers now that it has happened.
            int n = e.Changes?.Count ?? 0;
            for (int i = 0; i < _changeChips.Count; i++)
            {
                bool on = i < n;
                _changeChips[i].bg.gameObject.SetActive(on);
                if (on) UIChips.SetSignChip(_changeChips[i], e.Changes[i].Text, e.Changes[i].Sign);
            }
            _resultChips.gameObject.SetActive(n > 0);
        }

        /// <summary>
        /// The window is as tall as what it shows (KMS 2026-11-05: no empty band under one short card); the list of cards
        /// takes its own height, down to a few rows once the window is at its tallest. The top edge stays where it was.
        /// </summary>
        void FitHeight()
        {
            var frame = WindowFrame;
            if (frame == null || Body == null) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_listContent);
            float list = Mathf.Ceil(LayoutUtility.GetPreferredHeight(_listContent)) + 4f;
            _listLayout.preferredHeight = list;
            _listLayout.minHeight = Mathf.Min(list, 180f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(Body);
            float want = Mathf.Clamp(Mathf.Ceil(LayoutUtility.GetPreferredHeight(Body)) + 56f, MinHeight, MaxHeight);
            float old = frame.sizeDelta.y;
            if (Mathf.Abs(want - old) < 1f) return;
            frame.sizeDelta = new Vector2(frame.sizeDelta.x, want);
            frame.anchoredPosition += new Vector2(0f, (old - want) / 2f);
        }

        static Color ToneColor(DilemmaTone t) => DilemmaRules.IsGood(t) ? UITheme.Good : DilemmaRules.IsBad(t) ? UITheme.Bad : UITheme.Accent;

        static string Tinted(string text, DilemmaTone t) => t == DilemmaTone.Neutral ? text : UIBuilder.Colored(text, ToneColor(t));

        void Choose(Dilemma card, int choice)
        {
            if (card == null || !Services.TryGet<DilemmaService>(out var service)) return;
            service.Choose(card, choice);
            Refresh();
        }

        /// <summary>One open card: its title, time left, people, story and choices.</summary>
        class CardRow : MonoBehaviour
        {
            const int MaxChoices = 4, MaxCast = 2;
            Text _title, _timerText, _defaultText, _text;
            Image _timerFill;
            RectTransform _castRow;
            readonly List<(Image bg, Text text)> _cast = new();
            readonly ChoiceButton[] _choices = new ChoiceButton[MaxChoices];
            Dilemma _card;

            public static CardRow Create(Transform parent, DilemmaView owner)
            {
                var bg = UIBuilder.Panel(parent, "DilemmaCard", UITheme.RowBg);
                var row = bg.gameObject.AddComponent<CardRow>();
                var v = UIBuilder.VLayout(bg, 6, 0);
                v.padding = new RectOffset(14, 14, 10, 12);
                row._title = UIBuilder.Label(bg.transform, "", 18, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 26);

                // The time left: a bar running down, and what happens if nobody answers.
                var timer = UIBuilder.Row(bg.transform, 18, 10);
                row._timerText = UIBuilder.Label(timer, "", 13, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(84, 18);
                var holder = UIBuilder.Rect(timer, "TimerHolder");
                holder.Size(-1, -1, 1);
                var track = UIBuilder.Panel(holder, "TimerTrack", UIChips.Track, raycast: false);
                track.rectTransform.Anchor(new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0, 6));
                row._timerFill = UIChips.Fill(track.rectTransform, UITheme.Accent);
                row._defaultText = UIBuilder.Label(timer, "", 13, TextAnchor.MiddleRight, UITheme.TextMuted).Size(280, 18);

                // Who it is about.
                row._castRow = UIBuilder.Row(bg.transform, 26, 8);
                for (int i = 0; i < MaxCast; i++) row._cast.Add(UIChips.Chip(row._castRow, 13));
                UIBuilder.Rect(row._castRow, "Spacer").Size(-1, -1, 1);

                row._text = UIBuilder.Label(bg.transform, "", 15, TextAnchor.UpperLeft);
                row._text.verticalOverflow = VerticalWrapMode.Overflow;

                for (int i = 0; i < MaxChoices; i++)
                {
                    int index = i;
                    row._choices[i] = ChoiceButton.Create(bg.transform, () => owner.Choose(row._card, index));
                }
                return row;
            }

            public void Bind(Dilemma card, DilemmaService service, float now)
            {
                _card = card;
                _title.text = card.Title;
                float left = Mathf.Max(0f, card.DeadlineHour - now);
                _timerText.text = $"{Mathf.CeilToInt(left)}시간 남음";
                UIChips.SetFill(_timerFill, left / DilemmaRules.DeadlineHours);
                _timerFill.color = left < 6f ? UITheme.Warn : UITheme.Accent;
                _defaultText.text = $"안 고르면 '{card.Label(card.Def.Default)}'";
                ShowCast(card);
                _text.text = card.Text;
                for (int i = 0; i < MaxChoices; i++)
                {
                    bool has = i < card.Def.Choices.Length;
                    _choices[i].gameObject.SetActive(has);
                    if (!has) continue;
                    _choices[i].Bind(card.Label(i), DilemmaChips.Directions(card.Def.Choices[i].Hints), service.CannotChoose(card, i), i == card.Def.Default);
                }
            }

            /// <summary>The card's people as the guild knows them: class and level, stress and fatigue.</summary>
            void ShowCast(Dilemma card)
            {
                int n = 0;
                if (Services.TryGet<GuildRoster>(out var roster))
                    foreach (var id in card.Actors)
                    {
                        if (n >= _cast.Count) break;
                        var a = roster.Find(id);
                        if (a == null || a.Data == null) continue;
                        var d = a.Data;
                        string cls = UIBuilder.Colored($"{AdventurerData.ClassLabel(d.cls)} Lv.{d.level}", RosterView.ClassColor(d.cls));
                        string state = UIBuilder.Colored($"스트레스 {Mathf.RoundToInt(d.stress)} · 피로 {Mathf.RoundToInt(d.fatigue)}", UITheme.TextMuted);
                        UIChips.SetChip(_cast[n++], $"<b>{d.name}</b> {cls}  {state}", UIChips.Card, UITheme.Text);
                    }
                for (int i = 0; i < _cast.Count; i++) _cast[i].bg.gameObject.SetActive(i < n);
                _castRow.gameObject.SetActive(n > 0);
            }
        }

        /// <summary>One choice as a full-width button: its label (with its price, if any) and which way it moves things.</summary>
        class ChoiceButton : MonoBehaviour
        {
            Button _button;
            Image _edge;
            Text _label, _why;
            RectTransform _chipRow;
            readonly List<(Image bg, Text text)> _chips = new();

            public static ChoiceButton Create(Transform parent, UnityEngine.Events.UnityAction onClick)
            {
                var edge = UIBuilder.Panel(parent, "Choice", UIChips.CardEdge);
                edge.Size(-1, 62);
                var me = edge.gameObject.AddComponent<ChoiceButton>();
                me._edge = edge;
                var face = UIBuilder.Panel(edge.transform, "Face", UIChips.Card);
                face.rectTransform.Stretch(1, 1, 1, 1);
                var b = edge.gameObject.AddComponent<Button>();
                b.targetGraphic = face;
                var colors = b.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1.25f, 1.2f, 1.35f, 1f);
                colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                colors.disabledColor = new Color(0.75f, 0.75f, 0.75f, 0.7f);
                colors.colorMultiplier = 1.4f;
                b.colors = colors;
                b.onClick.AddListener(onClick);
                b.onClick.AddListener(GuildMaster.Audio.Sound.Click);
                me._button = b;

                var row = UIBuilder.Rect(face.transform, "Row");
                row.Stretch();
                var h = UIBuilder.HLayout(row, 12, 0);
                h.padding = new RectOffset(14, 14, 8, 8);
                h.childForceExpandHeight = true;

                var left = UIBuilder.Rect(row, "Left");
                left.Size(-1, -1, 1);
                var v = UIBuilder.VLayout(left, 4, 0);
                v.childAlignment = TextAnchor.MiddleLeft;
                me._label = UIBuilder.Label(left, "", 16, TextAnchor.MiddleLeft, UITheme.Text, FontStyle.Bold).Size(-1, 22);
                me._chipRow = UIBuilder.Row(left, 20, 5);
                for (int i = 0; i < DilemmaChips.MaxChoiceChips; i++) me._chips.Add(UIChips.Chip(me._chipRow, 12));
                UIBuilder.Rect(me._chipRow, "Spacer").Size(-1, -1, 1);
                me._why = UIBuilder.Label(left, "", 13, TextAnchor.MiddleLeft, UITheme.Bad).Size(-1, 18);
                return me;
            }

            public void Bind(string label, List<DilemmaChip> chips, string why, bool isDefault)
            {
                _label.text = label + (isDefault ? UIBuilder.Colored("  · 기본", UITheme.TextMuted) : "");
                _button.interactable = why == null;
                _edge.color = isDefault ? new Color(0.36f, 0.31f, 0.48f, 1f) : UIChips.CardEdge;
                int n = chips?.Count ?? 0;
                for (int i = 0; i < _chips.Count; i++)
                {
                    bool on = i < n;
                    _chips[i].bg.gameObject.SetActive(on);
                    if (on) UIChips.SetSignChip(_chips[i], chips[i].Text, chips[i].Sign);
                }
                _chipRow.gameObject.SetActive(n > 0);
                _why.gameObject.SetActive(why != null);
                _why.text = why ?? "";
                ((RectTransform)transform).Size(-1, why != null ? 82 : 62);
            }
        }
    }
}
