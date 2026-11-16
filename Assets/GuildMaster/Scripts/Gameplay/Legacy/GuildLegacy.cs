using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using GuildMaster.Items;
using GuildMaster.Notifications;
using GuildMaster.Units;
using GuildMaster.Welfare;
using UnityEngine;

namespace GuildMaster.Legacy
{
    /// <summary>A member held on at 1 HP after a lethal blow (P4-129: the spectator director shows it).</summary>
    public struct LastStandEvent { public Adventurer Adventurer; public Unit Killer; }

    /// <summary>What the memorial wall tells the newspaper (2026-11-15).</summary>
    public enum MemorialStory { Hero, Anniversary, Heirloom }

    /// <summary>A story of the memorial wall: a hero remembered, an anniversary kept, a relic handed on.</summary>
    public struct MemorialStoryEvent
    {
        public MemorialStory Kind;
        public int RecordId;
        /// <summary>The fallen; <see cref="Other"/> = the heir.</summary>
        public string Name, Other;
        /// <summary>The hero's reason, or the heirloom.</summary>
        public string Detail;
        public int Years, Count;
        public string[] Ids;
    }

    /// <summary>
    /// What death leaves behind (F-6). Keeps a record of every member who fell, follows what they dropped
    /// (the relics: brought home by a member, taken by a monster, or rotted away), lets the guild master hold a
    /// memorial and send someone to fetch the relics, and decides the last stands that let a member survive a
    /// lethal blow at 1 HP — followed by a wound that keeps them from quests until it heals or is treated.
    /// The numbers live in <see cref="LegacyRules"/>, the wording in <see cref="LegacyTexts"/>; this class is the
    /// glue to the game world. Deaths stay permanent.
    /// </summary>
    public class GuildLegacy : IDisposable, ISaveable, ISaveLink
    {
        readonly GameClock _clock;
        readonly GuildRoster _roster;
        readonly GuildTreasury _treasury;
        readonly NotificationCenter _center;
        readonly Func<float> _roll;
        readonly List<FallenRecord> _fallen = new();
        int _nextId = 1;

        public GuildLegacy(GameClock clock, GuildRoster roster, GuildTreasury treasury, NotificationCenter center, Func<float> roll = null)
        {
            _clock = clock;
            _roster = roster;
            _treasury = treasury;
            _center = center;
            _roll = roll ?? (() => GameRandom.Shared.Value);
            EventBus.Subscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Subscribe<GroundItemTakenEvent>(OnTaken);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<AdventurerSpeechEvent>(OnSpeech);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Unsubscribe<GroundItemTakenEvent>(OnTaken);
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            EventBus.Unsubscribe<AdventurerSpeechEvent>(OnSpeech);
            _fallen.Clear();
            _lastSaid.Clear();
        }

        /// <summary>Everyone who fell, oldest first.</summary>
        public IReadOnlyList<FallenRecord> Fallen => _fallen;

        /// <summary>Debug: a fixed roll for the last stand (0 = always holds on, 1 = never). Null = random.</summary>
        public float? ForcedRoll { get; set; }

        public FallenRecord Find(int id) => _fallen.Find(r => r.Id == id);

        // ---------------- Save (P5-4) ----------------

        [Serializable]
        class SavedRelic
        {
            public string item;
            public string name;
            public int count;
            public Vector2 position;
            public RelicState state;
            public string note;
            /// <summary>The item on the ground while it lies there (its place in the ground list), else -1.</summary>
            public int ground = -1;
            public float dropped_hour;
        }

        [Serializable]
        class SavedRecord
        {
            public int id;
            public string adventurer, name, class_label, values, killer;
            public int level, kills, quests, day;
            public float died_at;
            public Vector2 position;
            public List<SavedRelic> relics = new();
            public List<string> friends = new();
            public List<string> relation_ids = new();
            public List<int> relations = new();
            public bool memorial;
            public float memorial_hour;
            public bool recovery_rewarded;
            // The memorial wall (2026-11-15); missing in older saves, filled in on load.
            public int cls = -1;
            public List<string> titles = new();
            public string epitaph, hero, last_words, heir_id, heir_name, heir_item;
            public List<string> rem_ids = new(), rem_names = new(), rem_lines = new();
            public int anniversaries, last_anniversary;
        }

        [Serializable]
        class SaveData
        {
            public int next_id;
            public List<SavedRecord> fallen = new();
        }

        SaveData _restoring;

        public string SaveKey => "legacy";

