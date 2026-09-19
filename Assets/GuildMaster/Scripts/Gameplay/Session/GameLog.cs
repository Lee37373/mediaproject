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
    public class GameLog
    {
        const int MaxEntries = 200;

        readonly List<LogEntry> _entries = new();
        readonly GameClock _clock;
        readonly GameDatabase _db;

        public IReadOnlyList<LogEntry> Entries => _entries;

        public GameLog(GameClock clock, GameDatabase db)
        {
            _clock = clock;
            _db = db;
            EventBus.Subscribe<QuestPostedEvent>(e => Add($"의뢰 등록: {e.Quest.Title} (보상 {e.Quest.RewardGold}G / {e.Quest.RewardExp}EXP)", LogCategory.Quest));
            EventBus.Subscribe<QuestAcceptedEvent>(e => Add($"{e.Taker.TakerName}이(가) '{e.Quest.Title}' 의뢰를 수락했습니다.", LogCategory.Quest));
            EventBus.Subscribe<QuestAbandonedEvent>(e => Add($"{e.Taker.TakerName}이(가) '{e.Quest.Title}' 의뢰를 포기했습니다.", LogCategory.Quest));
            EventBus.Subscribe<QuestCompletedEvent>(e => Add($"의뢰 달성: {e.Quest.Title}", LogCategory.Quest));
            EventBus.Subscribe<QuestRewardPaidEvent>(e => Add($"{e.Taker.TakerName}에게 보상 지급: {e.Gold}G, {e.Exp}EXP", LogCategory.Quest));
            EventBus.Subscribe<QuestClosedEvent>(e =>
            {
                if (e.Status == QuestStatus.Cancelled) Add($"의뢰 취소: {e.Quest.Title} (보상 환불)", LogCategory.Quest);
                else if (e.Status == QuestStatus.Failed) Add($"의뢰 실패: {e.Quest.Title} (보상 환불)", LogCategory.Quest);
            });
            EventBus.Subscribe<PartyInviteAnsweredEvent>(e => Add(
                $"{e.Invitee.Data.name}이(가) {e.Leader.Data.name}의 파티 제안을 {(e.Accepted ? "수락" : "거절")}했습니다.", LogCategory.Quest));
            EventBus.Subscribe<PartyFormedEvent>(e => Add(
                $"파티 출발: {e.Leader.Data.name}, {string.Join(", ", e.Members.Select(m => m.Data.name))} → '{e.Quest.Title}'", LogCategory.Quest));
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
            // A raid the guild never saw stays unknown.
            EventBus.Subscribe<Town.RaidEndedEvent>(e => { if (e.Sighted) Add(e.Withdrawn > 0
                ? $"[침공] 남은 적 {e.Withdrawn}마리가 물러갔습니다. (처치 {e.Killed})"
                : $"[침공] 침공을 막아냈습니다! (처치 {e.Killed})", LogCategory.Danger); });
            EventBus.Subscribe<Town.BuildingDestroyedEvent>(e => Add($"[침공] {e.Building.DisplayName}이(가) 무너졌습니다!", LogCategory.Danger));
            EventBus.Subscribe<CraftCompletedEvent>(e => Add($"제작 완료: {_db.Item(e.Recipe.outputItemId)?.displayName ?? e.Recipe.outputItemId} x{e.Count}", LogCategory.Economy));
            EventBus.Subscribe<Town.BuildingLevelChangedEvent>(e => Add($"{e.Building.DisplayName} 업그레이드 완료 (Lv.{e.Level})", LogCategory.Info));
            EventBus.Subscribe<AdventurerLevelUpEvent>(e => Add($"{e.Adventurer.Data.name} 레벨 업! (Lv.{e.Level})", LogCategory.Info));
            EventBus.Subscribe<TradeEvent>(e =>
            {
                var name = _db.Item(e.ItemId)?.displayName ?? e.ItemId;
                Add(e.GuildBought
                    ? $"{e.TraderName}에게서 {name} x{e.Count} 매입 (-{e.Gold}G)"
                    : $"{e.TraderName}에게 {name} x{e.Count} 판매 (+{e.Gold}G)", LogCategory.Economy);
            });
            // Ecosystem news only when the guild could witness it (GuildIntel rule; also feeds LLM prompts).
            // Extinction is never announced: the guild cannot know that a species is gone everywhere.
            EventBus.Subscribe<NestClearedEvent>(e =>
            {
                if (Witnessed(e.Nest) || e.Killer is Adventurer)
                    Add($"[생태계] {e.Nest.DisplayName}이(가) 비었습니다.", LogCategory.Ecosystem);
            });
            EventBus.Subscribe<NestRecolonizedEvent>(e =>
            {
                if (Witnessed(e.Nest))
                    Add($"[생태계] {e.Nest.DisplayName}에 {e.Nest.Species.displayName}이(가) 다시 자리잡았습니다.", LogCategory.Ecosystem);
            });
            EventBus.Subscribe<NestDiscoveredEvent>(e => Add($"새 둥지 발견: {e.Nest.DisplayName}", LogCategory.Ecosystem));
            EventBus.Subscribe<DayPassedEvent>(e => Add($"{e.Day}일차가 밝았습니다.", LogCategory.Info));
        }

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
