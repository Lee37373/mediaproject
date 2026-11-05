using System;
using System.Collections.Generic;
using System.Linq;

namespace GuildMaster.Social
{
    /// <summary>Something people in town talk about.</summary>
    public class Rumor
    {
        public int Id;
        public string Text;
        /// <summary>Ids of the people it is about (never passed on to their face).</summary>
        public List<string> Subjects = new();
        /// <summary>How it reflects on the guild for a visitor: -1 bad, 0 none, +1 good.</summary>
        public int GuildTone;
        /// <summary>How it reflects on the first subject: -1 bad, 0 none, +1 good.</summary>
        public int SubjectTone;
        /// <summary>Game hour it started.</summary>
        public float Hour;
        public HashSet<string> KnownBy = new();

        public bool IsAbout(string id) => id != null && Subjects.Contains(id);
    }

    /// <summary>
    /// Who has heard what (user request 2026-10-01: "소문·뒷담"). Rumors start with the people who were there — a
    /// quest's party, the two in a fight — and spread one talk at a time; what a visitor hears about the guild
    /// changes their mind about joining, and what someone hears about a member colours how they feel about them.
    /// Old rumors die out. Pure logic (ids and hours), covered by EditMode tests.
    /// </summary>
    public class RumorBook
    {
        /// <summary>Game hours after which nobody bothers to pass a rumor on.</summary>
        public const float LifetimeHours = 72f;
        public const int Capacity = 40;
        /// <summary>The same text within this many hours is the same rumor (knowers are merged).</summary>
        const float SameWithinHours = 12f;

        readonly List<Rumor> _rumors = new();
        int _nextId = 1;

        public IReadOnlyList<Rumor> All => _rumors;

        public Rumor Add(string text, float hour, IEnumerable<string> knownBy, IEnumerable<string> subjects = null, int guildTone = 0, int subjectTone = 0)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var same = _rumors.FirstOrDefault(r => r.Text == text && hour - r.Hour < SameWithinHours);
            if (same != null)
            {
                if (knownBy != null) foreach (var k in knownBy) if (k != null) same.KnownBy.Add(k);
                return same;
            }
            var rumor = new Rumor
            {
                Id = _nextId++, Text = text, Hour = hour, GuildTone = guildTone, SubjectTone = subjectTone,
                Subjects = subjects?.Where(s => s != null).ToList() ?? new List<string>(),
            };
            if (knownBy != null) foreach (var k in knownBy) if (k != null) rumor.KnownBy.Add(k);
            _rumors.Add(rumor);
            while (_rumors.Count > Capacity) _rumors.RemoveAt(0);
            return rumor;
        }

        /// <summary>
        /// The freshest rumor <paramref name="speaker"/> can tell <paramref name="listener"/>: known to the speaker, new
        /// to the listener, not about the listener, still alive.
        /// </summary>
        public Rumor Pick(string speaker, string listener, float now)
        {
            Rumor best = null;
            foreach (var r in _rumors)
            {
                if (now - r.Hour > LifetimeHours) continue;
                if (!r.KnownBy.Contains(speaker) || r.KnownBy.Contains(listener) || r.IsAbout(listener)) continue;
                if (best == null || r.Hour > best.Hour) best = r;
            }
            return best;
        }

        public bool HasNewsFor(string speaker, string listener, float now) => Pick(speaker, listener, now) != null;

        /// <summary>Marks the rumor as heard. True the first time.</summary>
        public bool Learn(Rumor r, string listener) => r != null && listener != null && r.KnownBy.Add(listener);

        /// <summary>Everyone who knows <paramref name="id"/> leaves town for good (left the guild, visitor gone).</summary>
        public void Forget(string id)
        {
            foreach (var r in _rumors) r.KnownBy.Remove(id);
        }

        public void Prune(float now) => _rumors.RemoveAll(r => now - r.Hour > LifetimeHours);

        /// <summary>The id the next rumor gets (for saving).</summary>
        public int NextId => _nextId;

        /// <summary>Puts saved rumors back (P4-119), replacing the ones there.</summary>
        public void Restore(IEnumerable<Rumor> rumors, int nextId)
        {
            _rumors.Clear();
            if (rumors != null) _rumors.AddRange(rumors.Where(r => r != null && !string.IsNullOrWhiteSpace(r.Text)));
            while (_rumors.Count > Capacity) _rumors.RemoveAt(0);
            _nextId = Math.Max(nextId, _rumors.Count == 0 ? 1 : _rumors.Max(r => r.Id) + 1);
        }
    }
}
