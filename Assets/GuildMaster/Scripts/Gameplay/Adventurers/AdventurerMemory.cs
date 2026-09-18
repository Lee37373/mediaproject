using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>One remembered event of an adventurer (serialized with AdventurerData).</summary>
    [Serializable]
    public class MemoryEntry
    {
        /// <summary>Game time in total hours since the start.</summary>
        public float hour;
        /// <summary>1 = routine, 2 = notable, 3 = life-changing.</summary>
        public int importance;
        /// <summary>Entries with the same non-empty key on the same day merge (count++).</summary>
        public string key;
        public int count = 1;
        /// <summary>Text template; "{n}" is replaced by the count.</summary>
        public string text;

        public int Day => Mathf.FloorToInt(hour / 24f) + 1;
        public string Text => text.Replace("{n}", count.ToString());
        public string Format() => $"[{Day}일 {Mathf.FloorToInt(hour % 24f)}시] {Text}";
    }

    /// <summary>Add / merge / prune / select logic for adventurer memories (pure, testable).</summary>
    public static class AdventurerMemory
    {
        public const int Capacity = 30;
        public const int PromptCount = 6;
        const int PromptImportantCount = 3;

        /// <summary>
        /// Records an event. A same-key entry from the same day is merged instead of duplicated.
        /// When over capacity the least important, oldest entry is dropped.
        /// </summary>
        public static MemoryEntry Add(List<MemoryEntry> memories, float hour, int importance, string text, string key = null)
        {
            if (memories == null || string.IsNullOrWhiteSpace(text)) return null;
            int day = Mathf.FloorToInt(hour / 24f) + 1;
            if (!string.IsNullOrEmpty(key))
            {
                for (int i = memories.Count - 1; i >= 0; i--)
                {
                    var m = memories[i];
                    if (m.key != key || m.Day != day) continue;
                    m.count++;
                    m.hour = hour;
                    m.text = text;
                    m.importance = Mathf.Max(m.importance, importance);
                    // Keep chronological order: the merged entry becomes the latest.
                    memories.RemoveAt(i);
                    memories.Add(m);
                    return m;
                }
            }
            var entry = new MemoryEntry { hour = hour, importance = Mathf.Clamp(importance, 1, 3), key = key, text = text };
            memories.Add(entry);
            while (memories.Count > Capacity) memories.RemoveAt(IndexToDrop(memories));
            return entry;
        }

        static int IndexToDrop(List<MemoryEntry> memories)
        {
            int best = 0;
            for (int i = 1; i < memories.Count; i++)
            {
                var a = memories[i];
                var b = memories[best];
                if (a.importance < b.importance || (a.importance == b.importance && a.hour < b.hour)) best = i;
            }
            return best;
        }

        /// <summary>
        /// Memories for a prompt: the most important ones first (recent wins ties), filled up with
        /// the most recent, returned in chronological order.
        /// </summary>
        public static List<MemoryEntry> SelectForPrompt(IReadOnlyList<MemoryEntry> memories, int count = PromptCount)
        {
            var result = new List<MemoryEntry>();
            if (memories == null || memories.Count == 0 || count <= 0) return result;
            foreach (var m in memories.OrderByDescending(m => m.importance).ThenByDescending(m => m.hour).Take(Mathf.Min(PromptImportantCount, count)))
                result.Add(m);
            for (int i = memories.Count - 1; i >= 0 && result.Count < count; i--)
                if (!result.Contains(memories[i])) result.Add(memories[i]);
            result.Sort((a, b) => a.hour.CompareTo(b.hour));
            return result;
        }
    }
}
