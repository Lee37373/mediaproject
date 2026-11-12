using UnityEngine;

namespace GuildMaster.Incidents
{
    /// <summary>
    /// The four sudden incidents (P4-85), and the town's bounty on a nemesis (P4-109). The order is kept: saves store the number.
    /// </summary>
    public enum IncidentKind { RareMonster, Horde, Rescue, Ruin, Nemesis }

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
        /// <summary>The rare variant (or the nemesis, P4-109) while it lives (cleared when it dies: its pooled body is reused).</summary>
        public Monsters.Monster Target;
        /// <summary>Its name and level, kept for the log after it is gone.</summary>
        public string TargetName;
        public int TargetLevel;
        /// <summary>What it asks of a party (<see cref="IncidentRules.Requirement"/>), fixed when the rumour came.</summary>
        public float Requirement;

        // ---- Horde ----
        /// <summary>The horde's living members (the fallen are pruned every check; their pooled bodies are reused later).</summary>
        public readonly System.Collections.Generic.List<Monsters.Monster> Members = new();
        /// <summary>How many set out, its kind's name, and its level (for the rumour and the log).</summary>
        public int HordeSize;
        public string SpeciesName;
        public int HordeLevel;
        /// <summary>Its march (<see cref="Ecosystem.MigrationService.Band"/>) while it is on the way.</summary>
        public Ecosystem.MigrationService.Band Band;
        /// <summary>The site it is heading for (where it settles if nobody stops it). Not for the guild.</summary>
        public Ecosystem.Nest Site;
        /// <summary>Game hour the rumour is next brought up to date (a moving horde, while unseen).</summary>
        public float NextRumourHour;

        // ---- Rescue (the ambushers are in Members) ----
        /// <summary>The traveller while it lives and is on the map (it goes on its way once saved, and is gone on arrival).</summary>
        public Traveller Traveller;
        /// <summary>Where it was going.</summary>
        public Vector2 Destination;

        // ---- Ruin (its guardians are in Members) ----
        /// <summary>Which ruin (<see cref="World.MapData.Ruins"/>).</summary>
        public int RuinIndex = -1;
        public bool ChestOpened;

        // ---- Nemesis bounty (the nemesis is Target) ----
        /// <summary>Its pack as the guild last saw it (the hunt's danger), and what the bounty pays over a hunt of that danger.</summary>
        public int PackSize;
        public float RewardFactor = 1f;
        /// <summary>
        /// The level of the strongest other nemesis the guild knows lives in its den (as last seen; 0: none): the bounty is
        /// fought among them, so it is rated by the worst of them (<see cref="IncidentRules.NemesisDanger(Incident)"/>).
        /// </summary>
        public int PackLevel;

        /// <summary>A monster this incident's quest is about: the rare one, a member of the horde, an ambusher, a guardian.</summary>
        public bool IsTarget(Monsters.Monster m) => m != null && (m == Target || Members.Contains(m));

        /// <summary>Key under which a loaded incident is registered (quests point at it).</summary>
        public static string SaveKeyOf(int id) => "incident:" + id;
    }
}
