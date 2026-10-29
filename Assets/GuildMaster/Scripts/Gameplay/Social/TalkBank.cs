using System;
using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
using UnityEngine;

namespace GuildMaster.Social
{
    /// <summary>One line of a written town talk: who says it (A/B) and how, per speech level.</summary>
    [Serializable]
    public class TalkLineDto
    {
        /// <summary>"A" or "B".</summary>
        public string s;
        /// <summary>The line in a resident's or the merchant's own voice.</summary>
        public string t;
        /// <summary>The line for a member or visitor: warm / formal / curt / haughty (NpcVoice.Register).</summary>
        public string w, f, c, h;
    }

    /// <summary>A written four-line town talk and when it fits.</summary>
    [Serializable]
    public class TalkScriptDto
    {
        public string id;
        public string kind;
        public string outcome;
        /// <summary>Who may be A / B: letters M (member), V (visitor), R (resident), X (merchant); empty = anyone but residents and the merchant.</summary>
        public string a, b;
        /// <summary>Only for someone with these values (empty = anyone).</summary>
        public string valueA, valueB;
        /// <summary>Resident id of A / B (required when that side is a resident).</summary>
        public string resA, resB;
        /// <summary>What a quarrel was about (kept for making up later).</summary>
        public string about;
        public int weight = 1;
        public TalkLineDto[] lines;
    }

    [Serializable]
    public class TalkFileDto
    {
        public TalkScriptDto[] scripts;
    }

    /// <summary>One side of a talk as the bank sees it.</summary>
    public struct TalkSide
    {
        public TalkerKind Kind;
        public NpcVoice.Register Register;
        public string Values;
        public string ResidentId;
        public string Name;

        public static TalkSide Of(Talker t)
        {
            var side = new TalkSide { Kind = t.Kind, Name = t.Name, Register = NpcVoice.Register.Warm };
            if (t.Adventurer != null && t.Adventurer.Data != null)
            {
                var d = t.Adventurer.Data;
                side.Register = NpcVoice.RecruitRegister(d.mbti, d.traitIds);
                side.Values = d.values;
            }
            if (t.Resident != null) side.ResidentId = t.Resident.Profile.Id;
            return side;
        }
    }

    /// <summary>
    /// Town talks written ahead of time (user decision 2026-10-18: no model running for everyday talk). Each script
    /// is a four-line exchange for one kind of talk and one outcome; members and visitors speak in their speech level
    /// (from MBTI and traits, <see cref="NpcVoice.RecruitRegister"/>), residents and the merchant in their own voice,
    /// and scripts written for someone's values come up more often for them. The data lives in
    /// Resources/Dialogue/Town/*.json. Pure apart from <see cref="LoadResources"/>; covered by tests.
    /// </summary>
    public class TalkBank
    {
        public const string ResourceFolder = "Dialogue/Town";
        /// <summary>How much more often a script written for this person's values (or this resident) comes up.</summary>
        public const float SpecificBonus = 2f;

        class Entry
        {
            public TalkScriptDto Dto;
            public TownTalkKind Kind;
            public TalkOutcome Outcome;
        }

        readonly List<Entry> _entries = new();

        public int Count => _entries.Count;
        public IEnumerable<TalkScriptDto> Scripts { get { foreach (var e in _entries) yield return e.Dto; } }

        public static TalkBank FromJson(params string[] jsons)
        {
            var bank = new TalkBank();
            foreach (var json in jsons)
            {
                if (string.IsNullOrWhiteSpace(json)) continue;
                var file = JsonUtility.FromJson<TalkFileDto>(json);
                if (file?.scripts == null) continue;
                foreach (var s in file.scripts) bank.Add(s);
            }
            return bank;
        }

        /// <summary>Every file under Resources/Dialogue/Town (empty bank when there is none).</summary>
        public static TalkBank LoadResources()
        {
            var assets = Resources.LoadAll<TextAsset>(ResourceFolder);
            var texts = new List<string>();
            foreach (var a in assets) if (a != null) texts.Add(a.text);
            return FromJson(texts.ToArray());
        }

        public bool Add(TalkScriptDto s)
        {
            if (s == null || s.lines == null || s.lines.Length < 2) return false;
            if (!Enum.TryParse(s.kind, out TownTalkKind kind) || !Enum.TryParse(s.outcome, out TalkOutcome outcome)) return false;
            _entries.Add(new Entry { Dto = s, Kind = kind, Outcome = outcome });
            return true;
        }

        // ---------------- Choosing ----------------

        static char Letter(TalkerKind k) => k switch
        {
            TalkerKind.Member => 'M',
            TalkerKind.Visitor => 'V',
            TalkerKind.Resident => 'R',
            _ => 'X',
        };

