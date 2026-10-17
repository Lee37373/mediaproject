using System;
using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Persuasion;
using GuildMaster.Quests;
using UnityEngine;

namespace GuildMaster.Notifications
{
    /// <summary>Notification wording, kept apart from the event wiring so it can be tested and reworded in one place.</summary>
    public static class NotificationTexts
    {
        public static string Negotiable(string questTitle, int hoursWaiting)
            => $"'{questTitle}' 의뢰를 {Mathf.Max(0, hoursWaiting)}시간째 아무도 맡지 않았습니다. 길드원과 협상해 보세요.";

        public static string QuestDone(string questTitle) => $"'{questTitle}' 의뢰를 달성했습니다.";

        public static string QuestFailed(string questTitle) => $"'{questTitle}' 의뢰가 실패했습니다. 보상은 환불됩니다.";

        public static string Died(string name, string killer)
            => string.IsNullOrEmpty(killer) ? $"{name}이(가) 목숨을 잃었습니다..." : $"{name}이(가) {killer}에게 목숨을 잃었습니다...";

        public static string Crisis(string name, int hpPercent)
            => $"{name}의 체력이 {hpPercent}%까지 떨어졌습니다. 의뢰 중에 위험한 상태입니다.";

        public static string LevelUp(string name, int level) => $"{name}이(가) Lv.{level}이 되었습니다.";

        public static string Visitor(string name, string classLabel, int level)
            => $"방문자 {name}({classLabel} Lv.{level})이(가) 마을 광장에 왔습니다. 말을 걸어 영입해 보세요.";

        public static string RaidSighted(string direction) => $"{direction}쪽에서 적 무리가 몰려옵니다!";

        public static string RaidEnded(int killed, bool withdrawn)
            => withdrawn ? $"적이 물러갔습니다. (처치 {killed})" : $"침공을 막아냈습니다! (처치 {killed})";

        public static string BuildingDestroyed(string buildingName) => $"{buildingName}이(가) 무너졌습니다!";

        public static string MerchantArrived(int hoursOpen)
            => $"행상인이 노점을 열었습니다. 창고의 재료와 상품을 팔 수 있습니다. (약 {Mathf.Max(1, hoursOpen)}시간)";
    }

    /// <summary>
    /// Turns gameplay events into notifications (F-1). Only things the guild would know about are announced:
    /// a raid nobody saw stays unknown, exactly like the log and the raid banner.
    /// </summary>
    public class NotificationSources : IDisposable
    {
        /// <summary>A member out on a quest below this share of health counts as in danger.</summary>
        public const float CrisisHpRatio = 0.25f;
        /// <summary>Game hours between two danger notices for the same member.</summary>
        public const float CrisisCooldownHours = 6f;

        readonly NotificationCenter _center;
        readonly GameClock _clock;
        readonly QuestBoard _board;
        readonly Dictionary<string, float> _lastCrisis = new();

        public NotificationSources(NotificationCenter center, GameClock clock, QuestBoard board)
        {
            _center = center;
            _clock = clock;
            _board = board;
            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<QuestAcceptedEvent>(OnQuestAccepted);
            EventBus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
            EventBus.Subscribe<QuestClosedEvent>(OnQuestClosed);
            EventBus.Subscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Subscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Subscribe<Recruitment.VisitorArrivedEvent>(OnVisitor);
            EventBus.Subscribe<Town.RaidSightedEvent>(OnRaidSighted);
            EventBus.Subscribe<Town.RaidEndedEvent>(OnRaidEnded);
            EventBus.Subscribe<Town.BuildingDestroyedEvent>(OnBuildingDestroyed);
            EventBus.Subscribe<Town.MerchantArrivedEvent>(OnMerchant);
            EventBus.Subscribe<Reports.DailyReportClosedEvent>(OnDailyReport);
            EventBus.Subscribe<Disasters.DisasterSightedEvent>(OnDisasterSighted);
            EventBus.Subscribe<Disasters.DisasterEndedEvent>(OnDisasterEnded);
            EventBus.Subscribe<Ecosystem.BossSightedEvent>(OnBossSighted);
            EventBus.Subscribe<Ecosystem.RegionCleansedEvent>(OnRegionCleansed);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            EventBus.Unsubscribe<QuestAcceptedEvent>(OnQuestAccepted);
            EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
            EventBus.Unsubscribe<QuestClosedEvent>(OnQuestClosed);
            EventBus.Unsubscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Unsubscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Unsubscribe<Recruitment.VisitorArrivedEvent>(OnVisitor);
            EventBus.Unsubscribe<Town.RaidSightedEvent>(OnRaidSighted);
            EventBus.Unsubscribe<Town.RaidEndedEvent>(OnRaidEnded);
            EventBus.Unsubscribe<Town.BuildingDestroyedEvent>(OnBuildingDestroyed);
            EventBus.Unsubscribe<Town.MerchantArrivedEvent>(OnMerchant);
            EventBus.Unsubscribe<Reports.DailyReportClosedEvent>(OnDailyReport);
            EventBus.Unsubscribe<Disasters.DisasterSightedEvent>(OnDisasterSighted);
            EventBus.Unsubscribe<Disasters.DisasterEndedEvent>(OnDisasterEnded);
            EventBus.Unsubscribe<Ecosystem.BossSightedEvent>(OnBossSighted);
            EventBus.Unsubscribe<Ecosystem.RegionCleansedEvent>(OnRegionCleansed);
        }

