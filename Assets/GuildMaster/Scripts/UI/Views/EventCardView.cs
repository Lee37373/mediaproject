using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Incidents;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The event card window (F-13, user request 2026-10-11): the situation, two or three choices with their cost and
    /// odds, and what came of the answer. Opens by itself when a card is drawn (unless a menu or a dialogue already
    /// holds the game), and the game waits while it is open. [나중에] closes it and leaves the card waiting; the
    /// "다음 할 일" panel reopens it. Unanswered for <see cref="EventCards.ExpireHours"/> game hours, the default
    /// answer happens.
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

        Text _cardTitle, _narration, _cast, _resultHead, _result, _footer;
        Image _resultBox;
        readonly List<(Button button, Text hint)> _choices = new();
        Button _later, _ok;
        DrawnCard _card;
        bool _holdingClock;

        protected override void Build(RectTransform body)
        {
            var root = body.GetComponentInParent<Canvas>().rootCanvas.gameObject;
            root.AddComponent<Driver>().Owner = this;
            EventBus.Subscribe<EventCardDrawnEvent>(OnDrawn);

            UIBuilder.VLayout(body, 8, 6);
            _cardTitle = UIBuilder.Label(body, "", 24, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 36);
            var story = UIBuilder.Panel(body, "Story", UITheme.PanelBgLight);
            story.Size(-1, 130);
            _narration = UIBuilder.Label(story.transform, "", 17, TextAnchor.UpperLeft);
            _narration.rectTransform.Stretch(12, 12, 10, 10);
            _narration.verticalOverflow = VerticalWrapMode.Overflow;
            _cast = UIBuilder.Label(body, "", 14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 40);
            _cast.verticalOverflow = VerticalWrapMode.Overflow;

            for (int i = 0; i < 3; i++)
            {
                int index = i;
                var row = UIBuilder.Row(body, 44, 10);
                var button = UIBuilder.Button(row, "", () => Choose(index), 330, 42);
                var hint = UIBuilder.Label(row, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, -1, 1);
                _choices.Add((button, hint));
            }

            // The result in a tinted box with a big ✔ 성공 / ✘ 실패 line (2026-11-01): green good, red bad.
            _resultBox = UIBuilder.Panel(body, "Result", new Color(0f, 0f, 0f, 0f));
            _resultBox.Size(-1, 96);
            _resultHead = UIBuilder.Label(_resultBox.transform, "", 22, TextAnchor.UpperLeft, UITheme.Good, FontStyle.Bold);
            _resultHead.rectTransform.Anchor(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(-24, 30));
            _result = UIBuilder.Label(_resultBox.transform, "", 17, TextAnchor.UpperLeft);
            _result.rectTransform.Stretch(12, 12, 38, 6);
            _result.verticalOverflow = VerticalWrapMode.Overflow;

            var actions = UIBuilder.Row(body, 36, 10);
            _ok = UIBuilder.Button(actions, "확인", Close, 120, 34);
            _later = UIBuilder.Button(actions, "나중에", Close, 120, 34, UITheme.ButtonDanger);
            _footer = UIBuilder.Label(actions, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, -1, 1);
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
            _cast.text = CastLine();

            bool open = !_card.Resolved;
            var choices = _card.Def.Choices;
            for (int i = 0; i < _choices.Count; i++)
            {
                var (button, hint) = _choices[i];
                bool exists = i < choices.Count;
                button.transform.parent.gameObject.SetActive(exists && (open || i == _card.Chosen));
                if (!exists) continue;
                button.SetLabel(_card.Label(i));
                string why = open ? service?.CannotChoose(_card, i) : null;
                button.interactable = open && why == null;
                string odds = EventCards.Hint(choices[i], _card.A, _card.B);
                hint.text = !open ? UIBuilder.Colored("선택함", UITheme.Accent)
                    : why != null ? UIBuilder.Colored(why, UITheme.Bad) : odds;
            }

            ShowResult();
            _ok.gameObject.SetActive(_card.Resolved);
            _later.gameObject.SetActive(!_card.Resolved);
            if (!_card.Resolved && clock != null)
            {
                float left = Mathf.Max(0f, EventCards.ExpireHours - (clock.TotalHours - _card.DrawnAt));
                _footer.text = $"약 {Mathf.CeilToInt(left)}시간 안에 정하지 않으면 '{_card.Label(_card.Def.Choices.IndexOf(_card.Def.DefaultChoice))}' 쪽으로 흘러갑니다.";
            }
            else _footer.text = "";
        }

        void ShowResult()
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
            _resultHead.text = head;
            _resultHead.color = c;
            _result.text = _card.ResultText;
            _result.color = tone == CardTone.Neutral ? UITheme.Text : Color.Lerp(c, Color.white, 0.35f);
            _resultBox.color = tone == CardTone.Neutral ? new Color(1f, 1f, 1f, 0.05f) : new Color(c.r, c.g, c.b, 0.16f);
            // No head line for a plain result: the text moves up.
            _result.rectTransform.Stretch(12, 12, head.Length > 0 ? 38 : 10, 6);
        }

        string CastLine()
        {
            if (!_card.A.HasValue) return "";
            string Line(CardMember m) => $"{m.Name} Lv.{m.Level} · 스트레스 {Mathf.RoundToInt(m.Stress)} · 피로 {Mathf.RoundToInt(m.Fatigue)}";
            string s = Line(_card.A.Value);
            if (_card.B.HasValue) s += "\n" + Line(_card.B.Value);
            return s;
        }
    }
}
