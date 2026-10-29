using UnityEngine;

namespace GuildMaster.Recruitment
{
    /// <summary>How one argument in a recruit talk went.</summary>
    public enum PersuadeRoll { CriticalSuccess, Success, Failure, CriticalFailure }

    /// <summary>Where a recruit talk stands: 2-3 stages of three successes each.</summary>
    public enum PersuadeState { Ongoing, StageCleared, Won, Lost }

    /// <summary>The odds of one argument, in percent.</summary>
    public struct PersuadeChance
    {
        /// <summary>Chance of any success (critical included).</summary>
        public int Success;
        /// <summary>Part of <see cref="Success"/> that is a critical success.</summary>
        public int Critical;
        /// <summary>Part of the failures that is a critical failure.</summary>
        public int CriticalFail;
    }

    /// <summary>
    /// Recruiting as Mount &amp; Blade II: Bannerlord persuades (user decision 2026-10-29: "배너처럼 3번 성공 과정을 2~3번
    /// 거쳐야 됨, 실패 시 다시 영입 안 됨"). The guild master picks an argument; its chance of working is shown (once
    /// the guild master knows enough about the visitor to judge it) and a roll decides: a critical success counts
    /// twice, a critical failure ends the talk at once. Three successes clear a stage; a third failure in a stage
    /// ends it. Easy visitors take two stages, hard ones (traits that make recruiting harder) three. A visitor who
    /// says no never joins. Affinity — warmed by the town or soured by bad talk — shifts every chance.
    /// Pure rules, covered by tests.
    /// </summary>
    public static class RecruitPersuasion
    {
        public const int WinsPerStage = 3;
        /// <summary>Failures a stage forgives; the next one ends the talk (user choice 2026-10-29: "단계마다 실패 2번까지").</summary>
        public const int FailsAllowed = 2;
        public const int MinChance = 5, MaxChance = 95;
        /// <summary>The same argument again right away is less convincing.</summary>
        public const int RepeatPenalty = 15;
        /// <summary>Each stage after the first is harder (5 → 6 on 2026-10-29: the choices now follow the talk, so it got easier).</summary>
        public const int StagePenalty = 6;
        /// <summary>Points of affinity per point of chance, and the most it can add or take (2026-10-29: /2 and +20 → /3 and ±15).</summary>
        public const int AffinityStep = 3, AffinityBonusMax = 15;
        /// <summary>Part of a failure that is a critical one (ends the talk): 15% → 20% on 2026-10-29; half for a bad line.</summary>
        public const float CriticalFailShare = 0.2f, BadCriticalFailShare = 0.5f;

        /// <summary>Two stages, or three for a visitor whose traits make recruiting harder.</summary>
        public static int StagesFor(float recruitDifficulty) => recruitDifficulty > 0.001f ? 3 : 2;

        /// <summary>
        /// The chance before affinity and the rest, by how the line sits with this visitor. Lowered on 2026-10-29 (user:
        /// "영입이 쉬워졌으니 실패 확률도 올라가야") from 80/70/55/50/25/20: once the choices followed the visitor's own
        /// questions, the matching pitch came up nearly every turn.
        /// </summary>
        public static int BaseChance(RecruitReaction r) => r switch
        {
            RecruitReaction.ValueMatch => 75,
            RecruitReaction.TraitMatch => 65,
            RecruitReaction.Answered => 50,
            RecruitReaction.Friendly => 45,
            RecruitReaction.LowEffort => 20,
            RecruitReaction.Clash => 15,
            _ => 5,
        };

        /// <summary>Affinity 40 is neutral; each 3 points above or below moves the chance by 1 (±15).</summary>
        public static int AffinityBonus(int affinity)
            => Mathf.Clamp((affinity - RecruitmentSession.StartAffinity) / AffinityStep, -AffinityBonusMax, AffinityBonusMax);

        public static PersuadeChance Chance(RecruitReaction r, int affinity, int stage, bool repeated)
        {
            int success = BaseChance(r) + AffinityBonus(affinity) - stage * StagePenalty - (repeated ? RepeatPenalty : 0);
            success = Mathf.Clamp(success, MinChance, MaxChance);
            bool strong = r == RecruitReaction.ValueMatch || r == RecruitReaction.TraitMatch;
            bool bad = r == RecruitReaction.Clash || r == RecruitReaction.Rude || r == RecruitReaction.LowEffort;
            return new PersuadeChance
            {
                Success = success,
                Critical = Mathf.RoundToInt(success * (strong ? 0.25f : 0.1f)),
                CriticalFail = Mathf.RoundToInt((100 - success) * (bad ? BadCriticalFailShare : CriticalFailShare)),
            };
        }

        /// <summary>The roll for <paramref name="roll01"/> in [0, 1).</summary>
        public static PersuadeRoll Roll(PersuadeChance c, float roll01)
        {
            float x = Mathf.Clamp01(roll01) * 100f;
            if (x < c.Critical) return PersuadeRoll.CriticalSuccess;
            if (x < c.Success) return PersuadeRoll.Success;
            if (x >= 100f - c.CriticalFail) return PersuadeRoll.CriticalFailure;
            return PersuadeRoll.Failure;
        }

        public static bool IsSuccess(PersuadeRoll r) => r == PersuadeRoll.Success || r == PersuadeRoll.CriticalSuccess;

        /// <summary>
        /// Moves the visitor's progress by one roll (pure; tested): <paramref name="stage"/> (0-based), wins and fails in
        /// it. Returns what happened.
        /// </summary>
        public static PersuadeState Advance(PersuadeRoll roll, int stages, ref int stage, ref int wins, ref int fails)
        {
            if (roll == PersuadeRoll.CriticalFailure) return PersuadeState.Lost;
            if (roll == PersuadeRoll.Failure)
            {
                fails++;
                return fails > FailsAllowed ? PersuadeState.Lost : PersuadeState.Ongoing;
            }
            wins += roll == PersuadeRoll.CriticalSuccess ? 2 : 1;
            if (wins < WinsPerStage) return PersuadeState.Ongoing;
            stage++;
            wins = 0;
            fails = 0;
            return stage >= stages ? PersuadeState.Won : PersuadeState.StageCleared;
        }

        /// <summary>How affinity moves with a roll (it shifts the next chances).</summary>
        public static int AffinityDelta(PersuadeRoll r) => r switch
        {
            PersuadeRoll.CriticalSuccess => 6,
            PersuadeRoll.Success => 3,
            PersuadeRoll.Failure => -2,
            _ => -8,
        };

        /// <summary>The line under the visitor's answer.</summary>
        public static string Feedback(PersuadeRoll r, int chance) => r switch
        {
            PersuadeRoll.CriticalSuccess => $"▲▲ 크게 성공 (확률 {chance}%) · 성공 2번으로 침",
            PersuadeRoll.Success => $"▲ 성공 (확률 {chance}%)",
            PersuadeRoll.Failure => $"▼ 실패 (확률 {chance}%)",
            _ => $"▼▼ 크게 실패 (확률 {chance}%)",
        };

        /// <summary>"설득 2/3단계 · 성공 ●●○ · 실패 ×○" for the window.</summary>
        public static string Progress(int stages, int stage, int wins, int fails)
        {
            string w = new string('●', Mathf.Min(wins, WinsPerStage)) + new string('○', Mathf.Max(0, WinsPerStage - wins));
            string f = new string('×', Mathf.Min(fails, FailsAllowed)) + new string('○', Mathf.Max(0, FailsAllowed - fails));
            return $"설득 {Mathf.Min(stage + 1, stages)}/{stages}단계 · 성공 {w} · 실패 {f}";
        }
    }
}