        public string Capture()
        {
            Services.TryGet<Items.GroundItemService>(out var ground);
            var s = new SaveData { next_id = _nextId };
            foreach (var r in _fallen)
            {
                var sr = new SavedRecord
                {
                    id = r.Id, adventurer = r.AdventurerId, name = r.Name, class_label = r.ClassLabel, values = r.Values, killer = r.KillerName,
                    level = r.Level, kills = r.Kills, quests = r.QuestsDone, day = r.Day, died_at = r.DiedAtHour, position = r.Position,
                    memorial = r.MemorialHeld, memorial_hour = r.MemorialHour, recovery_rewarded = r.RecoveryRewarded,
                    cls = (int)r.Cls, epitaph = r.Epitaph, hero = r.HeroReason, last_words = r.LastWords,
                    heir_id = r.HeirId, heir_name = r.HeirName, heir_item = r.HeirItem,
                    anniversaries = r.Anniversaries, last_anniversary = r.LastAnniversaryDay,
                };
                sr.titles.AddRange(r.Titles);
                foreach (var m in r.Remembrances) { sr.rem_ids.Add(m.Id); sr.rem_names.Add(m.Name); sr.rem_lines.Add(m.Line); }
                foreach (var relic in r.Relics)
                    sr.relics.Add(new SavedRelic
                    {
                        item = relic.ItemId, name = relic.Name, count = relic.Count, position = relic.Position, state = relic.State, note = relic.Note,
                        ground = relic.Ground != null && ground != null ? ground.IndexOf(relic.Ground) : -1, dropped_hour = relic.DroppedAtHour,
                    });
                sr.friends.AddRange(r.FriendIds);
                var ids = new List<string>(r.Relations.Keys);
                ids.Sort(StringComparer.Ordinal);
                foreach (var id in ids) { sr.relation_ids.Add(id); sr.relations.Add(r.Relations[id]); }
                s.fallen.Add(sr);
            }
            return JsonUtility.ToJson(s);
        }

        /// <summary>The wall of the fallen; members sent for relics find the record by its id.</summary>
        public void Restore(string json, SaveContext ctx)
        {
            _restoring = JsonUtility.FromJson<SaveData>(json);
            _nextId = _restoring.next_id;
            _fallen.Clear();
            foreach (var sr in _restoring.fallen)
            {
                var r = new FallenRecord
                {
                    Id = sr.id, AdventurerId = sr.adventurer, Name = sr.name, ClassLabel = sr.class_label, Values = sr.values,
                    KillerName = sr.killer, Level = sr.level, Kills = sr.kills, QuestsDone = sr.quests, Day = sr.day,
                    DiedAtHour = sr.died_at, Position = sr.position, MemorialHeld = sr.memorial, MemorialHour = sr.memorial_hour,
                    RecoveryRewarded = sr.recovery_rewarded,
                    HeroReason = sr.hero, HeirId = sr.heir_id, HeirName = sr.heir_name, HeirItem = sr.heir_item,
                    Anniversaries = sr.anniversaries, LastAnniversaryDay = sr.last_anniversary,
                };
                r.Cls = sr.cls >= 0 ? (AdventurerClass)sr.cls : ClassFromLabel(sr.class_label);
                if (sr.titles != null) r.Titles.AddRange(sr.titles);
                if (sr.rem_ids != null)
                    for (int i = 0; i < sr.rem_ids.Count && i < sr.rem_names.Count && i < sr.rem_lines.Count; i++)
                        r.Remembrances.Add(new Remembrance { Id = sr.rem_ids[i], Name = sr.rem_names[i], Line = sr.rem_lines[i] });
                r.Epitaph = string.IsNullOrEmpty(sr.epitaph) ? MemorialRules.Epitaph(r) : sr.epitaph;
                r.LastWords = string.IsNullOrEmpty(sr.last_words) ? MemorialRules.LastWords(r) : sr.last_words;
                foreach (var relic in sr.relics)
                    r.Relics.Add(new RelicItem
                    {
                        ItemId = relic.item, Name = relic.name, Count = relic.count, Position = relic.position, State = relic.state,
                        Note = relic.note, DroppedAtHour = relic.dropped_hour,
                    });
                r.FriendIds.AddRange(sr.friends);
                for (int i = 0; i < sr.relation_ids.Count && i < sr.relations.Count; i++) r.Relations[sr.relation_ids[i]] = sr.relations[i];
                _fallen.Add(r);
                ctx.Register(RelicRetrievalState.RecordKey(r.Id), r);
            }
        }

