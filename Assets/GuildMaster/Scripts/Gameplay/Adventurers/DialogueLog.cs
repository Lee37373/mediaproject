using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    public enum DialogueKind { Recruit, Banter }

    /// <summary>One spoken line in an adventurer's conversation history (serialized with AdventurerData).</summary>
    [Serializable]
    public class DialogueLine
    {
        /// <summary>Game time in total hours since the start.</summary>
        public float hour;
        public DialogueKind kind;
        /// <summary>Who said it (display name; "길드 마스터" for the player).</summary>
        public string speaker;
        /// <summary>The other side of the conversation (name), for grouping and prompts.</summary>
        public string with;
        public bool fromPlayer;
        public string text;

        public int Day => Mathf.FloorToInt(hour / 24f) + 1;
        public string Format() => $"[{Day}일 {Mathf.FloorToInt(hour % 24f):00}시] {speaker}: {text}";
    }

    /// <summary>
    /// Per-adventurer conversation history: recruitment talks with the guild master and small talk
    /// with other members. Recruit talks resume from it; banter prompts quote the pair's last talk.
    /// </summary>
    public static class DialogueLog
    {
        public const int Capacity = 80;
        public const string MasterName = "길드 마스터";

        public static void Add(AdventurerData data, DialogueLine line)
        {
            if (data == null || line == null || string.IsNullOrWhiteSpace(line.text)) return;
            data.dialogue.Add(line);
            if (data.dialogue.Count > Capacity) data.dialogue.RemoveRange(0, data.dialogue.Count - Capacity);
        }

        /// <summary>Records a line said to/by the guild master during recruitment.</summary>
        public static void AddRecruit(AdventurerData visitor, float hour, bool fromPlayer, string text)
            => Add(visitor, new DialogueLine
            {
                hour = hour, kind = DialogueKind.Recruit, fromPlayer = fromPlayer,
                speaker = fromPlayer ? MasterName : visitor.name, with = MasterName, text = text,
            });

        /// <summary>Records a two-line exchange in both speakers' histories.</summary>
        public static void AddBanter(AdventurerData a, AdventurerData b, float hour, string lineA, string lineB)
        {
            foreach (var owner in new[] { a, b })
            {
                var other = owner == a ? b : a;
                Add(owner, new DialogueLine { hour = hour, kind = DialogueKind.Banter, speaker = a.name, with = other.name, text = lineA });
                Add(owner, new DialogueLine { hour = hour, kind = DialogueKind.Banter, speaker = b.name, with = other.name, text = lineB });
            }
        }

        public static IEnumerable<DialogueLine> Recruit(AdventurerData data)
            => data?.dialogue.Where(l => l.kind == DialogueKind.Recruit) ?? Enumerable.Empty<DialogueLine>();

        /// <summary>The most recent small-talk lines between this adventurer and another, oldest first.</summary>
        public static List<DialogueLine> LastBanterWith(AdventurerData data, string otherName, int maxLines)
        {
            var lines = data?.dialogue.Where(l => l.kind == DialogueKind.Banter && l.with == otherName).ToList() ?? new List<DialogueLine>();
            return lines.Skip(Math.Max(0, lines.Count - maxLines)).ToList();
        }
    }
}
