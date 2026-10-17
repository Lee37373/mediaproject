using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Ecosystem;
using UnityEngine;

namespace GuildMaster.Quests
{
    /// <summary>
    /// Our town's own quests (P4-82, user decisions 2026-10-17): the player no longer posts hunts. Every den the
    /// guild has seen with residents gets one hunt from the town, which pays it (the guild keeps its fee) —
    /// the farther, the better it pays (<see cref="QuestPricing"/>). No cap and no expiry: the guild master takes
    /// down what the guild should not bother with (<see cref="QuestBoard.Cancel"/>), and the town does not post
    /// that den again (the proposal cards can still bring it back). A den is posted again some hours after its
    /// last hunt ended if it is still seen occupied. Only what the guild has seen counts (<see cref="GuildIntel"/>).
    /// Disasters keep their own town quests (P4-72).
    /// </summary>
    public class TownQuestPoster : IDisposable, ISaveable
    {
        /// <summary>Tests: off keeps the board to what a test posts itself.</summary>
        public static bool Enabled = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Enabled = true;

        /// <summary>Game hours after a den's hunt ended (done, or seen gone) before the town posts that den again.</summary>
        public const float RepostAfterHours = 12f;

        readonly QuestBoard _board;
        readonly GuildIntel _intel;
        readonly EcosystemManager _eco;
        readonly GameClock _clock;
        readonly World.WorldService _world;

        /// <summary>Dens the guild master took the town's (or a patron's) hunt off the board for: not posted again.</summary>
        readonly HashSet<Nest> _removed = new();
        /// <summary>When each den's last hunt ended.</summary>
        readonly Dictionary<Nest, float> _lastEnded = new();

        public int Posted { get; private set; }

        public TownQuestPoster(QuestBoard board, GuildIntel intel, EcosystemManager eco, GameClock clock, World.WorldService world)
        {
            _board = board;
            _intel = intel;
            _eco = eco;
            _clock = clock;
            _world = world;
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

        /// <summary>The guild master took this den's hunt down: the town leaves it alone.</summary>
        public bool WasRemoved(Nest n) => n != null && _removed.Contains(n);

        void OnHour(HourPassedEvent e) => Check();

        void OnCompleted(QuestCompletedEvent e)
        {
            if (e.Quest.Type == QuestType.Hunt && e.Quest.TargetNest != null) _lastEnded[e.Quest.TargetNest] = _clock.TotalHours;
        }

        void OnClosed(QuestClosedEvent e)
        {
            var q = e.Quest;
            if (q.Type != QuestType.Hunt || q.TargetNest == null) return;
            if (e.Status == QuestStatus.Cancelled && e.Reason == QuestBoard.RemovedReason) _removed.Add(q.TargetNest);
            // A done hunt was already timed when it was completed; this covers the ones taken down by themselves.
            else if (e.Status != QuestStatus.TurnedIn) _lastEnded[q.TargetNest] = _clock.TotalHours;
        }

        /// <summary>Posts a hunt on every seen, occupied den that has none (hourly; callable from debug).</summary>
        public int Check()
        {
            if (!Enabled) return 0;
            int posted = 0;
            foreach (var n in _intel.KnownNests.ToList())
                if (Wants(n) && Post(n)) posted++;
            return posted;
        }

        /// <summary>Why the town would not post this den now, or null.</summary>
        public string SkipReason(Nest n)
        {
            if (n == null) return "no den";
            if (_removed.Contains(n)) return "removed by the guild master";
            if (_board.HasActiveHuntOn(n)) return "already on the board";
            var seen = _intel.Get(n);
            if (!n.Discovered || !seen.Observed || seen.LastCount <= 0 || _intel.SpeciesOf(n) == null) return "not seen occupied";
            if (_lastEnded.TryGetValue(n, out var ended) && _clock.TotalHours - ended < RepostAfterHours) return "hunted lately";
            return QuestBoard.HuntPointError(n.WorldPosition);
        }

        bool Wants(Nest n) => SkipReason(n) == null;

        bool Post(Nest n)
        {
            var species = _intel.SpeciesOf(n);
            int count = QuestPricing.HuntCount(_intel.Get(n).LastCount);
            var point = n.WorldPosition;
            var danger = QuestBoard.HuntDanger(species, count, point, QuestBoard.HuntRadius);
            int gold = QuestPricing.HuntReward(danger, count, Vector2.Distance(point, _world.TownCenterWorld));
            var draft = new QuestDraft
            {
                Type = QuestType.Hunt,
                Species = species,
                Count = count,
                Point = point,
                HasPoint = true,
                Radius = QuestBoard.HuntRadius,
                RewardGold = gold,
                RewardExp = QuestPricing.RewardExp(gold),
                MaxParty = QuestPricing.PartyFor(danger),
            };
            if (!_board.TryPostRequest(draft, QuestClient.Town, out _, out _, n)) return false;
            Posted++;
            return true;
        }

        // ---------------- Save (P5-4) ----------------

        [Serializable]
        class SaveData
        {
            public List<int> removed = new();
            public List<int> ended_nests = new();
            public List<float> ended_hours = new();
            public int posted;
        }

        public string SaveKey => "town_quests";

        public string Capture()
        {
            var nests = _eco.Nests;
            var s = new SaveData { posted = Posted };
            for (int i = 0; i < nests.Count; i++)
            {
                if (_removed.Contains(nests[i])) s.removed.Add(i);
                if (_lastEnded.TryGetValue(nests[i], out var h)) { s.ended_nests.Add(i); s.ended_hours.Add(h); }
            }
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            var nests = _eco.Nests;
            _removed.Clear();
            _lastEnded.Clear();
            Posted = s.posted;
            foreach (int i in s.removed) if (i >= 0 && i < nests.Count) _removed.Add(nests[i]);
            for (int k = 0; k < s.ended_nests.Count && k < s.ended_hours.Count; k++)
            {
                int i = s.ended_nests[k];
                if (i >= 0 && i < nests.Count) _lastEnded[nests[i]] = s.ended_hours[k];
            }
        }
    }
}