        /// <summary>Relics still lying there are the ground items again.</summary>
        public void Link(SaveContext ctx)
        {
            for (int i = 0; i < _fallen.Count && i < _restoring.fallen.Count; i++)
            {
                var saved = _restoring.fallen[i].relics;
                for (int k = 0; k < _fallen[i].Relics.Count && k < saved.Count; k++)
                {
                    if (saved[k].ground < 0) continue;
                    var relic = _fallen[i].Relics[k];
                    relic.Ground = ctx.Find<Items.GroundItem>(Items.GroundItemService.SaveId(saved[k].ground));
                    if (relic.Ground != null && relic.State == RelicState.Lying) relic.Ground.Kept = true;
                }
            }
            _restoring = null;
        }

        /// <summary>Adds a record without a death event (tests).</summary>
        internal FallenRecord Add(FallenRecord rec)
        {
            rec.Id = _nextId++;
            _fallen.Add(rec);
            return rec;
        }

        static string RelicKey(FallenRecord r) => "relic:" + r.Id;

        void Remember(Adventurer a, int importance, string text, string key = null)
        {
            if (a == null || a.Data == null) return;
            AdventurerMemory.Add(a.Data.memories, _clock.TotalHours, importance, text, key);
        }

        static void Relieve(Adventurer a, float amount)
        {
            if (a == null || a.Data == null || amount <= 0f) return;
            a.Data.stress = Mathf.Max(0f, a.Data.stress - amount);
        }

        // ---------------- Death and the relics ----------------

        void OnDied(AdventurerDiedEvent e)
        {
            var a = e.Adventurer;
            if (a == null || a.Data == null || a.IsVisitor) return;
            var d = a.Data;
            var rec = new FallenRecord
            {
                Id = _nextId++,
                AdventurerId = d.id,
                Name = d.name,
                ClassLabel = AdventurerData.ClassLabel(d.cls),
                Cls = d.cls,
                Values = d.values,
                Level = d.level,
                Kills = d.kills,
                QuestsDone = d.questsCompleted,
                KillerName = e.Killer != null ? e.Killer.DisplayName : null,
                Day = _clock.Day,
                DiedAtHour = _clock.TotalHours,
                Position = a.Position,
            };
            // The dead are already off the roster: these are the ones who are left.
            foreach (var m in _roster.Members)
            {
                if (m == null || !m.IsAlive || m == a) continue;
                int relation = Relationships.Get(m, a);
                rec.Relations[m.Data.id] = relation;
                if (relation >= StressRules.FriendRelation) rec.FriendIds.Add(m.Data.id);
            }

            if (a.DeathDrops != null)
                foreach (var g in a.DeathDrops)
                {
                    if (g == null || g.Item == null) continue;
                    g.Kept = true; // lies its full time, not swept off by the ground's item cap
                    rec.Relics.Add(new RelicItem
                    {
                        ItemId = g.Item.id,
                        Name = string.IsNullOrEmpty(g.Item.displayName) ? g.Item.id : g.Item.displayName,
                        Count = g.Count,
                        Position = g.Position,
                        State = RelicState.Lying,
                        Ground = g,
                        DroppedAtHour = g.DroppedAtHour,
                    });
                }
            if (d.titles != null) rec.Titles.AddRange(d.titles);
            WriteTheStone(rec, d.id);
            _fallen.Add(rec);
            while (_fallen.Count > LegacyRules.MaxRecords) _fallen.RemoveAt(0);
            HonourIfHero(rec);

            if (rec.HasRelics)
                _center?.Post(NotificationKind.Warning, LegacyTexts.RelicTitle,
                    LegacyTexts.RelicNotice(rec.Name, rec.Relics.Count, rec.HoursLeft(_clock.TotalHours)), RelicKey(rec), NotificationLink.Memorial(rec.Id));
        }

