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
    /// (user decision) and applies the level's effects — guild hall: EXP/hour, roster size, visitor
    /// level; town hall: max durability, daily tax; training ground: idle members gain EXP every
    /// hour. Blacksmith/apothecary levels are read by crafting (P4-2).
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
                case BuildingType.GuildHall:
                    _roster.Capacity = lv.rosterCapacity;
                    break;
                case BuildingType.TownHall:
                    b.SetMaxHp(lv.maxHp, heal: false);
                    _treasury.DailyTax = lv.dailyTax;
                    break;
                // Training ground: read every hour below. Blacksmith/apothecary: read by crafting.
            }
        }

        /// <summary>Visitors arrive this many levels higher (guild hall reputation).</summary>
        public int VisitorLevelBonus => Current(BuildingType.GuildHall)?.visitorLevelBonus ?? 0;

        /// <summary>Training ground: members idling in town gain a little EXP every game hour.</summary>
        void OnHour(HourPassedEvent e)
        {
            // A fallen training ground trains nobody until it is rebuilt (P4-76, as crafting stops, P4-74).
            var ground = _town.Get(BuildingType.TrainingGround);
            if (ground == null || ground.IsDestroyed) return;
            int exp = (int)(Current(BuildingType.TrainingGround)?.idleExpPerHour ?? 0f);
            if (exp <= 0) return;
            foreach (var a in _roster.Members.ToList())
                if (a != null && a.IsAlive && !a.IsVisitor && a.CurrentQuest == null && _town.IsInTown(a.Position))
                    a.GainExp(exp);
        }
    }
}
