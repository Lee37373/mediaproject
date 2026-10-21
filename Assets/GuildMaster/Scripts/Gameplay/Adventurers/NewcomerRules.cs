using System;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Newcomers (P4-99, user decisions 2026-10-21: "신입은 약한 몬스터를 먼저 사냥하거나 마을에서 레벨 올리기"). A member weaker
    /// than the monsters round the town — its strength (<see cref="AdventurerStats.Power"/>: level and gear, as the guild's
    /// facilities make it) under the level the young are born at on the near ring, where every free hunt goes — trains in
    /// town while the training ground teaches, or else hunts alone what it can take, close in. It does not tag along with a
    /// stronger band: in a six-day run seven of ten deaths were members of Lv1–3 out 33–59 cells, on days 2–4.
    /// The training ground pays more as the days go by and the further a member is behind the guild's best. Pure, tested.
    /// </summary>
    public static class NewcomerRules
    {
        /// <summary>The near ring (cells from the town): every free hunt roams at least this far (<see cref="FreeHunt.Radius"/>).</summary>
        public const float NearRing = FreeHunt.Radius;

        /// <summary>A newcomer: weaker than the level the near ring's young are born at.</summary>
        public static bool IsNewcomer(float power, int nearLevel) => power < nearLevel;

        /// <summary>A newcomer's hunt reaches at least this far past the innermost dens, so there is a den to go to.</summary>
        public const float InnerMargin = 6f;

        /// <summary>
        /// How far from the town a newcomer hunts: out to where the young are born no stronger than it
        /// (<paramref name="levelAt"/>: birth level by distance), at least just past the innermost dens, never past the near ring.
        /// </summary>
        public static float Reach(float power, float innermost, Func<float, int> levelAt)
        {
            float reach = Mathf.Min(innermost + InnerMargin, NearRing);
            for (float d = reach + 1f; d <= NearRing; d += 1f)
            {
                if (!WithinReach(power, levelAt(d))) break;
                reach = d;
            }
            return reach;
        }

        /// <summary>
        /// Ground a newcomer may work: the young there are born no stronger than it (level 1 ground always). Its hunts keep
        /// to it, and so do its quests — it neither takes nor joins one out where they are stronger (<see cref="QuestRisk.BeyondNewcomer"/>).
        /// </summary>
        public static bool WithinReach(float power, int levelThere) => levelThere <= Mathf.Max(1f, power);

        // ---------------- Training ----------------

        /// <summary>Game hours a newcomer trains before it thinks again (a quest it could take may have come up).</summary>
        public const float TrainHours = 2f;
        /// <summary>The training ground's EXP an hour grows by this share each game day (the wilds grow wilder too, P4-44).</summary>
        public const float GrowthPerDay = 0.1f;
        /// <summary>…and by this share for each level a member is behind the guild's best (catching up), counted up to <see cref="MaxCatchUpLevels"/>.</summary>
        public const float CatchUpPerLevel = 0.25f;
        public const int MaxCatchUpLevels = 12;

        /// <summary>
        /// EXP an hour at the training ground for a member <paramref name="levelsBehindTop"/> levels under the guild's best,
        /// <paramref name="days"/> game days in, where the ground's level gives <paramref name="baseExp"/>. Nothing when it gives none.
        /// </summary>
        public static int TrainingExp(float baseExp, float days, int levelsBehindTop)
        {
            if (baseExp <= 0f) return 0;
            float growth = 1f + GrowthPerDay * Mathf.Max(0f, days);
            float catchUp = 1f + CatchUpPerLevel * Mathf.Clamp(levelsBehindTop, 0, MaxCatchUpLevels);
            return Mathf.Max(1, Mathf.RoundToInt(baseExp * growth * catchUp));
        }
    }
}