        /// <summary>Whether a script side ("MV", "R", "X"…) admits this talker.</summary>
        static bool SideFits(string letters, string values, string residentId, TalkSide side)
        {
            if (side.Kind == TalkerKind.Resident)
                return !string.IsNullOrEmpty(letters) && letters.IndexOf('R') >= 0 && residentId == side.ResidentId;
            if (side.Kind == TalkerKind.Merchant)
                return !string.IsNullOrEmpty(letters) && letters.IndexOf('X') >= 0;
            if (!string.IsNullOrEmpty(letters) && letters.IndexOf(Letter(side.Kind)) < 0) return false;
            return string.IsNullOrEmpty(values) || values == side.Values;
        }

        /// <summary>How likely this script is for the pair (0 = it does not fit).</summary>
        public static float Weight(TalkScriptDto s, TalkSide a, TalkSide b)
        {
            if (!SideFits(s.a, s.valueA, s.resA, a) || !SideFits(s.b, s.valueB, s.resB, b)) return 0f;
            float w = Math.Max(1, s.weight);
            if (!string.IsNullOrEmpty(s.valueA) || !string.IsNullOrEmpty(s.resA)) w *= SpecificBonus;
            if (!string.IsNullOrEmpty(s.valueB) || !string.IsNullOrEmpty(s.resB)) w *= SpecificBonus;
            return w;
        }

        /// <summary>
        /// A script for this kind of talk, outcome and pair; scripts in <paramref name="recent"/> only when nothing
        /// else fits. Null when the bank has nothing for them.
        /// </summary>
        public TalkScriptDto Pick(TownTalkKind kind, TalkOutcome outcome, TalkSide a, TalkSide b, Func<float> roll, ICollection<string> recent = null)
        {
            var fresh = new List<(TalkScriptDto s, float w)>();
            var stale = new List<(TalkScriptDto s, float w)>();
            foreach (var e in _entries)
            {
                if (e.Kind != kind || e.Outcome != outcome) continue;
                float w = Weight(e.Dto, a, b);
                if (w <= 0f) continue;
                if (recent != null && recent.Contains(e.Dto.id)) stale.Add((e.Dto, w));
                else fresh.Add((e.Dto, w));
            }
            var pool = fresh.Count > 0 ? fresh : stale;
            if (pool.Count == 0) return null;
            float total = 0f;
            foreach (var p in pool) total += p.w;
            float r = Mathf.Clamp01(roll()) * total;
            foreach (var p in pool)
            {
                r -= p.w;
                if (r <= 0f) return p.s;
            }
            return pool[pool.Count - 1].s;
        }

        // ---------------- Text ----------------

        /// <summary>The line as this speaker says it.</summary>
        public static string LineFor(TalkLineDto line, TalkSide speaker)
        {
            if (line == null) return "";
            if (speaker.Kind == TalkerKind.Resident || speaker.Kind == TalkerKind.Merchant) return FirstOf(line.t, line.w);
            string byLevel = speaker.Register switch
            {
                NpcVoice.Register.Formal => line.f,
                NpcVoice.Register.Curt => line.c,
                NpcVoice.Register.Haughty => line.h,
                _ => line.w,
            };
            return FirstOf(byLevel, line.w, line.t);
        }

        static string FirstOf(params string[] options)
        {
            foreach (var o in options) if (!string.IsNullOrEmpty(o)) return o;
            return "";
        }

        /// <summary>Fills the names (with the particles that follow them) and the rumor.</summary>
        public static string Fill(string text, string nameA, string nameB, string rumor)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var s = text;
            foreach (var (key, raw) in new[] { ("A", nameA), ("B", nameB) })
            {
                string n = string.IsNullOrEmpty(raw) ? "그쪽" : raw;
                s = s.Replace("{" + key + "이}", n + MemoryRecorder.Josa(n, "이", "가"))
                     .Replace("{" + key + "을}", n + MemoryRecorder.Josa(n, "을", "를"))
                     .Replace("{" + key + "와}", n + MemoryRecorder.Josa(n, "과", "와"))
                     .Replace("{" + key + "은}", n + MemoryRecorder.Josa(n, "은", "는"))
                     .Replace("{" + key + "아}", n + MemoryRecorder.Josa(n, "아", "야"))
                     .Replace("{" + key + "}", n);
            }
            return s.Replace("{rumor}", (rumor ?? "").Trim().TrimEnd('.', ' '));
        }

        /// <summary>The script as lines to play: (0 = A speaks, 1 = B speaks, text).</summary>
        public static List<(int speaker, string text)> Render(TalkScriptDto script, TalkSide a, TalkSide b, string rumor)
        {
            var result = new List<(int, string)>();
            if (script?.lines == null) return result;
            foreach (var line in script.lines)
            {
                int who = line.s == "B" ? 1 : 0;
                string text = Fill(LineFor(line, who == 0 ? a : b), a.Name, b.Name, rumor);
                if (!string.IsNullOrWhiteSpace(text)) result.Add((who, text));
            }
            return result;
        }
    }
}
