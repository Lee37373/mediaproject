using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.Guide;
using GuildMaster.Notifications;
using GuildMaster.Quests;
using GuildMaster.Recruitment;
using GuildMaster.Welfare;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// "다음 할 일" (F-19, user request 2026-10-11): the next first thing to try (<see cref="GuideRules.Order"/>) and what
    /// needs the guild master right now — a worn-out member (opens the care window), a keen visitor (jumps to them), an
    /// empty quest board, a waiting decision card.
    /// 2026-11-13 (user: "too much text in a narrow box", picked option B and the scroll button): a purple scroll button
    /// under the minimap with a red count; it opens a wide card of roomy rows — an icon, a few words, one short line —
    /// with the full sentence in each row's tooltip. A row opens what it is about and closes the card. Y or the button
    /// toggles it; the button bounces when something new turns up.
    /// </summary>
    public class GuideView : MonoBehaviour
    {
        const float RefreshInterval = 0.5f;
        const float ButtonSize = 64f, Top = 340f, Margin = 8f;
        const float CardWidth = 470f, RowH = 72f, RowGap = 8f, HeaderH = 52f, Pad = 14f;
        const int Rows = 1 + GuideRules.MaxHints;

        RectTransform _button, _card;
        Image _bubble;
        Text _count, _header, _progress;
        readonly List<Row> _rows = new();
        readonly List<GuideHint> _hints = new();
        GuideStep? _step;
        bool _open;
        int _lastCount;
        float _timer;

        class Row
        {
            public RectTransform Root;
            public Button Button;
            public Image Back, Icon;
            public Text Title, Sub, Arrow;
            public string Tip;
            public System.Action OnClick;
        }

        public void Build(RectTransform canvas)
        {
            // The scroll button.
            // A wooden plank with nails when the wood pack is in (2026-11-13), the purple one otherwise.
            var btnImg = HudStyle.NewImage(canvas, WoodSkin.Present ? "hud_guide_button_wood" : "hud_guide_button", "GuideButton");
            if (btnImg.sprite == null) btnImg.color = UITheme.Button;
            btnImg.raycastTarget = true;
            _button = btnImg.rectTransform;
            // Pivot in the middle, so the hover, press and bounce grow from its centre (same place as before).
            _button.Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
                new Vector2(-Margin - ButtonSize / 2f, -Top - ButtonSize / 2f), new Vector2(ButtonSize, ButtonSize));
            var btn = btnImg.gameObject.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.normalColor = new Color(0.94f, 0.94f, 0.94f, 1f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.selectedColor = colors.normalColor;
            btn.colors = colors;
            btn.onClick.AddListener(Toggle);
            btn.onClick.AddListener(GuildMaster.Audio.Sound.Click);
            btnImg.gameObject.AddComponent<ButtonJuice>(); // 2026-11-14: the same lean-in and press as every other button
            btnImg.Tooltip(() => "다음 할 일 (Y)");

            _bubble = HudStyle.NewImage(_button, "hud_count", "Count");
            if (_bubble.sprite == null) _bubble.color = UITheme.Bad;
            var brt = _bubble.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = new Vector2(-6f, -6f);
            brt.sizeDelta = new Vector2(28f, 28f);
            _count = HudStyle.Label(brt, "", 15, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            _count.rectTransform.Stretch(0, 0, 0, 1);
            UISkin.Outline(_count);

            // The wide card, left of the button, its top level with the button's.
            var cardImg = UIBuilder.Panel(canvas, "GuideCard", UITheme.PanelBg);
            HudStyle.Card(cardImg);
            _card = cardImg.rectTransform;
            _card.Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-Margin - ButtonSize - 10f, -Top), new Vector2(CardWidth, 200f));
            HudStyle.Place(HudStyle.NewImage(_card, "hud_icon_todo", "Icon").rectTransform, Pad + 4, 14, 26, 26);
            _header = HudStyle.Label(_card, "다음 할 일", 20, UITheme.Accent, FontStyle.Bold);
            HudStyle.Place(_header.rectTransform, Pad + 38, 10, 220, 32);
            _progress = HudStyle.Label(_card, "", 16, UITheme.TextMuted, FontStyle.Bold, TextAnchor.MiddleRight);
            HudStyle.Place(_progress.rectTransform, CardWidth - Pad - 120, 10, 112, 32);
            for (int i = 0; i < Rows; i++) _rows.Add(MakeRow(i));
            _card.gameObject.SetActive(false);
        }

        Row MakeRow(int i)
        {
            var row = new Row();
            var back = HudStyle.NewImage(_card, "hud_tag", "Row");
            if (back.sprite == null) back.color = UITheme.RowBg;
            back.raycastTarget = true;
            row.Back = back;
            row.Root = back.rectTransform;
            HudStyle.Place(row.Root, Pad, HeaderH + i * (RowH + RowGap), CardWidth - 2 * Pad, RowH);
            row.Button = back.gameObject.AddComponent<Button>();
            row.Button.targetGraphic = back;
            var colors = row.Button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = Color.white;
            row.Button.colors = colors;
            row.Button.onClick.AddListener(() => { row.OnClick?.Invoke(); });
            back.Tooltip(() => row.Tip);
            row.Icon = HudStyle.NewImage(row.Root, "mark_alert", "Icon");
            row.Icon.preserveAspect = true;
            HudStyle.Place(row.Icon.rectTransform, 16, (RowH - 34) / 2, 34, 34);
            row.Title = HudStyle.Label(row.Root, "", 18, HudStyle.Fore, FontStyle.Bold);
            HudStyle.Place(row.Title.rectTransform, 64, 10, CardWidth - 2 * Pad - 100, 28);
            row.Sub = HudStyle.Label(row.Root, "", 14, HudStyle.Muted);
            HudStyle.Place(row.Sub.rectTransform, 64, 38, CardWidth - 2 * Pad - 100, 24);
            row.Arrow = HudStyle.Label(row.Root, "›", 26, HudStyle.Muted, FontStyle.Bold, TextAnchor.MiddleCenter);
            HudStyle.Place(row.Arrow.rectTransform, CardWidth - 2 * Pad - 40, (RowH - 36) / 2, 28, 36);
            foreach (var t in new[] { row.Title, row.Sub }) t.verticalOverflow = VerticalWrapMode.Truncate;
            return row;
        }

        public bool IsOpen => _open;

        void Toggle()
        {
            _open = !_open;
            _card.gameObject.SetActive(_open);
            if (_open) { Refresh(); UIMotion.PopIn(_card, 0.9f, 0.2f, fade: false); } // 2026-11-13: pops out of the button
            _timer = 0f;
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _card.gameObject.SetActive(false);
        }

        void Update()
        {
            if (_button == null) return;
            var kb = Keyboard.current;
            if (kb != null && kb.yKey.wasPressedThisFrame && !IsTyping() && !MenuState.IsOpen) Toggle();
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = RefreshInterval;
            Refresh();
        }

        void Refresh()
        {
            Services.TryGet<GuideTracker>(out var tracker);
            var snapshot = Snapshot(tracker);
            _step = tracker == null ? null : GuideRules.Next(tracker.IsDone);
            _hints.Clear();
            _hints.AddRange(GuideRules.Hints(snapshot));

            int count = _hints.Count + (_step.HasValue ? 1 : 0);
            _bubble.gameObject.SetActive(count > 0);
            _count.text = count.ToString();
            // Something new: the button hops (card shut) and the count pops (DOTween, 2026-11-13).
            if (count > _lastCount && !_open) UIMotion.Bounce(_button);
            if (count != _lastCount && count > 0) UIMotion.Punch(_bubble.rectTransform, 0.4f);
            _lastCount = count;
            if (!_open) return;

            int done = tracker != null ? GuideRules.Order.Count(tracker.IsDone) : 0;
            _progress.text = $"{done} / {GuideRules.Order.Length}";
            int used = 0;
            if (_step is { } step)
            {
                var stepCopy = step;
                Fill(_rows[used++], "hud_icon_todo", GuideRules.Title(step), GuideRules.Short(step), GuideRules.HowTo(step),
                     WoodSkin.Warm(new Color(0.42f, 0.34f, 0.56f, 1f)), HudStyle.Fore, step == GuideStep.Board ? () => { NotificationActions.Follow(NotificationLink.QuestBoard()); Close(); } : null);
            }
            for (int i = 0; i < _hints.Count && used < _rows.Count; i++)
            {
                var h = _hints[i];
                int index = i;
                bool urgent = h.Urgency >= 4;
                Fill(_rows[used++], "mark_alert", h.Title ?? h.Text, h.Sub ?? "", h.Text,
                     urgent ? new Color(0.5f, 0.3f, 0.26f, 1f) : WoodSkin.Warm(new Color(0.34f, 0.28f, 0.44f, 1f)),
                     urgent ? new Color(1f, 0.8f, 0.55f, 1f) : HudStyle.Fore,
                     h.Action == HintAction.None ? null : () => { Follow(index); Close(); });
            }
            if (used == 0)
                Fill(_rows[used++], "hud_icon_check_on", tracker != null && tracker.AllDone ? "기본 할 일을 모두 해 봤어요" : "급한 일은 없어요",
                     "새로운 일이 생기면 숫자로 알려 줘요", null, WoodSkin.Warm(new Color(0.3f, 0.26f, 0.36f, 1f)), HudStyle.Fore, null);
            for (int i = used; i < _rows.Count; i++)
                if (_rows[i].Root.gameObject.activeSelf) _rows[i].Root.gameObject.SetActive(false);
            float height = HeaderH + used * (RowH + RowGap) - RowGap + Pad;
            if (Mathf.Abs(_card.sizeDelta.y - height) > 0.5f) _card.sizeDelta = new Vector2(CardWidth, height);
        }

        static void Fill(Row row, string icon, string title, string sub, string tip, Color back, Color titleColor, System.Action onClick)
        {
            if (!row.Root.gameObject.activeSelf) row.Root.gameObject.SetActive(true);
            HudStyle.Slice(row.Icon, icon);
            row.Icon.type = Image.Type.Simple;
            row.Back.color = back;
            row.Title.text = title;
            row.Title.color = titleColor;
            row.Sub.text = sub;
            row.Tip = tip;
            row.OnClick = onClick;
            row.Button.interactable = onClick != null;
            row.Arrow.gameObject.SetActive(onClick != null);
        }

        GuideSnapshot Snapshot(GuideTracker tracker)
        {
            var s = new GuideSnapshot();
            Services.TryGet<GuildWelfare>(out var welfare);
            if (Services.TryGet<GuildRoster>(out var roster))
            {
                s.RosterFull = false; // main has no member cap any more (2026-10-31 merge)
                foreach (var m in roster.Members)
                {
                    if (m == null || !m.IsAlive || m.Data == null || m.IsVisitor) continue;
                    s.Members.Add(new GuideSnapshot.Member
                    {
                        Id = m.Data.id, Name = m.Data.name, Stress = m.Data.stress, Demand = welfare != null ? welfare.Care(m).Demand : CareDemand.None,
                    });
                    if (m.CurrentQuest == null && m.IsIdleInTown) s.IdleMembers++;
                    // Someone talked into joining counts as having had the recruit talk.
                    if (m.Data.recruitTurns > 0) tracker?.Mark(GuideStep.TalkVisitor);
                }
            }
            if (Services.TryGet<VisitorService>(out var visitors))
                foreach (var v in visitors.Visitors)
                {
                    if (v == null || !v.IsAlive || v.Data == null || !v.IsVisitor) continue;
                    s.Visitors.Add(new GuideSnapshot.Visitor { Id = v.Data.id, Name = v.Data.name, Affinity = v.Data.recruitAffinity });
                    if (v.Data.recruitTurns > 0) tracker?.Mark(GuideStep.TalkVisitor);
                }
            if (Services.TryGet<QuestBoard>(out var board)) s.OpenQuests = board.Quests.Count(q => q != null && q.Status == QuestStatus.Open);
            if (Services.TryGet<GuildTreasury>(out var treasury)) s.Gold = treasury.Gold;
            if (Services.TryGet<Dilemmas.DilemmaService>(out var cards) && cards.Open.Count > 0) s.PendingCard = cards.Open[0].Title;
            return s;
        }

        void Follow(int index)
        {
            if (index < 0 || index >= _hints.Count) return;
            var h = _hints[index];
            switch (h.Action)
            {
                case HintAction.Counsel:
                    NotificationActions.Follow(NotificationLink.Management(h.TargetId));
                    break;
                case HintAction.Visitor:
                    NotificationActions.Follow(NotificationLink.Adventurer(h.TargetId));
                    break;
                case HintAction.QuestBoard:
                    NotificationActions.Follow(NotificationLink.QuestBoard());
                    break;
                case HintAction.Decision:
                    NotificationActions.Follow(NotificationLink.Dilemmas());
                    break;
            }
        }

        static bool IsTyping() => Session.TextFocus.Typing; // P6-2: a field with the caret, not just a selected one
    }
}
