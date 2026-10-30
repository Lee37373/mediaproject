using System;
using System.Collections.Generic;
using GuildMaster.Traits;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>One thought a member holds (P4-104c): what happened, about whom, how much it weighs and until when. Saved with the member.</summary>
    [Serializable]
    public class ThoughtEntry
    {
        /// <summary>The kind (<see cref="ThoughtDef.Id"/>).</summary>
        public string id;
        /// <summary>Whom or what it is about (a member's id, a monster's name), empty for none; one entry per kind and subject.</summary>
        public string about;
        /// <summary>The words shown, the subject's name filled in ("동료 레오가 죽었다").</summary>
        public string label;
        /// <summary>Mood of one, after the member's traits (<see cref="ThoughtRules.TraitScale"/>).</summary>
        public float value;
        public int stack = 1;
        /// <summary>Game hours: when it first came, when it fades.</summary>
        public float since, until;
    }

    /// <summary>What kind of thing a thought is about, for the traits that colour it.</summary>
    public enum ThoughtTone { Plain, Fear, People, Gain, Loyalty, Glory }

    /// <summary>A kind of thought: its words, its weight, how long it lasts and how many of it stack.</summary>
    public sealed class ThoughtDef
    {
        public string Id;
        /// <summary>Words, "{0}" = the subject's name.</summary>
        public string Label;
        public float Mood;
        public float Hours;
        public int MaxStack = 1;
        public ThoughtTone Tone;
        /// <summary>What goes through the member's head when it comes (a bubble), "{0}" = the subject; none = no bubble.</summary>
        public string[] Inner = Array.Empty<string>();
        /// <summary>Goes to the game log when it comes (a few that tell a story: a close friend lost, a last stand, a great kill, being let down).</summary>
        public bool Log;
    }

    /// <summary>How a member feels, in words.</summary>
    public enum MoodLevel { Breaking, Low, Steady, Good, High }

    /// <summary>
    /// Thoughts and mood (P4-104c, G2 — RimWorld's thoughts, user decision 2026-10-24: stress stays as it was, the long weight,
    /// and mood is the day's sum of thoughts). Pure: the catalogue of thoughts, how they stack and fade, how traits colour
    /// them, mood and its levels, and what mood does to the choice of work. <see cref="ThoughtRecorder"/> gives members the
    /// thoughts; this is what they mean.
    /// </summary>
    public static class ThoughtRules
    {
        public const float BaseMood = 50f;
        /// <summary>Each stacked repeat of a thought weighs this share of the first.</summary>
        public const float StackShare = 0.5f;
        /// <summary>A thought this heavy shows as a bubble over the head and in the inspector's top three.</summary>
        public const float StrongAt = 6f;
        /// <summary>One this heavy goes to the log.</summary>
        public const float LoggedAt = 10f;
        /// <summary>Game hours between two thought bubbles of one member.</summary>
        public const float BubbleCooldownHours = 1f;

        // ---------------- The catalogue ----------------

        static ThoughtDef D(string id, string label, float mood, float hours, ThoughtTone tone = ThoughtTone.Plain, int stack = 1, params string[] inner)
            => new() { Id = id, Label = label, Mood = mood, Hours = hours, Tone = tone, MaxStack = stack, Inner = inner };

        static ThoughtDef Logged(ThoughtDef d)
        {
            d.Log = true;
            return d;
        }

        // A comrade's death, by how the member felt about them (Relationships).
        public static readonly ThoughtDef ComradeDiedClose = Logged(D("died_close", "아끼던 {0}이(가) 죽었다", -20f, 96f, ThoughtTone.People, 1, "({0}... 거짓말이지.)", "({0}, 왜 하필 너야...)"));
        public static readonly ThoughtDef ComradeDiedFriend = D("died_friend", "동료 {0}이(가) 죽었다", -12f, 72f, ThoughtTone.People, 1, "({0}... 편히 쉬어.)");
        /// <summary>
        /// One it was not close to: a small weight that stacks (held once, not per name) — three deaths in as many days left a
        /// twenty-strong guild low and sitting work out (P4-104e measure), friends' deaths are their own thoughts.
        /// </summary>
        public static readonly ThoughtDef ComradeDied = D("died", "길드원을 잃었다", -2f, 48f, ThoughtTone.People, 3);
        public static readonly ThoughtDef RivalDied = D("died_rival", "싫어하던 {0}이(가) 죽었다", 3f, 48f, ThoughtTone.People);
        public static readonly ThoughtDef SawComradeFall = D("saw_fall", "눈앞에서 {0}이(가) 쓰러졌다", -6f, 24f, ThoughtTone.Fear, 2, "(눈앞에서... {0}이(가)...)");

        public static readonly ThoughtDef QuestDone = D("quest_done", "의뢰를 해냈다", 6f, 24f, ThoughtTone.Gain, 3, "(해냈다!)", "(오늘 일은 깔끔했어.)");
        public static readonly ThoughtDef QuestFailed = D("quest_failed", "의뢰에 실패했다", -8f, 24f, ThoughtTone.Plain, 2, "(이게 뭐야, 망했어...)");
        public static readonly ThoughtDef LevelUp = D("level_up", "더 강해졌다", 5f, 12f, ThoughtTone.Plain, 2, "(몸이 가벼워졌어!)");
        public static readonly ThoughtDef NewGear = D("new_gear", "새 장비를 맞췄다", 4f, 24f, ThoughtTone.Gain);
        // Everyday good things (P4-104e measure: with nothing but quests and level-ups to lift it, mood only ever sank).
        public static readonly ThoughtDef GoodHaul = D("good_haul", "한몫 챙겨 돌아왔다", 3f, 12f, ThoughtTone.Gain, 2);
        public static readonly ThoughtDef WellRested = D("well_rested", "푹 쉬었다", 3f, 12f);
        public static readonly ThoughtDef NearDeath = D("near_death", "{0}에게 죽을 뻔했다", -8f, 24f, ThoughtTone.Fear, 2, "(죽는 줄 알았어...)");
        public static readonly ThoughtDef LastStand = Logged(D("last_stand", "구사일생으로 살아났다", -10f, 48f, ThoughtTone.Fear, 1, "(살았다... 진짜 살았어.)"));
        public static readonly ThoughtDef Slew = D("slew", "{0}을(를) 쓰러뜨렸다", 3f, 12f, ThoughtTone.Glory, 3);
        public static readonly ThoughtDef GreatKill = Logged(D("great_kill", "{0}을(를) 쓰러뜨리는 데 함께했다", 15f, 72f, ThoughtTone.Glory, 1, "(우리가 해냈어!)"));
        public static readonly ThoughtDef SawDragon = D("saw_dragon", "드래곤을 봤다", -6f, 24f, ThoughtTone.Fear, 1, "(저, 저게 드래곤...?)");
        public static readonly ThoughtDef RaidHeld = D("raid_held", "마을을 지켜냈다", 5f, 24f, ThoughtTone.Glory, 2, "(마을은 우리가 지킨다!)");
        public static readonly ThoughtDef Trapped = D("trapped", "덫에 걸렸다", -3f, 6f, ThoughtTone.Fear, 2);

        // Breaks and inspirations (P4-104d).
        public static readonly ThoughtDef LetItOut = D("let_it_out", "한바탕 쏟아내고 나니 후련하다", 12f, 36f);
        public static readonly ThoughtDef Inspired = D("inspired", "영감이 떠올랐다", 5f, 24f);
        public static readonly ThoughtDef Brawled = D("brawled", "{0}와(과) 주먹다짐을 했다", -5f, 24f, ThoughtTone.People);
        public static readonly ThoughtDef BerserkVictim = D("berserk_victim", "광폭해진 {0}에게 맞았다", -8f, 48f, ThoughtTone.Fear, 2, "({0}, 정신 차려!)");

        // Its nature and its quirks (P4-104e: what goes against its nature weighs on it, CK3).
        public static readonly ThoughtDef DreadfulWork = D("dreadful_work", "무서운 일을 맡았다", -6f, 24f, ThoughtTone.Plain, 1, "(괜히 맡았나...)");
        public static readonly ThoughtDef DullWork = D("dull_work", "시시한 일을 맡았다", -2f, 12f);
        public static readonly ThoughtDef NewLand = D("new_land", "새로운 땅을 보았다", 4f, 24f, ThoughtTone.Plain, 3, "(저런 곳이 있었구나!)");
        public static readonly ThoughtDef QuirkGained = D("quirk", "{0}이(가) 생겼다", -3f, 24f, ThoughtTone.Plain);
        public static readonly ThoughtDef Chat = D("chat", "즐거운 잡담을 나눴다", 2f, 8f, ThoughtTone.People, 2);
        public static readonly ThoughtDef Quarrel = D("quarrel", "{0}와(과) 말다툼을 했다", -3f, 12f, ThoughtTone.People, 1, "(쳇, 말이 안 통해.)");

        // Nemeses (P4-109, G4): a grudge against the monster that killed a friend or drove it off, and the day it falls.
        public static readonly ThoughtDef Grudge = D("grudge", "원수 {0}을(를) 잊지 않는다", -3f, 168f, ThoughtTone.Plain, 1, "({0}... 반드시 갚아 준다.)", "(두고 봐라, {0}.)");
        public static readonly ThoughtDef AvengedByHand = Logged(D("avenged_hand", "원수 {0}을(를) 내 손으로 쓰러뜨렸다", 15f, 72f, ThoughtTone.Glory, 1, "(갚았다... 드디어 갚았어!)"));
        public static readonly ThoughtDef Avenged = D("avenged", "원수 {0}이(가) 쓰러졌다", 8f, 48f, ThoughtTone.People, 1, "(이제야 편히 쉬겠구나.)");
        public static readonly ThoughtDef NemesisFell = D("nemesis_fell", "원수 {0}이(가) 남의 손에 죽었다", 3f, 24f);

        // Places worth going to (P4-105b).
        public static readonly ThoughtDef HotSpring = D("hot_spring", "온천에서 몸을 녹였다", 8f, 36f, ThoughtTone.Plain, 1, "(아... 살 것 같다.)");
        public static readonly ThoughtDef Blessed = D("blessed", "신의 가호를 받았다", 6f, 48f, ThoughtTone.Plain, 1, "(마음이 한결 편해졌어.)");
        public static readonly ThoughtDef CosyCamp = D("cosy_camp", "버려진 야영지에서 아늑하게 밤을 보냈다", 3f, 12f);
        public static readonly ThoughtDef Lookout = D("lookout", "감시탑에서 멀리 내다봤다", 2f, 12f, ThoughtTone.Plain, 1, "(저 멀리까지 다 보여!)");

        // The guild master's care (F-2).
        public static readonly ThoughtDef Talked = D("talked", "길드 마스터가 이야기를 들어 주었다", 5f, 24f, ThoughtTone.People);
        public static readonly ThoughtDef Bonus = D("bonus", "보너스를 받았다", 8f, 24f, ThoughtTone.Gain, 1, "(헤헤, 이 맛에 일하지.)");
        public static readonly ThoughtDef Ignored = Logged(D("ignored", "길드 마스터가 내 부탁을 무시했다", -10f, 48f, ThoughtTone.Loyalty, 2, "(내 말은 들은 척도 안 하네.)"));
        public static readonly ThoughtDef FriendLeft = D("friend_left", "친구 {0}이(가) 길드를 떠났다", -6f, 48f, ThoughtTone.Loyalty);

        // What a member's state says, worked out each time and never stored (situational).
        public static readonly ThoughtDef HeavyHeart = D("heavy_heart", "마음이 무겁다", -5f, 0f);
        public static readonly ThoughtDef Worn = D("worn", "지쳤다", -6f, 0f);
        public static readonly ThoughtDef Hurt = D("hurt", "다쳐서 아프다", -5f, 0f);
        public static readonly ThoughtDef Wounded = D("wounded", "크게 다쳤다", -8f, 0f, ThoughtTone.Fear);
        public static readonly ThoughtDef InDebt = D("in_debt", "빚이 있다", -3f, 0f, ThoughtTone.Gain);
        public static readonly ThoughtDef OnLeave = D("on_leave", "휴가 중이다", 6f, 0f);
        public static readonly ThoughtDef WithFriend = D("with_friend", "{0}와(과) 같은 파티다", 3f, 0f, ThoughtTone.People);
        public static readonly ThoughtDef WithRival = D("with_rival", "싫은 {0}와(과) 같은 파티다", -4f, 0f, ThoughtTone.People);
        // Against its nature, and its fears (P4-104e).
        public static readonly ThoughtDef Crowded = D("crowded", "사람이 너무 많아 불편하다", -4f, 0f);
        public static readonly ThoughtDef Lonely = D("lonely", "혼자라 쓸쓸하다", -3f, 0f);
        public static readonly ThoughtDef WantsRest = D("wants_rest", "그만 쉬고 싶다", -4f, 0f);
        public static readonly ThoughtDef Idle = D("idle", "할 일 없이 놀고 있다", -2f, 0f);
        public static readonly ThoughtDef Afraid = D("afraid", "{0}이(가) 무섭다", -6f, 0f);

        public static readonly IReadOnlyList<ThoughtDef> All = new[]
        {
            ComradeDiedClose, ComradeDiedFriend, ComradeDied, RivalDied, SawComradeFall, QuestDone, QuestFailed, LevelUp, NewGear, GoodHaul, WellRested,
            NearDeath, LastStand, Slew, GreatKill, SawDragon, RaidHeld, Trapped, LetItOut, Inspired, Brawled, BerserkVictim,
            DreadfulWork, DullWork, NewLand, QuirkGained, Chat, Quarrel, Grudge, AvengedByHand, Avenged, NemesisFell, HotSpring, Blessed, CosyCamp, Lookout,
            Talked, Bonus, Ignored, FriendLeft,
            HeavyHeart, Worn, Hurt, Wounded, InDebt, OnLeave, WithFriend, WithRival, Crowded, Lonely, WantsRest, Idle, Afraid,
        };

        public static ThoughtDef Find(string id)
        {
            foreach (var d in All) if (d.Id == id) return d;
            return null;
        }

        // ---------------- Traits colour them ----------------

        /// <summary>
        /// How much a thought of this tone weighs for this member: the brave shrug fear off (×0.5), cowards feel it more (×1.5);
        /// the sociable feel people more, loners less; the greedy feel gain more; the loyal feel being let down more. What it values
        /// too (P4-104e): money gain ×1.25, comrades people ×1.25, safety fear ×1.25, honour glory ×1.5. Nerves of steel (a quirk)
        /// halve fear.
        /// </summary>
        public static float TraitScale(ThoughtTone tone, IReadOnlyList<string> traitIds, string values = null, IReadOnlyList<string> quirks = null)
        {
            if (tone == ThoughtTone.Plain) return 1f;
            float k = 1f;
            if (traitIds != null)
                foreach (var t in traitIds)
                    k *= (tone, t) switch
                    {
                        (ThoughtTone.Fear, "brave") => 0.5f,
                        (ThoughtTone.Fear, "coward") => 1.5f,
                        (ThoughtTone.People, "sociable") => 1.5f,
                        (ThoughtTone.People, "loner") => 0.5f,
                        (ThoughtTone.Gain, "greedy") => 1.5f,
                        (ThoughtTone.Loyalty, "loyal") => 1.5f,
                        _ => 1f,
                    };
            k *= (tone, values) switch
            {
                (ThoughtTone.Gain, "돈") => 1.25f,
                (ThoughtTone.People, "동료") => 1.25f,
                (ThoughtTone.Fear, "안전") => 1.25f,
                (ThoughtTone.Glory, "명예") => 1.5f,
                _ => 1f,
            };
            if (tone == ThoughtTone.Fear && QuirkRules.Has(quirks, QuirkRules.SteelNerves)) k *= 0.5f;
            return k;
        }

        // ---------------- Holding them ----------------

        /// <summary>The weight of a thought with its repeats: each repeat adds <see cref="StackShare"/> of the first.</summary>
        public static float Weight(ThoughtEntry t) => t.value * (1f + StackShare * Mathf.Max(0, t.stack - 1));

        /// <summary>
        /// Gives a thought (pure). The same kind about the same subject is held once: it lasts from now again and stacks up to
        /// its limit. Returns the entry.
        /// </summary>
        public static ThoughtEntry Add(List<ThoughtEntry> thoughts, ThoughtDef def, float now, string about, string name, IReadOnlyList<string> traitIds,
            string values = null, IReadOnlyList<string> quirks = null)
        {
            about ??= "";
            foreach (var t in thoughts)
            {
                if (t.id != def.Id || t.about != about) continue;
                t.until = now + def.Hours;
                t.stack = Mathf.Min(def.MaxStack, t.stack + 1);
                return t;
            }
            var entry = new ThoughtEntry
            {
                id = def.Id, about = about, label = Fill(def.Label, name), value = def.Mood * TraitScale(def.Tone, traitIds, values, quirks),
                since = now, until = now + def.Hours,
            };
            thoughts.Add(entry);
            return entry;
        }

        /// <summary>Drops the thoughts that have faded by now (pure). Returns how many went.</summary>
        public static int Expire(List<ThoughtEntry> thoughts, float now) => thoughts.RemoveAll(t => t.until <= now);

        /// <summary>
        /// The subject's name into the words, its particles fitted to the name ("{0}이(가)" → "레오가", "{0}을(를)" → "슬라임을",
        /// "{0}와(과)" → "리안과").
        /// </summary>
        public static string Fill(string text, string name)
        {
            if (string.IsNullOrEmpty(name)) return text.Replace("{0}", "");
            return text.Replace("{0}이(가)", name + MemoryRecorder.Josa(name, "이", "가"))
                       .Replace("{0}을(를)", name + MemoryRecorder.Josa(name, "을", "를"))
                       .Replace("{0}와(과)", name + MemoryRecorder.Josa(name, "과", "와"))
                       .Replace("{0}", name);
        }

        // ---------------- Mood ----------------

        /// <summary>Mood from the held thoughts and the situational ones' sum, 0..100 (pure).</summary>
        public static float Mood(IReadOnlyList<ThoughtEntry> thoughts, float situational)
        {
            float m = BaseMood + situational;
            if (thoughts != null) foreach (var t in thoughts) m += Weight(t);
            return Mathf.Clamp(m, 0f, 100f);
        }

        public static MoodLevel LevelOf(float mood)
            => mood < 25f ? MoodLevel.Breaking : mood < 40f ? MoodLevel.Low : mood < 65f ? MoodLevel.Steady : mood < 80f ? MoodLevel.Good : MoodLevel.High;

        public static string Label(MoodLevel l) => l switch
        {
            MoodLevel.Breaking => "무너지기 직전",
            MoodLevel.Low => "우울",
            MoodLevel.Good => "좋음",
            MoodLevel.High => "최고",
            _ => "보통",
        };

        /// <summary>Stress's weight on the mood (F-2's levels): tense −5, strained −10, at the limit −18.</summary>
        public static float HeavyHeartMood(Welfare.StressLevel level) => level switch
        {
            Welfare.StressLevel.Tense => -5f,
            Welfare.StressLevel.Strained => -10f,
            Welfare.StressLevel.Breaking => -18f,
            _ => 0f,
        };

        /// <summary>
        /// Odds that a member this low sits a decision out instead of going to work (pure): none from "보통" up, one in ten when
        /// low, a third on the edge of breaking (P4-104e measure: a quarter when merely low left a grieving guild idle).
        /// </summary>
        public static float SitOutChance(float mood) => LevelOf(mood) switch
        {
            MoodLevel.Breaking => 0.35f,
            MoodLevel.Low => 0.1f,
            _ => 0f,
        };
    }
}
