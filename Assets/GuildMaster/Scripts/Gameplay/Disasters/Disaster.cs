using System.Collections.Generic;
using GuildMaster.Quests;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Disasters
{
    /// <summary>
    /// One natural disaster under way (P4-72): where it began, the cells it covers now, its core for
    /// the kinds worked at a core, and what the guild knows of it. Run by <see cref="DisasterService"/>.
    /// </summary>
    public class Disaster
    {
        public int Id { get; internal set; }
        public DisasterKind Kind { get; internal set; }
        public Vector2Int Origin { get; internal set; }
        public float StartedHour { get; internal set; }

        /// <summary>Cells it covers right now (burning, poisoned, buried, in the storm, split open).</summary>
        public readonly HashSet<Vector2Int> Cells = new();

        /// <summary>Wildfire: the hour each burning cell caught.</summary>
        internal readonly Dictionary<Vector2Int, float> LitAt = new();
        /// <summary>Work done on each cell so far (worker game hours).</summary>
        internal readonly Dictionary<Vector2Int, float> WorkDone = new();
        /// <summary>Spread owed but not yet laid down (fractions of a cell).</summary>
        internal float GrowthOwed;
        /// <summary>Sandstorm: where it is heading.</summary>
        internal Vector2 Drift;
        internal float NextStormRefresh;

        /// <summary>Kinds worked at a core (<see cref="DisasterRules.HasCore"/>): where it is, and the work it still needs.</summary>
        public Vector2 Core { get; internal set; }
        public float CoreWorkLeft { get; internal set; }

        /// <summary>The guild has seen it (only then does the town post a quest, P4-72 information rule).</summary>
        public bool Known { get; internal set; }
        public float SightedHour { get; internal set; }
        /// <summary>The town's quest for it, once posted.</summary>
        public Quest Quest { get; internal set; }

        /// <summary>No longer spreading: dealt with, or its time is up. Its cells clear away.</summary>
        public bool Ending { get; internal set; }
        public float EndingSinceHour { get; internal set; }
        /// <summary>The guild's work put an end to it (rather than it dying down by itself).</summary>
        public bool DealtWith { get; internal set; }
        /// <summary>All gone.</summary>
        public bool Over { get; internal set; }
        /// <summary>It has come up to the town walls.</summary>
        public bool ReachedTown { get; internal set; }
        /// <summary>Dens already abandoned because of it.</summary>
        internal readonly HashSet<Ecosystem.Nest> Fled = new();

        public HazardKind Hazard => DisasterRules.HazardOf(Kind);
        public string Name => DisasterRules.NameOf(Kind);
        public bool HasCore => DisasterRules.HasCore(Kind);
        public Vector2 WorldOrigin => GridMap.CellToWorld(Origin);

        /// <summary>What is left to work: cells, or 1 for an untouched core.</summary>
        public int WorkLeft => HasCore ? (Ending ? 0 : 1) : Cells.Count;
    }
}
