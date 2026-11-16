using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Legacy
{
    /// <summary>What became of one thing a fallen member dropped.</summary>
    public enum RelicState { Lying, Recovered, Lost }

    /// <summary>One item a fallen member dropped where they died (their gear and what they carried).</summary>
    public class RelicItem
    {
        public string ItemId;
        public string Name;
        public int Count;
        public Vector2 Position;
        public RelicState State;
        /// <summary>Who brought it back, or what took it / why it is gone.</summary>
        public string Note;
        /// <summary>The item on the map while <see cref="State"/> is Lying (not kept once resolved).</summary>
        internal Items.GroundItem Ground;
        /// <summary>Game hour the item was dropped (tells the item apart from a later one in a recycled ground object).</summary>
        internal float DroppedAtHour;
    }

    /// <summary>A friend's words about the fallen, kept on the stone (2026-11-15).</summary>
    public class Remembrance
    {
        public string Id;
        public string Name;
        public string Line;
    }

    /// <summary>
    /// A guild member who died (F-6): who they were, how they fell, what they left behind and what became of it,
    /// and whether the guild held a memorial. Plain data plus the small state changes that belong to it, so it is
    /// covered by EditMode tests.
    /// </summary>
    public class FallenRecord
    {
        public int Id;
        public string AdventurerId;
        public string Name;
        public string ClassLabel;
        /// <summary>The class itself, for the mark cut in the stone (2026-11-15).</summary>
        public Adventurers.AdventurerClass Cls;
        /// <summary>Titles they carried when they fell (P4-83: a region lord's fall) — a title makes a hero.</summary>
        public readonly List<string> Titles = new();
        public string Values;
        public int Level;
        public int Kills;
        public int QuestsDone;
        /// <summary>Who or what dealt the last blow (null = unknown).</summary>
        public string KillerName;
        public int Day;
        public float DiedAtHour;
        public Vector2 Position;
        public readonly List<RelicItem> Relics = new();
        /// <summary>Members who counted the dead as a friend when they fell (relieved when a relic comes home).</summary>
        public readonly List<string> FriendIds = new();
        /// <summary>How each member then in the guild felt about the dead when they fell (member id -> relation, -100..100).</summary>
        public readonly Dictionary<string, int> Relations = new();
        public bool MemorialHeld;
        public float MemorialHour;
        /// <summary>The friends have been relieved by a relic coming home (once per record).</summary>
        public bool RecoveryRewarded;

        // ---------------- The memorial wall (2026-11-15, MemorialRules) ----------------

        /// <summary>The line cut in the stone.</summary>
        public string Epitaph;
        /// <summary>Why they are a hero of the guild, null when not (a gold stone, fame when they fell).</summary>
        public string HeroReason;
        public bool IsHero => !string.IsNullOrEmpty(HeroReason);
        /// <summary>What they said last (heard just before the fall), or the line the wall gives them.</summary>
        public string LastWords;
        /// <summary>The closest friends' words about them.</summary>
        public readonly List<Remembrance> Remembrances = new();
        /// <summary>Who carries their relic now, and which one (once per record).</summary>
        public string HeirId, HeirName, HeirItem;
        public bool HasHeir => !string.IsNullOrEmpty(HeirId);
        /// <summary>Anniversaries remembered, and the day of the last (one a day at most).</summary>
        public int Anniversaries;
        public int LastAnniversaryDay;

        public int Count(RelicState state)
        {
            int n = 0;
            foreach (var r in Relics) if (r.State == state) n++;
            return n;
        }

        public bool HasRelics => Relics.Count > 0;
        public bool HasLying => Count(RelicState.Lying) > 0;

        /// <summary>Game hours until what still lies there rots away (0 when it is already past).</summary>
        public float HoursLeft(float now) => Mathf.Max(0f, DiedAtHour + LegacyRules.RelicHours - now);

        public void MarkRecovered(RelicItem relic, string by)
        {
            if (relic == null || relic.State != RelicState.Lying) return;
            relic.State = RelicState.Recovered;
            relic.Note = by;
            relic.Ground = null;
        }

        public void MarkLost(RelicItem relic, string why)
        {
            if (relic == null || relic.State != RelicState.Lying) return;
            relic.State = RelicState.Lost;
            relic.Note = why;
            relic.Ground = null;
        }

        /// <summary>The first relic that still lies on the ground, or null.</summary>
        public RelicItem FirstLying()
        {
            foreach (var r in Relics) if (r.State == RelicState.Lying) return r;
            return null;
        }
    }
}
