using UnityEngine;

namespace GuildMaster.Ecosystem
{
    /// <summary>
    /// How many monsters the wilds hold (user decision 2026-11-02: "몬스터 빈도·젠율을 높여야 함" → breeding and den size
    /// both ×1.5). Applied once where the kinds are made (<see cref="Data.DefaultContent"/>): every den holds half as many
    /// again, starts half as full again and breeds back half as fast again. A region lord's lair keeps its old size
    /// (<see cref="Unscaled"/>) — the lords were tuned in play against their guard count. Pure, tested.
    /// </summary>
    public static class MonsterAbundance
    {
        /// <summary>Den size and starting numbers (1 = as tuned before 2026-11-02).</summary>
        public const float PopulationScale = 1.5f;
        /// <summary>Breeding per hour (1 = as tuned before 2026-11-02).</summary>
        public const float BreedScale = 1.5f;

        /// <summary>A den's size or starting count after the scale (at least one).</summary>
        public static int Scaled(int count) => Mathf.Max(1, Mathf.RoundToInt(count * PopulationScale));

        /// <summary>The breeding rate after the scale.</summary>
        public static float ScaledGrowth(float perHour) => perHour * BreedScale;

        /// <summary>The size a den had before the scale (a lair keeps it).</summary>
        public static int Unscaled(int scaled) => Mathf.Max(1, Mathf.RoundToInt(scaled / PopulationScale));
    }
}