        // ---------------- Natural disasters (P4-72): only those the guild has seen ----------------

        void OnDisasterSighted(Disasters.DisasterSightedEvent e)
        {
            var d = e.Disaster;
            _center.Post(NotificationKind.Danger, $"{d.Name} 발생!",
                $"{QuestBoard.DescribeArea(d.WorldOrigin)}에서 {d.NameI} 번지고 있습니다. 마을이 {Disasters.DisasterRules.WorkName(d.Kind)} 의뢰를 냈습니다.",
                "disaster:" + d.Id, NotificationLink.Location(d.WorldOrigin));
        }

        void OnDisasterEnded(Disasters.DisasterEndedEvent e)
        {
            if (!e.Known) return;
            _center.Resolve("disaster:" + e.Disaster.Id);
            if (e.DealtWith)
                _center.Post(NotificationKind.Success, $"{e.Disaster.Name} 수습",
                    $"길드가 {e.Disaster.NameEul} 수습했습니다.", "disaster-end:" + e.Disaster.Id);
        }

        static string NegotiableKey(Quest q) => "negotiable:" + q.Id;

        // ---------------- Time-driven checks ----------------

        void OnHour(HourPassedEvent e)
        {
            CheckNegotiable();
            CheckCrises();
        }

        /// <summary>
        /// A quest left untaken long enough that the guild master can negotiate (P4-24/25). Announced once per quest,
        /// and only for the ones the guild master chose to take on (a patron's request, an escort): the town posts a
        /// hunt on every den the guild has seen (P4-82), so most of its far ones wait untaken and would flood the list.
        /// They can still be negotiated from the board.
        /// </summary>
        void CheckNegotiable()
        {
            if (_board == null || !Services.TryGet<PersuasionService>(out var persuasion)) return;
            foreach (var q in _board.Quests)
            {
                if (q.Client == QuestClient.Town || !persuasion.IsOpen(q)) continue;
                int waited = Mathf.FloorToInt(_clock.TotalHours - q.PostedAtHour);
                _center.PostOnce(NegotiableKey(q), NotificationKind.Quest, "협상 가능",
                    NotificationTexts.Negotiable(q.Title, waited), NotificationLink.Negotiation(q.Id));
            }
        }

        /// <summary>Members out on a quest with little health left (at most once per member and cooldown).</summary>
        void CheckCrises()
        {
            if (!Services.TryGet<GuildRoster>(out var roster)) return;
            foreach (var a in roster.Members)
            {
                if (a == null || !a.IsAlive || a.CurrentQuest == null || a.HpRatio >= CrisisHpRatio) continue;
                string id = a.Data.id;
                if (_lastCrisis.TryGetValue(id, out float last) && _clock.TotalHours - last < CrisisCooldownHours) continue;
                _lastCrisis[id] = _clock.TotalHours;
                _center.Post(NotificationKind.Warning, "길드원 위기",
                    NotificationTexts.Crisis(a.Data.name, Mathf.RoundToInt(a.HpRatio * 100f)),
                    "crisis:" + id, NotificationLink.Adventurer(id));
            }
        }

        // ---------------- Quests ----------------

        void OnQuestAccepted(QuestAcceptedEvent e) => _center.Resolve(NegotiableKey(e.Quest));

        void OnQuestCompleted(QuestCompletedEvent e)
            => _center.Post(NotificationKind.Success, "의뢰 달성", NotificationTexts.QuestDone(e.Quest.Title),
                "quest-done:" + e.Quest.Id, NotificationLink.QuestBoard());

