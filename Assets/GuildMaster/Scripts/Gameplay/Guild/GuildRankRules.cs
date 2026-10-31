using System;
using System.Collections.Generic;

namespace GuildMaster.Guild
{
    /// <summary>The guild's rank (P4-106, H1, user decision 2026-10-27: F to S, and S is the victory).</summary>
    public enum GuildRank { F, E, D, C, B, A, S }

    /// <summary>What a rank's condition counts.</summary>
    public enum GoalKind
    {
        Fame,
        QuestsDone,
        Recruits,
        Upgrades,
        NestsCleared,
        /// <summary>Living members at <see cref="Goal.Level"/> or above.</summary>
        MembersAtLevel,
        ZonesKnown,
        SpeciesKnown,
        RaidsHeld,
        Facilities,
        LordsSlain,
    }

    /// <summary>One condition for the next rank: <see cref="Count"/> of <see cref="Kind"/> (at <see cref="Level"/> for members).</summary>
    public readonly struct Goal
    {
        public readonly GoalKind Kind;
        public readonly int Count;
        public readonly int Level;

        public Goal(GoalKind kind, int count, int level = 0)
        {
            Kind = kind;
            Count = count;
            Level = level;
        }

        public string Label => Kind switch
        {
            GoalKind.Fame => $"명성 {Count}",
            GoalKind.QuestsDone => Count == 1 ? "첫 의뢰 완료" : $"의뢰 {Count}건 완료",
            GoalKind.Recruits => Count == 1 ? "첫 영입" : $"길드원 {Count}명 영입",
            GoalKind.Upgrades => Count == 1 ? "첫 건물 업그레이드" : $"건물 업그레이드 {Count}번",
            GoalKind.NestsCleared => $"둥지 {Count}곳 토벌",
            GoalKind.MembersAtLevel => $"Lv{Level} 이상 길드원 {Count}명",
            GoalKind.ZonesKnown => $"구역 {Count}곳 발견",
            GoalKind.SpeciesKnown => $"몬스터 {Count}종 발견",
            // A great horde stopped counts too (P4-108).
            GoalKind.RaidsHeld => Count == 1 ? "침공이나 대규모 무리 막아내기" : $"침공·대규모 무리 {Count}번 막아내기",
            GoalKind.Facilities => $"내실 건물 {Count}채",
            GoalKind.LordsSlain => Count >= GuildRankRules.RegionLordCount ? "지대 군주 넷 모두 처치" : $"지대 군주 {Count} 처치",
            _ => Kind.ToString(),
        };
    }

    /// <summary>
    /// The guild's fame and ranks (P4-106, H1 — Two Point Hospital's star ladder, Monster Hunter's hunter rank; user decisions
    /// 2026-10-27). Fame comes from what the guild did and saw (information hiding), and some of it is lost to deaths, failed
    /// quests and damage to the town — but a rank once reached is kept. Each rank asks for three things (fame and two tasks;
    /// the first one is the tutorial: a first quest, a first recruit, a first upgrade); S asks for the four region lords and is
    /// the victory. A rank opens only new things (a grant from the town, stronger visitors coming more often) — what was open
    /// before stays open. Pure numbers, tuned in play.
    /// </summary>
    public static class GuildRankRules
    {
        public const int RegionLordCount = 4;

        // ---- Fame ----
        public const int QuestLow = 5, QuestMedium = 10, QuestHigh = 20;
        public const int NestCleared = 3, ZoneFound = 3, SpeciesFound = 5, PlaceFound = 2;
        public const int RaidHeld = 15, LordSlain = 100, DragonSlain = 150;
        public const int MemberDied = -10, QuestFailed = -5, BuildingDestroyed = -10, Plundered = -10;
        /// <summary>The fame history kept for the rank window.</summary>
        public const int HistoryLength = 30;

        public static int QuestFame(Data.DangerLevel danger) => danger switch
        {
            Data.DangerLevel.Low => QuestLow,
            Data.DangerLevel.Medium => QuestMedium,
            _ => QuestHigh,
        };

        // ---- Ranks ----
        public static string Name(GuildRank r) => $"{r}등급";

