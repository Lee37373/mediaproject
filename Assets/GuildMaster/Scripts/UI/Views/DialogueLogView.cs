using System.Collections.Generic;
using GuildMaster.Adventurers;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Conversation history of one adventurer or visitor (recruit talks with the guild master and
    /// small talk with members), opened from the inspector's [대화 기록] button.
    /// </summary>
    public class DialogueLogView : UIPanel
    {
        public override string Title => "대화 기록";

        Adventurer _who;
        Text _header;
        RectTransform _content;
        ScrollRect _scroll;
        readonly List<Text> _rows = new();
        int _shown = -1;

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 6, 0);
            _header = UIBuilder.Label(body, "", 17, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 28);
            _content = UIBuilder.ScrollList(body, out _scroll, 2);
            _scroll.GetComponent<RectTransform>().Size(-1, -1, -1, 1);
        }

        public void Show(Adventurer who)
        {
            _who = who;
            _shown = -1;
            Open();
        }

        protected override void Refresh()
        {
            if (_who == null) { _header.text = "대상이 없습니다."; return; }
            var lines = _who.Data.dialogue;
            _header.text = $"{_who.Data.name}  {UIBuilder.Colored($"대화 {lines.Count}줄", UITheme.TextMuted)}";
            if (lines.Count == _shown) return;
            _shown = lines.Count;

            EnsureRows(_content, _rows, Mathf.Max(1, lines.Count), parent =>
            {
                var t = UIBuilder.Label(parent, "", 14);
                t.verticalOverflow = VerticalWrapMode.Overflow;
                return t;
            });
            if (lines.Count == 0)
            {
                _rows[0].text = UIBuilder.Colored("아직 나눈 대화가 없습니다.", UITheme.TextMuted);
                return;
            }
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                string tag = l.kind == DialogueKind.Recruit ? UIBuilder.Colored("[영입]", UITheme.Warn) : UIBuilder.Colored($"[잡담·{l.with}]", UITheme.Exp);
                var speakerColor = l.fromPlayer ? UITheme.Accent : l.speaker == _who.Data.name ? UITheme.Good : UITheme.Text;
                _rows[i].text = $"{UIBuilder.Colored($"{l.Day}일 {Mathf.FloorToInt(l.hour % 24f):00}시", UITheme.TextMuted)} {tag} {UIBuilder.Colored(l.speaker, speakerColor)}: {l.text}";
            }
            // Newest at the bottom: jump there when new lines arrive.
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 0f;
        }
    }
}
