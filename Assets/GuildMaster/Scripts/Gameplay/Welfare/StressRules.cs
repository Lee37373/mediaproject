using GuildMaster.Data;
using UnityEngine;

namespace GuildMaster.Welfare
{
    /// <summary>How close to breaking a guild member is (F-2), read from their stress value.</summary>
    public enum StressLevel { Calm, Tense, Strained, Breaking }

    /// <summary>What a stressed member asks the guild master for.</summary>
    public enum CareDemand { None, Talk, Leave, Bonus }

    /// <summary>
    /// What stress does to a guild member (F-2): the levels, which quests each level still takes, and how much
    /// stress the hard moments of guild life add. Pure numbers and rules with no Unity objects, so they are
    /// covered by EditMode tests. Every value here is meant to be tuned in play.
    /// </summary>
    public static class StressRules
    {
        /// <summary>From here on the member is less keen on quests.</summary>
        public const float TenseAt = 40f;
        /// <summary>From here on the member avoids high-danger quests and starts asking the guild master for things.</summary>
        public const float StrainedAt = 60f;
        /// <summary>From here on the member takes no quests, may walk away from one, and may quit.</summary>
        public const float BreakingAt = 80f;

        // ---- Extra sources of stress (combat damage and retreats already add some in Adventurer) ----

        /// <summary>A quest they were on failed.</summary>
        public const float QuestFailedStress = 15f;
        /// <summary>
        /// A guild member died: what everyone else feels depends on how they felt about the dead (their value in
        /// Relationships, -100..100). Below 0 they did not care and take no stress; from 0 it rises in a straight line
        /// from <see cref="DeathStressMin"/> to <see cref="DeathStressMax"/> at <see cref="DeathGriefFullAt"/> and above.
        /// </summary>
        public const float DeathStressMin = 5f;
        public const float DeathStressMax = 25f;
        public const int DeathGriefFullAt = 50;

        public static float DeathStress(int relation)
            => relation < 0 ? 0f : Mathf.Lerp(DeathStressMin, DeathStressMax, Mathf.Clamp01(relation / (float)DeathGriefFullAt));

        /// <summary>Relationship value (see Relationships) from which someone counts as a friend.</summary>
        public const int FriendRelation = 15;
        /// <summary>The guild master let a demand run out without answering it.</summary>
        public const float IgnoredDemandStress = 15f;
        /// <summary>A friend was dismissed or quit.</summary>
        public const float FriendLeftStress = 8f;
        /// <summary>Fatigue from which every hour of work adds stress.</summary>
        public const float OverworkFatigue = 80f;
        public const float OverworkStressPerHour = 2f;
        /// <summary>Walking away from a quest takes some weight off.</summary>
        public const float SabotageRelief = 10f;

        public static StressLevel LevelOf(float stress)
            => stress >= BreakingAt ? StressLevel.Breaking
             : stress >= StrainedAt ? StressLevel.Strained
             : stress >= TenseAt ? StressLevel.Tense
             : StressLevel.Calm;

        public static string Label(StressLevel level) => level switch
        {
            StressLevel.Tense => "긴장",
            StressLevel.Strained => "지침",
            StressLevel.Breaking => "한계",
            _ => "안정",
        };

        /// <summary>What the level means in play, for the management window.</summary>
        public static string Effect(StressLevel level) => level switch
        {
            StressLevel.Tense => "의뢰를 고를 때 조금 소극적입니다.",
            StressLevel.Strained => "위험도가 높은 의뢰는 꺼리고, 길드 마스터에게 뭔가를 요구할 수 있습니다.",
            StressLevel.Breaking => "의뢰를 맡지 않고, 의뢰 중이면 포기할 수 있으며, 오래 가면 길드를 떠납니다.",
            _ => "일에 지장이 없습니다.",
        };

        /// <summary>Whether a member this stressed still takes a quest of that danger.</summary>
        public static bool AllowsQuest(float stress, DangerLevel danger)
        {
            var level = LevelOf(stress);
            if (level == StressLevel.Breaking) return false;
            if (level == StressLevel.Strained && danger == DangerLevel.High) return false;
            return true;
        }

        /// <summary>Multiplier on how attractive a quest looks to a member this stressed.</summary>
        public static float QuestScoreFactor(float stress) => LevelOf(stress) == StressLevel.Tense ? 0.85f : 1f;
    }
}
