using System.Collections.Generic;
using GuildMaster.Data;
using GuildMaster.Monsters;
using UnityEngine;

namespace GuildMaster.Nemeses
{
    /// <summary>
    /// One nemesis (P4-109): the monster, what it was called, the name it was given, what it did to the guild, where it was
    /// last seen and its bounty — and, once it has fallen, who brought it down. Kept by <see cref="NemesisService"/>.
    /// </summary>
    public class Nemesis
    {
        /// <summary>Its monster's id (saved; the monster is found again by it).</summary>
        public string UnitId;
        /// <summary>The monster while it lives (null once fallen: its pooled body is someone else's).</summary>
        public Monster Monster;
        public MonsterDefinition Species;
        /// <summary>What it was called before ("고블린 족장", "황금 고블린"), and the name it was given ("그릭"; none for the rare one, the dragon, a lord).</summary>
        public string BaseName, Personal;
        public readonly List<NemesisDeed> Deeds = new();
        public float SinceHour;
        public Vector2 LastSeen;
        public float LastSeenHour;
        /// <summary>Its level as the guild last saw it.</summary>
        public int Level;

        public bool Fallen;
        public float FallenHour;
        /// <summary>The member who brought it down, null when it fell to anything else.</summary>
        public string SlainBy;

        /// <summary>The town's bounty on it (a <see cref="Incidents.IncidentKind.Nemesis"/> incident), once posted.</summary>
        public Incidents.Incident Bounty;
        /// <summary>A bounty waits for the fight it is in to end.</summary>
        public bool BountyDue;

        public string Title => NemesisRules.Title(Deeds);
        /// <summary>"레오 잡이 고블린 그릭".</summary>
        public string Name => NemesisRules.FullName(Title, BaseName, Personal);
        /// <summary>"그릭" (what members call it), or what it was called when it has no name of its own.</summary>
        public string ShortName => string.IsNullOrEmpty(Personal) ? BaseName : Personal;

        public int Kills
        {
            get
            {
                int k = 0;
                foreach (var d in Deeds) if (d.kind == DeedKind.Killed) k++;
                return k;
            }
        }

        /// <summary>It grows with its deeds and gets a bounty: not a region lord or the dragon (they have their own hunts).</summary>
        public bool Grows => Species != null && !Species.isRegionLord && !Species.isDragon;
    }
}
