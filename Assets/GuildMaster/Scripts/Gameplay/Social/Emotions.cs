using System;
using System.Collections.Generic;
using GuildMaster.Welfare;

namespace GuildMaster.Social
{
    /// <summary>What shows over someone's head (F-10). None = nothing to show.</summary>
    public enum Emotion { None, Angry, Sad, Strained, Excited, Happy, Sleepy }

    /// <summary>A passing feeling: what, until when (game hours) and why.</summary>
    public struct Feeling
    {
        public Emotion Kind;
        public float Since;
        public float Until;
        public string Reason;
    }

    /// <summary>
    /// How people feel right now (F-10, user request 2026-10-11): angry right after a fight, sad after a friend died,
    /// excited after a success, strained when stress is high, sleepy when worn out. Passing feelings come from what
    /// happened and fade after a fixed number of game hours; the lasting ones (strained, sleepy) are read from stress
    /// and fatigue. Pure rules, covered by EditMode tests.
    /// </summary>
    public static class EmotionRules
    {
        // ---- How long passing feelings last (game hours) ----
        public const float FightAngerHours = 8f;
        public const float WalkoutAngerHours = 6f;
        public const float GriefHours = 24f;
        /// <summary>A close friend (relation at or above <see cref="CloseFriend"/>) is mourned longer.</summary>
        public const float DeepGriefHours = 48f;
        public const int CloseFriend = 40;
        public const float FriendLeftSadHours = 12f;
        public const float QuestFailedSadHours = 6f;
        public const float SuccessHours = 5f;
        public const float LevelUpHours = 4f;
        public const float JoinedHours = 6f;
        public const float WarmHours = 3f;
        public const float ComfortedHours = 6f;
        public const float ReconciledHours = 6f;

        /// <summary>Fatigue from which someone looks sleepy.</summary>
        public const float SleepyFatigue = 80f;

        /// <summary>A relationship line counts as "just changed" (blinks in the graph) for this many game hours.</summary>
        public const float RecentChangeHours = 6f;

        /// <summary>Which feeling wins when several are true at once (higher first).</summary>
        public static int Priority(Emotion e) => e switch
        {
            Emotion.Sad => 6,
            Emotion.Angry => 5,
            Emotion.Strained => 4,
            Emotion.Excited => 3,
            Emotion.Happy => 2,
            Emotion.Sleepy => 1,
            _ => 0,
        };

        public static string Label(Emotion e) => e switch
        {
            Emotion.Angry => "화남",
            Emotion.Sad => "슬픔",
            Emotion.Strained => "지침",
            Emotion.Excited => "신남",
            Emotion.Happy => "기쁨",
            Emotion.Sleepy => "졸림",
            _ => "평온",
        };

        /// <summary>How long someone grieves a dead member they felt <paramref name="relation"/> about (0 = not at all).</summary>
        public static float GriefFor(int relation)
            => relation >= CloseFriend ? DeepGriefHours : relation >= StressRules.FriendRelation ? GriefHours : 0f;

        /// <summary>
        /// What a town talk leaves each side feeling: hours = 0 means nothing worth showing.
        /// <paramref name="side"/> 0 = the one who started the talk, 1 = the one spoken to.
        /// </summary>
        public static (Emotion kind, float hours) AfterTalk(TownTalkKind kind, TalkOutcome outcome, int side)
        {
            switch (outcome)
            {
                case TalkOutcome.Fight: return (Emotion.Angry, FightAngerHours);
                case TalkOutcome.Reconciled: return (Emotion.Happy, ReconciledHours);
                case TalkOutcome.Joined: return (side == 1 ? Emotion.Excited : Emotion.Happy, JoinedHours);
                case TalkOutcome.Warm:
                    if (kind == TownTalkKind.Comfort) return (Emotion.Happy, side == 1 ? ComfortedHours : WarmHours);
                    return (Emotion.Happy, WarmHours);
                default: return (Emotion.None, 0f);
            }
        }

