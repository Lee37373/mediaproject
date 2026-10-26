using System.Collections.Generic;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Places
{
    /// <summary>
    /// What each place does for a member and when it goes there (P4-105, user decision 2026-10-26). Pure numbers, one place,
    /// tuned in play.
    /// </summary>
    public static class PlaceRules
    {
        // ---- Hot spring ----
        public const float SpringHours = 2f, SpringStressRelief = 30f, SpringFatigueRelief = 60f, SpringHeal = 0.5f;
        /// <summary>It goes to soak when this stressed, this tired, or this low.</summary>
        public const float SpringStressFrom = 35f, SpringFatigueFrom = 50f, SpringMoodBelow = 45f;

        // ---- Shrine ----
        public const float ShrineHours = 1f, BlessingHours = 48f, BlessingHp = 1.1f, ShrineCooldownHours = 72f;
        /// <summary>It goes to pray when this low (or valuing safety or honour).</summary>
        public const float ShrineMoodBelow = 55f;

        // ---- Ore vein, herb patch ----
        public const float GatherHours = 2f, RegrowHours = 24f;
        public const int OreMin = 2, OreMax = 3, HerbMin = 3, HerbMax = 4;
        public const string HerbId = "herb";

        // ---- Old camp, watchtower ----
        /// <summary>A camp for the night is made at a known old camp this close.</summary>
        public const float OldCampReach = 15f;
        /// <summary>The firelight there draws a raider this much as often.</summary>
        public const float OldCampRaidFactor = 0.5f;
        /// <summary>From the top of a watchtower the land this far round is seen (once).</summary>
        public const float TowerSightRadius = 30f;

        // ---- Going there ----
        /// <summary>Places further than this from the town are not gone to on their own.</summary>
        public const float MaxDistanceFromTown = 200f;
        /// <summary>Not where the young are born more than this many levels above its power.</summary>
        public const int LevelMargin = 2;
        /// <summary>A walk there that has taken this many hours is given up.</summary>
        public const float GiveUpHours = 16f;

        // ---- Choosing (P4-105b): against the think tree's other options ----
        /// <summary>
        /// In the roam layer, against a hunt's 1: a soak or a prayer it wants comes first, gathering comes first for those who
        /// would rather (<see cref="GathersFirst"/>) and is otherwise what a day not spent hunting is spent on.
        /// </summary>
        public const float SpringScore = 1.5f, ShrineScore = 1.3f, GatherFirstScore = 1.2f, GatherScore = 0.6f;
        /// <summary>Stressed this much, it goes to soak before any work (the body layer, after rest 1, a potion 0.9, gear 0.8).</summary>
        public const float UrgentSpringStress = 60f, UrgentSpringScore = 0.7f;

        public static bool WantsSpring(float stress, float fatigue, float mood)
            => stress >= SpringStressFrom || fatigue >= SpringFatigueFrom || mood < SpringMoodBelow;

        public static bool NeedsSpring(float stress) => stress >= UrgentSpringStress;

        /// <summary>Low, or one who values safety or honour — and its last prayer far enough back.</summary>
        public static bool WantsShrine(float mood, string values, float now, float readyAt)
            => now >= readyAt && (mood < ShrineMoodBelow || values is "안전" or "명예");

        /// <summary>A newcomer, one who values safety, a diligent one: gathering before hunting.</summary>
        public static bool GathersFirst(bool newcomer, string values, IEnumerable<string> traitIds)
        {
            if (newcomer || values == "안전") return true;
            if (traitIds != null) foreach (var t in traitIds) if (t == "diligent") return true;
            return false;
        }

        /// <summary>Close enough to town, and nothing born there much stronger than it.</summary>
        public static bool SafeFor(int birthLevel, float power, float distanceFromTown, float reach = MaxDistanceFromTown)
            => distanceFromTown <= Mathf.Min(reach, MaxDistanceFromTown) && birthLevel <= power + LevelMargin;

        public static bool IsGathering(PlaceKind k) => k is PlaceKind.OreVein or PlaceKind.HerbPatch;

        public static string GatherItem(PlaceKind k) => k == PlaceKind.OreVein ? OreId : HerbId;

        public const string OreId = "iron_ore";

        public static string GatherItemName(PlaceKind k) => k == PlaceKind.OreVein ? "철광석" : "약초";

        /// <summary>How much one gathering brings home (<paramref name="roll01"/> in [0, 1)).</summary>
        public static int GatherCount(PlaceKind k, float roll01)
        {
            var (min, max) = k == PlaceKind.OreVein ? (OreMin, OreMax) : (HerbMin, HerbMax);
            return Mathf.Clamp(min + Mathf.FloorToInt(roll01 * (max - min + 1)), min, max);
        }

        /// <summary>Game hours spent there once arrived.</summary>
        public static float Hours(PlaceKind k) => k switch
        {
            PlaceKind.HotSpring => SpringHours,
            PlaceKind.Shrine => ShrineHours,
            _ => GatherHours,
        };

        /// <summary>What the place is and does, for the inspector.</summary>
        public static string Describe(PlaceKind k) => k switch
        {
            PlaceKind.HotSpring => $"지친 길드원이 찾아와 {SpringHours:0}시간 몸을 담급니다. 스트레스 −{SpringStressRelief:0}, 피로 −{SpringFatigueRelief:0}, 체력 회복.",
            PlaceKind.Shrine => $"기도하면 {BlessingHours:0}시간 동안 가호(최대 체력 +{(BlessingHp - 1f) * 100f:0}%)를 받습니다. 한 사람이 {ShrineCooldownHours:0}시간에 한 번.",
            PlaceKind.OreVein => $"캐면 철광석 {OreMin}~{OreMax}개. {RegrowHours:0}시간 뒤 다시 캘 수 있습니다.",
            PlaceKind.HerbPatch => $"따면 약초 {HerbMin}~{HerbMax}개(약방에서 물약 재료). {RegrowHours:0}시간 뒤 다시 자랍니다.",
            PlaceKind.OldCamp => "밤에 근처를 지나는 파티가 여기서 야영합니다. 불빛이 습격을 덜 부릅니다.",
            PlaceKind.WatchRuin => $"올라가면 둘레 {TowerSightRadius:0}칸이 한 번에 보입니다. 정찰하는 길드원이 먼저 찾아갑니다.",
            _ => "",
        };
    }
}
