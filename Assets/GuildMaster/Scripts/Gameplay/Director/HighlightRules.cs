using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Director
{
    /// <summary>What a highlight is about (user decision 2026-11-05: big foes, crises, the town's defence — not growth or town life).</summary>
    public enum HighlightKind
    {
        Dragon, Lord, Nemesis, Rare, Boss,
        LowHp, Wipe, LastStand, Retreat,
        Raid, Horde, DragonOverTown, GateBroken,
    }

    /// <summary>One line of a member's chronicle (P4-129, saved with <see cref="Adventurers.AdventurerData"/>).</summary>
    [Serializable]
    public class ChronicleEntry
    {
        /// <summary>Game time in total hours.</summary>
        public float hour;
        public string text;

        public int Day => Mathf.FloorToInt(hour / 24f) + 1;
        public string Format() => $"[{Day}일 {Mathf.FloorToInt(hour % 24f)}시] {text}";
    }

    /// <summary>
    /// The spectator director's numbers and words (P4-129, H7): how interesting each kind of moment is, how long the
    /// camera stays on one before another may take it, and the title cards. Pure, covered by EditMode tests.
    /// </summary>
    public static class HighlightRules
    {
        // ---- Detection ----
        /// <summary>A member in a fight at or below this share of its health is in trouble.</summary>
        public const float LowHpRatio = 0.25f;
        /// <summary>…and out of it again from here.</summary>
        public const float RecoveredRatio = 0.45f;
        /// <summary>Two or more members this close, fighting and at or below this share, may all fall together.</summary>
        public const float WipeRatio = 0.4f;
        public const float WipeRadius = 8f;
        /// <summary>A fight counts as going on while the member was hit or struck within this many (game) seconds.</summary>
        public const float CombatMemory = 4f;
        /// <summary>A big foe's fight is over once no member has been at it for this long (real seconds).</summary>
        public const float FightGoneSeconds = 6f;
        /// <summary>Other moments (a member low, a raid…) end once not seen going on for this long — no flicker (real seconds).</summary>
        public const float StateGrace = 2f;
        /// <summary>Moments that are one beat, not a state: how long they stay up (real seconds).</summary>
        public const float BeatSeconds = 7f;
        /// <summary>Within this distance of the town centre the dragon is over the town.</summary>
        public const float OverTownRadius = 28f;
        /// <summary>Members this close to a fight are in it (for the chronicle).</summary>
        public const float WitnessRadius = 10f;

        // ---- Camera ----
        /// <summary>The camera stays on a moment at least this long (real seconds)…</summary>
        public const float MinHold = 6f;
        /// <summary>…unless one this much more interesting comes up.</summary>
        public const float InterruptMargin = 20f;
        /// <summary>After the hold, another moment takes over only when it beats the shown one by this much.</summary>
        public const float SwitchMargin = 5f;
        /// <summary>After a moment ends the camera lingers this long before moving on (real seconds).</summary>
        public const float Linger = 2.5f;
        /// <summary>The director keeps its hands off the camera this long after the player moves it (real seconds).</summary>
        public const float ManualPause = 8f;
        /// <summary>How long a title card stays (real seconds).</summary>
        public const float CardSeconds = 3.5f;

        public const int ChronicleCapacity = 40;

        public static float Score(HighlightKind k) => k switch
        {
            HighlightKind.DragonOverTown => 95f,
            HighlightKind.Dragon => 90f,
            HighlightKind.LastStand => 88f,
            HighlightKind.Lord => 85f,
            HighlightKind.Wipe => 82f,
            HighlightKind.GateBroken => 80f,
            HighlightKind.Nemesis => 75f,
            HighlightKind.LowHp => 70f,
            HighlightKind.Horde => 68f,
            HighlightKind.Raid => 65f,
            HighlightKind.Rare => 60f,
            HighlightKind.Boss => 55f,
            HighlightKind.Retreat => 45f,
            _ => 0f,
        };

        /// <summary>A raid is more interesting the more raiders press it (up to +20).</summary>
        public static float RaidScore(int strength) => Score(HighlightKind.Raid) + Mathf.Min(20f, 2f * Mathf.Max(0, strength));

        /// <summary>The kind of a big foe, or null for an ordinary monster.</summary>
        public static HighlightKind? BigFoe(bool dragon, bool lord, bool nemesis, bool rare, bool boss)
            => dragon ? HighlightKind.Dragon : lord ? HighlightKind.Lord : nemesis ? HighlightKind.Nemesis
                : rare ? HighlightKind.Rare : boss ? HighlightKind.Boss : (HighlightKind?)null;

        public static bool IsBigFoe(HighlightKind k) => k <= HighlightKind.Boss;

        public static string Title(HighlightKind k) => k switch
        {
            HighlightKind.Dragon => "드래곤과의 결전",
            HighlightKind.Lord => "지대 군주 토벌전",
            HighlightKind.Nemesis => "원수와의 재회",
            HighlightKind.Rare => "희귀 몬스터",
            HighlightKind.Boss => "우두머리와의 싸움",
            HighlightKind.LowHp => "위기!",
            HighlightKind.Wipe => "전멸 위기",
            HighlightKind.LastStand => "구사일생",
            HighlightKind.Retreat => "후퇴",
            HighlightKind.Raid => "마을 방어전",
            HighlightKind.Horde => "대규모 무리",
            HighlightKind.DragonOverTown => "드래곤 습격",
            HighlightKind.GateBroken => "성문이 뚫렸다",
            _ => "",
        };

        /// <summary>
        /// Which moment the camera should show: the one it shows (<paramref name="current"/>, -1 = none, shown for
        /// <paramref name="shownFor"/> seconds) or the best other one. Returns the index into <paramref name="scores"/>.
        /// </summary>
        public static int Pick(IReadOnlyList<float> scores, int current, float shownFor)
        {
            int best = -1;
            for (int i = 0; i < scores.Count; i++)
                if (i != current && (best < 0 || scores[i] > scores[best])) best = i;
            if (current < 0 || current >= scores.Count) return best;
            if (best < 0) return current;
            float margin = shownFor < MinHold ? InterruptMargin : SwitchMargin;
            return scores[best] >= scores[current] + margin ? best : current;
        }

        /// <summary>Adds a line to a chronicle (the same text on the same game day is not written twice), oldest out first.</summary>
        public static bool Write(List<ChronicleEntry> chronicle, float hour, string text)
        {
            if (chronicle == null || string.IsNullOrWhiteSpace(text)) return false;
            int day = Mathf.FloorToInt(hour / 24f);
            foreach (var e in chronicle)
                if (e.text == text && Mathf.FloorToInt(e.hour / 24f) == day) return false;
            chronicle.Add(new ChronicleEntry { hour = Mathf.Round(hour * 100f) / 100f, text = text });
            while (chronicle.Count > ChronicleCapacity) chronicle.RemoveAt(0);
            return true;
        }

        /// <summary>"아리스", "아리스와 토르", "아리스 외 2명".</summary>
        public static string Names(IReadOnlyList<string> names)
        {
            if (names == null || names.Count == 0) return "길드원";
            if (names.Count == 1) return names[0];
            if (names.Count == 2) return $"{names[0]}{Adventurers.MemoryRecorder.Josa(names[0], "과", "와")} {names[1]}";
            return $"{names[0]} 외 {names.Count - 1}명";
        }
    }
}
