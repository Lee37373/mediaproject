using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Notifications;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>The notification list (F-1, N key): everything that was announced, newest first. Clicking an entry goes to what it is about.</summary>
    public class NotificationView : UIPanel
    {
        public override string Title => "알림";
        protected override float RefreshInterval => 0.25f;

        RectTransform _content;
        readonly List<NotificationRow> _rows = new();
        Text _summary;

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 6, 0);
            var top = UIBuilder.Row(body, 32);
            _summary = UIBuilder.Label(top, "", 15, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, -1, 1);
            UIBuilder.Button(top, "모두 읽음", MarkAllRead, 110, 30, UITheme.Header, 14);
            UIBuilder.Button(top, "비우기", ClearAll, 90, 30, UITheme.ButtonDanger, 14);
            _content = UIBuilder.ScrollList(body, out var scroll);
            scroll.GetComponent<RectTransform>().Size(-1, -1, -1, 1);
        }

        protected override void Refresh()
        {
            if (!Services.TryGet<NotificationCenter>(out var center)) return;
            var entries = center.Entries;
            _summary.text = entries.Count == 0
                ? "새 알림이 없습니다."
                : $"읽지 않은 알림 {center.Unread}개 · 전체 {entries.Count}개 · 항목을 누르면 해당 창이나 위치로 이동합니다.";
            EnsureRows(_content, _rows, entries.Count, NotificationRow.Create);
            // Newest first.
            for (int i = 0; i < entries.Count; i++) _rows[i].Bind(entries[entries.Count - 1 - i]);
        }

        void MarkAllRead()
        {
            if (Services.TryGet<NotificationCenter>(out var center)) center.MarkAllRead();
            Refresh();
        }

        void ClearAll()
        {
            if (Services.TryGet<NotificationCenter>(out var center)) center.Clear();
            Refresh();
        }

        class NotificationRow : MonoBehaviour
        {
            Image _bg;
            Image _bar;
            Text _head;
            Text _text;
            Notification _n;

            public static NotificationRow Create(Transform parent)
            {
                var bg = UIBuilder.Panel(parent, "NotificationRow", UITheme.RowBg);
                bg.Size(-1, 66);
                var row = bg.gameObject.AddComponent<NotificationRow>();
                row._bg = bg;
                UIBuilder.VLayout(bg, 2, 6).padding = new RectOffset(18, 12, 6, 6);
                row._head = UIBuilder.Label(bg.transform, "", 15, TextAnchor.MiddleLeft, UITheme.Text, FontStyle.Bold).Size(-1, 22);
                row._text = UIBuilder.Label(bg.transform, "", 14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 22);

                // Colour strip on the left edge, outside the row's layout.
                row._bar = UIBuilder.Panel(bg.transform, "Kind", UITheme.Accent, raycast: false);
                var le = row._bar.gameObject.AddComponent<LayoutElement>();
                le.ignoreLayout = true;
                row._bar.rectTransform.Anchor(new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(4, 0), new Vector2(6, -10));

                var btn = bg.gameObject.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.onClick.AddListener(row.Click);
                return row;
            }

            public void Bind(Notification n)
            {
                _n = n;
                var color = NotificationActions.KindColor(n.Kind);
                _bg.color = n.Read ? UITheme.RowBgAlt : UITheme.RowBg;
                _bar.color = n.Read ? new Color(color.r, color.g, color.b, 0.35f) : color;
                string dot = n.Read ? "" : UIBuilder.Colored("● ", color);
                string count = n.Count > 1 ? $" ×{n.Count}" : "";
                string go = n.HasLink ? UIBuilder.Colored("  › 이동", UITheme.TextMuted) : "";
                _head.text = $"{dot}{UIBuilder.Colored(n.TimeLabel, UITheme.TextMuted)}  {UIBuilder.Colored(n.Title + count, n.Read ? UITheme.TextMuted : color)}{go}";
                _text.text = n.Text;
                _text.color = n.Read ? UITheme.TextMuted : UITheme.Text;
            }

            void Click()
            {
                NotificationActions.Go(_n);
            }
        }
    }
}
