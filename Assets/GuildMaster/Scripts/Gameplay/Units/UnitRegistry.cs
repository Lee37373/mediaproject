using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Units
{
    /// <summary>Registry of live units with simple spatial queries.</summary>
    public static class UnitRegistry
    {
        static readonly List<Unit> s_units = new();

        public static IReadOnlyList<Unit> All => s_units;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => s_units.Clear();

        public static void Register(Unit u) { if (!s_units.Contains(u)) s_units.Add(u); }
        public static void Unregister(Unit u) => s_units.Remove(u);

        /// <summary>Nearest living unit within radius matching the predicate.</summary>
        public static Unit FindNearest(Vector2 from, float radius, Func<Unit, bool> predicate)
        {
            Unit best = null;
            float bestSq = radius * radius;
            for (int i = 0; i < s_units.Count; i++)
            {
                var u = s_units[i];
                if (!u.IsAlive) continue;
                float sq = ((Vector2)u.transform.position - from).sqrMagnitude;
                if (sq > bestSq || !predicate(u)) continue;
                bestSq = sq;
                best = u;
            }
            return best;
        }

        /// <summary>Nearest living unit hostile to 'self' and in its line of sight (or that attacked it) within radius.</summary>
        public static Unit FindNearestHostile(Unit self, float radius)
            => FindNearest(self.Position, radius, u => u != self && (self.WasRecentlyAttackedBy(u) || (self.IsHostileTo(u) && self.CanSee(u))));

        public static int CountInRadius(Vector2 from, float radius, Func<Unit, bool> predicate)
        {
            int n = 0;
            float r2 = radius * radius;
            foreach (var u in s_units)
                if (u.IsAlive && ((Vector2)u.transform.position - from).sqrMagnitude <= r2 && predicate(u)) n++;
            return n;
        }

        public static void Collect(Vector2 from, float radius, Func<Unit, bool> predicate, List<Unit> results)
        {
            results.Clear();
            float r2 = radius * radius;
            foreach (var u in s_units)
                if (u.IsAlive && ((Vector2)u.transform.position - from).sqrMagnitude <= r2 && predicate(u)) results.Add(u);
        }
    }
}
