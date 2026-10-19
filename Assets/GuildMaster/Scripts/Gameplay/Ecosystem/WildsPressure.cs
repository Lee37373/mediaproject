using GuildMaster.Monsters;
using UnityEngine;

namespace GuildMaster.Ecosystem
{
    /// <summary>
    /// The wilds grow wilder with time (P4-44, user decisions 2026-10-19): monsters born out in the far land come
    /// stronger the longer the game runs — the same kinds, at higher levels — and newcomers from beyond the map edge
    /// crowd in on the packs already there, which give way towards the town and in the end come at it. No steps: it
    /// rises a little all the time, after a few quiet days, and only beyond the inner half of the nest sites; the
    /// outermost sites get all of it. The pace follows the game's flow (balance runs), not a calendar. Pure, tested.
    /// </summary>
    public static class WildsPressure
    {
        /// <summary>Quiet days at the start: the guild finds its feet before the wilds stir.</summary>
        public const float GraceDays = 3f;
        /// <summary>Levels a day the outermost sites gain after the grace.</summary>
        public const float LevelsPerDay = 0.5f;
        /// <summary>The most the outermost sites gain (they meet the monsters' level cap long before; the middle keeps rising).</summary>
        public const float MaxBonus = 15f;
        /// <summary>Depth (<see cref="EcosystemManager.Depth"/>: 0 innermost .. 1 inner edge .. 2 outermost) from which it counts.</summary>
        public const float FromDepth = 0.5f;
        public const float FullDepth = 2f;

        /// <summary>Levels the outermost sites have gained after this many game days.</summary>
        public static float Bonus(float days) => Mathf.Clamp((days - GraceDays) * LevelsPerDay, 0f, MaxBonus);

        /// <summary>Share of it a site this deep gets: none in the inner half, all at the outermost.</summary>
        public static float Weight(float depth) => Mathf.InverseLerp(FromDepth, FullDepth, depth);

        /// <summary>Levels added to a site's young at this depth after this many game days.</summary>
        public static int LevelBonus(float depth, float days) => Mathf.RoundToInt(Bonus(days) * Weight(depth));

        /// <summary>Birth level at a site of this depth after this many game days, within the monsters' cap.</summary>
        public static int BirthLevel(float depth, float days)
            => Mathf.Min(MonsterLevel.MaxLevel, MonsterLevel.BirthLevel(depth) + LevelBonus(depth, days));

        /// <summary>
        /// Those already living there grow with the land: a resident below its den's birth level of the day gains a level
        /// about once in this many game hours. Without it a full den — which does not breed — kept its old monsters, and a
        /// 20-day run moved the far land's mean level by 0.2.
        /// </summary>
        public const float CatchUpHours = 24f;

        /// <summary>Whether a resident of this level, at a den breeding <paramref name="birthLevel"/>, grows this hour (roll 0..1).</summary>
        public static bool GrowsThisHour(int level, int birthLevel, float roll) => level < birthLevel && roll < 1f / CatchUpHours;

        // ---------------- Newcomers crowding in ----------------

        /// <summary>Chance per game hour, once the wilds stir and at their wildest, that newcomers come for a held outer site.</summary>
        public const float MinInvasionChance = 0.04f, MaxInvasionChance = 0.15f;

        /// <summary>None during the grace; then more often the wilder it gets.</summary>
        public static float InvasionChancePerHour(float days)
        {
            float b = Bonus(days);
            return b <= 0f ? 0f : Mathf.Lerp(MinInvasionChance, MaxInvasionChance, b / MaxBonus);
        }

        /// <summary>Newcomers only come for a site they would at least match: their strength over the residents'.</summary>
        public const float InvadeAtOdds = 1f;
    }
}
