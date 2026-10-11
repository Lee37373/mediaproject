using System.Collections.Generic;
using GuildMaster.Fog;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// How a band walks the country it does not know (P4-64, user request 2026-09-23): not one long
    /// route drawn over the whole map, but a leg at a time, read off the ground it can see.
    ///
    /// - A leg is at most <see cref="LegLength"/> cells towards the goal, so the band commits only
    ///   as far as it can make out and re-aims when it gets there.
    /// - The heading is chosen among a fan around the straight line by what is known: ground already
    ///   seen to be blocked costs the leg, risky cover costs it by the party's nerve
    ///   (<see cref="Units.RouteChoice"/>), and unseen ground costs nothing — it is what a scout
    ///   walks towards.
    /// - Planning and correcting the walk itself is <see cref="PathFollower.UnknownToMe"/>: the route
    ///   treats unseen cells as open and is redrawn the moment the band sees otherwise.
    /// </summary>
    public static class ExplorationRoute
    {
        /// <summary>How far ahead a band commits before looking again.</summary>
        public const float LegLength = 18f;
        /// <summary>Headings tried either side of the straight line, in degrees.</summary>
        static readonly float[] Fan = { 0f, 22f, -22f, 45f, -45f, 70f, -70f, 100f, -100f };
        /// <summary>Points sampled along a candidate leg.</summary>
        const int Samples = 9;
        /// <summary>What one sampled cell of ground already seen to be impassable costs a leg.</summary>
        const float BlockedPenalty = 3f;
        /// <summary>What being drawn away from the goal costs, per cell of lost progress.</summary>
        const float DetourWeight = 1f;
        /// <summary>What one sampled cell of unseen ground is worth while scouting.</summary>
        const float ScoutBonus = 0.6f;

        /// <summary>
        /// Most unseen cells <see cref="OpensOut"/> floods before it calls a patch of fog open country:
        /// bigger than the inside of any rock or lake a band walks round.
        /// </summary>
        public const int PocketProbe = 400;

        static readonly Vector2Int[] Steps = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        static readonly Queue<Vector2Int> s_flood = new();
        static readonly HashSet<Vector2Int> s_flooded = new();

        /// <summary>
        /// The next leg towards <paramref name="goal"/>, or the goal itself when it is within reach.
        /// <paramref name="unseen"/> is what the guild has never seen (<see cref="KnowledgeOf"/>, the
        /// walker's <see cref="PathFollower.UnknownToMe"/>; null: everything is known).
        /// <paramref name="scouting"/> rewards headings into the fog (that is the point of the trip).
        /// </summary>
        public static Vector2 NextLeg(WorldService world, System.Func<int, int, bool> unseen, Vector2 from, Vector2 goal,
            float caution, bool scouting = false)
        {
            if (world == null) return goal;
            var toGoal = goal - from;
            float distance = toGoal.magnitude;
            if (distance <= LegLength) return goal;

            var straight = toGoal / distance;
            float bestScore = float.NegativeInfinity;
            var best = from + straight * LegLength;
            foreach (float degrees in Fan)
            {
                var dir = Rotate(straight, degrees * Mathf.Deg2Rad);
                var end = from + dir * LegLength;
                if (!Reachable(world.Grid, unseen, end, out var snapped)) continue;
                float score = Score(world, unseen, from, snapped, goal, caution, scouting);
                if (score <= bestScore) continue;
                bestScore = score;
                best = snapped;
            }
            return best;
        }

        /// <summary>
        /// A leg end has to be somewhere the band could stand: unseen ground counts, unless what has
        /// been seen around it walls it in (<see cref="OpensOut"/>).
        /// </summary>
        static bool Reachable(GridMap grid, System.Func<int, int, bool> unseen, Vector2 end, out Vector2 snapped)
        {
            var cell = GridMap.WorldToCell(end);
            snapped = end;
            if (!grid.InBounds(cell)) return false;
            if (IsUnseen(unseen, cell)) return OpensOut(grid, unseen, cell);
            if (grid.IsWalkable(cell)) return true;
            if (!grid.TryFindNearestWalkable(cell, 3, out cell)) return false;
            snapped = GridMap.CellToWorld(cell);
            return !IsUnseen(unseen, cell) || OpensOut(grid, unseen, cell);
        }

        /// <summary>
        /// Whether the patch of unseen ground around <paramref name="cell"/> opens onto ground seen to
        /// be walkable — or is too big to tell (<paramref name="probe"/> cells). False when everything
        /// seen around it is rock, water or the map's edge (P4-80: the middle of a rock outcrop the
        /// guild had only seen the rim of was taken for open country; a leg aimed there could never be
        /// walked, every try searched 20 000 cells, and the band stood outside the gate re-aiming at
        /// it for two hours). Four-way, as walkers cannot cut a corner between two walls. Only what
        /// the guild has seen is used.
        /// </summary>
        public static bool OpensOut(GridMap grid, System.Func<int, int, bool> unseen, Vector2Int cell, int probe = PocketProbe)
        {
            if (!IsUnseen(unseen, cell)) return grid.IsWalkable(cell);
            s_flood.Clear();
            s_flooded.Clear();
            s_flood.Enqueue(cell);
            s_flooded.Add(cell);
            int count = 0;
            while (s_flood.Count > 0)
            {
                if (++count > probe) return true;
                var c = s_flood.Dequeue();
                foreach (var step in Steps)
                {
                    var n = c + step;
                    if (!grid.InBounds(n) || !s_flooded.Add(n)) continue;
                    if (unseen(n.x, n.y)) s_flood.Enqueue(n);
                    else if (grid.IsWalkable(n)) return true;
                }
            }
            return false;
        }

        static bool IsUnseen(System.Func<int, int, bool> unseen, Vector2Int c) => unseen != null && unseen(c.x, c.y);

        /// <summary>
        /// Worth of a leg: progress towards the goal, less what is known to stand in the way and the
        /// cover the party would rather avoid, plus the unseen ground it would put eyes on.
        /// </summary>
        static float Score(WorldService world, System.Func<int, int, bool> unseen, Vector2 from, Vector2 end, Vector2 goal,
            float caution, bool scouting)
        {
            float progress = Vector2.Distance(from, goal) - Vector2.Distance(end, goal);
            float blocked = 0f, risk = 0f, unseenCells = 0f;
            for (int i = 1; i <= Samples; i++)
            {
                var p = Vector2.Lerp(from, end, i / (float)Samples);
                var c = GridMap.WorldToCell(p);
                if (!world.Grid.InBounds(c)) { blocked++; continue; }
                if (IsUnseen(unseen, c)) { unseenCells++; continue; } // nothing is known here, good or bad
                if (!world.Grid.IsWalkable(c)) { blocked++; continue; }
                risk += world.Risk.At(c.x, c.y);
            }
            return progress * DetourWeight
                   - blocked * BlockedPenalty
                   - risk * Mathf.Max(0f, caution)
                   + (scouting ? unseenCells * ScoutBonus : 0f);
        }

        /// <summary>Ground the guild has never laid eyes on (no fog at all: everything is known).</summary>
        public static bool Unseen(FogOfWar fog, Vector2Int cell) => fog != null && !fog.IsExploredRaw(cell);

        /// <summary>Predicate for <see cref="PathFollower.UnknownToMe"/>; null when there is no fog.</summary>
        public static System.Func<int, int, bool> KnowledgeOf(FogOfWar fog)
            => fog == null ? null : (x, y) => !fog.IsExploredRaw(new Vector2Int(x, y));

        static Vector2 Rotate(Vector2 v, float radians)
        {
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }
}
