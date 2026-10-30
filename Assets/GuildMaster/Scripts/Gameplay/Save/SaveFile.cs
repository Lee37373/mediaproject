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
        /// <summary>
        /// Raised only for changes old saves cannot be read under; new sections are not one.
        /// 2: the map grew to 512 (P4-81) — a seed no longer makes the map a version-1 save was played on.
        /// 3: the underground band above the surface (P4-85 ruins) — the fog of a version-2 save no longer fits the map.
        /// 4: ridges, rivers and landforms (P4-101) — a seed makes another map, with the dens elsewhere.
        /// 5: zones, their landmarks and the road network (P4-102) — again another map for the same seed.
        /// 6: deep woods instead of the ridges on the region borders (P4-103) — another map again.
        /// 7: 96 dens instead of 64 and thirteen new kinds (P4-110) — the dens of a version-6 save are not where a seed puts them now.
        /// </summary>
        public const int CurrentVersion = 7;

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
