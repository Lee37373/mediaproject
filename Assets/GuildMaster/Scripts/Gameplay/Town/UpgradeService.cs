using System;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;

namespace GuildMaster.Town
{
    /// <summary>
    /// Building upgrades (P4-1): pays gold and stored materials, raises the level instantly
    /// (user decision) and applies the level's effects — guild hall: visitor level; town hall: max
    /// durability, daily tax; training ground: idle members gain EXP every hour, and newcomers train
    /// there (P4-99). Blacksmith/apothecary levels are read by crafting (P4-2).
    /// </summary>
    public class UpgradeService : IDisposable
    {
        readonly TownService _town;
        readonly GuildTreasury _treasury;
        readonly GameDatabase _db;
        readonly GuildRoster _roster;

        public UpgradeService(TownService town, GuildTreasury treasury, GameDatabase db, GuildRoster roster)
        {
            _town = town;
            _treasury = treasury;
            _db = db;
            _roster = roster;
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        public void Dispose() => EventBus.Unsubscribe<HourPassedEvent>(OnHour);

        public BuildingDefinition Definition(Building b) => b != null ? _db.Building(b.Type) : null;

        /// <summary>Current level data of a building type (Lv.1 when it has no definition).</summary>
        public BuildingLevel Current(BuildingType type)
        {
            var b = _town.Get(type);
            var def = _db.Building(type);
            return b != null && def != null ? def.Level(b.Level) : null;
        }

        /// <summary>
        /// The next level and what it costs this building: a facility's second and third of a kind pay more (P4-87,
        /// <see cref="FacilityRules.CopyCost"/>). Null at the top level or without a definition.
        /// </summary>
        public BuildingLevel NextLevel(Building b)
        {
            var def = Definition(b);
            if (def == null || b.Level >= def.MaxLevel) return null;
            int earlier = FacilityRules.IsFacility(b.Type) ? _town.EarlierOfKind(b) : 0;
            return ConstructionService.Priced(def.Level(b.Level + 1), earlier);
        }

        /// <summary>Why the building cannot be upgraded right now, or null if it can.</summary>
        public string CannotUpgradeReason(Building b)
        {
            var def = Definition(b);
            if (def == null) return "업그레이드할 수 없는 건물입니다.";
            if (b.IsDestroyed) return "무너진 건물은 업그레이드할 수 없습니다.";
            if (b.Level >= def.MaxLevel) return "최고 레벨입니다.";
            var next = NextLevel(b);
            if (_treasury.Gold < next.goldCost) return $"골드가 부족합니다 ({_treasury.Gold}/{next.goldCost}G).";
            foreach (var c in next.itemCosts)
                if (_treasury.GetStock(c.itemId) < c.count)
                    return $"{_db.Item(c.itemId)?.displayName ?? c.itemId}이(가) 부족합니다 ({_treasury.GetStock(c.itemId)}/{c.count}).";
            return null;
        }

        public bool TryUpgrade(Building b, out string error)
        {
            error = CannotUpgradeReason(b);
            if (error != null) return false;
            var next = NextLevel(b);
            if (!_treasury.TrySpendGold(next.goldCost, GoldReason.Upgrade)) { error = "골드가 부족합니다."; return false; }
            foreach (var c in next.itemCosts) _treasury.TryRemoveStock(c.itemId, c.count);
            b.SetLevel(b.Level + 1);
            Apply(b, next);
            return true;
        }

        /// <summary>Makes a level's effects take hold immediately.</summary>
        void Apply(Building b, BuildingLevel lv)
        {
            // A facility's level is read by FacilityService (BuildingLevelChangedEvent); it also grows sturdier (P4-87).
            if (FacilityRules.IsFacility(b.Type) && lv.maxHp > 0f) b.SetMaxHp(lv.maxHp, heal: false);
            switch (b.Type)
            {
                case BuildingType.TownHall:
                    b.SetMaxHp(lv.maxHp, heal: false);
                    _treasury.DailyTax = lv.dailyTax;
                    break;
                // Training ground: read every hour below. Blacksmith/apothecary: read by crafting.
            }
        }

        /// <summary>Visitors arrive this many levels higher (guild hall reputation).</summary>
        public int VisitorLevelBonus => Current(BuildingType.GuildHall)?.visitorLevelBonus ?? 0;

        /// <summary>
        /// What the standing training ground's level gives an hour before the day and the catch-up (P4-99), 0 when it
        /// teaches nothing: level 1, or fallen (P4-76, as crafting stops, P4-74).
        /// </summary>
        public float TrainingBaseExp
        {
            get
            {
                var ground = _town.Get(BuildingType.TrainingGround);
                return ground == null || ground.IsDestroyed ? 0f : Current(BuildingType.TrainingGround)?.idleExpPerHour ?? 0f;
            }
        }

        /// <summary>The training ground teaches now: newcomers train there instead of going out (P4-99).</summary>
        public bool Trains => TrainingBaseExp > 0f;

        /// <summary>
        /// Training ground: members idling in town gain EXP every game hour — more as the days go by and the further a
        /// member is behind the guild's best (P4-99, user decision 2026-10-21, <see cref="NewcomerRules.TrainingExp"/>).
        /// </summary>
        void OnHour(HourPassedEvent e)
        {
            float baseExp = TrainingBaseExp;
            if (baseExp <= 0f) return;
            var members = _roster.Members.Where(a => a != null && a.IsAlive && !a.IsVisitor).ToList();
            int top = members.Count == 0 ? 1 : members.Max(a => a.Data.level);
            float days = Ecosystem.EcosystemManager.Days;
            foreach (var a in members)
                if (a.CurrentQuest == null && _town.IsInTown(a.Position))
                    a.GainExp(NewcomerRules.TrainingExp(baseExp, days, top - a.Data.level));
        }

        /// <summary>What a member of this level gains an hour at the training ground now (shown in the building window).</summary>
        public int TrainingExpFor(int level)
        {
            int top = 1;
            foreach (var a in _roster.Members) if (a != null && a.IsAlive && !a.IsVisitor) top = Math.Max(top, a.Data.level);
            return NewcomerRules.TrainingExp(TrainingBaseExp, Ecosystem.EcosystemManager.Days, top - level);
        }
    }
}