        /// <summary>A stack left the ground: a member of the guild brought a relic home, or something else took it.</summary>
        void OnTaken(GroundItemTakenEvent e)
        {
            if (e.Item == null) return;
            foreach (var rec in _fallen)
            {
                foreach (var relic in rec.Relics)
                {
                    if (relic.State != RelicState.Lying || relic.ItemId != e.Item.id) continue;
                    if ((relic.Position - e.Position).sqrMagnitude > 0.0004f) continue;
                    var by = e.Taker as Adventurer;
                    if (!e.Absorbed && by != null && by.IsGuildMember && !by.IsVisitor) Recovered(rec, relic, by);
                    // Taken where nobody of the guild can see (P4-69): the guild does not know yet, let
                    // alone who took it. It stays "lying" until someone looks (CheckRelicsInSight).
                    else if (!SeenByGuild(e.Position)) relic.Ground = null;
                    else Lost(rec, relic, e.Absorbed ? "슬라임에게 삼켜졌다" : e.Taker != null ? $"{e.Taker.DisplayName}에게 넘어갔다" : "누군가 가져갔다", e.Taker != null ? e.Taker.DisplayName : null);
                    return;
                }
            }
        }

        void Recovered(FallenRecord rec, RelicItem relic, Adventurer by)
        {
            rec.MarkRecovered(relic, by.Data.name);
            Remember(by, 2, LegacyTexts.MemoryRelic(rec.Name), "relic:" + rec.Id);
            if (!rec.RecoveryRewarded)
            {
                rec.RecoveryRewarded = true;
                foreach (var m in _roster.Members.ToList())
                {
                    if (m == null || !m.IsAlive || !rec.FriendIds.Contains(m.Data.id)) continue;
                    Relieve(m, LegacyRules.RelicRecoveredFriendRelief);
                    Remember(m, 2, LegacyTexts.MemoryRelicHome(rec.Name), "relic-home:" + rec.Id);
                }
            }
            if (!rec.HasLying) _center?.Resolve(RelicKey(rec));
            _center?.Post(NotificationKind.Success, LegacyTexts.RecoveredTitle,
                LegacyTexts.Recovered(by.Data.name, rec.Name, LegacyTexts.ItemLabel(relic.Name, relic.Count)), "relic-back:" + rec.Id, NotificationLink.Memorial(rec.Id));
        }

        void Lost(FallenRecord rec, RelicItem relic, string why, string taker, bool rotted = false)
        {
            rec.MarkLost(relic, why);
            if (!rec.HasLying) _center?.Resolve(RelicKey(rec));
            string item = LegacyTexts.ItemLabel(relic.Name, relic.Count);
            string text = taker != null ? LegacyTexts.LostTo(rec.Name, item, taker)
                : rotted ? LegacyTexts.LostRotted(rec.Name, item)
                : LegacyTexts.LostVanished(rec.Name, item);
            _center?.Post(NotificationKind.Info, LegacyTexts.LostTitle, text, "relic-lost:" + rec.Id, NotificationLink.Memorial(rec.Id));
        }

        /// <summary>The spot is in the guild's sight right now (town watch, members, the guild master).</summary>
        static bool SeenByGuild(Vector2 p) => !Services.TryGet<Fog.FogOfWar>(out var fog) || fog.IsVisibleRaw(p);

        void OnHour(HourPassedEvent e)
        {
            CheckRelicsInSight();
            if (e.Hour == MemorialRules.AnniversaryHour) KeepAnniversaries(e.Day);
        }

        /// <summary>
        /// Relics no longer on the ground become known as lost only when the guild could know it
        /// (P4-69): once they would have rotted anyway (<see cref="GroundItemService.LifetimeHours"/>, a
        /// rule everyone knows), or once someone looks at the spot and finds nothing. Before, a relic a
        /// monster picked up far out in the fog was announced at once, with the taker's name.
        /// Also called by a retriever that reaches the spot (<see cref="RelicRetrievalState"/>).
        /// </summary>
        internal void CheckRelicsInSight()
        {
            if (!Services.TryGet<GroundItemService>(out var ground)) return;
            float now = _clock.TotalHours;
            foreach (var rec in _fallen)
            {
                if (!rec.HasLying) continue;
                foreach (var relic in rec.Relics)
                {
                    if (relic.State != RelicState.Lying) continue;
                    var g = relic.Ground;
                    if (g != null && ground.Contains(g) && Mathf.Approximately(g.DroppedAtHour, relic.DroppedAtHour)) continue;
                    bool rotted = now - relic.DroppedAtHour >= GroundItemService.LifetimeHours;
                    if (!rotted && !SeenByGuild(relic.Position)) continue; // gone, but nobody has looked since
                    Lost(rec, relic, rotted ? "시간이 지나 사라졌다" : "현장에서 사라졌다", null, rotted);
                }
            }
        }

        // ---------------- Retrieval ----------------

        public bool IsRetrieving(FallenRecord rec)
            => rec != null && _roster.Members.Any(m => m != null && m.IsAlive && m.Fsm.Current == m.RetrievalState && m.RetrievalState.Record == rec);

