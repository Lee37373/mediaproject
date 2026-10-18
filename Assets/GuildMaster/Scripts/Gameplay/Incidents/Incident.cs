using UnityEngine;

namespace GuildMaster.Incidents
{
    /// <summary>The four sudden incidents (P4-85). The order is kept: saves store the number.</summary>
    public enum IncidentKind { RareMonster, Horde, Rescue, Ruin }

    /// <summary>How an incident ended (<see cref="None"/> while it goes on).</summary>
    public enum IncidentOutcome
    {
        None,
        /// <summary>Its quest was done (the rare one slain by the guild...).</summary>
        Resolved,
        /// <summary>Somebody else settled it (a monster, a member on no quest for it).</summary>
        SettledByOthers,
        /// <summary>Its time ran out (the rare one slipped away...).</summary>
        Expired,
    }

    /// <summary>
    /// One sudden incident (P4-85): it happens somewhere on the map, the town hears a rumour some time later that
    /// puts it roughly somewhere (a circle), a client posts a quest for it, and it ends by the quest, by someone
    /// else, or by running out of time. What the guild knows (<see cref="Rumoured"/>, <see cref="Sighted"/>) is kept
    /// apart from what is true (<see cref="Spot"/>, <see cref="Target"/>): only the first may reach the UI and prompts.
    /// </summary>
    public class Incident
    {
        public int Id;
        public IncidentKind Kind;
        public float StartedHour;
        /// <summary>Where it really happened (the rare one's lair spot). Not for the guild.</summary>
        public Vector2 Spot;
        /// <summary>Game hour its rumour reaches the town.</summary>
        public float RumourAtHour;
        /// <summary>Game hour its time is up (the rare one leaves).</summary>
        public float EndsAtHour;

        // ---- What the guild knows ----
        public bool Rumoured;
        /// <summary>The rumour's circle (the true spot lies inside it).</summary>
        public Vector2 RumourCentre;
        public float RumourRadius;
        /// <summary>The guild has seen it with its own eyes.</summary>
        public bool Sighted;
        public Vector2 LastSeen;
        public float LastSeenHour;

        public bool Over => Outcome != IncidentOutcome.None;
        public IncidentOutcome Outcome;
        public float EndedHour;

        /// <summary>The quest the rumour brought (stays set once it is closed).</summary>
        public Quests.Quest Quest;

        // ---- Rare monster ----
        /// <summary>The rare variant while it lives (cleared when it dies: its pooled body is reused).</summary>
        public Monsters.Monster Target;
        /// <summary>Its name and level, kept for the log after it is gone.</summary>
        public string TargetName;
        public int TargetLevel;
        /// <summary>What it asks of a party (<see cref="IncidentRules.Requirement"/>), fixed when the rumour came.</summary>
        public float Requirement;

        /// <summary>Key under which a loaded incident is registered (quests point at it).</summary>
        public static string SaveKeyOf(int id) => "incident:" + id;
    }
}
