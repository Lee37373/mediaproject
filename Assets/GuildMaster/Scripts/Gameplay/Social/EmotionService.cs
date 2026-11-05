using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Quests;
using GuildMaster.Units;
using GuildMaster.Welfare;

namespace GuildMaster.Social
{
    /// <summary>
    /// Feeds the <see cref="EmotionBook"/> from what happens in the game (F-10, user request 2026-10-11): town talks
    /// (a fight, making up, being comforted), a friend's death or departure, quest results, level-ups, joining, and
    /// counselling. The UI reads <see cref="Of"/> for the icons over heads and <see cref="Book"/> for the relationship
    /// graph (which lines moved lately). Only listens; it changes nothing in the game. Saved as "emotions" (P4-119).
    /// </summary>
    public class EmotionService : IDisposable, ISaveable
    {
        readonly GameClock _clock;
        readonly GuildRoster _roster;
        ResidentService _residents;

        public EmotionBook Book { get; } = new();

        float Now => _clock != null ? _clock.TotalHours : 0f;

        public EmotionService(GameClock clock, GuildRoster roster, ResidentService residents = null)
        {
            _clock = clock;
            _roster = roster;
            _residents = residents;
            EventBus.Subscribe<TownTalkEvent>(OnTalk);
            EventBus.Subscribe<RelationChangedEvent>(OnRelation);
            EventBus.Subscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Subscribe<AdventurerLeftEvent>(OnLeft);
            EventBus.Subscribe<AdventurerJoinedEvent>(OnJoined);
            EventBus.Subscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Subscribe<QuestClosedEvent>(OnQuestClosed);
            EventBus.Subscribe<CounselEndedEvent>(OnCounsel);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<TownTalkEvent>(OnTalk);
            EventBus.Unsubscribe<RelationChangedEvent>(OnRelation);
            EventBus.Unsubscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Unsubscribe<AdventurerLeftEvent>(OnLeft);
            EventBus.Unsubscribe<AdventurerJoinedEvent>(OnJoined);
            EventBus.Unsubscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Unsubscribe<QuestClosedEvent>(OnQuestClosed);
            EventBus.Unsubscribe<CounselEndedEvent>(OnCounsel);
        }

        public void SetResidents(ResidentService residents) => _residents = residents;

        /// <summary>What this unit feels right now (members, visitors and residents; everyone else: None).</summary>
        public Emotion Of(Unit u, out string reason)
        {
            reason = null;
            switch (u)
            {
                case Adventurer a when a.IsAlive && a.Data != null:
                {
                    bool resting = a.Fsm != null && a.Fsm.Current == a.RestState;
                    return Book.Current(a.Data.id, Now, a.IsVisitor ? 0f : a.Data.stress, a.Data.fatigue, resting, out reason);
                }
                case Resident r when r.IsAlive && r.Profile != null:
                    return Book.Current(r.Profile.Id, Now, 0f, 0f, r.Asleep, out reason);
                default:
                    return Emotion.None;
            }
        }

        /// <summary>A feeling from outside the usual events (event cards).</summary>
        public void Feel(string id, Emotion kind, float hours, string reason) => Book.Feel(id, kind, Now, hours, reason);

        // ---------------- Events ----------------

        void OnTalk(TownTalkEvent e)
        {
            if (string.IsNullOrEmpty(e.AId) || string.IsNullOrEmpty(e.BId)) return;
            float now = Now;
            if (e.Outcome == TalkOutcome.Reconciled)
            {
                Book.Calm(e.AId, Emotion.Angry);
                Book.Calm(e.BId, Emotion.Angry);
            }
            var (ka, ha) = EmotionRules.AfterTalk(e.Kind, e.Outcome, 0);
            var (kb, hb) = EmotionRules.AfterTalk(e.Kind, e.Outcome, 1);
            Book.Feel(e.AId, ka, now, ha, TalkReason(e, true));
            Book.Feel(e.BId, kb, now, hb, TalkReason(e, false));
            var fx = TownTalkRules.Effects(e.Kind, e.Outcome);
            Book.MarkChanged(e.AId, e.BId, now, fx.FeelAB + fx.FeelBA);
        }

        static string TalkReason(TownTalkEvent e, bool isA)
        {
            string other = isA ? e.B : e.A;
            return e.Outcome switch
            {
                TalkOutcome.Fight => $"{other}{Wa(other)} 다툼",
                TalkOutcome.Reconciled => $"{other}{Wa(other)} 화해",
                TalkOutcome.Joined => isA ? $"{Reul(other)} 길드로 데려옴" : $"{other}의 권유로 길드 합류",
                TalkOutcome.Warm when e.Kind == TownTalkKind.Comfort => isA ? $"{Reul(other)} 위로함" : $"{other}의 위로",
                _ => $"{other}{Wa(other)} 즐거운 대화",
            };
        }

        void OnRelation(RelationChangedEvent e)
        {
            if (e.From == null || e.To == null || e.From.Data == null || e.To.Data == null) return;
            Book.MarkChanged(e.From.Data.id, e.To.Data.id, Now, e.New - e.Old);
        }