        /// <summary>The member best suited to fetch the relics: friends of the dead first, then the nearest. Null when none may go.</summary>
        public Adventurer FindRetriever(FallenRecord rec)
        {
            if (rec == null) return null;
            Adventurer best = null;
            float bestScore = float.MinValue;
            foreach (var m in _roster.Members)
            {
                if (!CanFetch(m)) continue;
                float score = -Vector2.Distance(m.Position, rec.Position) + (rec.FriendIds.Contains(m.Data.id) ? 1000f : 0f);
                if (score <= bestScore) continue;
                bestScore = score;
                best = m;
            }
            return best;
        }

        static bool CanFetch(Adventurer m)
            => m != null && m.IsAlive && m.IsGuildMember && !m.IsVisitor && m.CurrentQuest == null
               && m.IsAtLeisureInTown && m.HpRatio >= LegacyRules.RetrieverMinHp
               && GuildWelfare.AnyQuestRefusal(m) == null;

        public CareCheck CanSendRetriever(FallenRecord rec)
        {
            if (rec == null || !rec.HasLying) return CareCheck.No("되찾을 유품이 현장에 남아 있지 않습니다.");
            if (IsRetrieving(rec)) return CareCheck.No("이미 누군가 유품을 찾으러 갔습니다.");
            if (FindRetriever(rec) == null)
                return CareCheck.No($"보낼 수 있는 길드원이 없습니다. (마을에서 쉬는 체력 {LegacyRules.RetrieverMinHp * 100:0}% 이상, 휴가·중상이 아닌 길드원이 필요합니다.)");
            return CareCheck.Yes;
        }

        /// <summary>Sends the best-placed member out alone to bring the relics home.</summary>
        public CareResult SendRetriever(FallenRecord rec)
        {
            var check = CanSendRetriever(rec);
            if (!check.Ok) return CareResult.Fail(check.Reason);
            var who = FindRetriever(rec);
            var target = rec.FirstLying();
            if (who == null || target == null) return CareResult.Fail("보낼 수 있는 길드원이 없습니다.");
            who.RetrievalState.Begin(rec, target.Position);
            who.ChangeState(who.RetrievalState);
            return CareResult.Success(LegacyTexts.SentRetriever(who.Data.name, rec.Name));
        }

        // ---------------- Memorial ----------------

        public int MemorialCost(FallenRecord rec) => LegacyRules.MemorialCost(rec != null ? rec.Level : 1);

        public CareCheck CanHoldMemorial(FallenRecord rec)
        {
            if (rec == null) return CareCheck.No("추모할 길드원이 없습니다.");
            if (rec.MemorialHeld) return CareCheck.No("이미 추모식을 열었습니다.");
            if (_roster.Members.Count == 0) return CareCheck.No("추모식에 참석할 길드원이 없습니다.");
            int cost = MemorialCost(rec);
            return _treasury.Gold >= cost ? CareCheck.Yes : CareCheck.No($"길드 자금이 부족합니다. (필요 {cost}G)");
        }

        /// <summary>A memorial for the fallen: the minds of those who cared about them ease, the more the closer they were.</summary>
        public CareResult HoldMemorial(FallenRecord rec)
        {
            var check = CanHoldMemorial(rec);
            if (!check.Ok) return CareResult.Fail(check.Reason);
            int cost = MemorialCost(rec);
            if (!_treasury.TrySpendGold(cost, GoldReason.Memorial)) return CareResult.Fail($"길드 자금이 부족합니다. (필요 {cost}G)");
            rec.MemorialHeld = true;
            rec.MemorialHour = _clock.TotalHours;
            int members = 0, friends = 0;
            foreach (var m in _roster.Members.ToList())
            {
                if (m == null || !m.IsAlive || m.Data == null) continue;
                // Those who joined after the death, or never cared about the dead, have nothing to ease.
                if (!rec.Relations.TryGetValue(m.Data.id, out int relation)) continue;
                float relief = LegacyRules.MemorialRelief(relation);
                if (relief <= 0f) continue;
                members++;
                bool friend = rec.FriendIds.Contains(m.Data.id);
                if (friend) friends++;
                Relieve(m, relief);
                Remember(m, friend ? 3 : 2, friend ? LegacyTexts.MemoryMemorialFriend(rec.Name) : LegacyTexts.MemoryMemorial(rec.Name), "memorial:" + rec.Id);
            }
            SayRemembrances(rec);
            return CareResult.Success(LegacyTexts.MemorialDone(rec.Name, cost, members, friends));
        }

