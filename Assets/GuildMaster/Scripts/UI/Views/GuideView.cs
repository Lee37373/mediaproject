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
    /// "다음 할 일" (F-19, user request 2026-10-11): a small panel on the left under the side menu. The checklist of first
    /// things to try (<see cref="GuideRules.Order"/>, done ones ticked off) and clickable hints about what needs the
    /// guild master right now — a worn-out member (opens the care window), a keen visitor (jumps to them), an empty
    /// quest board, a waiting event card. Y or the header button folds it (J went to decision cards at the 2026-10-31 merge).
    /// </summary>
    public class GuideView : MonoBehaviour
    {
        const float RefreshInterval = 0.5f;
        const float Width = 300f;
        const float OpenHeight = 300f;
        const float FoldedHeight = 34f;
        const int MaxShownSteps = 3;

        RectTransform _root, _content;
        Text _header, _checklist;
        Button _fold;
        readonly List<(Button button, Text text)> _hintRows = new();
        readonly List<GuideHint> _hints = new();
        bool _folded;
        float _timer;

        public void Build(RectTransform canvas)
        {
            var bg = UIBuilder.Panel(canvas, "Guide", new Color(0.08f, 0.07f, 0.11f, 0.9f));
            _root = bg.rectTransform;
            // Under the side menu (which ends about 470px down; 10 buttons since the relationship graph).
            _root.Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-8, -340), new Vector2(Width, OpenHeight)); // under the minimap (2026-10-31 merge)

            var headerRow = UIBuilder.Rect(_root, "Header");
            headerRow.Anchor(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(-12, 26));
            _header = UIBuilder.Label(headerRow, "다음 할 일 (Y)", 16, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold);
            _header.rectTransform.Stretch(6, 34, 0, 0);
            _fold = UIBuilder.Button(headerRow, "-", Toggle, 26, 24);
            ((RectTransform)_fold.transform).Anchor(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(26, 24));

            _content = UIBuilder.Rect(_root, "Content");
            _content.Stretch(8, 8, 34, 8);
            UIBuilder.VLayout(_content, 4, 0);
            _checklist = UIBuilder.Label(_content, "", 14, TextAnchor.UpperLeft).Size(-1, 92);
            _checklist.verticalOverflow = VerticalWrapMode.Overflow;
            UIBuilder.Label(_content, "지금 챙길 것", 14, TextAnchor.MiddleLeft, UITheme.Accent).Size(-1, 20);
            for (int i = 0; i < GuideRules.MaxHints; i++)
            {
                int index = i;
                var button = UIBuilder.Button(_content, "", () => Follow(index), -1, 38, new Color(0.18f, 0.16f, 0.24f, 1f), 13);
                var text = button.GetComponentInChildren<Text>();
                text.alignment = TextAnchor.MiddleLeft;
                text.rectTransform.Stretch(8, 6, 1, 1);
                text.fontSize = 13;
                _hintRows.Add((button, text));
            }
        }

        void Toggle()
        {
            _folded = !_folded;
            _content.gameObject.SetActive(!_folded);
            _root.sizeDelta = new Vector2(Width, _folded ? FoldedHeight : OpenHeight);
            _fold.SetLabel(_folded ? "+" : "-");
            _timer = 0f;
        }

        void Update()
        {
            if (_root == null) return;
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
            if (tracker != null)
            {
                int done = GuideRules.Order.Count(tracker.IsDone);
                _header.text = $"다음 할 일 (Y)  {UIBuilder.Colored($"{done}/{GuideRules.Order.Length}", UITheme.TextMuted)}";
            }
            if (_folded) return;

            var sb = new StringBuilder();
            if (tracker == null || tracker.AllDone) sb.Append(UIBuilder.Colored("기본 할 일을 모두 해 봤습니다.", UITheme.Good));
            else
            {
                int shown = 0;
                foreach (var step in GuideRules.Order)
                {
                    if (tracker.IsDone(step) || shown >= MaxShownSteps) continue;
                    bool next = shown == 0;
                    sb.Append(UIBuilder.Colored((next ? "▶ " : "· ") + GuideRules.Title(step), next ? UITheme.Text : UITheme.TextMuted)).Append('\n');
                    if (next) sb.Append(UIBuilder.Colored("   " + GuideRules.HowTo(step), UITheme.TextMuted)).Append('\n');
                    shown++;
                }
            }
            _checklist.text = sb.ToString().TrimEnd('\n');

            _hints.Clear();
            _hints.AddRange(GuideRules.Hints(snapshot));
            for (int i = 0; i < _hintRows.Count; i++)
            {
                var (button, text) = _hintRows[i];
                bool on = i < _hints.Count;
                button.gameObject.SetActive(on || (i == 0 && _hints.Count == 0));
                if (!on)
                {
                    if (i == 0)
                    {
                        text.text = UIBuilder.Colored("급한 일은 없습니다.", UITheme.TextMuted);
                        button.interactable = false;
                    }
                    continue;
                }
                var h = _hints[i];
                text.text = h.Urgency >= 4 ? UIBuilder.Colored(h.Text, UITheme.Warn) : h.Text;
                button.interactable = h.Action != HintAction.None;
            }
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

        static bool IsTyping()
        {
            var es = EventSystem.current;
            return es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.GetComponent<InputField>() != null;
        }
    }
}
