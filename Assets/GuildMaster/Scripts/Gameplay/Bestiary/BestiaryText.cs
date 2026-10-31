using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Data;
using GuildMaster.World;

namespace GuildMaster.Bestiary
{
    /// <summary>
    /// What the bestiary says about a kind at each research level (P4-107): the log, the notifications and the bestiary window
    /// read it from here. Level 1: what it looks like from afar; 2: how it lives and fights, and what it is weak and strong
    /// against; 3: the guild's mastery.
    /// </summary>
    public static class BestiaryText
    {
        public static string ClassName(AdventurerClass c) => AdventurerData.ClassLabel(c);

        /// <summary>"약점 마법사 · 강함 전사" (empty for a kind with none).</summary>
        public static string Matchup(MonsterDefinition s)
            => s != null && BestiaryRules.TryMatchup(s.id, out var weak, out var strong) ? $"약점 {ClassName(weak)} · 강함 {ClassName(strong)}" : "";

        /// <summary>The news of a level reached, for the log and the notification.</summary>
        public static string LevelNews(MonsterDefinition s, int level) => level >= BestiaryRules.MaxLevel
            ? $"숙련 — 이 종에게 주는 피해 +{(BestiaryRules.MasteryDamage - 1f) * 100f:0}% · 받는 피해 −{(1f - BestiaryRules.MasteryTaken) * 100f:0}%"
            : $"{Matchup(s)} · 스킬과 생태가 도감에 기록됨";

        static string Danger(DangerLevel d) => d switch { DangerLevel.Low => "낮음", DangerLevel.Medium => "보통", _ => "높음" };

        static bool Overlord(MonsterDefinition s) => s.isRegionLord || s.isDragon;

        /// <summary>Where it lives, in words: its regions, or the kinds of ground it keeps to.</summary>
        static string Home(MonsterDefinition s)
        {
            var parts = new List<string>();
            foreach (var r in s.regions.Distinct()) parts.Add(TerrainRegions.Label(r));
            if (parts.Count == 0)
                foreach (var b in s.biomes.Distinct())
                    parts.Add(b switch { Biome.Forest => "숲", Biome.Mountain => "산", Biome.Shore => "물가", _ => "들판" });
            return parts.Count > 0 ? string.Join("·", parts) : "알 수 없음";
        }

        /// <summary>
        /// The kind's page at this research level, one fact a line: what anyone who saw it knows, then (from level 2) how it lives
        /// and fights and what it is weak and strong against, then (level 3) the mastery.
        /// </summary>
        public static List<string> Page(MonsterDefinition s, int level)
        {
            var lines = new List<string>();
            if (s == null || level <= 0) return lines;
            lines.Add($"위험 {Danger(s.danger)} · 체력 {BestiaryRules.Toughness(s.maxHp, Overlord(s))} · 사는 곳 {Home(s)}"
                      + (s.aggressiveToHumans ? " · 사람을 먼저 공격함" : ""));
            if (level < 2) return lines;
            var skills = s.skillIds.Select(SkillCatalog.NameOf).Where(n => !string.IsNullOrEmpty(n)).ToList();
            lines.Add("스킬: " + (skills.Count > 0 ? string.Join(", ", skills) : "없음"));
            var habits = new List<string> { $"번식 {BestiaryRules.Breeding(s.growthPerHour)}" };
            if (s.packHunter) habits.Add("무리 지어 사냥함");
            if (s.canRaid) habits.Add("마을을 습격함");
            if (s.fleesFromPredators) habits.Add("천적을 피해 달아남");
            lines.Add(string.Join(" · ", habits));
            if (s.prey.Count > 0) lines.Add("사냥감: " + string.Join(", ", s.prey.Where(p => p != null).Select(p => p.displayName)));
            if (s.rivals.Count > 0) lines.Add("다투는 종: " + string.Join(", ", s.rivals.Where(p => p != null).Select(p => p.displayName)));
            if (BestiaryRules.TryMatchup(s.id, out var weak, out var strong))
                lines.Add($"약점 {ClassName(weak)}(받는 피해 +{(BestiaryRules.Weak - 1f) * 100f:0}%) · 강함 {ClassName(strong)}(받는 피해 −{(1f - BestiaryRules.Strong) * 100f:0}%)");
            lines.Add($"의뢰 위험 판단 ×{BestiaryRules.RiskFactor(level):0.##}");
            if (level >= BestiaryRules.MaxLevel) lines.Add(LevelNews(s, level));
            return lines;
        }
    }
}
