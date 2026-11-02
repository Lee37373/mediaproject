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
    /// - Scouting has no spot drawn round the town (P4-121, user intent 2026-11-02: "the route is updated every time
    ///   from what they see now and how the party stands"): each leg is picked from where the band stands
    ///   (<see cref="ScoutLeg"/>) by a fresh <see cref="LegView"/> — the fog, what it sees and cannot take, how worn it is.
    /// </summary>
    public static class ExplorationRoute
    {
        /// <summary>How far ahead a band commits before looking again.</summary>
        public const float LegLength = 18f;
        /// <summary>Headings tried either side of the straight line, in degrees.</summary>
        static readonly float[] Fan = { 0f, 22f, -22f, 45f, -45f, 70f, -70f, 100f, -100f };
        /// <summary>Headings a scouting leg is picked from: all round, every 22.5°.</summary>
        const int ScoutHeadings = 16;
        /// <summary>What unseen country round a leg's end is worth, all of it unseen (and half that a leg further on).</summary>
        const float ExploreWeight = 8f;
        /// <summary>What going on the way the last leg went is worth, per unit of cosine: no turning back and forth.</summary>
        const float KeepHeadingWeight = 2.5f;
        /// <summary>What a worn band (<see cref="LegView.Strain"/> 1) gives up per cell a leg takes it further from town.</summary>
        const float HomePull = 0.8f;
        /// <summary>What a leg ending past the band's reach costs, per cell past it.</summary>
        const float BeyondReachWeight = 1.5f;
        /// <summary>What a sampled cell inside the town costs a leg: bands walk round it, not across.</summary>
        const float InTownPenalty = 2f;
        /// <summary>What a cell into something to keep away from costs, at the leg's end and again at its nearest pass.</summary>
        const float AvoidWeight = 2f;
        /// <summary>A leg ending in deep woods the guild knows of: in there a scout sees a cell or two, and is ambushed (P4-103).</summary>
        const float KnownDeepPenalty = 6f;
        /// <summary>Radius round a leg's end its unseen share is read over.</summary>
        const float LookRadius = 10f;

        /// <summary>
        /// What a band takes into account when it aims a leg (P4-121), set fresh by its leader for every leg: where it
        /// stands now, not where it stood when it left the gate.
        /// </summary>
        public class LegView
        {
            /// <summary>The town's middle: a worn band leans back towards it.</summary>
            public Vector2 Town;
            /// <summary>How far from town the band goes now (its strength, <see cref="FreeHunt.RoamRadius"/>).</summary>
            public float Reach = float.PositiveInfinity;
            /// <summary>0 fresh .. 1 due home (<see cref="FreeHunt.Strain"/>): legs lean towards town by this much.</summary>
            public float Strain;
            /// <summary>The way the last leg went (zero at the start): the band keeps on rather than turn back.</summary>
            public Vector2 Heading;
            /// <summary>What the band would rather not walk up to: monsters in sight none of it could take, dens it knows it cannot.</summary>
            public readonly List<(Vector2 at, float radius)> Avoid = new();
            /// <summary>Breaks ties between like headings (null: none).</summary>
            public Core.GameRandom Rng;
        }
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
        /// <paramref name="view"/> (P4-121): what the band sees now — headings up to a monster it cannot take, or across
        /// the town, are worth less.
        /// </summary>
        public static Vector2 NextLeg(WorldService world, System.Func<int, int, bool> unseen, Vector2 from, Vector2 goal,
            float caution, bool scouting = false, LegView view = null)
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
                float score = Score(world, unseen, from, snapped, goal, caution, scouting) - Hindrance(world, from, snapped, view);
                if (score <= bestScore) continue;
                bestScore = score;
                best = snapped;
            }
            return best;
        }

        /// <summary>
        /// A scouting leg picked from where the band stands (P4-121, user intent 2026-11-02): all round, the heading that
        /// puts eyes on the most country nobody has seen, going on the way it went, within its reach, round the town
        /// rather than across it, clear of what it saw and could not take, and — the more worn it is — back towards town.
        /// It used to walk to a spot drawn at random round the town, wherever it stood: from one side to the other and
        /// back across the town, and redrawn every survey while something too strong was in sight.
        /// <paramref name="from"/> itself when no heading can be walked.
        /// </summary>
        public static Vector2 ScoutLeg(WorldService world, System.Func<int, int, bool> unseen, Vector2 from, float caution, LegView view)
        {
            if (world == null) return from;
            view ??= new LegView { Town = from };
            float bestScore = float.NegativeInfinity;
            var best = from;
            float outNow = Vector2.Distance(from, view.Town);
            bool going = view.Heading.sqrMagnitude > 1e-4f;
            var heading = going ? view.Heading.normalized : Vector2.zero;
            for (int i = 0; i < ScoutHeadings; i++)
            {
                float angle = i * Mathf.PI * 2f / ScoutHeadings;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                if (!Reachable(world.Grid, unseen, from + dir * LegLength, out var end)) continue;
                float score = ExploreWeight * (UnseenAround(world.Grid, unseen, end) + 0.5f * UnseenAround(world.Grid, unseen, end + dir * LegLength))
                              + Score(world, unseen, from, end, end, caution, true)
                              - Hindrance(world, from, end, view);
                if (going) score += KeepHeadingWeight * Vector2.Dot(dir, heading);
                float outThere = Vector2.Distance(end, view.Town);
                score -= view.Strain * HomePull * (outThere - outNow);
                score -= BeyondReachWeight * Mathf.Max(0f, outThere - view.Reach);
                var cell = GridMap.WorldToCell(end);
                if (!IsUnseen(unseen, cell) && world.Grid.IsDeep(cell)) score -= KnownDeepPenalty;
                if (view.Rng != null) score += view.Rng.Range(0f, 0.4f);
                if (score <= bestScore) continue;
                bestScore = score;
                best = end;
            }
            return best;
        }

        /// <summary>
        /// What a leg costs for what it walks into (P4-121): town cells along it, and how far it goes into the circles of
        /// <see cref="LegView.Avoid"/> — by its end and by its nearest pass, so a leg slipping by under a monster's nose
        /// costs as well as one ending there, and of two legs both inside one, the one leaving it wins.
        /// </summary>
        static float Hindrance(WorldService world, Vector2 from, Vector2 end, LegView view)
        {
            if (view == null) return 0f;
            float cost = 0f;
            for (int i = 1; i <= Samples; i++)
                if (world.Grid.IsInTown(GridMap.WorldToCell(Vector2.Lerp(from, end, i / (float)Samples)))) cost += InTownPenalty;
            foreach (var (at, radius) in view.Avoid)
            {
                cost += AvoidWeight * Mathf.Max(0f, radius - Vector2.Distance(end, at));
                cost += AvoidWeight * Mathf.Max(0f, radius - ClosestPass(from, end, at));
            }
            return cost;
        }

        /// <summary>How close a straight walk from <paramref name="a"/> to <paramref name="b"/> comes to <paramref name="p"/>.</summary>
        public static float ClosestPass(Vector2 a, Vector2 b, Vector2 p)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(a + ab * t, p);
        }

        /// <summary>Share of the ground round <paramref name="centre"/> (a 4-cell lattice within <see cref="LookRadius"/>) nobody has seen; off the map counts as seen.</summary>
        static float UnseenAround(GridMap grid, System.Func<int, int, bool> unseen, Vector2 centre)
        {
            if (unseen == null) return 0f;
            int total = 0, dark = 0;
            for (float dy = -8f; dy <= 8f; dy += 4f)
            for (float dx = -8f; dx <= 8f; dx += 4f)
            {
                if (dx * dx + dy * dy > LookRadius * LookRadius) continue;
                total++;
                var c = GridMap.WorldToCell(centre + new Vector2(dx, dy));
                if (grid.InBounds(c) && unseen(c.x, c.y)) dark++;
            }
            return total == 0 ? 0f : dark / (float)total;
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
