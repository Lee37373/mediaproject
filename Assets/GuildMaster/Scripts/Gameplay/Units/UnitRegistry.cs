using System;
using System.Collections.Generic;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Units
{
    /// <summary>
    /// Registry of live units with spatial queries. Queries go through a bucket grid (P5-2) built
    /// at most once a frame, on the first query after the frame starts or the roster changes: a
    /// query visits only the buckets its circle touches instead of every unit on the map.
    /// Distances are still checked against where units are now, so a unit that moved since the
    /// snapshot is found as long as it moved less than <see cref="Slack"/> (a whole frame of
    /// walking even at x8). One teleported further away is found from the next frame on.
    /// </summary>
    public static class UnitRegistry
    {
        static readonly List<Unit> s_units = new();

        public static ListView<Unit> All => new(s_units);

        // ---------------- Spatial index ----------------

        public const float BucketSize = 4f;
        const int Dim = 128, Mask = Dim - 1; // wraps every 512 units: far-apart units may share a bucket, never get lost
        /// <summary>How far a unit may have moved since the snapshot and still be found.</summary>
        const float Slack = 1.5f;

        static readonly int[] s_head = NewHeads();
        static int[] s_next = new int[256];
        static Unit[] s_slot = new Unit[256];
        static Vector2[] s_pos = new Vector2[256];
        static int s_count;
        static int s_builtFrame = -1;
        static bool s_dirty = true;

        static int[] NewHeads()
        {
            var h = new int[Dim * Dim];
            Array.Fill(h, -1);
            return h;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            s_units.Clear();
            Array.Fill(s_head, -1);
            Array.Clear(s_slot, 0, s_slot.Length);
            s_count = 0;
            s_builtFrame = -1;
            s_dirty = true;
        }

        public static void Register(Unit u)
        {
            if (s_units.Contains(u)) return;
            s_units.Add(u);
            s_dirty = true;
        }

        public static void Unregister(Unit u)
        {
            if (s_units.Remove(u)) s_dirty = true;
        }

        static int BucketCoord(float v) => Mathf.FloorToInt(v / BucketSize);
        static int Bucket(int bx, int by) => (by & Mask) * Dim + (bx & Mask);

        /// <summary>Snapshots every registered unit's position into the bucket grid now.</summary>
        public static void RebuildIndex()
        {
            for (int i = 0; i < s_count; i++)
                s_head[Bucket(BucketCoord(s_pos[i].x), BucketCoord(s_pos[i].y))] = -1;
            int n = s_units.Count;
            if (s_slot.Length < n)
            {
                int size = Mathf.NextPowerOfTwo(n);
                s_next = new int[size];
                s_slot = new Unit[size];
                s_pos = new Vector2[size];
            }
            else Array.Clear(s_slot, n, s_count > n ? s_count - n : 0);
            for (int i = 0; i < n; i++)
            {
                var u = s_units[i];
                var p = u.Position;
                int b = Bucket(BucketCoord(p.x), BucketCoord(p.y));
                s_slot[i] = u;
                s_pos[i] = p;
                s_next[i] = s_head[b];
                s_head[b] = i;
            }
            s_count = n;
            s_builtFrame = Time.frameCount;
            s_dirty = false;
        }

        /// <summary>
        /// Builds the index if it is stale. Outside play mode (edit-mode tests) the frame never
        /// advances while units move, so every query takes a fresh snapshot.
        /// </summary>
        static void EnsureIndex()
        {
            if (s_dirty || s_builtFrame != Time.frameCount || !Application.isPlaying) RebuildIndex();
        }

        /// <summary>Bucket rectangle covering a circle (plus slack); false when it is so wide a full scan is cheaper.</summary>
        static bool Range(Vector2 p, float radius, out int x0, out int y0, out int x1, out int y1)
        {
            float r = radius + Slack;
            x0 = BucketCoord(p.x - r); x1 = BucketCoord(p.x + r);
            y0 = BucketCoord(p.y - r); y1 = BucketCoord(p.y + r);
            return x1 - x0 < Dim && y1 - y0 < Dim;
        }

        // The query loops below are written out rather than shared through a callback so that a
        // query allocates nothing and costs no delegate call per unit.

        /// <summary>Nearest living unit within radius matching the predicate.</summary>
        public static Unit FindNearest(Vector2 from, float radius, Func<Unit, bool> predicate)
            => FindNearest(from, radius, predicate, static (u, p) => p(u));

        /// <summary>As above with state for the predicate (pass a static lambda: nothing is allocated).</summary>
        public static Unit FindNearest<T>(Vector2 from, float radius, T state, Func<Unit, T, bool> predicate)
        {
            EnsureIndex();
            Unit best = null;
            float bestSq = radius * radius;
            if (!Range(from, radius, out int x0, out int y0, out int x1, out int y1))
            {
                foreach (var u in s_units)
                {
                    if (!u.IsAlive) continue;
                    float sq = (u.Position - from).sqrMagnitude;
                    if (sq > bestSq || !predicate(u, state)) continue;
                    bestSq = sq;
                    best = u;
                }
                return best;
            }
            float loose = Sq(radius + Slack);
            for (int by = y0; by <= y1; by++)
            for (int bx = x0; bx <= x1; bx++)
            for (int i = s_head[Bucket(bx, by)]; i >= 0; i = s_next[i])
            {
                if ((s_pos[i] - from).sqrMagnitude > loose) continue;
                var u = s_slot[i];
                if (!u.IsAlive) continue;
                float sq = (u.Position - from).sqrMagnitude;
                if (sq > bestSq || !predicate(u, state)) continue;
                bestSq = sq;
                best = u;
            }
            return best;
        }

        /// <summary>Nearest living unit hostile to 'self' and in its line of sight (or that attacked it) within radius.</summary>
        public static Unit FindNearestHostile(Unit self, float radius)
            => FindNearest(self.Position, radius, self,
                static (u, self) => u != self && (self.WasRecentlyAttackedBy(u) || (self.IsHostileTo(u) && self.CanSee(u))));

        public static int CountInRadius(Vector2 from, float radius, Func<Unit, bool> predicate)
            => CountInRadius(from, radius, predicate, static (u, p) => p(u));

        public static int CountInRadius<T>(Vector2 from, float radius, T state, Func<Unit, T, bool> predicate)
        {
            EnsureIndex();
            int n = 0;
            float r2 = radius * radius;
            if (!Range(from, radius, out int x0, out int y0, out int x1, out int y1))
            {
                foreach (var u in s_units)
                    if (u.IsAlive && (u.Position - from).sqrMagnitude <= r2 && predicate(u, state)) n++;
                return n;
            }
            float loose = Sq(radius + Slack);
            for (int by = y0; by <= y1; by++)
            for (int bx = x0; bx <= x1; bx++)
            for (int i = s_head[Bucket(bx, by)]; i >= 0; i = s_next[i])
            {
                if ((s_pos[i] - from).sqrMagnitude > loose) continue;
                var u = s_slot[i];
                if (u.IsAlive && (u.Position - from).sqrMagnitude <= r2 && predicate(u, state)) n++;
            }
            return n;
        }

        public static void Collect(Vector2 from, float radius, Func<Unit, bool> predicate, List<Unit> results)
            => Collect(from, radius, predicate, static (u, p) => p == null || p(u), results);

        /// <summary>Living units within radius passing the predicate, in no particular order.</summary>
        public static void Collect<T>(Vector2 from, float radius, T state, Func<Unit, T, bool> predicate, List<Unit> results)
        {
            EnsureIndex();
            results.Clear();
            float r2 = radius * radius;
            if (!Range(from, radius, out int x0, out int y0, out int x1, out int y1))
            {
                foreach (var u in s_units)
                    if (u.IsAlive && (u.Position - from).sqrMagnitude <= r2 && predicate(u, state)) results.Add(u);
                return;
            }
            float loose = Sq(radius + Slack);
            for (int by = y0; by <= y1; by++)
            for (int bx = x0; bx <= x1; bx++)
            for (int i = s_head[Bucket(bx, by)]; i >= 0; i = s_next[i])
            {
                if ((s_pos[i] - from).sqrMagnitude > loose) continue;
                var u = s_slot[i];
                if (u.IsAlive && (u.Position - from).sqrMagnitude <= r2 && predicate(u, state)) results.Add(u);
            }
        }

        /// <summary>Living units within radius (no filter).</summary>
        public static void Collect(Vector2 from, float radius, List<Unit> results)
            => Collect<Func<Unit, bool>>(from, radius, null, static (_, _) => true, results);

        // ---------------- Raw index access (crowd separation) ----------------

        /// <summary>First slot in the bucket at bucket coordinates (bx, by), or -1. Valid until the next rebuild.</summary>
        internal static int BucketHead(int bx, int by) => s_head[Bucket(bx, by)];
        internal static int NextInBucket(int slot) => s_next[slot];
        internal static Unit SlotUnit(int slot) => s_slot[slot];
        internal static Vector2 SlotPosition(int slot) => s_pos[slot];
        internal static int SlotCount => s_count;
        internal static int BucketOf(float v) => BucketCoord(v);

        static float Sq(float x) => x * x;
    }
}
