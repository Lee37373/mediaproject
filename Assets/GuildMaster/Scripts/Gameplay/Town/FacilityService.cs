using System;
using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Units;

namespace GuildMaster.Town
{
    /// <summary>
    /// The guild's facilities at work (P4-87, rules in <see cref="FacilityRules"/>): reads the standing ones (a fallen one
    /// stops until it is repaired, user decision 2026-10-19), gives every member the stat scale, marks what a member hits,
    /// and speeds up stress relief in town while a dormitory stands (P4-92). Members' stats are worked out again whenever
    /// one is built, raised, falls, is repaired or comes down.
    /// </summary>
    public class FacilityService : IDisposable
    {
        readonly TownService _town;
        readonly GuildRoster _roster;

        int _seenVersion = -1;
        bool _dirty = true;
        StatScale _scale = FacilityRules.Unbuilt;
        float _poison, _armorBreak, _slow;
        float _stressRelief = 1f;
        readonly Dictionary<FacilityEffect, List<int>> _levels = new();

        public FacilityService(TownService town, GuildRoster roster)
        {
            _town = town;
            _roster = roster;
            EventBus.Subscribe<BuildingBuiltEvent>(OnBuilt);
            EventBus.Subscribe<BuildingDemolishedEvent>(OnDemolished);
            EventBus.Subscribe<BuildingLevelChangedEvent>(OnLevel);
            EventBus.Subscribe<BuildingDestroyedEvent>(OnDestroyed);
            EventBus.Subscribe<BuildingRepairedEvent>(OnRepaired);
            EventBus.Subscribe<UnitDamagedEvent>(OnDamaged);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<BuildingBuiltEvent>(OnBuilt);
            EventBus.Unsubscribe<BuildingDemolishedEvent>(OnDemolished);
            EventBus.Unsubscribe<BuildingLevelChangedEvent>(OnLevel);
            EventBus.Unsubscribe<BuildingDestroyedEvent>(OnDestroyed);
            EventBus.Unsubscribe<BuildingRepairedEvent>(OnRepaired);
            EventBus.Unsubscribe<UnitDamagedEvent>(OnDamaged);
        }

        // ---------------- What stands ----------------

        /// <summary>
        /// Its game is still running. A plain service outlives the play session in the editor (only a boot clears
        /// <see cref="Services"/>), so pure tests run afterwards must not read a finished game's scale.
        /// </summary>
        public bool IsLive => _town != null;

        /// <summary>Every member's attack, defence and health as they stand now.</summary>
        public StatScale MemberScale { get { Refresh(); return _scale; } }
        /// <summary>Share of each hit burnt as poison, armour broken, speed taken off (0 = no such building standing).</summary>
        public float Poison { get { Refresh(); return _poison; } }
        public float ArmorBreak { get { Refresh(); return _armorBreak; } }
        public float Slow { get { Refresh(); return _slow; } }
        /// <summary>How much faster members in town shed stress (1 with no dormitory standing).</summary>
        public float StressRelief { get { Refresh(); return _stressRelief; } }

        /// <summary>
        /// <see cref="StressRelief"/> of the running game, or 1 with none (pure tests, before boot) — times the harvest
        /// festival's on its day (P4-111b), the one other thing that eases stress in town.
        /// </summary>
        public static float TownStressRelief
            => (Services.TryGet<FacilityService>(out var f) && f.IsLive ? f.StressRelief : 1f) * Seasons.SeasonService.FestivalRelief;

        /// <summary>Members get the guild's scale; visitors, candidates and refugees only the weakened base.</summary>
        public StatScale ScaleFor(Adventurer a)
            => a != null && a.IsGuildMember && !a.IsVisitor ? MemberScale : FacilityRules.Unbuilt;

        /// <summary>
        /// How much this one counts (1, ½, ¼ by its rank among the standing ones of its kind, strongest first; 0 when fallen
        /// or past the third).
        /// </summary>
        public float WeightOf(Building b)
        {
            if (b == null || b.IsDestroyed || !FacilityRules.IsFacility(b.Type)) return 0f;
            int stronger = 0;
            foreach (var o in _town.Buildings)
            {
                if (o == b || o.Type != b.Type || o.IsDestroyed) continue;
                // Higher level first; the same level by build order.
                if (o.Level > b.Level || o.Level == b.Level && _town.EarlierOfKind(o) < _town.EarlierOfKind(b)) stronger++;
            }
            return stronger < FacilityRules.CopyWeights.Length ? FacilityRules.CopyWeights[stronger] : 0f;
        }

