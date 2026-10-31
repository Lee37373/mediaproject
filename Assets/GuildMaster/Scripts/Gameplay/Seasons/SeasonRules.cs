using GuildMaster.Disasters;
using GuildMaster.Storytelling;
using UnityEngine;

namespace GuildMaster.Seasons
{
    public enum Season { Spring, Summer, Autumn, Winter }

    /// <summary>
    /// The seasons (P4-111b, H11 — user decisions 2026-10-30: five days a season, a twenty-day year; shown by the light's
    /// tint and felt in numbers only, the ground and its paths stay as they are). Spring breeds, summer burns, autumn trades
    /// and feasts, winter is dear, cold and hungry. Pure, tested; <see cref="SeasonService"/> tells of them and puts them to use.
    /// </summary>
    public static class SeasonRules
    {
        public const int DaysPerSeason = 5;
        public const int DaysPerYear = DaysPerSeason * 4;

        public static Season Of(int day) => (Season)((Mathf.Max(1, day) - 1) / DaysPerSeason % 4);

        /// <summary>The day of its season, 1 .. <see cref="DaysPerSeason"/>.</summary>
        public static int DayOf(int day) => (Mathf.Max(1, day) - 1) % DaysPerSeason + 1;

        public static int Year(int day) => (Mathf.Max(1, day) - 1) / DaysPerYear + 1;

        public static Season Next(Season s) => (Season)(((int)s + 1) % 4);

        /// <summary>The first day of a season (not the game's first): the day it comes.</summary>
        public static bool Begins(int day) => day > 1 && DayOf(day) == 1;

        /// <summary>The day before a new season: the town talks of it.</summary>
        public static bool Eve(int day) => DayOf(day) == DaysPerSeason;

        public static string Name(Season s) => s switch
        {
            Season.Spring => "봄",
            Season.Summer => "여름",
            Season.Autumn => "가을",
            _ => "겨울",
        };

        // ---------------- What each does ----------------

        /// <summary>Dens breed this much faster (spring) or slower (winter).</summary>
        public static float BreedFactor(Season s) => s switch { Season.Spring => 1.3f, Season.Winter => 0.7f, _ => 1f };

        /// <summary>A disaster monster's strike sets one off this much more often (summer: fires and drought).</summary>
        public static float DisasterFactor(Season s) => s == Season.Summer ? 1.75f : 1f;

        /// <summary>A swollen den raids the town this much more often (winter: hungry packs).</summary>
        public static float RaidFactor(Season s) => s == Season.Winter ? 1.3f : 1f;

        /// <summary>The season's say in the storyteller's draw.</summary>
        public static float BeatWeight(Season s, StoryBeat b) => (s, b) switch
        {
            (Season.Summer, StoryBeat.Disaster) => 2f,
            (Season.Winter, StoryBeat.TownRaid) => 1.5f,
            (Season.Winter, StoryBeat.Horde) => 1.5f,
            _ => 1f,
        };

        /// <summary>Which disaster the season favours when the storyteller sets one off.</summary>
        public static float DisasterKindWeight(Season s, DisasterKind k) => (s, k) switch
        {
            (Season.Summer, DisasterKind.Wildfire) => 2.5f,
            (Season.Summer, DisasterKind.Sandstorm) => 1.5f,
            (Season.Winter, DisasterKind.Avalanche) => 2.5f,
            (Season.Spring, DisasterKind.PoisonFog) => 1.5f,
            _ => 1f,
        };

        /// <summary>Game hours between the merchant's visits (autumn: more of them, P4-3's 48 otherwise).</summary>
        public static float MerchantInterval(Season s, float usual) => s == Season.Autumn ? AutumnMerchantInterval : usual;
        public const float AutumnMerchantInterval = 30f;

        /// <summary>What the merchant pays (autumn: the harvest's buyers are in town).</summary>
        public static float MerchantPriceFactor(Season s) => s == Season.Autumn ? 1.15f : 1f;

        /// <summary>The day's meals cost this much more (winter).</summary>
        public static float MealFactor(Season s) => s == Season.Winter ? 1.5f : 1f;

        /// <summary>Everyone walks this much slower (winter's snow and mud, in numbers only).</summary>
        public static float WalkFactor(Season s) => s == Season.Winter ? 0.92f : 1f;

        /// <summary>A night by the fire takes this much of the fatigue off (winter: cold).</summary>
        public static float CampRestFactor(Season s) => s == Season.Winter ? 0.6f : 1f;

        /// <summary>The harvest festival: this day of autumn, the whole day.</summary>
        public const int FestivalDay = 3;

        public static bool IsFestival(int day) => Of(day) == Season.Autumn && DayOf(day) == FestivalDay;

        /// <summary>On the festival day stress eases this much faster in town.</summary>
        public const float FestivalStressRelief = 2f;

        /// <summary>Hours of the festival day at which those in town have a good time of it.</summary>
        public static bool FestivalHour(int hour) => hour == 12 || hour == 20;

        // ---------------- How it looks and what the town says ----------------

        /// <summary>The light's tint (times the day's light): a fresh green, a warm summer, an amber autumn, a cold blue winter.</summary>
        public static Color Tint(Season s) => s switch
        {
            Season.Spring => new Color(0.96f, 1f, 0.93f),
            Season.Summer => new Color(1f, 0.97f, 0.86f),
            Season.Autumn => new Color(1f, 0.88f, 0.76f),
            _ => new Color(0.82f, 0.9f, 1f),
        };

        /// <summary>Game hours over which the tint turns at the start of a season.</summary>
        public const float TintTurnHours = 12f;

        /// <summary>What changes, as the town says it the day before.</summary>
        public static string Changes(Season s) => s switch
        {
            Season.Spring => "몬스터 번식이 늘어납니다",
            Season.Summer => "산불·모래폭풍 같은 재해가 잦아집니다",
            Season.Autumn => $"행상인이 자주 오고 값을 더 쳐 줍니다. {FestivalDay}일째에는 수확 축제가 열립니다",
            _ => "식비가 오르고, 걸음이 느려지고, 야영이 춥습니다. 굶주린 무리가 마을을 노립니다",
        };
    }
}
