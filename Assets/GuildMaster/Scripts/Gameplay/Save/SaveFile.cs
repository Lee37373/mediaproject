using System;
using System.Collections.Generic;

namespace GuildMaster.Save
{
    /// <summary>
    /// One save slot on disk (P5-4): the world seed, a summary for the slot list, and one section per
    /// system (<see cref="Core.ISaveable"/>), each holding that system's own JSON.
    /// </summary>
    [Serializable]
    public class SaveFile
    {
        /// <summary>Raised only for changes old saves cannot be read under; new sections are not one.</summary>
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public int seed;
        /// <summary>Real time of saving (UTC, round-trip format).</summary>
        public string saved_at;
        public SaveSummary summary = new();
        public List<SaveSection> sections = new();

        public SaveSection Find(string key) => sections.Find(s => s.key == key);
        public bool Has(string key) => Find(key) != null;
    }

    /// <summary>What the slot list shows without reading the sections.</summary>
    [Serializable]
    public class SaveSummary
    {
        public int day;
        public int hour;
        public float total_hours;
        public int gold;
        public int members;
    }

    [Serializable]
    public class SaveSection
    {
        public string key;
        public string json;
    }
}
