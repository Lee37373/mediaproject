using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Dilemmas;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Decision cards (P4-111c, H3 — user decisions 2026-10-30: a window of their own, the outcome shown only roughly): the
    /// open cards, each with its two or three choices and what they roughly do, the hours left before the default is taken;
    /// below, the decisions taken lately and what came of them. Opened with J and from the "결정할 일" notification.
    /// </summary>
    public class DilemmaView : UIPanel
    {
        public override string Title => "결정";
        protected override float RefreshInterval => 0.25f;

        Text _status, _empty, _past;
        RectTransform _listContent;
        readonly List<CardRow> _rows = new();

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 8, 0);
            _status = UIBuilder.Label(body, "", 15, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 26);
            _listContent = UIBuilder.ScrollList(body, out var scroll);
            scroll.GetComponent<RectTransform>().Size(-1, -1, -1, 1);
            _empty = UIBuilder.Label(_listContent, "", 15, TextAnchor.MiddleCenter, UITheme.TextMuted).Size(-1, 70);
            _empty.horizontalOverflow = HorizontalWrapMode.Wrap;
            UIBuilder.Label(body, "지난 결정", 15, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 24);
            _past = UIBuilder.Label(body, "", 13, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 150);
            _past.horizontalOverflow = HorizontalWrapMode.Wrap;
            _past.verticalOverflow = VerticalWrapMode.Truncate;
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

            var past = service.History.Reverse().Take(6).Select(r =>
                $"{clock.AgoText(r.Hour)} · {UIBuilder.Colored(r.Title, UITheme.Accent)} → {r.Choice}{(r.ByDeadline ? UIBuilder.Colored(" (기한 지남)", UITheme.Warn) : "")}\n   {r.Result}");
            _past.text = service.History.Count == 0 ? "아직 없습니다." : string.Join("\n", past);
        }

        void Choose(Dilemma card, int choice)
        {
            if (!Services.TryGet<DilemmaService>(out var service)) return;
            service.Choose(card, choice);
            Refresh();
        }

        /// <summary>One open card: its story and its choices.</summary>
        class CardRow : MonoBehaviour
        {
            const int MaxChoices = 3;
            Image _bg;
            Text _title, _text;
            readonly Button[] _buttons = new Button[MaxChoices];
            readonly Text[] _hints = new Text[MaxChoices];
            Dilemma _card;
            DilemmaView _owner;

            public static CardRow Create(Transform parent, DilemmaView owner)
            {
                var bg = UIBuilder.Panel(parent, "DilemmaCard", UITheme.RowBg);
                bg.Size(-1, 212);
                var row = bg.gameObject.AddComponent<CardRow>();
                row._bg = bg;
                row._owner = owner;
                var v = UIBuilder.VLayout(bg, 4, 0);
                v.padding = new RectOffset(12, 12, 8, 8);
                row._title = UIBuilder.Label(bg.transform, "", 17).Size(-1, 26);
                row._text = UIBuilder.Label(bg.transform, "", 14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 48);
                row._text.horizontalOverflow = HorizontalWrapMode.Wrap;
                row._text.verticalOverflow = VerticalWrapMode.Truncate;
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
                _bg.Size(-1, 96 + 40 * card.Def.Choices.Length); // as tall as its choices
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
