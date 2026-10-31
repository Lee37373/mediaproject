using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Guild
{
    /// <summary>A rule of the guild was changed (P4-112): from which way to which, and how many it pleased and galled.</summary>
    public struct GuildLawChangedEvent { public GuildLaw Law; public int From, To; public int Pleased, Displeased; }

    /// <summary>The guild's holiday has come (the holiday rule, P4-112): nobody goes out today.</summary>
    public struct GuildHolidayEvent { public int Day; }

    /// <summary>
    /// The guild's rules (P4-112, H2; the rules themselves in <see cref="GuildLawRules"/>). Holds which way each is set and until
    /// when a changed one stays as it is; the guild master changes them in the rules window (P). A rule opens at its rank
    /// (<see cref="GuildRankService"/>). When one changes, those it pleases or galls most say so; how each member takes the
    /// rules as they stand is a thought of the moment (<see cref="ThoughtRecorder.Situational"/>), no more (user decision). The
    /// systems read the numbers from the statics here, which hold the defaults (the game as it was) whenever no guild is up.
    /// Saved as "laws".
    /// </summary>
    public class GuildLaws : MonoBehaviour, ISaveable
    {
        static readonly int[] s_choice = new int[GuildLawRules.Count];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => SetDefaults();

        static void SetDefaults()
        {
            for (int i = 0; i < s_choice.Length; i++) s_choice[i] = GuildLawRules.All[i].Default;
        }

        /// <summary>The way this rule is set now (its default with no guild up).</summary>
        public static int Of(GuildLaw law) => s_choice[(int)law];

        // ---------------- What the systems ask ----------------

        /// <summary>The share of a loot's price the guild pays its finder (1, or the finder's fee when loot is the guild's).</summary>
        public static float LootShare => Of(GuildLaw.Loot) == 1 ? GuildLawRules.LootFeeShare : 1f;
        public static float SplitEqualShare => GuildLawRules.SplitEqualShare(Of(GuildLaw.Split));
        public static float RestBelowHp => GuildLawRules.RestBelowHp(Of(GuildLaw.Wounded));
        public static float RestAboveFatigue => GuildLawRules.RestAboveFatigue(Of(GuildLaw.Wounded));
        public static bool HealFirst => Of(GuildLaw.Wounded) == 1;
        public static int NightMarch => Of(GuildLaw.NightMarch);
        public static bool MandatoryTraining => Of(GuildLaw.Training) == 1;
        public static bool PartyOnlyRisky => Of(GuildLaw.RiskyWork) == 1;
        public static bool GuildPaysUpkeep => Of(GuildLaw.Upkeep) == 1;
        public static bool KeepToTrails => Of(GuildLaw.DeepWoods) == 1;
        public static float ShopFactor => GuildLawRules.ShopFactor(Of(GuildLaw.Shop));

        /// <summary>Today is the guild's holiday.</summary>
        public static bool IsHoliday => Of(GuildLaw.Holiday) == 1 && Services.TryGet<GameClock>(out var c) && GuildLawRules.IsHolidayDay(c.Day);
        public static float HolidayRelief => IsHoliday ? GuildLawRules.HolidayRelief : 1f;

        // ---------------- The guild's choices ----------------

        GameClock _clock;
        GuildRoster _roster;
        readonly float[] _lockedUntil = new float[GuildLawRules.Count];

        public void Init(GameClock clock, GuildRoster roster)
        {
            _clock = clock;
            _roster = roster;
            SetDefaults();
            EventBus.Subscribe<DayPassedEvent>(OnDay);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<DayPassedEvent>(OnDay);
            SetDefaults();
        }

        void OnDay(DayPassedEvent e)
        {
            if (Of(GuildLaw.Holiday) == 1 && GuildLawRules.IsHolidayDay(e.Day)) EventBus.Publish(new GuildHolidayEvent { Day = e.Day });
        }

        static GuildRank Rank => Services.TryGet<GuildRankService>(out var r) ? r.Rank : GuildRank.F;

        public bool IsOpen(GuildLaw law) => GuildLawRules.IsOpen(law, Rank);

        /// <summary>Game hours until this rule may change again (0 = now).</summary>
        public float HoursLocked(GuildLaw law) => Mathf.Max(0f, _lockedUntil[(int)law] - _clock.TotalHours);

        /// <summary>Why the rule cannot be set this way now; null = it can.</summary>
        public string CannotChange(GuildLaw law, int option)
        {
            var def = GuildLawRules.Def(law);
            if (option < 0 || option >= def.Options.Length) return "없는 방식입니다.";
            if (!IsOpen(law)) return $"길드 {GuildRankRules.Name(def.OpensAt)}에 열립니다.";
            if (Of(law) == option) return "지금 이 방식입니다.";
            float left = HoursLocked(law);
            if (left > 0f) return $"{Mathf.CeilToInt(left)}시간 뒤에 바꿀 수 있습니다.";
            return null;
        }

        /// <summary>Sets the rule this way (the guild master's call); <paramref name="force"/> skips the rank and the lock (debug). True when it changed.</summary>
        public bool Change(GuildLaw law, int option, bool force = false)
        {
            var def = GuildLawRules.Def(law);
            if (option < 0 || option >= def.Options.Length || Of(law) == option) return false;
            if (!force && CannotChange(law, option) != null) return false;
            int from = Of(law);
            s_choice[(int)law] = option;
            _lockedUntil[(int)law] = _clock.TotalHours + GuildLawRules.LockHours;
            var (pleased, displeased) = React(law, from, option);
            Debug.Log($"[Laws] {def.Name}: {def.Options[from].Label} → {def.Options[option].Label} (pleased {pleased}, galled {displeased})");
            EventBus.Publish(new GuildLawChangedEvent { Law = law, From = from, To = option, Pleased = pleased, Displeased = displeased });
            return true;
        }

        IEnumerable<Adventurer> Members()
        {
            foreach (var m in _roster.Members)
                if (m != null && m.IsAlive && m.IsGuildMember && !m.IsVisitor && m.Data != null) yield return m;
        }

        static float Feeling(Adventurer a, GuildLaw law, int option)
            => GuildLawRules.Feeling(law, option, a.Data.traitIds, a.Data.values, GuildLawRules.Def(law).NewcomersOnly && a.IsNewcomer);

        /// <summary>
        /// Those a change touches: each member's feeling now against before; the few it moves most say so (a line of the new
        /// way's, pleased or galled). Returns how many it pleased and galled.
        /// </summary>
        (int pleased, int displeased) React(GuildLaw law, int from, int to)
        {
            var o = GuildLawRules.Def(law).Options[to];
            var moved = new List<(Adventurer member, float delta)>();
            foreach (var m in Members())
            {
                float delta = Feeling(m, law, to) - Feeling(m, law, from);
                if (!Mathf.Approximately(delta, 0f)) moved.Add((m, delta));
            }
            int said = 0;
            foreach (var (m, delta) in moved.OrderByDescending(x => Mathf.Abs(x.delta)))
            {
                if (said >= GuildLawRules.MaxReactions || Mathf.Abs(delta) < GuildLawRules.ReactAt) break;
                var lines = delta > 0f ? o.Pleased : o.Displeased;
                if (lines.Length == 0) continue;
                m.Say(lines[(said + (int)law) % lines.Length]);
                said++;
            }
            return (moved.Count(x => x.delta > 0f), moved.Count(x => x.delta < 0f));
        }

        /// <summary>How many members are glad of the rule set this way, and how many galled (for the window).</summary>
        public (int liked, int disliked) Reactions(GuildLaw law, int option)
        {
            int liked = 0, disliked = 0;
            foreach (var m in Members())
            {
                float f = Feeling(m, law, option);
                if (f > 0f) liked++;
                else if (f < 0f) disliked++;
            }
            return (liked, disliked);
        }

        /// <summary>How this member takes the rules as they stand (its thought of the moment).</summary>
        public static LawMood MoodOf(Adventurer a)
            => GuildLawRules.Mood(s_choice, a.Data.traitIds, a.Data.values, a.IsNewcomer);

        // ---------------- Save (P5-4) ----------------

        [Serializable]
        class SaveData
        {
            public int[] choices;
            public float[] locked_until;
        }

        public string SaveKey => "laws";

        public string Capture() => JsonUtility.ToJson(new SaveData { choices = (int[])s_choice.Clone(), locked_until = (float[])_lockedUntil.Clone() });

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            SetDefaults();
            Array.Clear(_lockedUntil, 0, _lockedUntil.Length);
            for (int i = 0; i < GuildLawRules.Count; i++)
            {
                if (s.choices != null && i < s.choices.Length && s.choices[i] >= 0 && s.choices[i] < GuildLawRules.All[i].Options.Length)
                    s_choice[i] = s.choices[i];
                if (s.locked_until != null && i < s.locked_until.Length) _lockedUntil[i] = s.locked_until[i];
            }
        }
    }
}
