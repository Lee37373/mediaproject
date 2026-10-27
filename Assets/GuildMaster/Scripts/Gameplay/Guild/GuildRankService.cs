using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Ecosystem;
using GuildMaster.Economy;
using GuildMaster.Quests;
using GuildMaster.Save;
using GuildMaster.Town;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Guild
{
    /// <summary>The guild's fame went up or down (P4-106): by <see cref="Amount"/>, for <see cref="Reason"/>.</summary>
    public struct FameChangedEvent { public int Amount; public int Fame; public string Reason; }

    /// <summary>The guild reached a new rank (P4-106); <see cref="GuildRank.S"/> is the victory.</summary>
    public struct GuildRankChangedEvent { public GuildRank Rank; public GuildRank Previous; public int Grant; }

    /// <summary>
    /// The guild's fame and rank (P4-106, H1, user decisions 2026-10-27; numbers in <see cref="GuildRankRules"/>). Listens for
    /// what the guild does and sees — quests done or failed, dens cleared by its members, land and kinds and places found,
    /// raids held, lords and the dragon slain, members lost, buildings destroyed, gold plundered — keeps the fame and the
    /// counts the conditions ask for, and climbs the ranks when every condition of the next is met (several at once if so):
    /// the town's grant goes into the treasury, and S is the victory. A rank is never lost (decision ③). Saved as "guild_rank".
    /// </summary>
    public class GuildRankService : MonoBehaviour, ISaveable
    {
        GameClock _clock;
        GuildRoster _roster;
        GuildTreasury _treasury;
        /// <summary>
        /// Events while a saved game is being put back are not news: from this service's own restore to the end of the load
        /// (<see cref="GameLoadedEvent"/>) nothing counts (the roster's members join again, buildings take their levels).
        /// </summary>
        bool _loading;

        public int Fame { get; private set; }
        public GuildRank Rank { get; private set; } = GuildRank.F;
        public int QuestsDone { get; private set; }
        public int Recruits { get; private set; }
        public int Upgrades { get; private set; }
        public int NestsCleared { get; private set; }
        public int RaidsHeld { get; private set; }
        /// <summary>Kinds of monster the guild has been given fame for seeing (those known at the start are not news).</summary>
        int _speciesCredited;

        /// <summary>The latest changes to the fame, newest last.</summary>
        public readonly List<FameEntry> History = new();

        [Serializable]
        public struct FameEntry
        {
            public float hour;
            public int amount;
            public string reason;
        }

        /// <param name="loading">A saved game is about to be put back: nothing counts until it is (a save from before P4-106 has no section to restore).</param>
        public void Init(GameClock clock, GuildRoster roster, GuildTreasury treasury, bool loading)
        {
            _clock = clock;
            _roster = roster;
            _treasury = treasury;
            _loading = loading;
            _speciesCredited = SpeciesKnownNow();
            EventBus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
            EventBus.Subscribe<QuestClosedEvent>(OnQuestClosed);
            EventBus.Subscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Subscribe<AdventurerJoinedEvent>(OnJoined);
            EventBus.Subscribe<NestClearedEvent>(OnNestCleared);
            EventBus.Subscribe<ZoneDiscoveredEvent>(OnZone);
            EventBus.Subscribe<Places.PlaceDiscoveredEvent>(OnPlace);
            EventBus.Subscribe<RaidEndedEvent>(OnRaidEnded);
            EventBus.Subscribe<Incidents.IncidentEndedEvent>(OnIncidentEnded);
            EventBus.Subscribe<RegionCleansedEvent>(OnLord);
            EventBus.Subscribe<Dragons.DragonSlainEvent>(OnDragon);
            EventBus.Subscribe<Dragons.DragonPlunderedEvent>(OnPlundered);
            EventBus.Subscribe<BuildingDestroyedEvent>(OnBuildingDestroyed);
            EventBus.Subscribe<BuildingLevelChangedEvent>(OnBuildingLevel);
            EventBus.Subscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<GameLoadedEvent>(OnLoaded);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
            EventBus.Unsubscribe<QuestClosedEvent>(OnQuestClosed);
            EventBus.Unsubscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Unsubscribe<AdventurerJoinedEvent>(OnJoined);
            EventBus.Unsubscribe<NestClearedEvent>(OnNestCleared);
            EventBus.Unsubscribe<ZoneDiscoveredEvent>(OnZone);
            EventBus.Unsubscribe<Places.PlaceDiscoveredEvent>(OnPlace);
            EventBus.Unsubscribe<RaidEndedEvent>(OnRaidEnded);
            EventBus.Unsubscribe<Incidents.IncidentEndedEvent>(OnIncidentEnded);
            EventBus.Unsubscribe<RegionCleansedEvent>(OnLord);
            EventBus.Unsubscribe<Dragons.DragonSlainEvent>(OnDragon);
            EventBus.Unsubscribe<Dragons.DragonPlunderedEvent>(OnPlundered);
            EventBus.Unsubscribe<BuildingDestroyedEvent>(OnBuildingDestroyed);
            EventBus.Unsubscribe<BuildingLevelChangedEvent>(OnBuildingLevel);
            EventBus.Unsubscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            EventBus.Unsubscribe<GameLoadedEvent>(OnLoaded);
        }

        // ---------------- Where it stands ----------------

        /// <summary>The numbers the conditions count, now.</summary>
        public GuildStanding Standing => new()
        {
            Fame = Fame, QuestsDone = QuestsDone, Recruits = Recruits, Upgrades = Upgrades, NestsCleared = NestsCleared,
            RaidsHeld = RaidsHeld,
            ZonesKnown = Services.TryGet<ZoneDiscovery>(out var zones) ? zones.KnownCount : 0,
            SpeciesKnown = SpeciesKnownNow(),
            Facilities = Services.TryGet<TownService>(out var town) ? town.Buildings.Count(b => b != null && FacilityRules.IsFacility(b.Type)) : 0,
            LordsSlain = Services.TryGet<RegionLordService>(out var lords) ? lords.KnownFallen : 0,
            MembersAtLevel = level => _roster == null ? 0 : _roster.Members.Count(m => m != null && m.IsAlive && m.Data.level >= level),
        };

        static int SpeciesKnownNow() => Services.TryGet<GuildIntel>(out var intel) ? intel.KnownSpecies.Count() : 0;

        public GuildRank Next => GuildRankRules.Next(Rank);

        public IReadOnlyList<Goal> NextGoals => GuildRankRules.IsTop(Rank) ? Array.Empty<Goal>() : GuildRankRules.GoalsFor(Next);

        /// <summary>The victory (S, user decision ④⑤).</summary>
        public bool Victory => GuildRankRules.IsTop(Rank);

        public int VisitorBonus => GuildRankRules.VisitorBonus(Rank);

        public float VisitInterval => GuildRankRules.VisitInterval(Rank);

        /// <summary>Visitors' extra levels for the guild's rank now (0 with no rank service, e.g. pure tests).</summary>
        public static int RankVisitorBonus => Services.TryGet<GuildRankService>(out var r) ? r.VisitorBonus : 0;

        /// <summary>The time between visits for the guild's rank now (1 with no rank service).</summary>
        public static float RankVisitInterval => Services.TryGet<GuildRankService>(out var r) ? r.VisitInterval : 1f;

        // ---------------- Fame ----------------

        /// <summary>Fame up or down (never below zero), noted in the history; then the ranks are checked.</summary>
        public void AddFame(int amount, string reason)
        {
            if (amount == 0 || _loading) return;
            int before = Fame;
            Fame = Mathf.Max(0, Fame + amount);
            int moved = Fame - before;
            if (moved == 0) return;
            History.Add(new FameEntry { hour = _clock != null ? _clock.TotalHours : 0f, amount = moved, reason = reason });
            if (History.Count > GuildRankRules.HistoryLength) History.RemoveRange(0, History.Count - GuildRankRules.HistoryLength);
            EventBus.Publish(new FameChangedEvent { Amount = moved, Fame = Fame, Reason = reason });
            CheckRank();
        }

        /// <summary>Climbs as far as the conditions now allow (each rank's grant paid; S is the victory).</summary>
        public void CheckRank()
        {
            if (_loading) return;
            var standing = Standing;
            while (!GuildRankRules.IsTop(Rank) && GuildRankRules.Reached(Next, standing))
            {
                var previous = Rank;
                Rank = Next;
                int grant = GuildRankRules.Grant(Rank);
                if (grant > 0 && _treasury != null) _treasury.AddGold(grant, GoldReason.Grant);
                EventBus.Publish(new GuildRankChangedEvent { Rank = Rank, Previous = previous, Grant = grant });
            }
        }

        // ---------------- What the guild did and saw ----------------

        void OnQuestCompleted(QuestCompletedEvent e)
        {
            if (_loading || e.Quest == null) return;
            QuestsDone++;
            AddFame(GuildRankRules.QuestFame(e.Quest.Danger), $"의뢰 완료: {e.Quest.Title}");
            CheckRank();
        }

        void OnQuestClosed(QuestClosedEvent e)
        {
            if (e.Status == QuestStatus.Failed && e.Quest != null) AddFame(GuildRankRules.QuestFailed, $"의뢰 실패: {e.Quest.Title}");
        }

        void OnDied(AdventurerDiedEvent e)
        {
            var a = e.Adventurer;
            if (a != null && a.IsGuildMember && !a.IsVisitor) AddFame(GuildRankRules.MemberDied, $"길드원 {a.Data.name} 사망");
        }

        void OnJoined(AdventurerJoinedEvent e)
        {
            if (_loading) return;
            Recruits++;
            CheckRank();
        }

        void OnNestCleared(NestClearedEvent e)
        {
            if (_loading || e.Killer is not Adventurer a || !a.IsGuildMember) return;
            NestsCleared++;
            AddFame(GuildRankRules.NestCleared, "둥지 토벌");
        }

        void OnZone(ZoneDiscoveredEvent e) => AddFame(GuildRankRules.ZoneFound, $"새 구역: {e.Zone?.Name}");

        void OnPlace(Places.PlaceDiscoveredEvent e) => AddFame(GuildRankRules.PlaceFound, $"새 장소: {e.Place?.Name()}");

        void OnRaidEnded(RaidEndedEvent e)
        {
            if (_loading || !e.Sighted || e.Killed <= 0) return;
            RaidsHeld++;
            AddFame(GuildRankRules.RaidHeld, "침공을 막아냄");
        }

        /// <summary>
        /// A great horde stopped on its way to the town (P4-108, user decision 2026-10-27): it counts as a raid held for the ranks
        /// — with the dens near the town cleared no raid may come for days (two measured 8-day runs saw none). Its fame came
        /// with its quest.
        /// </summary>
        void OnIncidentEnded(Incidents.IncidentEndedEvent e)
        {
            if (_loading || !e.Known || e.Incident is not { Kind: Incidents.IncidentKind.Horde, Outcome: Incidents.IncidentOutcome.Resolved }) return;
            RaidsHeld++;
            CheckRank();
        }

        void OnLord(RegionCleansedEvent e) => AddFame(GuildRankRules.LordSlain, $"지대 군주 처치: {e.Lord?.displayName}");

        void OnDragon(Dragons.DragonSlainEvent e) => AddFame(GuildRankRules.DragonSlain, "드래곤 처치");

        void OnPlundered(Dragons.DragonPlunderedEvent e) => AddFame(GuildRankRules.Plundered, "드래곤에게 금고를 털림");

        void OnBuildingDestroyed(BuildingDestroyedEvent e)
            => AddFame(GuildRankRules.BuildingDestroyed, $"{(e.Building != null ? e.Building.DisplayName : "건물")} 파괴됨");

        void OnBuildingLevel(BuildingLevelChangedEvent e)
        {
            if (_loading || e.Level < 2) return;
            Upgrades++;
            CheckRank();
        }

        void OnLevelUp(AdventurerLevelUpEvent e) => CheckRank();

        /// <summary>Kinds newly seen this hour, and the counted conditions (zones, members, lords) looked at again.</summary>
        void OnHour(HourPassedEvent e)
        {
            if (_loading) return;
            int known = SpeciesKnownNow();
            if (known > _speciesCredited)
            {
                int fresh = known - _speciesCredited;
                _speciesCredited = known;
                AddFame(GuildRankRules.SpeciesFound * fresh, fresh == 1 ? "새 몬스터 발견" : $"새 몬스터 {fresh}종 발견");
            }
            CheckRank();
        }

        void OnLoaded(GameLoadedEvent e) => _loading = false;

        // ---------------- Save (P5-4) ----------------

        [Serializable]
        class SaveData
        {
            public int fame;
            public GuildRank rank;
            public int quests, recruits, upgrades, nests, raids, species;
            public List<FameEntry> history = new();
        }

        public string SaveKey => "guild_rank";

        public string Capture() => JsonUtility.ToJson(new SaveData
        {
            fame = Fame, rank = Rank, quests = QuestsDone, recruits = Recruits, upgrades = Upgrades, nests = NestsCleared,
            raids = RaidsHeld, species = _speciesCredited, history = new List<FameEntry>(History),
        });

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            Fame = s.fame;
            Rank = s.rank;
            QuestsDone = s.quests;
            Recruits = s.recruits;
            Upgrades = s.upgrades;
            NestsCleared = s.nests;
            RaidsHeld = s.raids;
            _speciesCredited = s.species;
            History.Clear();
            if (s.history != null) History.AddRange(s.history);
            _loading = true;
        }
    }
}