        void OnDied(AdventurerDiedEvent e)
        {
            var dead = e.Adventurer;
            if (dead == null || dead.Data == null) return;
            string name = dead.Data.name;
            if (_roster != null)
                foreach (var m in _roster.Members.ToList())
                {
                    if (m == null || m == dead || !m.IsAlive) continue;
                    float hours = EmotionRules.GriefFor(Relationships.Get(m, dead));
                    Book.Feel(m.Data.id, Emotion.Sad, Now, hours, $"{Reul(name)} 잃음");
                }
            if (_residents != null)
                foreach (var r in _residents.Residents)
                    if (r != null && r.Profile.FeelingToward(dead.Data.id) >= ResidentProfile.GoodOpinion)
                        Book.Feel(r.Profile.Id, Emotion.Sad, Now, EmotionRules.GriefHours, $"{Ga(name)} 죽었다는 소식");
            Book.Forget(dead.Data.id);
        }

        void OnLeft(AdventurerLeftEvent e)
        {
            var gone = e.Adventurer;
            if (gone == null || gone.Data == null) return;
            if (_roster != null)
                foreach (var m in _roster.Members.ToList())
                    if (m != null && m != gone && m.IsAlive && Relationships.Get(m, gone) >= StressRules.FriendRelation)
                        Book.Feel(m.Data.id, Emotion.Sad, Now, EmotionRules.FriendLeftSadHours, $"{Ga(gone.Data.name)} 길드를 떠남");
            Book.Forget(gone.Data.id);
        }

        void OnJoined(AdventurerJoinedEvent e)
        {
            // The founders join at the very start; that is not news to anyone.
            if (e.Adventurer == null || e.Adventurer.Data == null || Now < 1f) return;
            Book.Feel(e.Adventurer.Data.id, Emotion.Excited, Now, EmotionRules.JoinedHours, "길드에 새로 들어옴");
        }

        void OnLevelUp(AdventurerLevelUpEvent e)
        {
            if (e.Adventurer == null || e.Adventurer.Data == null) return;
            Book.Feel(e.Adventurer.Data.id, Emotion.Excited, Now, EmotionRules.LevelUpHours, $"레벨 {e.Level} 달성");
        }

        void OnQuestClosed(QuestClosedEvent e)
        {
            if (e.Quest == null) return;
            bool success = e.Status == QuestStatus.Completed || e.Status == QuestStatus.TurnedIn;
            bool failed = e.Status == QuestStatus.Failed;
            if (!success && !failed) return;
            foreach (var t in e.Quest.Assignees)
            {
                if (t is not Adventurer a || !a.IsAlive || a.Data == null) continue;
                if (success) Book.Feel(a.Data.id, Emotion.Excited, Now, EmotionRules.SuccessHours, $"'{e.Quest.Title}' 의뢰 성공");
                else Book.Feel(a.Data.id, Emotion.Sad, Now, EmotionRules.QuestFailedSadHours, $"'{e.Quest.Title}' 의뢰 실패");
            }
        }

        void OnCounsel(CounselEndedEvent e)
        {
            if (e.Member == null || e.Member.Data == null) return;
            if (e.Outcome == CounselOutcome.Relieved)
            {
                Book.Calm(e.Member.Data.id, Emotion.Angry);
                Book.Feel(e.Member.Data.id, Emotion.Happy, Now, EmotionRules.ComfortedHours, "면담으로 마음이 풀림");
            }
            else if (e.Outcome == CounselOutcome.Walkout)
                Book.Feel(e.Member.Data.id, Emotion.Angry, Now, EmotionRules.WalkoutAngerHours, "면담에서 마음이 상함");
        }

        // ---------------- Save (P4-119) ----------------

        [Serializable]
        class FeelingData
        {
            public string id;
            public int kind;
            public float since;
            public float until;
            public string reason;
        }

        [Serializable]
        class ChangeData
        {
            public string pair;
            public float hour;
            public int delta;
        }

        [Serializable]
        class SaveData
        {
            public List<FeelingData> feelings = new();
            public List<ChangeData> changes = new();
        }

        public string SaveKey => "emotions";

        /// <summary>Feelings that are over are left out (they show nothing and would only be cleared later).</summary>
        public string Capture()
        {
            float now = Now;
            var s = new SaveData();
            foreach (var kv in Book.AllFeelings.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                foreach (var f in kv.Value)
                    if (now < f.Until)
                        s.feelings.Add(new FeelingData { id = kv.Key, kind = (int)f.Kind, since = f.Since, until = f.Until, reason = f.Reason });
            foreach (var kv in Book.AllChanges.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                s.changes.Add(new ChangeData { pair = kv.Key, hour = kv.Value.hour, delta = kv.Value.delta });
            return UnityEngine.JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = UnityEngine.JsonUtility.FromJson<SaveData>(json);
            Book.Clear();
            if (s.feelings != null)
                foreach (var f in s.feelings)
                    if (Enum.IsDefined(typeof(Emotion), f.kind))
                        Book.RestoreFeeling(f.id, new Feeling { Kind = (Emotion)f.kind, Since = f.since, Until = f.until, Reason = f.reason });
            if (s.changes != null) foreach (var c in s.changes) Book.RestoreChange(c.pair, c.hour, c.delta);
        }

        static string Ga(string w) => w + MemoryRecorder.Josa(w, "이", "가");
        static string Reul(string w) => w + MemoryRecorder.Josa(w, "을", "를");
        static string Wa(string w) => MemoryRecorder.Josa(w, "과", "와");
    }
}
