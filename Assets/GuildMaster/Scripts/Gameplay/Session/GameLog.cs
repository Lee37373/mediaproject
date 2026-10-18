using System.Linq;
using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Economy;
using GuildMaster.Quests;

namespace GuildMaster.Session
{
    public enum LogCategory { Info, Quest, Combat, Economy, Ecosystem, Danger }

    public struct LogEntry
    {
        public string Time;
        public string Text;
        public LogCategory Category;
    }

    public struct LogAddedEvent { public LogEntry Entry; }

    /// <summary>
    /// Turns gameplay events into a readable, bounded history (shown in the UI and usable as
    /// recent-events context for LLM prompts).
    /// </summary>
    public class GameLog : ISaveable
    {
        const int MaxEntries = 200;

        readonly List<LogEntry> _entries = new();
        readonly GameClock _clock;
        readonly GameDatabase _db;

        public IReadOnlyList<LogEntry> Entries => _entries;

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SaveData
        {
            public List<string> times = new();
            public List<string> texts = new();
            public List<LogCategory> categories = new();
        }

        public string SaveKey => "log";

        public string Capture()
        {
            var s = new SaveData();
            foreach (var e in _entries)
            {
                s.times.Add(e.Time);
                s.texts.Add(e.Text);
                s.categories.Add(e.Category);
            }
            return UnityEngine.JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = UnityEngine.JsonUtility.FromJson<SaveData>(json);
            _entries.Clear();
            for (int i = 0; i < s.texts.Count && i < s.times.Count && i < s.categories.Count; i++)
                _entries.Add(new LogEntry { Time = s.times[i], Text = s.texts[i], Category = s.categories[i] });
        }

