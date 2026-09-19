using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>One direction of a relationship: how the owner feels about another member.</summary>
    [Serializable]
    public class RelationEntry
    {
        public string id;
        public int value;
    }

    /// <summary>A's feeling towards B changed (published only when it actually moved).</summary>
    public struct RelationChangedEvent
    {
        public Adventurer From;
        public Adventurer To;
        public int Old;
        public int New;
        public string Reason;
    }

    /// <summary>
    /// Directional affinity between guild members (-100..100, P3-5). Stored on
    /// <see cref="AdventurerData.relations"/>; the first lookup seeds it from personality
    /// (sociable/loner, shared values). Changed by small talk and shared experiences, read by
    /// banter and party formation.
    /// </summary>
    public static class Relationships
    {
        public const int Min = -100, Max = 100;

        public static int Get(Adventurer from, Adventurer to)
        {
            if (from == null || to == null || from == to) return 0;
            return Entry(from, to).value;
        }

        /// <summary>Adds delta (clamped to the range) and publishes the change.</summary>
        public static void Add(Adventurer from, Adventurer to, int delta, string reason)
        {
            if (from == null || to == null || from == to || delta == 0) return;
            var e = Entry(from, to);
            int old = e.value;
            e.value = Mathf.Clamp(old + delta, Min, Max);
            if (e.value != old)
                EventBus.Publish(new RelationChangedEvent { From = from, To = to, Old = old, New = e.value, Reason = reason });
        }

        static RelationEntry Entry(Adventurer from, Adventurer to)
        {
            var list = from.Data.relations;
            foreach (var r in list) if (r.id == to.Data.id) return r;
            var e = new RelationEntry { id = to.Data.id, value = Seed(from.Traits?.PartyAffinity ?? 1f, from.Data.values, to.Data.values) };
            list.Add(e);
            return e;
        }

        /// <summary>Starting feeling: sociable +5 / loner -5, same values +5.</summary>
        public static int Seed(float partyAffinity, string ownValues, string otherValues)
        {
            int v = partyAffinity > 1f ? 5 : partyAffinity < 1f ? -5 : 0;
            if (!string.IsNullOrEmpty(ownValues) && ownValues == otherValues) v += 5;
            return v;
        }

        public static string Label(int v) => v >= 40 ? "친함" : v >= 15 ? "호감" : v > -15 ? "보통" : v > -40 ? "껄끄러움" : "적대";

        public static string Describe(int v) => $"{Label(v)}({(v > 0 ? "+" : "")}{v})";

        /// <summary>Strongest feelings first (for UI), only members still in the roster.</summary>
        public static IEnumerable<(Adventurer other, int value)> Strongest(Adventurer from, IEnumerable<Adventurer> others, int count)
            => others.Where(o => o != from && o != null)
                     .Select(o => (o, Get(from, o)))
                     .Where(x => x.Item2 != 0)
                     .OrderByDescending(x => Mathf.Abs(x.Item2))
                     .Take(count);
    }
}