        /// <summary>What it takes to reach <paramref name="rank"/> from the one below (none for F).</summary>
        public static IReadOnlyList<Goal> GoalsFor(GuildRank rank) => rank switch
        {
            GuildRank.E => new[] { new Goal(GoalKind.QuestsDone, 1), new Goal(GoalKind.Recruits, 1), new Goal(GoalKind.Upgrades, 1) },
            GuildRank.D => new[] { new Goal(GoalKind.Fame, 60), new Goal(GoalKind.NestsCleared, 5), new Goal(GoalKind.MembersAtLevel, 3, 5) },
            GuildRank.C => new[] { new Goal(GoalKind.Fame, 180), new Goal(GoalKind.ZonesKnown, 8), new Goal(GoalKind.RaidsHeld, 1) },
            GuildRank.B => new[] { new Goal(GoalKind.Fame, 400), new Goal(GoalKind.MembersAtLevel, 4, 10), new Goal(GoalKind.SpeciesKnown, 8) },
            GuildRank.A => new[] { new Goal(GoalKind.Fame, 700), new Goal(GoalKind.LordsSlain, 1), new Goal(GoalKind.Facilities, 2) },
            GuildRank.S => new[] { new Goal(GoalKind.Fame, 1000), new Goal(GoalKind.LordsSlain, RegionLordCount) },
            _ => Array.Empty<Goal>(),
        };

        public static bool IsTop(GuildRank r) => r >= GuildRank.S;

        public static GuildRank Next(GuildRank r) => IsTop(r) ? r : r + 1;

        /// <summary>The town's grant on reaching it (S is the victory).</summary>
        public static int Grant(GuildRank r) => r switch
        {
            GuildRank.E => 100,
            GuildRank.D => 300,
            GuildRank.C => 600,
            GuildRank.B => 1200,
            GuildRank.A => 2500,
            _ => 0,
        };

        /// <summary>Levels added to visitors' (on top of the guild hall's, user decision ②).</summary>
        public static int VisitorBonus(GuildRank r) => r switch
        {
            GuildRank.D or GuildRank.C => 1,
            GuildRank.B or GuildRank.A => 2,
            GuildRank.S => 3,
            _ => 0,
        };

        /// <summary>A known guild draws visitors more often: the time between visits is this much as long.</summary>
        public static float VisitInterval(GuildRank r) => r >= GuildRank.A ? 0.7f : r >= GuildRank.C ? 0.85f : 1f;

        /// <summary>What reaching it opens, for the rank window and the banner.</summary>
        public static string Rewards(GuildRank r)
        {
            if (r == GuildRank.S) return "승리";
            var parts = new List<string>();
            if (Grant(r) > 0) parts.Add($"하사금 {Grant(r)}G");
            if (r > GuildRank.F && VisitorBonus(r) > VisitorBonus(r - 1)) parts.Add($"방문자 레벨 +{VisitorBonus(r)}");
            if (r > GuildRank.F && VisitInterval(r) < VisitInterval(r - 1)) parts.Add($"방문자가 더 자주 옴(간격 ×{VisitInterval(r):0.##})");
            // A rule of the guild's to set (P4-112, H2).
            foreach (var law in GuildLawRules.OpenedAt(r)) parts.Add($"길드 규칙 '{law.Name}' 열림");
            return string.Join(" · ", parts);
        }

        /// <summary>Where a condition stands now (pure: the numbers come from <see cref="GuildStanding"/>).</summary>
        public static int Progress(in Goal g, in GuildStanding s) => g.Kind switch
        {
            GoalKind.Fame => s.Fame,
            GoalKind.QuestsDone => s.QuestsDone,
            GoalKind.Recruits => s.Recruits,
            GoalKind.Upgrades => s.Upgrades,
            GoalKind.NestsCleared => s.NestsCleared,
            GoalKind.MembersAtLevel => s.MembersAtLevel?.Invoke(g.Level) ?? 0,
            GoalKind.ZonesKnown => s.ZonesKnown,
            GoalKind.SpeciesKnown => s.SpeciesKnown,
            GoalKind.RaidsHeld => s.RaidsHeld,
            GoalKind.Facilities => s.Facilities,
            GoalKind.LordsSlain => s.LordsSlain,
            _ => 0,
        };

        public static bool Met(in Goal g, in GuildStanding s) => Progress(g, s) >= g.Count;

        /// <summary>Every condition for <paramref name="rank"/> met.</summary>
        public static bool Reached(GuildRank rank, in GuildStanding s)
        {
            foreach (var g in GoalsFor(rank)) if (!Met(g, s)) return false;
            return true;
        }

        /// <summary>The rank the guild climbs to from <paramref name="from"/>: one step at a time, as far as the conditions go.</summary>
        public static GuildRank Climb(GuildRank from, in GuildStanding s)
        {
            var r = from;
            while (!IsTop(r) && Reached(Next(r), s)) r = Next(r);
            return r;
        }
    }

    /// <summary>The numbers the rank conditions count (filled by the rank service, made by hand in tests).</summary>
    public struct GuildStanding
    {
        public int Fame, QuestsDone, Recruits, Upgrades, NestsCleared, ZonesKnown, SpeciesKnown, RaidsHeld, Facilities, LordsSlain;
        /// <summary>Living members at the level or above.</summary>
        public Func<int, int> MembersAtLevel;
    }
}