        // ---------------- The memorial wall (2026-11-15, MemorialRules) ----------------

        /// <summary>What each member said last and when (game hours): words heard just before a fall become the last words.</summary>
        readonly Dictionary<string, (string text, float hour)> _lastSaid = new();

        void OnSpeech(AdventurerSpeechEvent e)
        {
            var a = e.Adventurer;
            if (a == null || a.Data == null || a.IsVisitor || string.IsNullOrWhiteSpace(e.Text)) return;
            // Only what is said in a fight can be last words (not a chat in town an hour before a fall).
            bool fighting = (a.CurrentTarget != null && a.CurrentTarget.IsAlive) || Time.time - a.LastAttackedTime < 10f;
            if (!fighting) return;
            _lastSaid[a.Data.id] = (e.Text.Trim().Trim('"'), _clock.TotalHours);
        }

        static AdventurerClass ClassFromLabel(string label)
        {
            foreach (AdventurerClass c in Enum.GetValues(typeof(AdventurerClass)))
                if (AdventurerData.ClassLabel(c) == label) return c;
            return AdventurerClass.Warrior;
        }

        /// <summary>The stone's line, the last words, whether a hero, and the closest friends' words.</summary>
        void WriteTheStone(FallenRecord rec, string id)
        {
            rec.Epitaph = MemorialRules.Epitaph(rec);
            rec.HeroReason = MemorialRules.HeroReason(rec);
            rec.LastWords = _lastSaid.TryGetValue(id, out var said) && _clock.TotalHours - said.hour <= MemorialRules.LastWordsHours
                ? said.text : MemorialRules.LastWords(rec);
            _lastSaid.Remove(id);
            var friends = new List<KeyValuePair<string, int>>();
            foreach (var kv in rec.Relations) if (kv.Value >= Welfare.StressRules.FriendRelation) friends.Add(kv);
            friends.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
            for (int i = 0; i < friends.Count && rec.Remembrances.Count < MemorialRules.Remembrances; i++)
            {
                var m = _roster.Find(friends[i].Key);
                if (m == null || m.Data == null) continue;
                rec.Remembrances.Add(new Remembrance
                {
                    Id = m.Data.id, Name = m.Data.name,
                    Line = MemorialRules.Remembrance(rec.Name, rec.KillerName, friends[i].Value, rec.Id * 7 + i),
                });
            }
        }

        /// <summary>A hero's fall: fame for the guild, a notice, a story, and those who knew them carry on in their name.</summary>
        void HonourIfHero(FallenRecord rec)
        {
            if (!rec.IsHero) return;
            if (Services.TryGet<Guild.GuildRankService>(out var rank)) rank.AddFame(MemorialRules.HeroFame, $"영웅 {rec.Name}");
            _center?.Post(NotificationKind.Info, MemorialRules.HeroTitle, MemorialRules.HeroNotice(rec.Name, rec.HeroReason), "hero:" + rec.Id, NotificationLink.Memorial(rec.Id));
            foreach (var m in _roster.Members.ToList())
                if (m != null && m.IsAlive && m.Data != null && rec.Relations.TryGetValue(m.Data.id, out int r) && r >= 0)
                    ThoughtRecorder.Give(m, MemorialRules.HeroRemembered, "hero:" + rec.AdventurerId, rec.Name);
            EventBus.Publish(new MemorialStoryEvent { Kind = MemorialStory.Hero, RecordId = rec.Id, Name = rec.Name, Detail = rec.HeroReason, Ids = new[] { rec.AdventurerId } });
        }

        /// <summary>The friends whose words are on the stone say them aloud (a memorial, an anniversary).</summary>
        void SayRemembrances(FallenRecord rec, int seed = 0)
        {
            foreach (var w in rec.Remembrances)
            {
                var m = _roster.Find(w.Id);
                if (m == null || !m.IsAlive) continue;
                string line = seed == 0 ? w.Line
                    : MemorialRules.Remembrance(rec.Name, rec.KillerName, rec.Relations.TryGetValue(w.Id, out int r) ? r : 40, rec.Id * 7 + seed);
                EventBus.Publish(new AdventurerSpeechEvent { Adventurer = m, Text = line });
            }
        }

