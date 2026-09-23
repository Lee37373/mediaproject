using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Session;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>Bottom-left event feed: latest log entries plus NPC speech (chat).</summary>
    public class LogView : MonoBehaviour
    {
        const int VisibleLines = 11;
        static readonly Color SpeechColor = new(0.65f, 0.85f, 1f);

        readonly Queue<string> _lines = new();
        Text _text;
        bool _dirty;

        public void Build(RectTransform canvas)
        {
            var panel = UIBuilder.Panel(canvas, "Log", new Color(0.05f, 0.04f, 0.07f, 0.72f), raycast: false);
            panel.rectTransform.Anchor(Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(8, 8), new Vector2(620, 236));
            _text = UIBuilder.Label(panel.transform, "", 14, TextAnchor.LowerLeft);
            _text.rectTransform.Stretch(10, 10, 8, 8);
            _text.verticalOverflow = VerticalWrapMode.Overflow;

            if (Services.TryGet<GameLog>(out var log))
                foreach (var e in log.Entries) Push(e);
            EventBus.Subscribe<LogAddedEvent>(OnLog);
            EventBus.Subscribe<AdventurerSpeechEvent>(OnSpeech);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<LogAddedEvent>(OnLog);
            EventBus.Unsubscribe<AdventurerSpeechEvent>(OnSpeech);
        }

        void OnLog(LogAddedEvent e) => Push(e.Entry);

        /// <summary>NPC lines (speech bubbles) also go to the feed, like a chat window.</summary>
        void OnSpeech(AdventurerSpeechEvent e)
        {
            if (e.Adventurer == null || string.IsNullOrWhiteSpace(e.Text) || e.NoFeed) return;
            string time = Services.TryGet<GameClock>(out var clock) ? $"{clock.Day}일 {clock.Hour:00}:{clock.Minute:00}" : "";
            Enqueue($"<color=#{UITheme.Hex(UITheme.TextMuted)}>[{time}]</color> " +
                    $"<color=#{UITheme.Hex(UITheme.Accent)}>{e.Adventurer.Data.name}</color>: " +
                    $"<color=#{UITheme.Hex(SpeechColor)}>\"{e.Text.Replace("<", "＜")}\"</color>");
        }

        void Push(LogEntry e)
        {
            var color = e.Category switch
            {
                LogCategory.Quest => UITheme.Accent,
                LogCategory.Economy => UITheme.TextMuted,
                LogCategory.Ecosystem => new Color(0.6f, 0.85f, 0.6f),
                LogCategory.Danger => UITheme.Bad,
                LogCategory.Combat => UITheme.Warn,
                _ => UITheme.Text,
            };
            Enqueue($"<color=#{UITheme.Hex(UITheme.TextMuted)}>[{e.Time}]</color> <color=#{UITheme.Hex(color)}>{e.Text}</color>");
        }

        void Enqueue(string line)
        {
            _lines.Enqueue(line);
            while (_lines.Count > VisibleLines) _lines.Dequeue();
            _dirty = true;
        }

        void LateUpdate()
        {
            if (!_dirty) return;
            _dirty = false;
            _text.text = string.Join("\n", _lines);
        }
    }
}