        /// <summary>
        /// The feeling to show: the strongest passing feeling still running, or what stress and tiredness say.
        /// Ties go to the most recent.
        /// </summary>
        public static Emotion Pick(IReadOnlyList<Feeling> active, float now, float stress, float fatigue, bool asleep, out string reason)
        {
            var best = Emotion.None;
            float bestSince = float.MinValue;
            reason = null;
            if (active != null)
            {
                for (int i = 0; i < active.Count; i++)
                {
                    var f = active[i];
                    if (f.Kind == Emotion.None || now >= f.Until) continue;
                    if (Priority(f.Kind) > Priority(best) || (Priority(f.Kind) == Priority(best) && f.Since > bestSince))
                    {
                        best = f.Kind;
                        bestSince = f.Since;
                        reason = f.Reason;
                    }
                }
            }
            if (stress >= StressRules.StrainedAt && Priority(Emotion.Strained) > Priority(best))
            {
                best = Emotion.Strained;
                reason = $"스트레스 {Math.Round(stress)}";
            }
            if ((asleep || fatigue >= SleepyFatigue) && best == Emotion.None)
            {
                best = Emotion.Sleepy;
                reason = asleep ? "잘 시간" : $"피로 {Math.Round(fatigue)}";
            }
            return best;
        }
    }

    /// <summary>
    /// Who feels what, and which relationships moved lately (F-10). Kept by <see cref="EmotionService"/>; pure data so
    /// it is covered by tests.
    /// </summary>
    public class EmotionBook
    {
        readonly Dictionary<string, List<Feeling>> _feelings = new();
        readonly Dictionary<string, (float hour, int delta)> _changes = new();

        /// <summary>Adds a passing feeling. The same feeling again is replaced (the newer reason, the later end).</summary>
        public void Feel(string id, Emotion kind, float now, float hours, string reason)
        {
            if (string.IsNullOrEmpty(id) || kind == Emotion.None || hours <= 0f) return;
            if (!_feelings.TryGetValue(id, out var list)) _feelings[id] = list = new List<Feeling>();
            list.RemoveAll(f => f.Kind == kind || now >= f.Until);
            list.Add(new Feeling { Kind = kind, Since = now, Until = now + hours, Reason = reason });
        }

        /// <summary>Ends a feeling early (making up ends the anger).</summary>
        public void Calm(string id, Emotion kind)
        {
            if (id != null && _feelings.TryGetValue(id, out var list)) list.RemoveAll(f => f.Kind == kind);
        }

        public IReadOnlyList<Feeling> Of(string id)
            => id != null && _feelings.TryGetValue(id, out var list) ? list : (IReadOnlyList<Feeling>)Array.Empty<Feeling>();

        public Emotion Current(string id, float now, float stress, float fatigue, bool asleep, out string reason)
            => EmotionRules.Pick(Of(id), now, stress, fatigue, asleep, out reason);

        public void Forget(string id)
        {
            if (id == null) return;
            _feelings.Remove(id);
            var gone = new List<string>();
            foreach (var key in _changes.Keys)
                if (key.StartsWith(id + "|", StringComparison.Ordinal) || key.EndsWith("|" + id, StringComparison.Ordinal)) gone.Add(key);
            foreach (var key in gone) _changes.Remove(key);
        }

        // ---------------- Relationship changes (for the graph) ----------------

        public static string PairKey(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;

        /// <summary>The relationship between two people just moved by <paramref name="delta"/>.</summary>
        public void MarkChanged(string a, string b, float now, int delta)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a == b || delta == 0) return;
            _changes[PairKey(a, b)] = (now, delta);
        }

        // ---------------- Save (P4-119) ----------------

        /// <summary>Everyone's passing feelings, by id (for saving).</summary>
        public IEnumerable<KeyValuePair<string, List<Feeling>>> AllFeelings => _feelings;

        /// <summary>The last move of every pair, by <see cref="PairKey"/> (for saving).</summary>
        public IEnumerable<KeyValuePair<string, (float hour, int delta)>> AllChanges => _changes;

        public void Clear()
        {
            _feelings.Clear();
            _changes.Clear();
        }

        /// <summary>Puts a saved feeling back as it was (its start, end and reason).</summary>
        public void RestoreFeeling(string id, Feeling f)
        {
            if (string.IsNullOrEmpty(id) || f.Kind == Emotion.None) return;
            if (!_feelings.TryGetValue(id, out var list)) _feelings[id] = list = new List<Feeling>();
            list.Add(f);
        }

        public void RestoreChange(string pairKey, float hour, int delta)
        {
            if (!string.IsNullOrEmpty(pairKey) && delta != 0) _changes[pairKey] = (hour, delta);
        }

        /// <summary>True when the pair's relationship moved within <paramref name="window"/> game hours; sign of the last move.</summary>
        public bool ChangedRecently(string a, string b, float now, float window, out int sign)
        {
            sign = 0;
            if (a == null || b == null || !_changes.TryGetValue(PairKey(a, b), out var c) || now - c.hour > window) return false;
            sign = Math.Sign(c.delta);
            return true;
        }
    }
}
