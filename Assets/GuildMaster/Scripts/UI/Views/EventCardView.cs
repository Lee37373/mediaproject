using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Incidents;
using GuildMaster.Social;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The event card window (F-13, user request 2026-10-11): the situation, two or three choices and what came of the
    /// answer. Opens by itself when a card is drawn (unless a menu or a dialogue already holds the game), and the game waits
    /// while it is open. [나중에] closes it and leaves the card waiting; the "다음 할 일" panel reopens it. Unanswered for
    /// <see cref="EventCards.ExpireHours"/> game hours, the default answer happens.
    /// 2026-11-05 (UI round, the team's "too much empty space"): a window as big as its content — the time left as a bar,
    /// who it is about as chips, the story without a box, each choice a full-width button carrying which way it moves
    /// things (<see cref="EventCards.Directions"/>, the direction only) and its odds or price, and the result with what it
    /// actually changed.
    /// </summary>
    public class EventCardView : UIPanel
    {
        class Driver : MonoBehaviour
        {
            public EventCardView Owner;
            void Update() { if (Owner != null) Owner.DriverTick(); }
        }

        public override string Title => "사건 카드";
        protected override float RefreshInterval => 0.25f;

        static readonly Color Card = new(0.17f, 0.15f, 0.21f, 1f);
        static readonly Color CardEdge = new(0.30f, 0.26f, 0.40f, 1f);
        static readonly Color ChipGood = new(0.14f, 0.25f, 0.16f, 1f), ChipGoodText = new(0.61f, 0.90f, 0.61f, 1f);
        static readonly Color ChipBad = new(0.29f, 0.14f, 0.19f, 1f), ChipBadText = new(1f, 0.70f, 0.67f, 1f);
        static readonly Color ChipMid = new(0.29f, 0.23f, 0.12f, 1f), ChipMidText = new(0.98f, 0.82f, 0.54f, 1f);
        static readonly Color Track = new(0.23f, 0.20f, 0.28f, 1f);

        Text _cardTitle, _timerText, _defaultText, _narration, _resultHead, _result, _footer;
        RectTransform _timerRow, _castRow;
        Image _timerFill;
        readonly List<(Image bg, Text text)> _castChips = new();
        Image _resultBox;
        RectTransform _resultChips;
        readonly List<(Image bg, Text text)> _changeChips = new();
        readonly List<ChoiceButton> _choices = new();
        Button _later, _ok;
        DrawnCard _card;
        bool _holdingClock;

        protected override void Build(RectTransform body)
        {
            var root = body.GetComponentInParent<Canvas>().rootCanvas.gameObject;
            root.AddComponent<Driver>().Owner = this;
            EventBus.Subscribe<EventCardDrawnEvent>(OnDrawn);

            UIBuilder.VLayout(body, 8, 4);
            _cardTitle = UIBuilder.Label(body, "", 22, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 32);

            // The time left: a bar running down, and what happens if nobody answers.
            _timerRow = UIBuilder.Row(body, 20, 10);
            _timerText = UIBuilder.Label(_timerRow, "", 13, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(80, 20);
            var holder = UIBuilder.Rect(_timerRow, "TimerHolder");
            holder.Size(-1, -1, 1);
            var track = UIBuilder.Panel(holder, "TimerTrack", Track, raycast: false);
            track.rectTransform.Anchor(new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0, 6));
            _timerFill = Fill(track.rectTransform, UITheme.Accent);
            _defaultText = UIBuilder.Label(_timerRow, "", 13, TextAnchor.MiddleRight, UITheme.TextMuted).Size(240, 20);

            // Who it is about (members, and the resident a choice touches).
            _castRow = UIBuilder.Row(body, 30, 8);
            for (int i = 0; i < 3; i++) _castChips.Add(Chip(_castRow, 13));
            UIBuilder.Rect(_castRow, "Spacer").Size(-1, -1, 1);

            _narration = UIBuilder.Label(body, "", 16, TextAnchor.UpperLeft);
            _narration.verticalOverflow = VerticalWrapMode.Overflow;

            for (int i = 0; i < 3; i++)
            {
                int index = i;
                _choices.Add(ChoiceButton.Create(body, () => Choose(index)));
            }

            // The result: a tinted box, a big ✔ 성공 / ✘ 실패 line (2026-11-01), the story of it and what it changed.
            _resultBox = UIBuilder.Panel(body, "Result", new Color(0f, 0f, 0f, 0f));
            var rv = UIBuilder.VLayout(_resultBox, 6, 0);
            rv.padding = new RectOffset(14, 14, 10, 10);
            _resultHead = UIBuilder.Label(_resultBox.transform, "", 20, TextAnchor.MiddleLeft, UITheme.Good, FontStyle.Bold).Size(-1, 26);
            _result = UIBuilder.Label(_resultBox.transform, "", 16, TextAnchor.UpperLeft);
            _result.verticalOverflow = VerticalWrapMode.Overflow;
            _resultChips = UIBuilder.Row(_resultBox.transform, 22, 6);
            for (int i = 0; i < 6; i++) _changeChips.Add(Chip(_resultChips, 12));
            UIBuilder.Rect(_resultChips, "Spacer").Size(-1, -1, 1);

            UIBuilder.Rect(body, "Push").Size(-1, 0, -1, 1);
            var actions = UIBuilder.Row(body, 40, 10);
            _footer = UIBuilder.Label(actions, "", 13, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 40, 1);
            _later = UIBuilder.Button(actions, "나중에", Close, 120, 38);
            _ok = UIBuilder.Button(actions, "확인", Close, 120, 38, new Color(0.25f, 0.45f, 0.3f));
        }

        void OnDestroy() => EventBus.Unsubscribe<EventCardDrawnEvent>(OnDrawn);

        void OnDrawn(EventCardDrawnEvent e)
        {
            // Not over the title or pause menu, and not over a talk that already holds the game: the guide panel
            // shows the waiting card instead.
            if (MenuState.IsOpen || (Services.TryGet<GameClock>(out var clock) && clock.IsHeld)) return;
            ShowCard(e.Card);
        }

        /// <summary>Opens the window on a card (the waiting one when null).</summary>
        public void ShowCard(DrawnCard card = null)
        {
            if (card == null && Services.TryGet<EventCardService>(out var service)) card = service.Pending;
            if (card == null) return;
            _card = card;
            if (!_holdingClock && Services.TryGet<GameClock>(out var clock)) { clock.Hold(); _holdingClock = true; }
            Open();
        }

        void DriverTick()
        {
            if (_holdingClock && !Visible)
            {
                if (Services.TryGet<GameClock>(out var clock)) clock.Release();
                _holdingClock = false;
            }
        }

        void Choose(int index)
        {
            if (_card == null || !Services.TryGet<EventCardService>(out var service)) return;
            service.Choose(_card, index);
            Refresh();
        }

        protected override void Refresh()
        {
            if (_card == null) return;
            Services.TryGet<EventCardService>(out var service);
            Services.TryGet<GameClock>(out var clock);
            _cardTitle.text = _card.Title;
            _narration.text = _card.Narration;
            bool open = !_card.Resolved;
            string nameA = _card.A?.Name, nameB = _card.B?.Name;

            // Time left.
            _timerRow.gameObject.SetActive(open && clock != null);
            if (open && clock != null)
            {
                float left = Mathf.Max(0f, EventCards.ExpireHours - (clock.TotalHours - _card.DrawnAt));
                _timerText.text = $"{Mathf.CeilToInt(left)}시간 남음";
                SetFill(_timerFill, left / EventCards.ExpireHours);
                _timerFill.color = left < 2f ? UITheme.Warn : UITheme.Accent;
                _defaultText.text = $"안 고르면 '{_card.Label(_card.Def.Choices.IndexOf(_card.Def.DefaultChoice))}'";
            }

            ShowCast();

            var choices = _card.Def.Choices;
            for (int i = 0; i < _choices.Count; i++)
            {
                bool exists = i < choices.Count;
                bool show = exists && (open || i == _card.Chosen);
                _choices[i].gameObject.SetActive(show);
                if (!show) continue;
                var c = choices[i];
                string why = open ? service?.CannotChoose(_card, i) : null;
                _choices[i].Bind(_card.Label(i), c, open, why, EventCards.ChanceOf(c, _card.A, _card.B),
                                 open ? EventCards.Directions(c, nameA, nameB, ResidentName) : null, c.Default, choices.Count - 1);
            }

            ShowResult(nameA, nameB);
            _ok.gameObject.SetActive(_card.Resolved);
            _later.gameObject.SetActive(!_card.Resolved);
            _footer.text = open ? "결과는 방향만 보여 줍니다 · ▲ 오름 ▼ 내림" : "";
            FitHeight();
        }

        const float MaxHeight = 540f, MinHeight = 260f;

        /// <summary>The window is as tall as what it shows (no empty band above the buttons once the card is
        /// answered); the top edge stays where it was.</summary>
        void FitHeight()
        {
            var frame = WindowFrame;
            if (frame == null || Body == null) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(Body);
            float want = Mathf.Clamp(Mathf.Ceil(LayoutUtility.GetPreferredHeight(Body)) + 56f, MinHeight, MaxHeight);
            float old = frame.sizeDelta.y;
            if (Mathf.Abs(want - old) < 1f) return;
            frame.sizeDelta = new Vector2(frame.sizeDelta.x, want);
            frame.anchoredPosition += new Vector2(0f, (old - want) / 2f);
        }

        /// <summary>Who the card is about: its members (class colour, level, stress, fatigue) and the resident a choice touches.</summary>
        void ShowCast()
        {
            int n = 0;
            void Member(CardMember? m)
            {
                if (!m.HasValue || n >= _castChips.Count) return;
                var v = m.Value;
                Color cls = UITheme.TextMuted;
                if (Services.TryGet<GuildRoster>(out var roster) && roster.Find(v.Id) is { Data: { } d })
                    cls = RosterView.ClassColor(d.cls);
                string clsName = Services.TryGet<GuildRoster>(out var r2) && r2.Find(v.Id) is { Data: { } d2 } ? AdventurerData.ClassLabel(d2.cls) + " " : "";
                SetChip(_castChips[n++], $"<b>{v.Name}</b> {UIBuilder.Colored($"{clsName}Lv.{v.Level}", cls)}  {UIBuilder.Colored($"스트레스 {Mathf.RoundToInt(v.Stress)} · 피로 {Mathf.RoundToInt(v.Fatigue)}", UITheme.TextMuted)}", Card, UITheme.Text);
            }
            Member(_card.A);
            Member(_card.B);
            // A resident one of the choices touches (Toby, Greta…).
            string residentId = null;
            foreach (var c in _card.Def.Choices)
            {
                residentId ??= c.Success?.ResidentId;
                residentId ??= c.Failure?.ResidentId;
            }
            if (!string.IsNullOrEmpty(residentId) && n < _castChips.Count && Services.TryGet<ResidentService>(out var residents)
                && residents.Find(residentId) is { } res)
                SetChip(_castChips[n++], $"<b>{res.Profile.Name}</b> {UIBuilder.Colored($"주민 · 호감 {res.Profile.GuildOpinion}", UITheme.TextMuted)}", Card, UITheme.Text);
            for (int i = 0; i < _castChips.Count; i++) _castChips[i].bg.gameObject.SetActive(i < n);
            _castRow.gameObject.SetActive(n > 0);
        }

        void ShowResult(string nameA, string nameB)
        {
            if (!_card.Resolved)
            {
                _resultBox.gameObject.SetActive(false);
                return;
            }
            _resultBox.gameObject.SetActive(true);
            var tone = _card.Tone;
            Color c = EventCards.IsGood(tone) ? UITheme.Good : EventCards.IsBad(tone) ? UITheme.Bad : UITheme.Text;
            string head = EventCards.Badge(tone);
            if (_card.Expired) head = (head.Length > 0 ? head + "  " : "") + UIBuilder.Colored("(시간이 지나 저절로 정해짐)", UITheme.Warn);
            _resultHead.gameObject.SetActive(head.Length > 0);
            _resultHead.text = head;
            _resultHead.color = c;
            _result.text = _card.ResultText;
            _result.color = tone == CardTone.Neutral ? UITheme.Text : Color.Lerp(c, Color.white, 0.35f);
            _resultBox.color = tone == CardTone.Neutral ? new Color(1f, 1f, 1f, 0.05f) : new Color(c.r, c.g, c.b, 0.16f);

            // What it changed, with the numbers now that it has happened.
            var changes = new List<(string text, int sign)>();
            if (_card.Chosen >= 0 && _card.Chosen < _card.Def.Choices.Count)
            {
                var choice = _card.Def.Choices[_card.Chosen];
                var effect = _card.Succeeded || choice.Failure == null ? choice.Success : choice.Failure;
                changes = EventCards.Changes(choice, effect, nameA, nameB, ResidentName);
            }
            for (int i = 0; i < _changeChips.Count; i++)
            {
                bool on = i < changes.Count;
                _changeChips[i].bg.gameObject.SetActive(on);
                if (on) SetSignChip(_changeChips[i], changes[i].text, changes[i].sign);
            }
            _resultChips.gameObject.SetActive(changes.Count > 0);
        }

        static string ResidentName(string id)
            => Services.TryGet<ResidentService>(out var r) && r.Find(id) is { } res ? res.Profile.Name : "주민";

        // ---------------- Little pieces ----------------

        static Image Fill(RectTransform track, Color color)
        {
            var fill = UIBuilder.Panel(track, "Fill", color, raycast: false);
            var rt = fill.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return fill;
        }

        static void SetFill(Image fill, float ratio)
        {
            var rt = fill.rectTransform;
            rt.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>A rounded chip as wide as its text.</summary>
        internal static (Image bg, Text text) Chip(Transform parent, int fontSize)
        {
            var bg = UIBuilder.Panel(parent, "Chip", Card, raycast: false);
            var h = UIBuilder.HLayout(bg, 0, 0);
            h.padding = new RectOffset(8, 8, 2, 2);
            h.childForceExpandWidth = false;
            var t = UIBuilder.Label(bg.transform, "", fontSize, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return (bg, t);
        }

        internal static void SetChip((Image bg, Text text) chip, string text, Color bg, Color fg)
        {
            chip.bg.color = bg;
            chip.text.text = text;
            chip.text.color = fg;
        }

        internal static void SetSignChip((Image bg, Text text) chip, string text, int sign)
        {
            if (sign > 0) SetChip(chip, text, ChipGood, ChipGoodText);
            else if (sign < 0) SetChip(chip, text, ChipBad, ChipBadText);
            else SetChip(chip, text, ChipMid, ChipMidText);
        }

        /// <summary>One choice as a full-width button: its label, which way it moves things, and its odds or price.</summary>
        class ChoiceButton : MonoBehaviour
        {
            Button _button;
            Image _edge;
            Text _label, _why, _odds, _price;
            RectTransform _chipRow, _right, _oddsBox;
            Image _oddsFill;
            readonly List<(Image bg, Text text)> _chips = new();

            public static ChoiceButton Create(Transform parent, UnityEngine.Events.UnityAction onClick)
            {
                var edge = UIBuilder.Panel(parent, "Choice", CardEdge);
                edge.Size(-1, 64);
                var me = edge.gameObject.AddComponent<ChoiceButton>();
                me._edge = edge;
                var face = UIBuilder.Panel(edge.transform, "Face", Card);
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
                for (int i = 0; i < EventCards.MaxChips; i++) me._chips.Add(Chip(me._chipRow, 12));
                UIBuilder.Rect(me._chipRow, "Spacer").Size(-1, -1, 1);
                me._why = UIBuilder.Label(left, "", 13, TextAnchor.MiddleLeft, UITheme.Bad).Size(-1, 18);

                me._right = UIBuilder.Rect(row, "Right");
                me._right.Size(110, -1);
                UIBuilder.VLayout(me._right, 4, 0).childAlignment = TextAnchor.MiddleRight;
                me._oddsBox = UIBuilder.Rect(me._right, "Odds");
                UIBuilder.VLayout(me._oddsBox, 4, 0).childAlignment = TextAnchor.MiddleRight;
                me._odds = UIBuilder.Label(me._oddsBox, "", 14, TextAnchor.MiddleRight, UITheme.Accent, FontStyle.Bold).Size(-1, 20);
                var track = UIBuilder.Panel(me._oddsBox, "OddsTrack", Track, raycast: false);
                track.Size(-1, 6);
                me._oddsFill = Fill(track.rectTransform, UITheme.Accent);
                me._price = UIBuilder.Label(me._right, "", 15, TextAnchor.MiddleRight, UITheme.Accent, FontStyle.Bold).Size(-1, 22);
                return me;
            }

            public void Bind(string label, CardChoice c, bool open, string why, float chance, List<(string text, int sign)> chips, bool isDefault, int others = 0)
            {
                _label.text = open ? label + (isDefault ? UIBuilder.Colored("  · 기본", UITheme.TextMuted) : "")
                                   : UIBuilder.Colored("고른 것  ", UITheme.TextMuted) + label;
                _button.interactable = open && why == null;
                _edge.color = open && isDefault ? new Color(0.36f, 0.31f, 0.48f, 1f) : CardEdge;

                int n = chips?.Count ?? 0;
                for (int i = 0; i < _chips.Count; i++)
                {
                    bool on = i < n;
                    _chips[i].bg.gameObject.SetActive(on);
                    if (on) SetSignChip(_chips[i], chips[i].text, chips[i].sign);
                }
                _chipRow.gameObject.SetActive(n > 0);
                _why.gameObject.SetActive(open && why != null);
                _why.text = why ?? "";

                bool rolled = c.Failure != null;
                _oddsBox.gameObject.SetActive(open && rolled);
                if (open && rolled)
                {
                    _odds.text = $"성공 {Mathf.RoundToInt(chance * 100f)}%";
                    SetFill(_oddsFill, chance);
                }
                bool hiddenNote = !open && others > 0;
                _price.gameObject.SetActive((open && c.Cost > 0) || hiddenNote);
                _price.text = open ? (c.Cost > 0 ? $"비용 {c.Cost}G" : "") : UIBuilder.Colored($"다른 {others}개 숨김", UITheme.TextMuted);
                _price.fontStyle = open ? FontStyle.Bold : FontStyle.Normal;
                _price.fontSize = open ? 15 : 13;
                _right.gameObject.SetActive((open && (rolled || c.Cost > 0)) || hiddenNote);
                ((RectTransform)transform).Size(-1, open ? 64 : 44);
            }
        }
    }
}