        void Refresh()
        {
            if (!_dirty && _seenVersion == _town.Version) return;
            _dirty = false;
            _seenVersion = _town.Version;
            foreach (var list in _levels.Values) list.Clear();
            foreach (var b in _town.Buildings)
            {
                var effect = FacilityRules.EffectOf(b.Type);
                if (effect == FacilityEffect.None || b.IsDestroyed) continue;
                if (!_levels.TryGetValue(effect, out var list)) _levels[effect] = list = new List<int>();
                list.Add(b.Level);
            }
            _scale = FacilityRules.MemberScale(Levels(FacilityEffect.Attack), Levels(FacilityEffect.Defense), Levels(FacilityEffect.MaxHp));
            _poison = FacilityRules.AfflictionOf(Levels(FacilityEffect.Poison));
            _armorBreak = FacilityRules.AfflictionOf(Levels(FacilityEffect.ArmorBreak));
            _slow = FacilityRules.AfflictionOf(Levels(FacilityEffect.Slow));
            _stressRelief = StressReliefOf(Levels(FacilityEffect.StressRelief));
        }

        /// <summary>One dormitory at most (<see cref="FacilityRules.MaxCopies"/>): the highest level standing.</summary>
        static float StressReliefOf(List<int> levels)
        {
            int best = 0;
            foreach (int l in levels) best = Math.Max(best, l);
            return FacilityRules.StressReliefAt(best);
        }

        List<int> Levels(FacilityEffect e)
        {
            if (!_levels.TryGetValue(e, out var list)) _levels[e] = list = new List<int>();
            return list;
        }

        // ---------------- Changes ----------------

        void OnBuilt(BuildingBuiltEvent e) => Changed(e.Building);
        void OnDemolished(BuildingDemolishedEvent e) => Changed(e.Building, gone: true);
        void OnLevel(BuildingLevelChangedEvent e) => Changed(e.Building);
        void OnDestroyed(BuildingDestroyedEvent e) => Changed(e.Building);
        void OnRepaired(BuildingRepairedEvent e) => Changed(e.Building);

        /// <summary>
        /// A facility changed: work the members' stats out again (health keeps its share). A demolished building is still in
        /// the town list when the event comes, so it is left out by hand.
        /// </summary>
        void Changed(Building b, bool gone = false)
        {
            if (b == null || !FacilityRules.IsFacility(b.Type)) return;
            _dirty = true;
            if (gone) RefreshWithout(b);
            foreach (var a in _roster.Members)
                if (a != null && a.IsAlive) a.RecalculateStats();
        }

        void RefreshWithout(Building gone)
        {
            Refresh();
            var effect = FacilityRules.EffectOf(gone.Type);
            if (gone.IsDestroyed) return; // rubble counted for nothing already
            var list = Levels(effect);
            list.Remove(gone.Level);
            _scale = FacilityRules.MemberScale(Levels(FacilityEffect.Attack), Levels(FacilityEffect.Defense), Levels(FacilityEffect.MaxHp));
            _poison = FacilityRules.AfflictionOf(Levels(FacilityEffect.Poison));
            _armorBreak = FacilityRules.AfflictionOf(Levels(FacilityEffect.ArmorBreak));
            _slow = FacilityRules.AfflictionOf(Levels(FacilityEffect.Slow));
            _stressRelief = StressReliefOf(Levels(FacilityEffect.StressRelief));
            // The town list drops it right after the event (Version goes up): worked out once more then.
        }

        // ---------------- Marks on what members hit ----------------

        /// <summary>A member's blow (not a poison step) on a monster still alive leaves the marks of the standing buildings.</summary>
        void OnDamaged(UnitDamagedEvent e)
        {
            if (e.OverTime || e.Amount <= 0f || e.Source is not Adventurer a || !a.IsGuildMember || a.IsVisitor) return;
            var target = e.Target;
            if (target == null || !target.IsAlive || target.Faction != Faction.Monster) return;
            Refresh();
            if (_poison > 0f) target.Afflictions.Poison(e.Amount * _poison, FacilityRules.AfflictionSeconds, a);
            if (_armorBreak > 0f) target.Afflictions.BreakArmor(_armorBreak, FacilityRules.AfflictionSeconds);
            if (_slow > 0f) target.Afflictions.Chill(_slow, FacilityRules.AfflictionSeconds);
        }
    }
}
