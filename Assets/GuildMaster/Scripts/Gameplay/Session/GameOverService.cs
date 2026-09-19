using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Player;
using GuildMaster.Quests;
using GuildMaster.Recruitment;

namespace GuildMaster.Session
{
    public struct GameOverEvent
    {
        public string Reason;
        public int Day;
        public int Members;
        public int QuestsCompleted;
        public int Recruits;
        /// <summary>Raid waves the town has faced (the current one included).</summary>
        public int Waves;
        /// <summary>Species wiped out by the end (revealed on the result screen only).</summary>
        public int ExtinctSpecies;
    }

    /// <summary>
    /// Ends the session: freezes the clock and publishes the summary for the game-over screen.
    /// Triggers: guild master downed (P3-4a), town hall destroyed (P3-6).
    /// </summary>
    public class GameOverService
    {
        readonly GameClock _clock;
        int _recruits;

        public bool IsOver { get; private set; }

        public GameOverService(GameClock clock)
        {
            _clock = clock;
            EventBus.Subscribe<PlayerDownedEvent>(OnPlayerDowned);
            EventBus.Subscribe<Town.BuildingDestroyedEvent>(e => { if (e.Building.Type == Town.BuildingType.TownHall) End("마을회관이 파괴되었습니다."); });
            EventBus.Subscribe<RecruitmentEndedEvent>(e => { if (e.Outcome == RecruitOutcome.Joined) _recruits++; });
        }

        void OnPlayerDowned(PlayerDownedEvent e)
        {
            string by = e.Killer != null ? $"{e.Killer.DisplayName}에게 " : "";
            End($"길드 마스터가 {by}쓰러졌습니다.");
        }

        public void End(string reason)
        {
            if (IsOver) return;
            IsOver = true;
            if (Services.TryGet<GameLog>(out var log)) log.Add("[게임 오버] " + reason, LogCategory.Danger);
            _clock.Freeze();
            int done = Services.TryGet<QuestBoard>(out var board)
                ? board.History.Count(q => q.Status == QuestStatus.TurnedIn) : 0;
            int members = Services.TryGet<GuildRoster>(out var roster) ? roster.Members.Count : 0;
            EventBus.Publish(new GameOverEvent
            {
                Reason = reason, Day = _clock.Day, Members = members, QuestsCompleted = done, Recruits = _recruits,
                Waves = Services.TryGet<Town.WaveManager>(out var waves) ? waves.Wave : 0,
                ExtinctSpecies = Services.TryGet<Ecosystem.EcosystemManager>(out var eco) ? eco.Species.Count(eco.IsExtinct) : 0,
            });
        }
    }
}
