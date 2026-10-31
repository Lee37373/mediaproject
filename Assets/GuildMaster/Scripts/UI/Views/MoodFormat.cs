using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using UnityEngine;

namespace GuildMaster.UI
{
    /// <summary>A member's mood and thoughts as the windows show them (P4-104c): "64 좋음", "+6 의뢰를 해냈다 ×2 · 18시간".</summary>
    public static class MoodFormat
    {
        public static Color ColorOf(MoodLevel level) => level switch
        {
            MoodLevel.Breaking => UITheme.Bad,
            MoodLevel.Low => UITheme.Warn,
            MoodLevel.Good or MoodLevel.High => UITheme.Good,
            _ => UITheme.Text,
        };

        /// <summary>"64 좋음", coloured by its level.</summary>
        public static string Mood(Adventurer a)
        {
            float mood = ThoughtRecorder.MoodOf(a);
            var level = ThoughtRules.LevelOf(mood);
            return UIBuilder.Colored($"{mood:0} {ThoughtRules.Label(level)}", ColorOf(level));
        }

        static string Signed(float v) => v > 0f ? $"+{v:0}" : $"{v:0}";

        /// <summary>" · 전투 영감 18시간" while one lasts (P4-104d), else empty.</summary>
        public static string Inspiration(Adventurer a, float now)
        {
            var d = a.Data;
            string s = "";
            if (d.inspiration != InspirationKind.None)
            {
                int left = Mathf.Max(1, Mathf.CeilToInt(d.inspirationUntil - now));
                s += " · " + UIBuilder.Colored($"{BreakRules.Label(d.inspiration)} {left}시간", UITheme.Accent);
            }
            // A shrine's blessing (P4-105b).
            if (d.blessedUntil > now)
                s += " · " + UIBuilder.Colored($"가호 {Mathf.Max(1, Mathf.CeilToInt(d.blessedUntil - now))}시간", UITheme.Good);
            return s;
        }

        /// <summary>One thought: its weight, words, stacks and the hours it still lasts (none for a situational one).</summary>
        public static string Thought(ThoughtEntry t, float now)
        {
            float w = ThoughtRules.Weight(t);
            string stack = t.stack > 1 ? $" ×{t.stack}" : "";
            string left = t.until > 0f ? UIBuilder.Colored($" · {Mathf.Max(1, Mathf.CeilToInt(t.until - now))}시간", UITheme.TextMuted) : "";
            return $"{UIBuilder.Colored(Signed(w), w >= 0f ? UITheme.Good : UITheme.Bad)} {t.label}{stack}{left}";
        }

        /// <summary>The heaviest few on its mind, comma-separated, for one line ("-" for nothing).</summary>
        public static string Heaviest(Adventurer a, int count)
        {
            var list = ThoughtRecorder.OnItsMind(a).Take(count).ToList();
            return list.Count == 0 ? "-" : string.Join(", ", list.Select(t => $"{t.label} {UIBuilder.Colored(Signed(ThoughtRules.Weight(t)), ThoughtRules.Weight(t) >= 0f ? UITheme.Good : UITheme.Bad)}"));
        }

        /// <summary>Everything on its mind, one a line, the heaviest first, at most <paramref name="max"/>.</summary>
        public static string Lines(Adventurer a, float now, int max)
        {
            List<ThoughtEntry> list = ThoughtRecorder.OnItsMind(a);
            if (list.Count == 0) return UIBuilder.Colored("특별한 생각이 없습니다.", UITheme.TextMuted);
            var shown = list.Take(max).Select(t => Thought(t, now)).ToList();
            if (list.Count > max) shown[shown.Count - 1] += UIBuilder.Colored($"  (그 밖에 {list.Count - max}개)", UITheme.TextMuted);
            return string.Join("\n", shown);
        }
    }
}
