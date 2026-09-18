using System.Collections.Generic;
using GuildMaster.Core;

namespace GuildMaster.Traits
{
    /// <summary>
    /// Random trait assignment: 1 (40%) / 2 (40%) / 3 (20%) traits, each positive with 55%
    /// probability, never two identical or conflicting traits.
    /// </summary>
    public static class TraitRoller
    {
        public const float PositiveChance = 0.55f;

        public static int RollCount(GameRandom rng)
        {
            float r = rng.Value;
            return r < 0.4f ? 1 : r < 0.8f ? 2 : 3;
        }

        public static List<TraitDefinition> Roll(GameRandom rng, IReadOnlyList<TraitDefinition> pool)
            => Roll(rng, pool, RollCount(rng));

        public static List<TraitDefinition> Roll(GameRandom rng, IReadOnlyList<TraitDefinition> pool, int count)
        {
            var result = new List<TraitDefinition>();
            if (pool == null || pool.Count == 0) return result;
            var candidates = new List<TraitDefinition>();
            for (int i = 0; i < count; i++)
            {
                bool positive = rng.Chance(PositiveChance);
                Collect(pool, result, positive, candidates);
                // Fall back to the other polarity if this one is exhausted.
                if (candidates.Count == 0) Collect(pool, result, !positive, candidates);
                if (candidates.Count == 0) break;
                result.Add(rng.Pick(candidates));
            }
            return result;
        }

        static void Collect(IReadOnlyList<TraitDefinition> pool, List<TraitDefinition> chosen, bool positive, List<TraitDefinition> into)
        {
            into.Clear();
            foreach (var t in pool)
            {
                if (t == null || t.isPositive != positive || chosen.Contains(t)) continue;
                bool conflict = false;
                foreach (var c in chosen)
                    if (t.ConflictsWith(c)) { conflict = true; break; }
                if (!conflict) into.Add(t);
            }
        }
    }
}