        /// <summary>A year (and every year) after they fell, those who were fond of them remember them at dusk.</summary>
        void KeepAnniversaries(int today)
        {
            foreach (var rec in _fallen)
            {
                if (rec.LastAnniversaryDay == today || !MemorialRules.IsAnniversary(rec.Day, today)) continue;
                rec.LastAnniversaryDay = today;
                rec.Anniversaries++;
                int years = MemorialRules.Years(rec.Day, today);
                var ids = new List<string> { rec.AdventurerId };
                foreach (var m in _roster.Members.ToList())
                {
                    if (m == null || !m.IsAlive || m.Data == null || m.IsVisitor) continue;
                    if (!rec.Relations.TryGetValue(m.Data.id, out int relation) || relation < 0) continue;
                    bool friend = rec.FriendIds.Contains(m.Data.id);
                    Relieve(m, friend ? MemorialRules.AnniversaryFriendRelief : MemorialRules.AnniversaryRelief);
                    ThoughtRecorder.Give(m, MemorialRules.Anniversary, "anniversary:" + rec.AdventurerId, rec.Name);
                    Remember(m, friend ? 2 : 1, $"{rec.Name}의 {years}주기를 함께 기렸다", "anniversary:" + rec.Id);
                    ids.Add(m.Data.id);
                }
                SayRemembrances(rec, today);
                _center?.Post(NotificationKind.Info, MemorialRules.AnniversaryTitle, MemorialRules.AnniversaryNotice(rec.Name, years, ids.Count - 1),
                    "anniversary:" + rec.Id, NotificationLink.Memorial(rec.Id));
                EventBus.Publish(new MemorialStoryEvent { Kind = MemorialStory.Anniversary, RecordId = rec.Id, Name = rec.Name, Years = years, Count = ids.Count - 1, Ids = ids.ToArray() });
            }
        }

        /// <summary>The relic an heir would carry: the first that came home.</summary>
        public static RelicItem HeirloomOf(FallenRecord rec)
        {
            if (rec == null) return null;
            foreach (var r in rec.Relics) if (r.State == RelicState.Recovered) return r;
            return null;
        }

        /// <summary>Who may take the relic on: friends of the fallen first, then the same class (<see cref="MemorialRules.RankHeirs{T}"/>).</summary>
        public List<Adventurer> HeirCandidates(FallenRecord rec)
        {
            if (rec == null || rec.HasHeir || HeirloomOf(rec) == null) return new List<Adventurer>();
            var alive = _roster.Members.Where(m => m != null && m.IsAlive && m.Data != null && m.IsGuildMember && !m.IsVisitor);
            return MemorialRules.RankHeirs(alive, m => rec.Relations.TryGetValue(m.Data.id, out int v) ? v : 0, m => m.Data.cls == rec.Cls);
        }

        public CareCheck CanBequeath(FallenRecord rec)
        {
            if (rec == null) return CareCheck.No("고인이 없습니다.");
            if (rec.HasHeir)
                return CareCheck.No($"{rec.HeirName}{MemoryRecorder.Josa(rec.HeirName, "이", "가")} 이미 {rec.Name}의 {rec.HeirItem}{MemoryRecorder.Josa(rec.HeirItem, "을", "를")} 물려받았습니다.");
            if (HeirloomOf(rec) == null) return CareCheck.No("되찾은 유품이 없습니다. 먼저 유품을 회수해야 물려줄 수 있습니다.");
            if (HeirCandidates(rec).Count == 0) return CareCheck.No("유품을 물려받을 길드원이 없습니다.");
            return CareCheck.Yes;
        }

        /// <summary>Hands the relic on: the heir carries it (a little stronger), holds the thought of it, and is known for it.</summary>
        public CareResult Bequeath(FallenRecord rec, Adventurer heir)
        {
            var check = CanBequeath(rec);
            if (!check.Ok) return CareResult.Fail(check.Reason);
            if (heir == null || !heir.IsAlive || heir.Data == null) return CareResult.Fail("물려받을 길드원이 없습니다.");
            string item = MemorialRules.Keepsake(rec.Cls);
            heir.Data.heirlooms ??= new List<string>();
            heir.Data.heirlooms.Add(MemorialRules.HeirloomName(rec.Name, item));
            string title = MemorialRules.HeirTitle(rec.Name);
            if (!heir.Data.titles.Contains(title)) heir.Data.titles.Add(title);
            heir.RecalculateStats();
            rec.HeirId = heir.Data.id;
            rec.HeirName = heir.Data.name;
            rec.HeirItem = item;
            ThoughtRecorder.Give(heir, MemorialRules.Heirloom, "heirloom:" + rec.AdventurerId, rec.Name);
            Remember(heir, 3, $"{rec.Name}의 {item}{MemoryRecorder.Josa(item, "을", "를")} 물려받았다", "heirloom:" + rec.Id);
            EventBus.Publish(new MemorialStoryEvent
            {
                Kind = MemorialStory.Heirloom, RecordId = rec.Id, Name = rec.Name, Other = heir.Data.name, Detail = item,
                Ids = new[] { heir.Data.id, rec.AdventurerId },
            });
            return CareResult.Success(MemorialRules.HeirloomGiven(heir.Data.name, rec.Name, item));
        }

