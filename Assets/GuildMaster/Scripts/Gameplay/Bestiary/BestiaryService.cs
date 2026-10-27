using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using GuildMaster.Ecosystem;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Bestiary
{
    /// <summary>The guild's research of a kind reached a new level (P4-107): 2 shows its weakness, 3 is mastery.</summary>
    public struct ResearchLevelEvent { public MonsterDefinition Species; public int Level; }

    /// <summary>
    /// The guild's bestiary (P4-107, H4, user decisions 2026-10-27; numbers in <see cref="BestiaryRules"/>). Builds on what the
    /// guild has seen (<see cref="GuildIntel"/>: a kind it never saw is nowhere here): research points per kind for every hour
    /// one is in sight, every one a member kills, every skill of it seen (once an hour a kind), every piece of its loot bought.
    /// Its levels decide what the bestiary window shows, how dangerous the kind's quests look to the members
    /// (<see cref="RiskFactor"/>), and — with each kind's weakness and strength by class, which hold whether the guild knows
    /// them or not — how hard a hit lands (<see cref="Unit.DamageFactor"/>). Saved as "bestiary".
    /// </summary>
    public class BestiaryService : MonoBehaviour, ISaveable
    {
        GuildIntel _intel;
        EcosystemManager _eco;
        FogOfWar _fog;
        GameClock _clock;
        readonly Dictionary<MonsterDefinition, int> _points = new();
        /// <summary>The game hour a kind's skill last counted (at most once an hour).</summary>
        readonly Dictionary<MonsterDefinition, int> _skillHour = new();
        /// <summary>The level last told, so a level-up is told once.</summary>
        readonly Dictionary<MonsterDefinition, int> _told = new();

        public void Init(GuildIntel intel, EcosystemManager eco, FogOfWar fog, GameClock clock)
        {
            _intel = intel;
            _eco = eco;
            _fog = fog;
            _clock = clock;
            Unit.DamageFactor = DamageFactor;
            EventBus.Subscribe<UnitDiedEvent>(OnDied);
            EventBus.Subscribe<SkillUsedEvent>(OnSkill);
            EventBus.Subscribe<TradeEvent>(OnTrade);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        void OnDestroy()
        {
            if (Unit.DamageFactor == (Func<Unit, Unit, float>)DamageFactor) Unit.DamageFactor = null;
            EventBus.Unsubscribe<UnitDiedEvent>(OnDied);
            EventBus.Unsubscribe<SkillUsedEvent>(OnSkill);
            EventBus.Unsubscribe<TradeEvent>(OnTrade);
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
        }

        // ---------------- Where it stands ----------------

        public int Points(MonsterDefinition s) => s != null && _points.TryGetValue(s, out int p) ? p : 0;

        public int Level(MonsterDefinition s) => BestiaryRules.Level(Points(s), _intel != null && _intel.IsKnown(s));

        /// <summary>The kinds the guild has seen, in the ecosystem's order.</summary>
        public IEnumerable<MonsterDefinition> Seen => _eco != null && _intel != null ? _eco.Species.Where(_intel.IsKnown) : Enumerable.Empty<MonsterDefinition>();

        /// <summary>How much of a quest's risk is left for the kind it is about (1 with no bestiary, e.g. pure tests).</summary>
        public static float RiskFactor(MonsterDefinition species)
            => species != null && Services.TryGet<BestiaryService>(out var b) ? BestiaryRules.RiskFactor(b.Level(species)) : 1f;

        /// <summary>The kind's research level for anyone asking (0 with no bestiary).</summary>
        public static int LevelOf(MonsterDefinition species) => species != null && Services.TryGet<BestiaryService>(out var b) ? b.Level(species) : 0;

        // ---------------- A hit ----------------

        /// <summary>
        /// A member's hit on a kind: more if it is weak to the member's class, less if strong against it, and more again once the
        /// guild has mastered the kind; a mastered kind's hit on a member lands a little lighter.
        /// </summary>
        float DamageFactor(Unit source, Unit target)
        {
            if (source is Adventurer a && target is Monster m && m.Def != null && a.Data != null)
            {
                float f = BestiaryRules.ClassFactor(m.Def.id, a.Data.cls);
                if (a.IsGuildMember && !a.IsVisitor && Level(m.Def) >= BestiaryRules.MaxLevel) f *= BestiaryRules.MasteryDamage;
                return f;
            }
            if (source is Monster from && target is Adventurer to && from.Def != null && to.IsGuildMember && !to.IsVisitor
                && Level(from.Def) >= BestiaryRules.MaxLevel)
                return BestiaryRules.MasteryTaken;
            return 1f;
        }

        // ---------------- Research ----------------

        /// <summary>Research points for a kind the guild has seen (never for one it has not: information hiding).</summary>
        public void Add(MonsterDefinition s, int points)
        {
            if (s == null || points <= 0 || _intel == null || !_intel.IsKnown(s)) return;
            _points[s] = Points(s) + points;
            Tell(s);
        }

        void Tell(MonsterDefinition s)
        {
            int level = Level(s);
            if (_told.TryGetValue(s, out int told) && told >= level) return;
            _told[s] = level;
            if (level >= 2) EventBus.Publish(new ResearchLevelEvent { Species = s, Level = level });
        }

        /// <summary>A member's kill: one more studied up close (a boss, a lord, the dragon much more).</summary>
        void OnDied(UnitDiedEvent e)
        {
            if (e.Victim is not Monster m || m.Def == null || e.Killer is not Adventurer a || !a.IsGuildMember) return;
            Add(m.Def, m.IsBoss || m.Def.isRegionLord || m.Def.isDragon ? BestiaryRules.BossKill : BestiaryRules.Kill);
        }

        /// <summary>A skill seen used (at most once an hour a kind).</summary>
        void OnSkill(SkillUsedEvent e)
        {
            if (e.User is not Monster m || m.Def == null || _fog == null || !_fog.IsVisibleRaw(m.Position)) return;
            int hour = Mathf.FloorToInt(_clock != null ? _clock.TotalHours : 0f);
            if (_skillHour.TryGetValue(m.Def, out int last) && last == hour) return;
            _skillHour[m.Def] = hour;
            Add(m.Def, BestiaryRules.SkillSeen);
        }

        /// <summary>Loot the guild buys: a point for each piece, to every kind that drops it.</summary>
        void OnTrade(TradeEvent e)
        {
            if (!e.GuildBought || e.Count <= 0 || _eco == null) return;
            foreach (var s in _eco.Species)
                if (s.loot.Any(l => l.item != null && l.item.id == e.ItemId)) Add(s, BestiaryRules.LootBought * e.Count);
        }

        /// <summary>Every kind in sight this hour.</summary>
        void OnHour(HourPassedEvent e)
        {
            if (_fog == null) return;
            var seen = new HashSet<MonsterDefinition>();
            foreach (var u in UnitRegistry.All)
                if (u is Monster m && m.IsAlive && m.Def != null && _fog.IsVisibleRaw(m.Position)) seen.Add(m.Def);
            foreach (var s in seen) Add(s, BestiaryRules.SeenHour);
        }

        // ---------------- Save (P5-4) ----------------

        [Serializable]
        class SaveData
        {
            public List<string> species = new();
            public List<int> points = new();
        }

        public string SaveKey => "bestiary";

        public string Capture()
        {
            var s = new SaveData();
            foreach (var kv in _points.OrderBy(kv => kv.Key.id, StringComparer.Ordinal))
            {
                s.species.Add(kv.Key.id);
                s.points.Add(kv.Value);
            }
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            _points.Clear();
            _told.Clear();
            for (int i = 0; i < s.species.Count && i < s.points.Count; i++)
            {
                var def = _eco?.Species.FirstOrDefault(d => d.id == s.species[i]);
                if (def == null) continue;
                _points[def] = s.points[i];
                _told[def] = BestiaryRules.Level(s.points[i], true); // known already: not told again
            }
        }
    }
}
