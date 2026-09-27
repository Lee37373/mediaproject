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
    /// <summary>
    /// What death leaves behind (F-6). Keeps a record of every member who fell, follows what they dropped
    /// (the relics: brought home by a member, taken by a monster, or rotted away), lets the guild master hold a
    /// memorial and send someone to fetch the relics, and decides the last stands that let a member survive a
    /// lethal blow at 1 HP — followed by a wound that keeps them from quests until it heals or is treated.
    /// The numbers live in <see cref="LegacyRules"/>, the wording in <see cref="LegacyTexts"/>; this class is the
    /// glue to the game world. Deaths stay permanent.
    /// </summary>
    public class GuildLegacy : IDisposable
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
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Unsubscribe<GroundItemTakenEvent>(OnTaken);
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            _fallen.Clear();
        }

        /// <summary>Everyone who fell, oldest first.</summary>
        public IReadOnlyList<FallenRecord> Fallen => _fallen;

        /// <summary>Debug: a fixed roll for the last stand (0 = always holds on, 1 = never). Null = random.</summary>
        public float? ForcedRoll { get; set; }

        public FallenRecord Find(int id) => _fallen.Find(r => r.Id == id);

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
            _fallen.Add(rec);
            while (_fallen.Count > LegacyRules.MaxRecords) _fallen.RemoveAt(0);

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

        void OnHour(HourPassedEvent e) => CheckRelicsInSight();

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
            return CareResult.Success(LegacyTexts.MemorialDone(rec.Name, cost, members, friends));
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
            a.Say(LegacyTexts.LastStandLine(GameRandom.Shared));
            _center?.Post(NotificationKind.Warning, LegacyTexts.LastStandTitle, LegacyTexts.LastStandNotice(d.name, by, LegacyRules.WoundedHours),
                "laststand:" + d.id, NotificationLink.Management(d.id));
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
