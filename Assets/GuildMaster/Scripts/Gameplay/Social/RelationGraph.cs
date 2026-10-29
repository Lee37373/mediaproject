using System;
using GuildMaster.Adventurers;

namespace GuildMaster.Social
{
    /// <summary>
    /// The numbers behind the relationship graph (F-10): one line per pair, how strong, which way. Feelings are
    /// one-directional in the game (A's feeling for B is not B's for A); the graph shows the average of whatever
    /// exists. Members keep feelings for each other (<see cref="Relationships"/>), residents for everyone they have
    /// talked to (<see cref="ResidentProfile.FeelingToward"/>). Reads only: never seeds a relationship that does not
    /// exist yet. Pure, covered by tests.
    /// </summary>
    public static class RelationGraph
    {
        /// <summary>Lines weaker than this are not drawn (everyone starts near 0).</summary>
        public const int ShowFrom = 5;
        public const float MinThickness = 2f;
        public const float MaxThickness = 10f;

        /// <summary>A member's existing feeling toward someone, or null when they have none yet.</summary>
        public static int? Peek(AdventurerData from, string toId)
        {
            if (from?.relations == null || string.IsNullOrEmpty(toId)) return null;
            foreach (var r in from.relations)
                if (r.id == toId) return r.value;
            return null;
        }

        /// <summary>A resident's feeling toward someone, or null when they have not met.</summary>
        public static int? Peek(ResidentProfile from, string toId)
        {
            if (from == null || string.IsNullOrEmpty(toId)) return null;
            int v = from.FeelingToward(toId);
            return v != 0 ? (int?)v : null;
        }

        /// <summary>The line between two people: the average of the directions that exist (0 = none).</summary>
        public static int Bond(int? ab, int? ba)
        {
            if (ab.HasValue && ba.HasValue) return (int)Math.Round((ab.Value + ba.Value) / 2f, MidpointRounding.AwayFromZero);
            return ab ?? ba ?? 0;
        }

        public static bool Shown(int bond) => Math.Abs(bond) >= ShowFrom;

        /// <summary>Line thickness in UI pixels for a bond of -100..100.</summary>
        public static float Thickness(int bond)
            => MinThickness + (MaxThickness - MinThickness) * Math.Min(1f, Math.Abs(bond) / 100f);

        public static string Describe(int? v) => v.HasValue ? (v.Value > 0 ? "+" : "") + v.Value : "모름";
    }
}