        // ---------------- Last stand and the wound ----------------

        public bool IsWounded(Adventurer a) => a != null && a.Data != null && a.Data.woundedUntilHour > _clock.TotalHours;

        public float WoundedHoursLeft(Adventurer a) => IsWounded(a) ? a.Data.woundedUntilHour - _clock.TotalHours : 0f;

        /// <summary>Why this member takes no quest because of a wound; null = not wounded.</summary>
        public static string WoundedRefusal(Adventurer a)
            => Services.TryGet<GuildLegacy>(out var legacy) && legacy.IsWounded(a) ? LegacyTexts.WoundedRefusal : null;

        /// <summary>
        /// Called by an adventurer whose health just ran out: true = they hold on at 1 HP (the caller keeps them alive).
        /// Only guild members, at most once per <see cref="LegacyRules.LastStandCooldownHours"/> and never while still wounded.
        /// </summary>
        internal bool TryLastStand(Adventurer a, Unit killer)
        {
            if (a == null || a.Data == null || !a.IsGuildMember || a.IsVisitor) return false;
            var d = a.Data;
            float now = _clock.TotalHours;
            if (IsWounded(a) || !LegacyRules.CanLastStand(now, d.lastStandHour)) return false;
            if ((ForcedRoll ?? _roll()) >= LegacyRules.LastStandChance(d.level, d.values)) return false;

            d.lastStandHour = now;
            d.woundedUntilHour = now + LegacyRules.WoundedHours;
            d.stress = Mathf.Min(100f, d.stress + LegacyRules.LastStandStress);
            string by = killer != null ? killer.DisplayName : null;
            Remember(a, 3, LegacyTexts.MemoryLastStand(by), "last-stand");
            ThoughtRecorder.Give(a, ThoughtRules.LastStand);
            a.Say(LegacyTexts.LastStandLine(GameRandom.Shared));
            _center?.Post(NotificationKind.Warning, LegacyTexts.LastStandTitle, LegacyTexts.LastStandNotice(d.name, by, LegacyRules.WoundedHours),
                "laststand:" + d.id, NotificationLink.Management(d.id));
            EventBus.Publish(new LastStandEvent { Adventurer = a, Killer = killer });
            return true;
        }

        public int TreatCost(Adventurer a) => LegacyRules.TreatCost(a != null && a.Data != null ? a.Data.level : 1);

        public CareCheck CanTreat(Adventurer a)
        {
            if (a == null || a.Data == null || !a.IsAlive || a.IsVisitor || !a.IsGuildMember) return CareCheck.No("길드원이 아닙니다.");
            if (!IsWounded(a)) return CareCheck.No("중상을 입지 않았습니다.");
            int cost = TreatCost(a);
            return _treasury.Gold >= cost ? CareCheck.Yes : CareCheck.No($"길드 자금이 부족합니다. (필요 {cost}G)");
        }

        /// <summary>Pays for a healer: the wound is gone at once and the member is back on their feet.</summary>
        public CareResult Treat(Adventurer a)
        {
            var check = CanTreat(a);
            if (!check.Ok) return CareResult.Fail(check.Reason);
            int cost = TreatCost(a);
            if (!_treasury.TrySpendGold(cost, GoldReason.Medical)) return CareResult.Fail($"길드 자금이 부족합니다. (필요 {cost}G)");
            a.Data.woundedUntilHour = _clock.TotalHours;
            float target = a.Stats.MaxHp * LegacyRules.TreatHpRatio;
            if (a.Hp < target) a.Heal(target - a.Hp);
            Remember(a, 2, LegacyTexts.MemoryTreated(cost), "treated");
            return CareResult.Success(LegacyTexts.Treated(a.Data.name, cost, Mathf.RoundToInt(a.HpRatio * 100f)));
        }
    }
}