        public GameLog(GameClock clock, GameDatabase db)
        {
            _clock = clock;
            _db = db;
            // P4-82: a client's quest says who posted it and the guild's fee; only the guild's own escrow is refunded.
            EventBus.Subscribe<QuestPostedEvent>(e => Add(e.Quest.FundedByClient
                ? $"의뢰 등록({e.Quest.ClientLabel}): {e.Quest.Title} (보상 {e.Quest.RewardGold}G / {e.Quest.RewardExp}EXP · 수수료 {e.Quest.FeeGold}G)"
                : $"의뢰 등록: {e.Quest.Title} (보상 {e.Quest.RewardGold}G 예치)", LogCategory.Quest));
            EventBus.Subscribe<QuestAcceptedEvent>(e => Add($"{e.Taker.TakerName}이(가) '{e.Quest.Title}' 의뢰를 수락했습니다.", LogCategory.Quest));
            EventBus.Subscribe<QuestAbandonedEvent>(e => Add($"{e.Taker.TakerName}이(가) '{e.Quest.Title}' 의뢰를 포기했습니다.", LogCategory.Quest));
            EventBus.Subscribe<QuestCompletedEvent>(e => Add(e.Quest.FundedByClient && e.Quest.FeeGold > 0
                ? $"의뢰 달성: {e.Quest.Title} (수수료 {e.Quest.FeeGold}G 받음)"
                : $"의뢰 달성: {e.Quest.Title}", LogCategory.Quest));
            EventBus.Subscribe<QuestRewardPaidEvent>(e => Add($"{e.Taker.TakerName}에게 보상 지급: {e.Gold}G, {e.Exp}EXP", LogCategory.Quest));
            EventBus.Subscribe<QuestClosedEvent>(e =>
            {
                string refund = e.Quest.FundedByClient ? "" : " (보상 환불)";
                if (e.Status == QuestStatus.Cancelled && e.Reason != null) Add($"의뢰 철회: {e.Quest.Title} — {e.Reason}{refund}", LogCategory.Quest);
                else if (e.Status == QuestStatus.Cancelled) Add($"의뢰 취소: {e.Quest.Title}{refund}", LogCategory.Quest);
                else if (e.Status == QuestStatus.Failed) Add($"의뢰 실패: {e.Quest.Title}{refund}", LogCategory.Quest);
            });
            // Camps out in the wild (P4-66). Guild members are always in the guild's sight.
            EventBus.Subscribe<CampPitchedEvent>(e =>
            {
                var who = e.Camp.Leader?.Data.name ?? "길드원";
                string party = e.Camp.Members.Count > 1 ? $"{who} 일행" : who;
                string why = e.Camp.Reason == CampReason.Night ? "밤을 보내려고" : "지친 몸을 추스르려고";
                Add($"[야영] {party}이(가) {QuestBoard.DescribeArea(e.Camp.Position)}에서 {why} 모닥불을 피웠습니다.", LogCategory.Info);
            });
            EventBus.Subscribe<CampRaidedEvent>(e => Add(
                $"[야영] {QuestBoard.DescribeArea(e.Camp.Position)} 야영지에 {e.Raider?.DisplayName ?? "무언가"}이(가) 습격! " +
                (!e.Warned ? "자다가 기습당했습니다." : e.Camp.Watcher != null ? "불침번이 먼저 알아챘습니다." : "불가에 깨어 있다가 알아챘습니다."), LogCategory.Danger));
            EventBus.Subscribe<PartyInviteAnsweredEvent>(e => Add(
                $"{e.Invitee.Data.name}이(가) {e.Leader.Data.name}의 파티 제안을 {(e.Accepted ? "수락" : "거절")}했습니다.", LogCategory.Quest));
            EventBus.Subscribe<PartyFormedEvent>(e => Add(
                $"파티 출발: {e.Leader.Data.name}, {string.Join(", ", e.Members.Select(m => m.Data.name))} → '{e.Quest.Title}'", LogCategory.Quest));
            // Standing parties (P4-50): who joins or leaves, and how the leader split the pay.
            EventBus.Subscribe<PartyChangedEvent>(e =>
            {
                if (e.Member == null) return;
                if (e.Joined)
                {
                    // The one a party forms around does not "join" its own party.
                    if (e.Member != e.Party.Leader) Add($"{e.Member.Data.name}이(가) {e.Party.Name}에 합류했습니다.", LogCategory.Quest);
                }
                else if (e.Reason != "사망") Add($"{e.Member.Data.name}이(가) 파티를 떠났습니다 ({e.Reason ?? "해산"}).", LogCategory.Quest);
            });
            EventBus.Subscribe<PartySettledEvent>(e =>
            {
                if (e.Pool <= 0) return;
                Add($"{e.Party.Name} 정산 {e.Pool}G{(e.FromLlm ? "" : " (규칙 분배)")}: {string.Join(", ", e.Shares.Select(s => $"{s.member.Data.name} {s.share}G"))}", LogCategory.Economy);
            });
            EventBus.Subscribe<LivingCostsDayEvent>(e =>
            {
                if (e.Received <= 0 && e.Outstanding <= 0) return;
                var parts = new List<string> { $"식비 {e.Meals}G" };
                if (e.Lodging > 0) parts.Add($"숙박비 {e.Lodging}G");
                if (e.Repaid > 0) parts.Add($"외상 상환 {e.Repaid}G");
                string owed = e.Outstanding > 0 ? $" 길드원 외상이 {e.Outstanding}G 남았습니다." : "";
                Add($"{e.Day}일차 길드원 생활비 +{e.Received}G ({string.Join(" · ", parts)}).{owed}", LogCategory.Economy);
            });
            // Only when the relationship grade changes (friendly → close, etc.), not every point.
            EventBus.Subscribe<RelationChangedEvent>(e =>
            {
                string from = Relationships.Label(e.Old), to = Relationships.Label(e.New);
                if (from != to) Add($"{e.From.Data.name}의 {e.To.Data.name}에 대한 감정: {from} → {to} ({e.Reason})", LogCategory.Info);
            });
            EventBus.Subscribe<AdventurerJoinedEvent>(e => Add($"{e.Adventurer.Data.name}({AdventurerData.ClassLabel(e.Adventurer.Data.cls)})이(가) 길드에 합류했습니다.", LogCategory.Info));
            EventBus.Subscribe<Recruitment.VisitorArrivedEvent>(e => Add($"방문자 {e.Visitor.Data.name}({AdventurerData.ClassLabel(e.Visitor.Data.cls)} Lv.{e.Visitor.Data.level})이(가) 마을 광장에 왔습니다. 말을 걸어 영입해 보세요.", LogCategory.Info));
            EventBus.Subscribe<VisitorLeftTownEvent>(e => Add($"방문자 {e.Visitor.Data.name}이(가) 마을을 떠났습니다.", LogCategory.Info));
            EventBus.Subscribe<Recruitment.RecruitmentEndedEvent>(e =>
            {
                if (e.Outcome == Recruitment.RecruitOutcome.Left) Add($"{e.Visitor.Data.name}이(가) 영입 제안을 거절했습니다.", LogCategory.Info);
            });
            EventBus.Subscribe<AdventurerDiedEvent>(e => Add($"{e.Adventurer.Data.name}이(가) {(e.Killer != null ? e.Killer.DisplayName + "에게 " : "")}목숨을 잃었습니다...", LogCategory.Danger));
            EventBus.Subscribe<Town.RaidSightedEvent>(e => Add($"[침공] {e.Direction}쪽에서 적 무리가 관측되었습니다! 길드원들이 방어에 나섭니다.", LogCategory.Danger));
            // Natural disasters (P4-72): only the ones the guild has seen.
            EventBus.Subscribe<Disasters.DisasterSightedEvent>(e => Add(
                $"[재해] {Quests.QuestBoard.DescribeArea(e.Disaster.WorldOrigin)}에서 {e.Disaster.NameI} 관측되었습니다. 마을이 {Disasters.DisasterRules.WorkName(e.Disaster.Kind)} 의뢰를 냈습니다.", LogCategory.Danger));
            EventBus.Subscribe<Disasters.DisasterReachedTownEvent>(e => Add(
                $"[재해] {e.Disaster.NameI} 마을에 닿았습니다! 성문과 건물이 피해를 입고 오늘 세금이 줄어듭니다.", LogCategory.Danger));
            EventBus.Subscribe<Disasters.DisasterEndedEvent>(e =>
            {
                if (!e.Known) return;
                Add(e.DealtWith ? $"[재해] {e.Disaster.NameEul} 수습했습니다." : $"[재해] {e.Disaster.NameI} 저절로 사그라들었습니다.", LogCategory.Ecosystem);
            });
            // Sudden incidents (P4-85): heard by rumour; the end only when the guild knew of it.
            EventBus.Subscribe<Incidents.IncidentRumouredEvent>(e => Add($"[소문] {Incidents.IncidentRules.RumourText(e.Incident)}", LogCategory.Danger));
            EventBus.Subscribe<Incidents.IncidentRumourUpdatedEvent>(e => Add($"[소문] {Incidents.IncidentRules.RumourUpdateText(e.Incident)}", LogCategory.Danger));
            EventBus.Subscribe<Incidents.IncidentSightedEvent>(e => Add($"{Incidents.IncidentRules.Tag(e.Incident.Kind)} {Incidents.IncidentRules.SightedText(e.Incident)}", LogCategory.Danger));
            EventBus.Subscribe<Incidents.IncidentEndedEvent>(e =>
            {
                if (e.Known && Incidents.IncidentRules.EndedText(e.Incident) is { } text) Add($"{Incidents.IncidentRules.Tag(e.Incident.Kind)} {text}", LogCategory.Ecosystem);
            });
            // A raid the guild never saw stays unknown.
            EventBus.Subscribe<Town.RaidEndedEvent>(e => { if (e.Sighted) Add(e.Withdrawn > 0
                ? $"[침공] 남은 적 {e.Withdrawn}마리가 물러갔습니다. (처치 {e.Killed})"
                : $"[침공] 침공을 막아냈습니다! (처치 {e.Killed})", LogCategory.Danger); });
            EventBus.Subscribe<Town.BuildingDestroyedEvent>(e => Add($"[침공] {e.Building.DisplayName}이(가) 무너졌습니다!", LogCategory.Danger));
            EventBus.Subscribe<Town.GateBrokenEvent>(e => Add($"[침공] {e.Gate.DisplayName}이 부서졌습니다! 수리하기 전까지 몬스터가 드나듭니다.", LogCategory.Danger));
            EventBus.Subscribe<CraftCompletedEvent>(e => Add($"제작 완료: {_db.Item(e.Recipe.outputItemId)?.displayName ?? e.Recipe.outputItemId} x{e.Count}", LogCategory.Economy));
            EventBus.Subscribe<Town.BuildingLevelChangedEvent>(e => Add($"{e.Building.DisplayName} 업그레이드 완료 (Lv.{e.Level})", LogCategory.Info));
            EventBus.Subscribe<AdventurerLevelUpEvent>(e => Add($"{e.Adventurer.Data.name} 레벨 업! (Lv.{e.Level})", LogCategory.Info));
            EventBus.Subscribe<AdventurerGearUpgradedEvent>(e => Add($"{e.Adventurer.Data.name}이(가) {e.Tier}등급 장비를 샀습니다 ({e.Price}G).", LogCategory.Economy));
            EventBus.Subscribe<Town.MerchantArrivedEvent>(e => Add("행상인이 마을 광장에 노점을 열었습니다. 창고의 재료와 상품을 팔 수 있습니다. (6시간)", LogCategory.Economy));
            EventBus.Subscribe<Town.MerchantDepartingEvent>(e => Add("행상인이 노점을 접고 마을을 떠납니다.", LogCategory.Economy));
            EventBus.Subscribe<MerchantSaleEvent>(e => Add($"행상인에게 {_db.Item(e.ItemId)?.displayName ?? e.ItemId} x{e.Count} 판매 (+{e.Gold}G)", LogCategory.Economy));
            EventBus.Subscribe<TradeEvent>(e =>
            {
                var name = _db.Item(e.ItemId)?.displayName ?? e.ItemId;
                Add(e.GuildBought
                    ? $"{e.TraderName}에게서 {name} x{e.Count} 매입 (-{e.Gold}G)"
                    : $"{e.TraderName}에게 {name} x{e.Count} 판매 (+{e.Gold}G)", LogCategory.Economy);
            });
            // Ecosystem news only when the guild could witness it (GuildIntel rule; also feeds LLM prompts).
            EventBus.Subscribe<NestClearedEvent>(e =>
            {
                if (Witnessed(e.Nest) || e.Killer is Adventurer)
                    Add($"[생태계] {KnownName(e.Nest)}이(가) 비었습니다.", LogCategory.Ecosystem);
            });
            EventBus.Subscribe<NestRecolonizedEvent>(e =>
            {
                if (Witnessed(e.Nest))
                    Add($"[생태계] 먼 곳에서 온 {e.Nest.Species.displayName} 무리가 {e.Nest.DisplayName}에 자리잡았습니다.", LogCategory.Ecosystem);
            });
            // Territorial routs, only when the guild saw them (GuildIntel rule).
            EventBus.Subscribe<NestAbandonedEvent>(e => { if (e.Observed) Add($"{(e.DrivenBy == null ? $"{e.Cause}{Adventurers.MemoryRecorder.Josa(e.Cause ?? "", "을", "를")} 피해 " : $"{(e.DrivenBy == e.Species ? "다른 " : "")}{e.DrivenBy.displayName} 무리에게 밀린 ")}{e.Species.displayName} 무리가 {e.From.DisplayName}을(를) 버리고 떠났습니다{(e.To == null ? ". 갈 곳 없이 떠돌기 시작합니다" : "")}.", LogCategory.Ecosystem); });
            // Roaming packs (P4-58): seen for the first time, and settling where the guild could see it.
            EventBus.Subscribe<RoamingPackSightedEvent>(e => Add($"둥지 없이 떠도는 {e.Pack.Species.displayName} 무리({e.Pack.Members.Count}마리)를 발견했습니다.", LogCategory.Ecosystem));
            EventBus.Subscribe<RoamingPackSettledEvent>(e => { if (e.Observed) Add($"떠돌던 {e.Pack.Species.displayName} 무리가 {e.Nest.DisplayName}에 자리 잡았습니다.", LogCategory.Ecosystem); });
            EventBus.Subscribe<IntrudersRoutedEvent>(e => { if (e.Observed) Add($"{e.IntruderHome.Species.displayName} 무리가 {e.Territory.DisplayName}의 기세에 눌려 물러났습니다.", LogCategory.Ecosystem); });
            EventBus.Subscribe<BandSettledEvent>(e => { if (e.Observed) Add($"{e.Species.displayName} 무리 {e.Count}마리가 옮겨 와 {e.To.DisplayName}에 자리 잡았습니다.", LogCategory.Ecosystem); });
            // Found by seeing the den, not who lives in it (P4-76: the log named the real owners).
            EventBus.Subscribe<NestDiscoveredEvent>(e => Add($"새 둥지 발견: {KnownName(e.Nest)}", LogCategory.Ecosystem));
            // Monsters using ground items (P4-21), only when the guild sees it happen.
            EventBus.Subscribe<Monsters.MonsterItemUsedEvent>(e =>
            {
                if (e.Monster == null || e.Item == null || !Services.TryGet<Fog.FogOfWar>(out var fog) || !fog.IsVisibleRaw(e.Monster.Position)) return;
                Add(e.Absorbed ? $"{e.Monster.DisplayName}이(가) {e.Item.displayName}을(를) 삼켰습니다."
                               : $"{e.Monster.DisplayName}이(가) {e.Item.displayName}을(를) 주워 착용했습니다.", LogCategory.Ecosystem);
            });
            // Pack bosses (P4-57): seen for the first time, and killed where the guild could see it.
            EventBus.Subscribe<BossSightedEvent>(e => Add(e.Boss.IsRegionLord
                ? $"[지대 군주] {Monsters.RegionLords.RegionName(e.Boss.Def.lordRegion)}의 군주 {e.Boss.DisplayName}(Lv.{e.Boss.Level})을(를) 발견했습니다! 거처를 {e.Boss.Def.lairEscort?.displayName} 무리가 지키고 있습니다."
                : $"[보스] {e.Boss.DisplayName}(Lv.{e.Boss.Level})이(가) 이끄는 {e.Boss.Def.displayName} 무리를 발견했습니다!", LogCategory.Danger));
            // Region lords (P4-83): a fall the guild learned of, and the four of them down.
            EventBus.Subscribe<RegionCleansedEvent>(e =>
            {
                string who = e.Participants != null && e.Participants.Count > 0 ? $" (함께한 길드원: {string.Join(", ", e.Participants)})" : "";
                Add($"[지대 정화] {e.Lord.displayName}이(가) 쓰러졌습니다 — {Monsters.RegionLords.RegionName(e.Region)} 바깥 땅에 더는 새 몬스터가 나타나지 않습니다. ({e.Fallen}/{e.Total}){who}", LogCategory.Quest);
            });
            EventBus.Subscribe<AllLordsSlainEvent>(e => Add($"[승리] 지대 군주 {e.Lords}을 모두 쓰러뜨렸습니다! ({e.Day}일차)", LogCategory.Quest));
            EventBus.Subscribe<Units.UnitDiedEvent>(e =>
            {
                if (e.Victim is not Monsters.Monster { IsBoss: true } boss || !Services.TryGet<Fog.FogOfWar>(out var fog) || !fog.IsVisibleRaw(boss.Position)) return;
                Add(e.Killer != null ? $"[보스] {e.Killer.DisplayName}이(가) {boss.DisplayName}을(를) 쓰러뜨렸습니다!" : $"[보스] {boss.DisplayName}이(가) 쓰러졌습니다.", LogCategory.Ecosystem);
            });
            // Other villages (P4-84): what the guild saw of them, and the rumour of a new one.
            EventBus.Subscribe<Villages.VillageDiscoveredEvent>(e => Add(
                $"[마을] 새 마을 발견: {e.Village.DisplayName} ({Monsters.RegionLords.RegionName(e.Village.Region)}, {Quests.QuestBoard.DescribeArea(e.Village.WorldPosition)}). 길드 마스터가 직접 가면 영입 후보를 만날 수 있습니다.", LogCategory.Info));
            EventBus.Subscribe<Villages.VillageRaidSightedEvent>(e => Add(
                $"[마을] {e.Village.DisplayName}이(가) {e.Species?.displayName} {e.Raiders}마리에게 습격받고 있습니다! 마을이 방어 의뢰를 냈습니다.", LogCategory.Danger));
            EventBus.Subscribe<Villages.VillageRaidEndedEvent>(e =>
            {
                if (e.Sighted && !e.Fell) Add($"[마을] {e.Village.DisplayName} 습격이 끝났습니다. (처치 {e.Killed}, 물러남 {e.Withdrawn})", LogCategory.Danger);
            });
            EventBus.Subscribe<Villages.VillageRuinsSeenEvent>(e => Add(e.SawItFall
                ? $"[마을] {e.Village.DisplayName}이(가) 무너졌습니다! 다시 세워지지 않습니다."
                : $"[마을] {e.Village.DisplayName}이(가) 무너져 폐허만 남은 것을 확인했습니다.", LogCategory.Danger));
            EventBus.Subscribe<Villages.VillageFoundedEvent>(e => Add(
                $"[마을] {Monsters.RegionLords.RegionName(e.Village.Region)} 어딘가에 새 마을이 생겼다는 소문이 들립니다.", LogCategory.Info));
            EventBus.Subscribe<Villages.VillagerJoinedEvent>(e => Add(
                $"{e.Adventurer.Data.name}이(가) {e.Village.DisplayName}에서 길드에 들어와 혼자 우리 마을로 향합니다.", LogCategory.Info));
            EventBus.Subscribe<Villages.VillageRefugeesFledEvent>(e =>
            {
                if (e.Seen) Add($"[마을] {e.Village.DisplayName} 사람 {e.Count}명이 우리 마을로 피난을 떠납니다.", LogCategory.Danger);
            });
            EventBus.Subscribe<RefugeeArrivedEvent>(e => Add(
                $"피난민 {e.Visitor.Data.name}({AdventurerData.ClassLabel(e.Visitor.Data.cls)} Lv.{e.Visitor.Data.level})이(가) {e.From}에서 우리 마을에 왔습니다. 말을 걸어 영입해 보세요.", LogCategory.Info));
            EventBus.Subscribe<Units.UnitDiedEvent>(e =>
            {
                if (e.Victim is not Adventurer { IsVisitor: true } refugee || !Services.TryGet<Fog.FogOfWar>(out var fog) || !fog.IsVisibleRaw(refugee.Position)) return;
                Add($"피난 가던 {refugee.Data.name}이(가) {(e.Killer != null ? e.Killer.DisplayName + "에게 " : "")}쓰러졌습니다.", LogCategory.Danger);
            });
            EventBus.Subscribe<DayPassedEvent>(e => Add($"{e.Day}일차가 밝았습니다.", LogCategory.Info));
        }

        /// <summary>The den as the guild knows it: named after the owners last seen there, "정체불명" before that.</summary>
        static string KnownName(Nest nest)
            => Services.TryGet<GuildIntel>(out var intel) ? intel.NameOf(nest) : nest.NameFor(null);

        static bool Witnessed(Nest nest)
            => nest.Discovered && Services.TryGet<Fog.FogOfWar>(out var fog) && fog.IsVisibleRaw(nest.Cell);

        public void Add(string text, LogCategory category = LogCategory.Info)
        {
            var entry = new LogEntry { Time = $"{_clock.Day}일 {_clock.Hour:00}:{_clock.Minute:00}", Text = text, Category = category };
            _entries.Add(entry);
            if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            EventBus.Publish(new LogAddedEvent { Entry = entry });
        }

        /// <summary>Last N entries as plain lines (for prompts).</summary>
        public IEnumerable<string> Recent(int count)
        {
            for (int i = System.Math.Max(0, _entries.Count - count); i < _entries.Count; i++)
                yield return _entries[i].Text;
        }
    }
}
