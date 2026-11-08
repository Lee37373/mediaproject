using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Villages;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Quests
{
    /// <summary>
    /// The other villages' errands (P4-133 F-5, user decision 2026-11-08): each village the guild knows and has not seen in ruins
    /// asks one errand at a time — carry goods to it, dig ore or pick herbs for it from the spots the guild has found, or bring in
    /// the parts of a den by it that the guild has seen occupied (<see cref="ErrandOffers"/>). It pays the gold (the guild keeps
    /// its fee) and an item besides (<see cref="QuestBoard.PaysInKind"/>). Some hours after its last errand ended, each hour by
    /// chance (<see cref="ErrandRules.VillageGapHours"/>, <see cref="ErrandRules.VillageChancePerHour"/>). Its own dice, so the
    /// shared ones are not moved.
    /// </summary>
    public class ErrandPoster : IDisposable, ISaveable
    {
        /// <summary>Tests: off keeps the board to what a test posts itself.</summary>
        public static bool Enabled = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Enabled = true;

        readonly QuestBoard _board;
        readonly VillageService _villages;
        readonly GameClock _clock;
        GameRandom _rng;
        /// <summary>When each village's last errand ended (by village id).</summary>
        readonly Dictionary<int, float> _lastEnded = new();

        public int Posted { get; private set; }

        public ErrandPoster(QuestBoard board, VillageService villages, GameClock clock, int seed)
        {
            _board = board;
            _villages = villages;
            _clock = clock;
            _rng = new GameRandom(seed ^ 0x3E44A);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<QuestCompletedEvent>(OnCompleted);
            EventBus.Subscribe<QuestClosedEvent>(OnClosed);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            EventBus.Unsubscribe<QuestCompletedEvent>(OnCompleted);
            EventBus.Unsubscribe<QuestClosedEvent>(OnClosed);
        }

        void OnHour(HourPassedEvent e) => Check();

        void OnCompleted(QuestCompletedEvent e) => Ended(e.Quest);

        void OnClosed(QuestClosedEvent e)
        {
            // A done errand was timed when it was done; this covers the ones that came down or failed.
            if (e.Status != QuestStatus.TurnedIn) Ended(e.Quest);
        }

        void Ended(Quest q)
        {
            if (q.Client == QuestClient.Village && q.Village != null && ErrandRules.IsErrand(q.Type)) _lastEnded[q.Village.Id] = _clock.TotalHours;
        }

        /// <summary>Each village that may, rolls for an errand (hourly; callable from debug).</summary>
        public int Check()
        {
            if (!Enabled || _villages == null) return 0;
            int posted = 0;
            foreach (var v in _villages.Villages.ToList())
            {
                if (SkipReason(v) != null || !_rng.Chance(ErrandRules.VillageChancePerHour)) continue;
                if (PostFor(v, null, out _) != null) posted++;
            }
            return posted;
        }

        /// <summary>Why this village would not ask an errand now, or null.</summary>
        public string SkipReason(Village v)
        {
            if (v == null) return "no village";
            if (!v.Known) return "not known to the guild";
            if (v.SeenRuined || v.IsDestroyed) return "in ruins";
            if (HasErrand(v)) return "has an errand on the board";
            if (_lastEnded.TryGetValue(v.Id, out var ended) && _clock.TotalHours - ended < ErrandRules.VillageGapHours) return "asked lately";
            return null;
        }

        bool HasErrand(Village v) => _board.Quests.Any(q => q.IsActive && q.Client == QuestClient.Village && q.Village == v && ErrandRules.IsErrand(q.Type));

        /// <summary>
        /// The village asks one errand of the kinds there is ground for (<paramref name="kind"/>: that one only), picked by
        /// <see cref="ErrandRules.DeliverWeight"/> and the rest. Null (and why) when none can be asked.
        /// </summary>
        public Quest PostFor(Village v, QuestType? kind, out string error)
        {
            error = null;
            if (v == null || v.IsDestroyed) { error = "의뢰처 마을이 없습니다."; return null; }
            var options = new List<(QuestDraft draft, float weight, Ecosystem.Nest den)>();
            int wanted = ErrandRules.VillageCount(_rng);

            if (kind is null or QuestType.Deliver)
                options.Add((ErrandOffers.DeliverDraft(v, ErrandRules.PickGoods(_rng), 1f), ErrandRules.DeliverWeight, null));
            if (kind is null or QuestType.Gather)
            {
                // One of the two kinds of spot the guild knows (both: either).
                var spots = new List<ErrandOffers.Gather>();
                foreach (var k in new[] { PlaceKind.OreVein, PlaceKind.HerbPatch })
                    if (ErrandOffers.TryGather(k, out var g)) spots.Add(g);
                if (spots.Count > 0 && ErrandOffers.GatherDraft(_rng.Pick(spots), wanted, 1f, out var gd))
                    options.Add((gd, ErrandRules.GatherWeight, null));
            }
            if (kind is null or QuestType.Collect)
            {
                var dens = ErrandOffers.Collects(v.WorldPosition, ErrandRules.CollectReach);
                if (dens.Count > 0)
                {
                    var c = dens.OrderBy(d => Vector2.Distance(d.Den.WorldPosition, v.WorldPosition)).First();
                    if (ErrandOffers.CollectDraft(c, wanted, 1f, out var cd)) options.Add((cd, ErrandRules.CollectWeight, c.Den));
                }
            }
            if (options.Count == 0) { error = "지금 맡길 만한 일이 없습니다."; return null; }

            float total = options.Sum(o => o.weight), roll = _rng.Range(0f, total);
            var pick = options[options.Count - 1];
            foreach (var o in options)
            {
                if (roll < o.weight) { pick = o; break; }
                roll -= o.weight;
            }
            if (!_board.TryPostRequest(pick.draft, QuestClient.Village, out var quest, out error, pick.den, v)) return null;
            Posted++;
            return quest;
        }

        // ---------------- Save ----------------

        [Serializable]
        class SaveData
        {
            public List<int> villages = new();
            public List<float> ended = new();
            public int posted;
        }

        public string SaveKey => "errands";

        public string Capture()
        {
            var s = new SaveData { posted = Posted };
            foreach (var kv in _lastEnded.OrderBy(k => k.Key)) { s.villages.Add(kv.Key); s.ended.Add(kv.Value); }
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            _lastEnded.Clear();
            Posted = s.posted;
            for (int i = 0; i < s.villages.Count && i < s.ended.Count; i++) _lastEnded[s.villages[i]] = s.ended[i];
        }
    }
}
