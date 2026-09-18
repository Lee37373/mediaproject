using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Economy;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Quests
{
    public struct QuestPostedEvent { public Quest Quest; }
    public struct QuestAcceptedEvent { public Quest Quest; public IQuestTaker Taker; }
    public struct QuestAbandonedEvent { public Quest Quest; public IQuestTaker Taker; }
    public struct QuestProgressEvent { public Quest Quest; }
    public struct QuestCompletedEvent { public Quest Quest; }
    public struct QuestRewardPaidEvent { public Quest Quest; public IQuestTaker Taker; public int Gold; public int Exp; }
    public struct QuestClosedEvent { public Quest Quest; public QuestStatus Status; }

    /// <summary>
    /// The quest board: posts quests with escrowed rewards, lets adventurers accept/abandon them,
    /// tracks progress from game events, and pays out contribution-weighted rewards on turn-in.
    /// </summary>
    public class QuestBoard : MonoBehaviour
    {
        const float TurnInDeadlineHours = 36f;

        readonly List<Quest> _quests = new();
        readonly List<Quest> _history = new();
        GuildTreasury _treasury;
        EcosystemManager _eco;
        GameClock _clock;
        int _nextId = 101;
        float _exploreTimer;

        public IReadOnlyList<Quest> Quests => _quests;
        public IReadOnlyList<Quest> History => _history;

        public void Init(GuildTreasury treasury, EcosystemManager eco, GameClock clock)
        {
            _treasury = treasury;
            _eco = eco;
            _clock = clock;
            EventBus.Subscribe<UnitDiedEvent>(OnUnitDied);
            EventBus.Subscribe<NestClearedEvent>(OnNestCleared);
            EventBus.Subscribe<SpeciesExtinctEvent>(OnSpeciesExtinct);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<UnitDiedEvent>(OnUnitDied);
            EventBus.Unsubscribe<NestClearedEvent>(OnNestCleared);
            EventBus.Unsubscribe<SpeciesExtinctEvent>(OnSpeciesExtinct);
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
        }

        // ---------------- Posting ----------------

        public string Validate(QuestDraft d)
        {
            if (d.RewardGold < 0 || d.RewardExp < 0) return "보상은 0 이상이어야 합니다.";
            if (d.RewardGold == 0 && d.RewardExp == 0) return "골드 또는 EXP 보상을 설정하세요.";
            if (!_treasury.CanAfford(d.RewardGold, d.RewardExp)) return "길드 자금(골드/EXP)이 부족합니다.";
            switch (d.Type)
            {
                case QuestType.Hunt:
                    if (d.Species == null) return "대상 몬스터를 선택하세요.";
                    if (Services.TryGet<GuildIntel>(out var known) && !known.IsKnown(d.Species)) return "아직 확인하지 못한 몬스터입니다.";
                    if (d.Count < 1) return "토벌 수는 1 이상이어야 합니다.";
                    break;
                case QuestType.ClearNest:
                    if (d.Nest == null) return "소탕할 둥지를 선택하세요.";
                    if (!d.Nest.Discovered) return "발견하지 못한 둥지입니다.";
                    if (Services.TryGet<GuildIntel>(out var intel) && intel.Get(d.Nest) is { Observed: true, LastCount: 0 })
                        return "마지막 관측에서 비어 있던 둥지입니다.";
                    if (_quests.Any(q => q.IsActive && q.Type == QuestType.ClearNest && q.Nest == d.Nest)) return "이미 등록된 둥지 의뢰가 있습니다.";
                    break;
                case QuestType.Explore:
                    if (Services.TryGet<FogOfWar>(out var fog) && fog.ExploredFraction(d.Point, d.Radius <= 0 ? 8f : d.Radius) > 0.85f)
                        return "이미 탐사된 지역입니다.";
                    break;
            }
            return null;
        }

        public bool TryPost(QuestDraft draft, out Quest quest, out string error)
        {
            quest = null;
            error = Validate(draft);
            if (error != null) return false;
            if (!_treasury.TrySpend(draft.RewardGold, draft.RewardExp)) { error = "길드 자금이 부족합니다."; return false; }
            quest = new Quest($"Q_{_nextId++}", draft, _clock.TotalHours);
            quest.Danger = EvaluateDanger(quest);
            _quests.Add(quest);
            EventBus.Publish(new QuestPostedEvent { Quest = quest });
            return true;
        }

        public DangerLevel EvaluateDanger(Quest q)
        {
            DangerLevel d = DangerLevel.Low;
            switch (q.Type)
            {
                case QuestType.Hunt:
                    d = q.Species.danger;
                    if (q.RequiredCount >= 8 && d < DangerLevel.High) d++;
                    break;
                case QuestType.ClearNest:
                    d = NestDanger(q.Nest);
                    break;
                case QuestType.Explore:
                    d = ExploreDanger(q.Point, q.Radius);
                    break;
                case QuestType.Defend:
                    d = DangerLevel.Medium;
                    break;
            }
            return d;
        }

        public bool Cancel(Quest q)
        {
            if (!q.IsActive) return false;
            Close(q, QuestStatus.Cancelled, refund: true);
            return true;
        }

        // ---------------- Adventurer API ----------------

        public IEnumerable<Quest> AvailableFor(IQuestTaker t)
            => _quests.Where(q => q.HasFreeSlot && !q.IsAssigned(t));

        public Quest FindById(string id) => _quests.FirstOrDefault(q => q.Id == id);

        public Quest ActiveQuestOf(IQuestTaker t) => _quests.FirstOrDefault(q => q.IsAssigned(t) && q.Status != QuestStatus.TurnedIn);

        public bool TryAccept(Quest q, IQuestTaker t)
        {
            if (q == null || !q.HasFreeSlot || q.IsAssigned(t) || !t.IsAvailableForQuest) return false;
            q.AddAssignee(t);
            if (q.Status == QuestStatus.Open) q.Status = QuestStatus.InProgress;
            EventBus.Publish(new QuestAcceptedEvent { Quest = q, Taker = t });
            return true;
        }

        public void Abandon(Quest q, IQuestTaker t)
        {
            if (q == null || !q.RemoveAssignee(t)) return;
            if (q.IsActive && q.Assignees.Count == 0) q.Status = QuestStatus.Open;
            EventBus.Publish(new QuestAbandonedEvent { Quest = q, Taker = t });
        }

        /// <summary>Called when an assignee reports at the guild. Returns true if a reward was paid.</summary>
        public bool TryTurnIn(Quest q, IQuestTaker t, out int gold, out int exp)
        {
            gold = exp = 0;
            if (q == null || q.Status != QuestStatus.Completed) return false;
            if (!q.TryTakePayout(t, out gold, out exp)) return false;
            q.RemoveAssignee(t);
            EventBus.Publish(new QuestRewardPaidEvent { Quest = q, Taker = t, Gold = gold, Exp = exp });
            if (q.PendingPayouts == 0) Close(q, QuestStatus.TurnedIn, refund: false);
            return true;
        }

        // ---------------- Progress tracking ----------------

        void OnUnitDied(UnitDiedEvent e)
        {
            if (e.Victim is not Monster m || e.Killer is not IQuestTaker taker) return;
            foreach (var q in _quests)
            {
                if (q.Status != QuestStatus.InProgress || !q.IsAssigned(taker)) continue;
                if (q.Type == QuestType.Hunt && q.Species == m.Def)
                {
                    q.Progress++;
                    q.AddContribution(taker, 1);
                    EventBus.Publish(new QuestProgressEvent { Quest = q });
                    if (q.Progress >= q.RequiredCount) Complete(q);
                }
                else if (q.Type == QuestType.ClearNest && q.Nest == m.Home)
                {
                    q.AddContribution(taker, 1);
                    EventBus.Publish(new QuestProgressEvent { Quest = q });
                }
            }
        }

        void OnNestCleared(NestClearedEvent e)
        {
            foreach (var q in _quests.ToList())
                if (q.Status == QuestStatus.InProgress && q.Type == QuestType.ClearNest && q.Nest == e.Nest)
                    Complete(q);
        }

        void OnSpeciesExtinct(SpeciesExtinctEvent e)
        {
            foreach (var q in _quests.ToList())
                if (q.IsActive && q.Type == QuestType.Hunt && q.Species == e.Species)
                {
                    // Partial success counts: pay out if anything was hunted, otherwise fail and refund.
                    if (q.Progress > 0 && q.Assignees.Count > 0) Complete(q);
                    else Close(q, QuestStatus.Failed, refund: true);
                }
        }

        void Update()
        {
            _exploreTimer -= Time.deltaTime;
            if (_exploreTimer > 0f) return;
            _exploreTimer = 1f;
            if (!Services.TryGet<FogOfWar>(out var fog)) return;
            foreach (var q in _quests.ToList())
            {
                if (q.Type != QuestType.Explore || q.Status != QuestStatus.InProgress) continue;
                int pct = Mathf.RoundToInt(fog.ExploredFraction(q.Point, q.Radius) * 100f);
                if (pct != q.Progress) { q.Progress = pct; EventBus.Publish(new QuestProgressEvent { Quest = q }); }
                if (pct >= 85) Complete(q);
            }
        }

        void OnHour(HourPassedEvent e)
        {
            Services.TryGet<GuildIntel>(out var intel);
            foreach (var q in _quests.ToList())
            {
                // Rewards not collected in time (e.g. the adventurer died) return to the guild.
                if (q.Status == QuestStatus.Completed && _clock.TotalHours - q.CompletedAtHour > TurnInDeadlineHours && q.Assignees.Count == 0)
                    Close(q, QuestStatus.TurnedIn, refund: true);
                // A nest that emptied out of sight: once someone actually sees it empty, refund.
                else if (q.IsActive && q.Type == QuestType.ClearNest && q.Nest.IsEmpty && intel != null && intel.Get(q.Nest).VisibleNow)
                    Close(q, QuestStatus.Failed, refund: true);
            }
        }

        // ---------------- Danger from observations only ----------------

        /// <summary>Clear-nest danger: species danger, +1 when at least 5 were last observed there.</summary>
        public static DangerLevel NestDanger(Nest nest)
        {
            var d = nest.Species.danger;
            int seen = Services.TryGet<GuildIntel>(out var intel) ? intel.Get(nest).LastCount : 0;
            return seen >= 5 && d < DangerLevel.High ? d + 1 : d;
        }

        /// <summary>Explore danger: the most dangerous discovered, not-seen-empty nest near the area.</summary>
        public static DangerLevel ExploreDanger(Vector2 point, float radius)
        {
            var level = DangerLevel.Low;
            if (!Services.TryGet<GuildIntel>(out var intel)) return level;
            foreach (var n in intel.KnownNests)
            {
                var ni = intel.Get(n);
                if (ni.Observed && ni.LastCount <= 0) continue;
                if (Vector2.Distance(n.WorldPosition, point) < radius + 6f && n.Species.danger > level) level = n.Species.danger;
            }
            return level;
        }

        /// <summary>Removes a dead/departed taker from every quest.</summary>
        public void RemoveTakerEverywhere(IQuestTaker t)
        {
            foreach (var q in _quests.ToList())
            {
                if (!q.IsAssigned(t)) continue;
                if (q.Status == QuestStatus.Completed)
                {
                    q.RemoveAssignee(t);
                    if (q.TryTakePayout(t, out var g, out var x)) { _treasury.AddGold(g); _treasury.AddExp(x); }
                    if (q.PendingPayouts == 0) Close(q, QuestStatus.TurnedIn, refund: false);
                }
                else Abandon(q, t);
            }
        }

        void Complete(Quest q)
        {
            if (q.Status == QuestStatus.Completed) return;
            q.Status = QuestStatus.Completed;
            q.CompletedAtHour = _clock.TotalHours;
            q.ComputePayouts();
            EventBus.Publish(new QuestCompletedEvent { Quest = q });
        }

        void Close(Quest q, QuestStatus status, bool refund)
        {
            if (refund)
            {
                if (q.Status == QuestStatus.Completed)
                {
                    var (g, x) = q.DrainRemainingPayouts();
                    _treasury.AddGold(g);
                    _treasury.AddExp(x);
                }
                else
                {
                    _treasury.AddGold(q.RewardGold);
                    _treasury.AddExp(q.RewardExp);
                }
            }
            q.Status = status;
            _quests.Remove(q);
            _history.Add(q);
            EventBus.Publish(new QuestClosedEvent { Quest = q, Status = status });
        }
    }
}