        void OnQuestClosed(QuestClosedEvent e)
        {
            _center.Resolve(NegotiableKey(e.Quest));
            // A quest the player cancelled needs no announcement; a failure does.
            if (e.Status == QuestStatus.Failed)
                _center.Post(NotificationKind.Warning, "의뢰 실패", NotificationTexts.QuestFailed(e.Quest.Title),
                    "quest-failed:" + e.Quest.Id, NotificationLink.QuestBoard());
        }

        // ---------------- Region lords (P4-83) ----------------

        void OnBossSighted(Ecosystem.BossSightedEvent e)
        {
            if (e.Boss == null || !e.Boss.IsRegionLord) return;
            _center.PostOnce("lord-seen:" + e.Boss.Def.id, NotificationKind.Danger, "지대 군주 발견",
                $"{Monsters.RegionLords.RegionName(e.Boss.Def.lordRegion)}의 군주 {e.Boss.DisplayName}(Lv.{e.Boss.Level})을(를) 발견했습니다. 마을이 토벌 의뢰를 올립니다.",
                NotificationLink.Location(e.Boss.Position));
        }

        void OnRegionCleansed(Ecosystem.RegionCleansedEvent e)
            => _center.Post(NotificationKind.Success, "지대 정화",
                $"{e.Lord.displayName}이(가) 쓰러졌습니다. {Monsters.RegionLords.RegionName(e.Region)} 바깥 땅에 더는 새 몬스터가 나타나지 않습니다. ({e.Fallen}/{e.Total})",
                "cleansed:" + e.Lord.id, NotificationLink.Location(e.Position));

        // ---------------- Guild members ----------------

        void OnDied(AdventurerDiedEvent e)
        {
            if (e.Adventurer == null) return;
            var link = NotificationLink.Location(e.Adventurer.Position);
            _center.Post(NotificationKind.Danger, "길드원 사망",
                NotificationTexts.Died(e.Adventurer.Data.name, e.Killer != null ? e.Killer.DisplayName : null),
                "died:" + e.Adventurer.Data.id, link);
        }

        void OnLevelUp(AdventurerLevelUpEvent e)
        {
            if (e.Adventurer == null) return;
            _center.Post(NotificationKind.Success, "레벨 업", NotificationTexts.LevelUp(e.Adventurer.Data.name, e.Level),
                "levelup:" + e.Adventurer.Data.id, NotificationLink.Adventurer(e.Adventurer.Data.id));
        }

        void OnVisitor(Recruitment.VisitorArrivedEvent e)
        {
            if (e.Visitor == null) return;
            var d = e.Visitor.Data;
            _center.Post(NotificationKind.Info, "방문자 도착",
                NotificationTexts.Visitor(d.name, AdventurerData.ClassLabel(d.cls), d.level),
                "visitor:" + d.id, NotificationLink.Adventurer(d.id));
        }

        // ---------------- Town ----------------

        void OnRaidSighted(Town.RaidSightedEvent e)
            => _center.Post(NotificationKind.Danger, "침공!", NotificationTexts.RaidSighted(e.Direction),
                "raid:" + e.RaidId, NotificationLink.Location(e.Position));

        void OnRaidEnded(Town.RaidEndedEvent e)
        {
            // A raid the guild never saw stays unknown.
            if (!e.Sighted) return;
            _center.Resolve("raid:" + e.RaidId);
            _center.Post(NotificationKind.Success, "침공 종료", NotificationTexts.RaidEnded(e.Killed, e.Withdrawn > 0),
                "raid-end:" + e.RaidId);
        }

        void OnBuildingDestroyed(Town.BuildingDestroyedEvent e)
        {
            if (e.Building == null) return;
            _center.Post(NotificationKind.Danger, "건물 파괴", NotificationTexts.BuildingDestroyed(e.Building.DisplayName),
                "building-lost:" + e.Building.DisplayName, NotificationLink.Location(e.Building.transform.position));
        }

        void OnMerchant(Town.MerchantArrivedEvent e)
        {
            int hours = Mathf.CeilToInt(e.CloseAtHour - _clock.TotalHours);
            _center.Post(NotificationKind.Info, "행상인 도착", NotificationTexts.MerchantArrived(hours), "merchant");
        }

        // ---------------- Accounts ----------------

        /// <summary>A day ended (F-3): the result in gold, leading to that day's report.</summary>
        void OnDailyReport(Reports.DailyReportClosedEvent e)
        {
            var r = e.Report;
            if (r == null) return;
            var kind = r.Net > 0 ? NotificationKind.Success : r.Net < 0 ? NotificationKind.Warning : NotificationKind.Info;
            _center.Post(kind, $"{r.Day}일차 정산", Reports.DailyReportTexts.Summary(r), "report:" + r.Day, NotificationLink.Report(r.Day));
        }
    }
}
