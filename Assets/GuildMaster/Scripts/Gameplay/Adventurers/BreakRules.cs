using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>How badly a member breaks (P4-104d).</summary>
    public enum BreakSeverity { None, Minor, Major, Extreme }

    /// <summary>
    /// What a breaking member does (P4-104d, user decision 2026-10-24: down to the extreme). Minor: walks off its quest behind its
    /// party (<see cref="Sabotage"/>), wanders, squanders its gold. Major: a fistfight, deserts its party. Extreme: quits the guild,
    /// goes berserk — at anyone near, comrades too, and may kill (user decision 2026-10-25).
    /// </summary>
    public enum BreakKind { None, Sabotage, Wander, Spree, Brawl, Desert, Quit, Berserk }

    /// <summary>A spell of high spirits (P4-104d, RimWorld's inspirations).</summary>
    public enum InspirationKind { None, Combat, Training }

    /// <summary>Where a member stands when it breaks: what it can do there.</summary>
    public struct BreakContext
    {
        /// <summary>On a quest still running.</summary>
        public bool OnQuest;
        /// <summary>Out with others (a quest party, a hunting band).</summary>
        public bool WithParty;
        /// <summary>In town and free (not out, not fighting).</summary>
        public bool InTownFree;
        public int Gold;
        /// <summary>Someone it dislikes is in town with it.</summary>
        public bool RivalInTown;
        /// <summary>Someone (a comrade or a monster) is near enough to go for.</summary>
        public bool AnyoneNear;
        public string Values;
        public IReadOnlyList<string> TraitIds;
    }

    /// <summary>
    /// When a low member breaks and how (P4-104d), and when a happy one is inspired (pure). Kept apart from F-2's stress, which
    /// has its own walking off and quitting (user decision 2026-10-25: both stay). Every number here is meant to be tuned in play.
    /// </summary>
    public static class BreakRules
    {
        // ---- When ----

        /// <summary>Mood under which a member may break, by severity: minor, major, extreme.</summary>
        public const float MinorBelow = 25f, MajorBelow = 15f, ExtremeBelow = 6f;
        /// <summary>Chance per game hour under each line.</summary>
        public const float MinorChance = 0.06f, MajorChance = 0.08f, ExtremeChance = 0.1f;
        /// <summary>Game hours after a break before the next one can come.</summary>
        public const float CooldownHours = 24f;

        /// <summary>The worst break this mood allows (the dice still decide whether it comes).</summary>
        public static BreakSeverity SeverityAt(float mood)
            => mood < ExtremeBelow ? BreakSeverity.Extreme : mood < MajorBelow ? BreakSeverity.Major : mood < MinorBelow ? BreakSeverity.Minor : BreakSeverity.None;

        public static float ChanceOf(BreakSeverity s) => s switch
        {
            BreakSeverity.Extreme => ExtremeChance,
            BreakSeverity.Major => MajorChance,
            BreakSeverity.Minor => MinorChance,
            _ => 0f,
        };

        // ---- How ----

        /// <summary>Gold worth squandering.</summary>
        public const int SpreeMinGold = 20;

        /// <summary>
        /// The break a member this low takes where it stands (pure; <paramref name="roll"/> in [0, 1)). Out on a quest: walks off it,
        /// behind its party (minor) or deserting it (major); at the worst it goes berserk where there is anyone to go for, else
        /// quits. In town: wanders or squanders (the money-minded squander more), comes to blows with someone it dislikes, quits
        /// or goes berserk — the bold berserk, the timid and those who value safety quit. None when nothing fits.
        /// </summary>
        public static BreakKind Choose(BreakSeverity severity, in BreakContext c, float roll)
        {
            bool bold = Has(c.TraitIds, "brave") || c.Values == "모험" || c.Values == "명예";
            bool timid = Has(c.TraitIds, "coward") || c.Values == "안전";
            switch (severity)
            {
                case BreakSeverity.Extreme:
                    if (c.AnyoneNear && !c.InTownFree && c.OnQuest) return BreakKind.Berserk;
                    float berserk = bold ? 0.7f : timid ? 0.2f : 0.45f;
                    if (c.AnyoneNear && roll < berserk) return BreakKind.Berserk;
                    return c.InTownFree ? BreakKind.Quit : c.AnyoneNear ? BreakKind.Berserk : BreakKind.None;
                case BreakSeverity.Major:
                    if (c.OnQuest) return BreakKind.Desert;
                    if (c.InTownFree && c.RivalInTown && roll < 0.7f) return BreakKind.Brawl;
                    return c.InTownFree ? Minor(c, roll) : BreakKind.None;
                case BreakSeverity.Minor:
                    if (c.OnQuest) return BreakKind.Sabotage;
                    return c.InTownFree ? Minor(c, roll) : BreakKind.None;
            }
            return BreakKind.None;
        }

        static BreakKind Minor(in BreakContext c, float roll)
        {
            if (c.Gold < SpreeMinGold) return BreakKind.Wander;
            float spree = c.Values == "돈" || Has(c.TraitIds, "greedy") ? 0.7f : 0.4f;
            return roll < spree ? BreakKind.Spree : BreakKind.Wander;
        }

        static bool Has(IReadOnlyList<string> ids, string id)
        {
            if (ids == null) return false;
            foreach (var t in ids) if (t == id) return true;
            return false;
        }

        // ---- What it does ----

        /// <summary>Game hours a wander lasts; a spree's lying about afterwards.</summary>
        public const float WanderHours = 3f, SpreeHangoverHours = 2f;
        /// <summary>Share of its gold a spree throws away (between the two).</summary>
        public const float SpreeShareMin = 0.4f, SpreeShareMax = 0.7f;
        /// <summary>A fistfight stops when one is down to this share of its health (nobody dies of it), or after this long.</summary>
        public const float BrawlFloor = 0.3f, BrawlHours = 1f;
        /// <summary>What a fistfight does to how the two feel about each other (both ways).</summary>
        public const int BrawlRelation = -15;
        /// <summary>A berserker comes to after this long, or once it is down to this share of its health.</summary>
        public const float BerserkHours = 1f, BerserkSpentAt = 0.2f;
        /// <summary>How far a berserker looks for someone to go for, and how far comrades come to stop it.</summary>
        public const float BerserkReach = 12f;

        /// <summary>Gold a spree throws away (pure): its share of what it carries, at least something.</summary>
        public static int SpreeCost(int gold, float roll01)
            => gold <= 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(gold * Mathf.Lerp(SpreeShareMin, SpreeShareMax, roll01)), 1, gold);

        /// <summary>
        /// What a hit in a fistfight may take (pure): never below <see cref="BrawlFloor"/> of the victim's health — blows, not
        /// wounds.
        /// </summary>
        public static float BrawlDamage(float amount, float hp, float maxHp) => Mathf.Clamp(Mathf.Min(amount, hp - maxHp * BrawlFloor), 0f, amount);

        // ---- Inspiration ----

        /// <summary>Mood from which a member may be inspired, the chance a game hour, how long it lasts and how long until the next.</summary>
        public const float InspiredAbove = 80f, InspirationChance = 0.04f, InspirationHours = 24f, InspirationCooldownHours = 48f;
        /// <summary>A combat inspiration's attack, a training one's EXP.</summary>
        public const float CombatAttack = 1.25f, TrainingExp = 1.5f;

        /// <summary>Which inspiration comes (pure): the fighting kind for those out fighting, the learning kind for the rest — the quick learners more.</summary>
        public static InspirationKind ChooseInspiration(bool outFighting, IReadOnlyList<string> traitIds, float roll)
        {
            float training = Has(traitIds, "quick_learner") ? 0.7f : outFighting ? 0.3f : 0.5f;
            return roll < training ? InspirationKind.Training : InspirationKind.Combat;
        }

        // ---- Words ----

        public static string Label(BreakKind k) => k switch
        {
            BreakKind.Sabotage => "태업",
            BreakKind.Wander => "방황",
            BreakKind.Spree => "탕진",
            BreakKind.Brawl => "주먹다짐",
            BreakKind.Desert => "무단 귀환",
            BreakKind.Quit => "탈퇴",
            BreakKind.Berserk => "광폭",
            _ => "-",
        };

        public static string Label(BreakSeverity s) => s switch
        {
            BreakSeverity.Minor => "가벼운",
            BreakSeverity.Major => "심한",
            BreakSeverity.Extreme => "극단적인",
            _ => "",
        };

        /// <summary>What it shouts as it breaks.</summary>
        public static string Line(BreakKind k) => k switch
        {
            BreakKind.Sabotage => "몰라, 난 이제 안 해. 그냥 따라만 갈게.",
            BreakKind.Wander => "...잠깐 혼자 있고 싶어.",
            BreakKind.Spree => "오늘은 다 잊고 마셔버릴 거야!",
            BreakKind.Brawl => "너 이리 와 봐!",
            BreakKind.Desert => "더는 못 해! 난 돌아간다!",
            BreakKind.Quit => "이딴 길드, 그만두겠어.",
            BreakKind.Berserk => "으아아아악!!",
            _ => null,
        };

        public static string Label(InspirationKind k) => k switch
        {
            InspirationKind.Combat => "전투 영감",
            InspirationKind.Training => "수련 영감",
            _ => "-",
        };

        public static string Effect(InspirationKind k) => k switch
        {
            InspirationKind.Combat => $"공격력 ×{CombatAttack:0.##}",
            InspirationKind.Training => $"얻는 EXP ×{TrainingExp:0.#}",
            _ => "",
        };
    }
}
