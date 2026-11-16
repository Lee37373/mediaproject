using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Director;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Reports
{
    /// <summary>The morning's paper is out (P4-130): the window opens on it.</summary>
    public struct NewsPaperDeliveredEvent { public NewsPaper Paper; }

    /// <summary>
    /// The town newspaper (P4-130, F-9 — user decisions 2026-11-05). Notes the day's stories as they happen — the moments
    /// the spectator director found (P4-129), deaths and newcomers, raids, slain lords and the dragon, decisions, quarrels
    /// and friendships, disasters, the seasons — from what the guild knows only (the same events the notifications tell),
    /// composes the paper when the day is closed (<see cref="DailyReportClosedEvent"/>) and hands it over at
    /// <see cref="NewsRules.DeliveryHour"/> the morning after (<see cref="NewsPaperDeliveredEvent"/>). Written lines only, no
    /// model. Saved as "news".
    /// </summary>
    public class NewsDesk : IDisposable, ISaveable
    {
        /// <summary>The paper's window opens by itself and the game waits (off for tests and measuring runs).</summary>
        public static bool AutoOpen = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => AutoOpen = true;

        readonly GameClock _clock;
        readonly GuildRoster _roster;
        readonly List<NewsArticle> _desk = new();
        readonly List<NewsPaper> _papers = new();
        readonly Dictionary<int, string> _raidDirection = new();

        /// <summary>Stories noted for days not yet closed.</summary>
        public IReadOnlyList<NewsArticle> Desk => _desk;
        /// <summary>Papers of the last days, oldest first.</summary>
        public IReadOnlyList<NewsPaper> Papers => _papers;
        public NewsPaper Latest => _papers.Count > 0 ? _papers[_papers.Count - 1] : null;

        public NewsDesk(GameClock clock, GuildRoster roster)
        {
            _clock = clock;
            _roster = roster;
            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<DailyReportClosedEvent>(OnDayClosed);
            EventBus.Subscribe<HighlightEndedEvent>(OnHighlight);
            EventBus.Subscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Subscribe<AdventurerJoinedEvent>(OnJoined);
            EventBus.Subscribe<AdventurerLeftEvent>(OnLeft);
            EventBus.Subscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Subscribe<QuestCompletedEvent>(OnQuestDone);
            EventBus.Subscribe<QuestClosedEvent>(OnQuestClosed);
            EventBus.Subscribe<MentalBreakEvent>(OnBreak);
            EventBus.Subscribe<InspirationEvent>(OnInspired);
            EventBus.Subscribe<RelationChangedEvent>(OnRelation);
            EventBus.Subscribe<Social.TownTalkEvent>(OnTownTalk);
            EventBus.Subscribe<Town.RaidSightedEvent>(OnRaidSighted);
            EventBus.Subscribe<Town.RaidEndedEvent>(OnRaidEnded);
            EventBus.Subscribe<Town.BuildingDestroyedEvent>(OnBuildingLost);
            EventBus.Subscribe<Town.MerchantArrivedEvent>(OnMerchant);
            EventBus.Subscribe<Guild.GuildRankChangedEvent>(OnRankUp);
            EventBus.Subscribe<Dilemmas.DilemmaResolvedEvent>(OnDecision);
            EventBus.Subscribe<Dragons.DragonSlainEvent>(OnDragonSlain);
            EventBus.Subscribe<Dragons.DragonSightedEvent>(OnDragonSighted);
            EventBus.Subscribe<Dragons.DragonLairFoundEvent>(OnDragonLair);
            EventBus.Subscribe<Dragons.DragonHoardRaidedEvent>(OnDragonHoard);
            EventBus.Subscribe<Dragons.DragonPlunderedEvent>(OnDragonPlundered);
            EventBus.Subscribe<Ecosystem.RegionCleansedEvent>(OnRegionCleansed);
            EventBus.Subscribe<Nemeses.NemesisDeedEvent>(OnNemesisDeed);
            EventBus.Subscribe<Nemeses.NemesisFellEvent>(OnNemesisFell);
            EventBus.Subscribe<Disasters.DisasterSightedEvent>(OnDisaster);
            EventBus.Subscribe<Disasters.DisasterEndedEvent>(OnDisasterEnded);
            EventBus.Subscribe<Incidents.IncidentEndedEvent>(OnIncidentEnded);
            EventBus.Subscribe<Seasons.SeasonChangedEvent>(OnSeason);
            EventBus.Subscribe<Seasons.FestivalEvent>(OnFestival);
            EventBus.Subscribe<Villages.VillageDiscoveredEvent>(OnVillage);
            EventBus.Subscribe<Legacy.MemorialStoryEvent>(OnMemorial);
            EventBus.Subscribe<Villages.VillageRuinsSeenEvent>(OnVillageLost);
            EventBus.Subscribe<Villages.VillageRaidSightedEvent>(OnVillageRaid);
            EventBus.Subscribe<Places.PlaceDiscoveredEvent>(OnPlace);
            EventBus.Subscribe<World.ZoneDiscoveredEvent>(OnZone);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            EventBus.Unsubscribe<DailyReportClosedEvent>(OnDayClosed);
            EventBus.Unsubscribe<HighlightEndedEvent>(OnHighlight);
            EventBus.Unsubscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Unsubscribe<AdventurerJoinedEvent>(OnJoined);
            EventBus.Unsubscribe<AdventurerLeftEvent>(OnLeft);
            EventBus.Unsubscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestDone);
            EventBus.Unsubscribe<QuestClosedEvent>(OnQuestClosed);
            EventBus.Unsubscribe<MentalBreakEvent>(OnBreak);
            EventBus.Unsubscribe<InspirationEvent>(OnInspired);
            EventBus.Unsubscribe<RelationChangedEvent>(OnRelation);
            EventBus.Unsubscribe<Social.TownTalkEvent>(OnTownTalk);
            EventBus.Unsubscribe<Town.RaidSightedEvent>(OnRaidSighted);
            EventBus.Unsubscribe<Town.RaidEndedEvent>(OnRaidEnded);
            EventBus.Unsubscribe<Town.BuildingDestroyedEvent>(OnBuildingLost);
            EventBus.Unsubscribe<Town.MerchantArrivedEvent>(OnMerchant);
            EventBus.Unsubscribe<Guild.GuildRankChangedEvent>(OnRankUp);
            EventBus.Unsubscribe<Dilemmas.DilemmaResolvedEvent>(OnDecision);
            EventBus.Unsubscribe<Dragons.DragonSlainEvent>(OnDragonSlain);
            EventBus.Unsubscribe<Dragons.DragonSightedEvent>(OnDragonSighted);
            EventBus.Unsubscribe<Dragons.DragonLairFoundEvent>(OnDragonLair);
            EventBus.Unsubscribe<Dragons.DragonHoardRaidedEvent>(OnDragonHoard);
            EventBus.Unsubscribe<Dragons.DragonPlunderedEvent>(OnDragonPlundered);
            EventBus.Unsubscribe<Ecosystem.RegionCleansedEvent>(OnRegionCleansed);
            EventBus.Unsubscribe<Nemeses.NemesisDeedEvent>(OnNemesisDeed);
            EventBus.Unsubscribe<Nemeses.NemesisFellEvent>(OnNemesisFell);
            EventBus.Unsubscribe<Disasters.DisasterSightedEvent>(OnDisaster);
            EventBus.Unsubscribe<Disasters.DisasterEndedEvent>(OnDisasterEnded);
            EventBus.Unsubscribe<Incidents.IncidentEndedEvent>(OnIncidentEnded);
            EventBus.Unsubscribe<Seasons.SeasonChangedEvent>(OnSeason);
            EventBus.Unsubscribe<Seasons.FestivalEvent>(OnFestival);
            EventBus.Unsubscribe<Villages.VillageDiscoveredEvent>(OnVillage);
            EventBus.Unsubscribe<Legacy.MemorialStoryEvent>(OnMemorial);
            EventBus.Unsubscribe<Villages.VillageRuinsSeenEvent>(OnVillageLost);
            EventBus.Unsubscribe<Villages.VillageRaidSightedEvent>(OnVillageRaid);
            EventBus.Unsubscribe<Places.PlaceDiscoveredEvent>(OnPlace);
            EventBus.Unsubscribe<World.ZoneDiscoveredEvent>(OnZone);
        }

        // ---------------- Days ----------------

        void OnDayClosed(DailyReportClosedEvent e)
        {
            if (e.Report != null) Close(e.Report.Day, e.Report);
        }

        /// <summary>Composes the paper of <paramref name="day"/> from the stories noted on it (public for tests and debugging).</summary>
        public NewsPaper Close(int day, DailyReport report)
        {
            var stories = _desk.Where(a => a.Day <= day).ToList();
            _desk.RemoveAll(a => a.Day <= day);
            var paper = NewsRules.Compose(day, stories, report);
            _papers.RemoveAll(p => p.day == day);
            _papers.Add(paper);
            while (_papers.Count > NewsRules.PaperCapacity) _papers.RemoveAt(0);
            return paper;
        }

        void OnHour(HourPassedEvent e)
        {
            if (e.Hour >= NewsRules.DeliveryHour) DeliverDue(e.Day);
        }

        /// <summary>Hands over the latest paper of a day before <paramref name="today"/> if it is not yet (older ones quietly).</summary>
        void DeliverDue(int today)
        {
            NewsPaper due = null;
            foreach (var p in _papers)
            {
                if (p.delivered || p.day >= today) continue;
                p.delivered = true;
                due = p;
            }
            if (due != null) EventBus.Publish(new NewsPaperDeliveredEvent { Paper = due });
        }

        /// <summary>Debug: today's paper so far, out now.</summary>
        public NewsPaper PrintNow()
        {
            int day = _clock != null ? _clock.Day : 1;
            Services.TryGet<DailyLedger>(out var ledger);
            var paper = Close(day, ledger?.Current);
            paper.delivered = true;
            EventBus.Publish(new NewsPaperDeliveredEvent { Paper = paper });
            return paper;
        }

        /// <summary>Puts a story on the desk (public for tests). The least newsworthy go first once the desk is full.</summary>
        public void Note(NewsArticle a)
        {
            if (a == null || string.IsNullOrEmpty(a.headline)) return;
            a.hour = _clock != null ? Mathf.Round(_clock.TotalHours * 100f) / 100f : 0f;
            _desk.Add(a);
            if (_desk.Count <= NewsRules.DeskCapacity) return;
            int worst = 0;
            for (int i = 1; i < _desk.Count; i++)
                if (_desk[i].score < _desk[worst].score) worst = i;
            _desk.RemoveAt(worst);
        }

        // ---------------- What the guild knows ----------------

        static bool Seen(Unit u)
            => u != null && (!Services.TryGet<FogOfWar>(out var fog) || fog.IsVisible(u.Position));

        static bool Member(Adventurer a) => a != null && a.Data != null && a.IsGuildMember && !a.IsVisitor;

        string NameOf(string id)
        {
            var a = _roster != null ? _roster.Find(id) : null;
            return a != null && a.Data != null ? a.Data.name : null;
        }

        List<string> Names(IEnumerable<string> ids) => ids.Select(NameOf).Where(n => n != null).ToList();

        // ---------------- Moments (P4-129) ----------------

        void OnHighlight(HighlightEndedEvent e)
        {
            var h = e.Highlight;
            if (h == null) return;
            var people = h.Members.Where(m => m != null && m.Data != null).ToList();
            var ids = people.Select(m => m.Data.id).ToList();
            var names = people.Select(m => m.Data.name).ToList();
            string first = names.Count > 0 ? names[0] : null;
            string topic = h.Key;
            NewsArticle a;
            switch (h.Kind)
            {
                case HighlightKind.Dragon:
                case HighlightKind.Lord:
                case HighlightKind.Nemesis:
                case HighlightKind.Rare:
                case HighlightKind.Boss:
                {
                    if (ids.Count == 0) return;
                    string foe = h.FoeName ?? "강적";
                    string fight = HighlightRules.Title(h.Kind);
                    a = h.Outcome != null ? NewsRules.BigFoeSlain(names, foe, fight) : NewsRules.BigFight(names, foe, fight);
                    // The same foe's own news (the dragon slain, a region cleansed, a nemesis fallen) is the same story.
                    if (h.Kind == HighlightKind.Dragon) topic = "dragon";
                    else if (h.Kind == HighlightKind.Lord && h.Focus is Monster lord && lord.Def != null) topic = "lord:" + lord.Def.id;
                    else if (h.Kind == HighlightKind.Nemesis) topic = "nemesis:" + h.FocusId;
                    break;
                }
                case HighlightKind.LowHp:
                    if (first == null) return;
                    a = NewsRules.LowHp(first, h.FoeName);
                    topic = "close:" + ids[0];
                    break;
                case HighlightKind.LastStand:
                    if (first == null) return;
                    a = NewsRules.LastStand(first, h.FoeName);
                    topic = "close:" + ids[0];
                    break;
                case HighlightKind.Retreat:
                    if (first == null) return;
                    a = NewsRules.Retreat(first, h.FoeName);
                    topic = "close:" + ids[0];
                    break;
                case HighlightKind.Wipe:
                    if (ids.Count == 0) return;
                    a = NewsRules.Wipe(names);
                    break;
                case HighlightKind.Raid:
                {
                    if (ids.Count == 0) return;
                    _raidDirection.TryGetValue(RaidId(h.Key), out var dir);
                    a = NewsRules.RaidFought(h.FoeName ?? "적", dir, names);
                    break;
                }
                case HighlightKind.Horde:
                    a = NewsRules.Horde(h.FoeName ?? "몬스터", names);
                    break;
                case HighlightKind.DragonOverTown:
                    a = NewsRules.DragonOverTown(h.FoeName ?? "드래곤", names);
                    topic = "dragon-town";
                    break;
                case HighlightKind.GateBroken:
                    a = NewsRules.GateBroken();
                    break;
                default:
                    return;
            }
            Note(a.About(ids).At(h.Position).On(topic));
        }

        static int RaidId(string key)
            => key != null && key.StartsWith("raid:") && int.TryParse(key.Substring(5), out int id) ? id : -1;

        // ---------------- Members ----------------

        void OnDied(AdventurerDiedEvent e)
        {
            var a = e.Adventurer;
            if (a == null || a.Data == null || a.IsVisitor) return;
            string killer = Seen(e.Killer) ? e.Killer.DisplayName : null;
            Note(NewsRules.Death(a.Data.name, killer).About(a.Data.id).At(a.Position).On("close:" + a.Data.id));
        }

        void OnJoined(AdventurerJoinedEvent e)
        {
            var a = e.Adventurer;
            if (a == null || a.Data == null) return;
            Note(NewsRules.Joined(a.Data.name, AdventurerData.ClassLabel(a.Data.cls)).About(a.Data.id));
        }

        void OnLeft(AdventurerLeftEvent e)
        {
            var a = e.Adventurer;
            if (a == null || a.Data == null) return;
            Note(NewsRules.Left(a.Data.name).About(a.Data.id).At(a.Position));
        }

        void OnLevelUp(AdventurerLevelUpEvent e)
        {
            if (!Member(e.Adventurer)) return;
            Note(NewsRules.LevelUp(e.Adventurer.Data.name, e.Level).About(e.Adventurer.Data.id));
        }

        void OnBreak(MentalBreakEvent e)
        {
            if (e.Stage != MentalBreakEvent.Phase.Began || !Member(e.Adventurer) || e.Kind == BreakKind.Quit) return;
            var a = e.Adventurer;
            Note(NewsRules.Break(a.Data.name, BreakRules.Label(e.Kind), e.Other?.Data?.name)
                .About(a.Data.id, e.Other?.Data?.id).At(a.Position).On("break:" + a.Data.id));
        }

        void OnInspired(InspirationEvent e)
        {
            if (e.Ended || !Member(e.Adventurer)) return;
            var a = e.Adventurer;
            Note(NewsRules.Inspiration(a.Data.name, BreakRules.Label(e.Kind), BreakRules.Effect(e.Kind)).About(a.Data.id).On("inspired:" + a.Data.id));
        }

        static string Pair(string a, string b) => string.CompareOrdinal(a, b) < 0 ? $"pair:{a}|{b}" : $"pair:{b}|{a}";

        /// <summary>Two members who became close friends or sworn enemies (the relationship window's "친함"/"적대").</summary>
        void OnRelation(RelationChangedEvent e)
        {
            if (!Member(e.From) || !Member(e.To)) return;
            const int Close = 40;
            NewsArticle a = null;
            if (e.Old < Close && e.New >= Close) a = NewsRules.Friends(e.From.Data.name, e.To.Data.name);
            else if (e.Old > -Close && e.New <= -Close) a = NewsRules.Enemies(e.From.Data.name, e.To.Data.name);
            if (a != null) Note(a.About(e.From.Data.id, e.To.Data.id).On(Pair(e.From.Data.id, e.To.Data.id)));
        }

        void OnTownTalk(Social.TownTalkEvent e)
        {
            if (string.IsNullOrEmpty(e.A) || string.IsNullOrEmpty(e.B)) return;
            NewsArticle a = e.Outcome switch
            {
                Social.TalkOutcome.Fight => NewsRules.Quarrel(e.A, e.B),
                Social.TalkOutcome.Reconciled => NewsRules.MakeUp(e.A, e.B),
                _ => e.Kind == Social.TownTalkKind.Comfort && e.Outcome == Social.TalkOutcome.Warm ? NewsRules.Comfort(e.A, e.B) : null,
            };
            if (a != null) Note(a.About(e.AId, e.BId).On(Pair(e.AId ?? e.A, e.BId ?? e.B)));
        }

        // ---------------- Quests and decisions ----------------

        void OnQuestDone(QuestCompletedEvent e)
        {
            if (e.Quest != null) Note(NewsRules.QuestDone(e.Quest.Title));
        }

        void OnQuestClosed(QuestClosedEvent e)
        {
            if (e.Quest != null && e.Status == QuestStatus.Failed) Note(NewsRules.QuestFailed(e.Quest.Title));
        }

        void OnDecision(Dilemmas.DilemmaResolvedEvent e)
        {
            if (e.Card == null) return;
            Note(NewsRules.Decision(e.Card.Title, e.Card.Label(e.Choice), e.Result).About(e.Card.Actors).On("dilemma:" + e.Card.Id));
        }

        void OnRankUp(Guild.GuildRankChangedEvent e)
            => Note(NewsRules.RankUp(Guild.GuildRankRules.Name(e.Rank), e.Grant).On("rank"));

        // ---------------- Town ----------------

        void OnRaidSighted(Town.RaidSightedEvent e) => _raidDirection[e.RaidId] = e.Direction;

        void OnRaidEnded(Town.RaidEndedEvent e)
        {
            // A raid the guild never saw stays unknown.
            if (!e.Sighted) return;
            string topic = "raid:" + e.RaidId;
            var fought = _desk.LastOrDefault(a => a.topic == topic);
            var defenders = fought != null ? Names(fought.people) : new List<string>();
            var art = NewsRules.Raid(e.Killed, e.Withdrawn > 0, defenders).On(topic);
            if (fought != null)
            {
                art.About(fought.people);
                if (fought.has_place) art.At(fought.Place);
            }
            Note(art);
            _raidDirection.Remove(e.RaidId);
        }

        void OnBuildingLost(Town.BuildingDestroyedEvent e)
        {
            if (e.Building != null) Note(NewsRules.BuildingLost(e.Building.DisplayName).At(e.Building.transform.position));
        }

        void OnMerchant(Town.MerchantArrivedEvent e) => Note(NewsRules.Merchant().On("merchant"));

        // ---------------- Big foes ----------------

        void OnDragonSlain(Dragons.DragonSlainEvent e)
        {
            string name = e.Def != null ? e.Def.displayName : "드래곤";
            var names = e.Participants?.ToList() ?? new List<string>();
            var ids = _roster != null ? _roster.Members.Where(m => m?.Data != null && names.Contains(m.Data.name)).Select(m => m.Data.id) : null;
            Note(NewsRules.DragonSlain(name, names).About(ids).At(e.Position).On("dragon"));
        }

        void OnDragonSighted(Dragons.DragonSightedEvent e)
        {
            if (e.Dragon == null) return;
            Note(NewsRules.DragonSighted(e.Dragon.DisplayName, e.First).At(e.Position).On("dragon-seen"));
        }

        void OnDragonLair(Dragons.DragonLairFoundEvent e) => Note(NewsRules.DragonLair(e.HoardValue).At(e.Position).On("dragon-lair"));

        void OnDragonHoard(Dragons.DragonHoardRaidedEvent e)
        {
            string name = e.By?.Data?.name ?? "길드원";
            Note(NewsRules.DragonHoard(name, e.Value, e.Woke).About(e.By?.Data?.id).At(e.Position).On("dragon-hoard"));
        }

        void OnDragonPlundered(Dragons.DragonPlunderedEvent e) => Note(NewsRules.DragonPlundered(e.Gold).At(e.Position).On("dragon-town"));

        void OnRegionCleansed(Ecosystem.RegionCleansedEvent e)
        {
            if (e.Lord == null) return;
            var names = e.Participants?.ToList() ?? new List<string>();
            var ids = _roster != null ? _roster.Members.Where(m => m?.Data != null && names.Contains(m.Data.name)).Select(m => m.Data.id) : null;
            Note(NewsRules.LordSlain(e.Lord.displayName, RegionLords.RegionName(e.Region), e.Fallen, e.Total, names)
                .About(ids).At(e.Position).On("lord:" + e.Lord.id));
        }

        void OnNemesisDeed(Nemeses.NemesisDeedEvent e)
        {
            var n = e.Nemesis;
            if (n == null) return;
            Note(NewsRules.NemesisDeed(e.OldName ?? n.Name, n.Name, e.Deed.victim, e.Deed.kind == Nemeses.DeedKind.Killed, e.IsNew)
                .At(n.LastSeen).On("nemesis:" + n.UnitId));
        }

        /// <summary>The memorial wall's stories (2026-11-15): a hero, an anniversary, an heirloom.</summary>
        void OnMemorial(Legacy.MemorialStoryEvent e)
        {
            var art = e.Kind switch
            {
                Legacy.MemorialStory.Hero => NewsRules.Hero(e.Name, e.Detail),
                Legacy.MemorialStory.Anniversary => NewsRules.Anniversary(e.Name, e.Years, e.Count),
                _ => NewsRules.Heirloom(e.Other, e.Name, e.Detail),
            };
            Note(art.About(e.Ids).On($"memorial:{e.Kind}:{e.RecordId}"));
        }

        void OnNemesisFell(Nemeses.NemesisFellEvent e)
        {
            var n = e.Nemesis;
            if (n == null) return;
            Note(NewsRules.NemesisFell(n.Name, e.Slayer?.Data?.name).About(e.Slayer?.Data?.id).At(n.LastSeen).On("nemesis:" + n.UnitId));
        }

        // ---------------- The land ----------------

        void OnDisaster(Disasters.DisasterSightedEvent e)
        {
            var d = e.Disaster;
            if (d != null) Note(NewsRules.Disaster(d.Name, QuestBoard.DescribeArea(d.WorldOrigin)).At(d.WorldOrigin).On("disaster:" + d.Id));
        }

        void OnDisasterEnded(Disasters.DisasterEndedEvent e)
        {
            var d = e.Disaster;
            if (d == null || !e.Known || !e.DealtWith) return;
            Note(NewsRules.DisasterOver(d.Name, d.NameEul).At(d.WorldOrigin).On("disaster:" + d.Id));
        }

        void OnIncidentEnded(Incidents.IncidentEndedEvent e)
        {
            var i = e.Incident;
            // A nemesis's fall is told as its own (P4-109).
            if (i == null || !e.Known || i.Kind == Incidents.IncidentKind.Nemesis || i.Outcome != Incidents.IncidentOutcome.Resolved) return;
            if (Incidents.IncidentRules.EndedText(i) is { } text)
                Note(NewsRules.Incident(Incidents.IncidentRules.Tag(i.Kind), text).At(i.RumourCentre).On("incident:" + i.Id));
        }

        void OnSeason(Seasons.SeasonChangedEvent e)
            => Note(NewsRules.Season(Seasons.SeasonRules.Name(e.Season), Seasons.SeasonRules.Changes(e.Season)).On("season"));

        void OnFestival(Seasons.FestivalEvent e) => Note(NewsRules.Festival().On("festival"));

        void OnVillage(Villages.VillageDiscoveredEvent e)
        {
            var v = e.Village;
            if (v != null) Note(NewsRules.Village(v.DisplayName, RegionLords.RegionName(v.Region)).At(v.WorldPosition).On("village:" + v.Id));
        }

        void OnVillageLost(Villages.VillageRuinsSeenEvent e)
        {
            var v = e.Village;
            if (v != null) Note(NewsRules.VillageLost(v.DisplayName).At(v.WorldPosition).On("village:" + v.Id));
        }

        void OnVillageRaid(Villages.VillageRaidSightedEvent e)
        {
            var v = e.Village;
            if (v != null) Note(NewsRules.VillageRaid(v.DisplayName, e.Species != null ? e.Species.displayName : "몬스터", e.Raiders).At(v.WorldPosition).On("village:" + v.Id));
        }

        void OnPlace(Places.PlaceDiscoveredEvent e)
        {
            var p = e.Place;
            if (p != null) Note(NewsRules.Discovery(p.Name(), Places.PlaceRules.Describe(p.Kind)).At(p.WorldPosition));
        }

        void OnZone(World.ZoneDiscoveredEvent e)
        {
            var z = e.Zone;
            if (z != null) Note(NewsRules.Discovery(z.Name, World.ZoneDiscovery.Describe(z)).At(World.GridMap.CellToWorld(z.Heart)));
        }

        // ---------------- Save ----------------

        [Serializable]
        class SaveData
        {
            public List<NewsArticle> desk = new();
            public List<NewsPaper> papers = new();
        }

        public string SaveKey => "news";

        public string Capture() => JsonUtility.ToJson(new SaveData { desk = _desk.ToList(), papers = _papers.ToList() });

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            _desk.Clear();
            _papers.Clear();
            _raidDirection.Clear();
            if (s == null) return;
            if (s.desk != null) _desk.AddRange(s.desk.Where(a => a != null));
            if (s.papers != null) _papers.AddRange(s.papers.Where(p => p != null));
        }

        /// <summary>For DebugCommands: the desk and the latest paper.</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder($"자동 열기 {(AutoOpen ? "켬" : "끔")} · 모은 기사 {_desk.Count} · 소식지 {_papers.Count}\n");
            foreach (var a in _desk.OrderByDescending(s => s.score).Take(12))
                sb.Append($"  [{a.kind} {a.score:0}] {a.headline} ({a.topic})\n");
            var p = Latest;
            if (p != null)
            {
                sb.Append($"{p.day}일차 소식지{(p.delivered ? "" : " (배달 전)")}:\n");
                foreach (var a in p.articles) sb.Append($"  [{NewsRules.Tag(a.kind)} {a.score:0}] {a.headline} — {a.body}\n");
            }
            return sb.ToString();
        }
    }
}
