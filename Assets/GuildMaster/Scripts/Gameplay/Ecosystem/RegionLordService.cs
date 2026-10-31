using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Ecosystem
{
    /// <summary>
    /// The guild learned that a region lord fell (P4-83): at once when the guild was there or saw it, else the next
    /// time it sees the lair. The region's outer land was cleansed when it died.
    /// </summary>
    public struct RegionCleansedEvent
    {
        public Region Region;
        public MonsterDefinition Lord;
        public Vector2 Position;
        /// <summary>Guild members who took part (and now carry its title), by name.</summary>
        public IReadOnlyList<string> Participants;
        /// <summary>Lords the guild knows fell, this one included, and how many there are.</summary>
        public int Fallen;
        public int Total;
    }

    /// <summary>Every region lord is down and the guild knows it (P4-83): the victory. Play goes on.</summary>
    public struct AllLordsSlainEvent { public int Day; public int Lords; }

    /// <summary>
    /// The four region lords (P4-83, user decisions 2026-10-17, rules in <see cref="RegionLords"/>): which lair is
    /// whose, who fell and when, the cleansed land (a cleansed region's outer sites are sealed: nothing is born
    /// there or moves in, <see cref="Nest.Sealed"/>), the titles of those who took part, and the victory.
    /// The lair itself and its lord live in the ecosystem like any nest (<see cref="EcosystemManager"/>).
    /// </summary>
    public class RegionLordService : IDisposable, ISaveable
    {
        public class LordInfo
        {
            public Region Region;
            public MonsterDefinition Def;
            public Nest Lair;
            public bool Slain;
            public float SlainHour;
            /// <summary>The guild knows it fell (saw it, or has seen the lair since).</summary>
            public bool Known;
            /// <summary>Who took part, by name (the title went to them).</summary>
            public readonly List<string> Participants = new();
            /// <summary>Members who hit it while it lived (ids): they took part wherever they stood when it fell.</summary>
            internal readonly HashSet<string> Hitters = new();
        }

        readonly EcosystemManager _eco;
        readonly WorldService _world;
        readonly GameClock _clock;
        readonly List<LordInfo> _lords = new();
        readonly HashSet<Region> _cleansed = new();

        public IReadOnlyList<LordInfo> Lords => _lords;
        /// <summary>All the lords are down and the guild knows it (shown once).</summary>
        public bool Victory { get; private set; }
        public int KnownFallen => _lords.Count(l => l.Known);

        public RegionLordService(EcosystemManager eco, WorldService world, GameClock clock)
        {
            _eco = eco;
            _world = world;
            _clock = clock;
            foreach (var region in RegionLords.OuterRegions)
                foreach (var n in eco.Nests)
                    if (n.IsLair && n.Lord != null && n.Lord.lordRegion == region)
                        _lords.Add(new LordInfo { Region = region, Def = n.Lord, Lair = n });
            EventBus.Subscribe<UnitDamagedEvent>(OnDamaged);
            EventBus.Subscribe<UnitDiedEvent>(OnDied);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<UnitDamagedEvent>(OnDamaged);
            EventBus.Unsubscribe<UnitDiedEvent>(OnDied);
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
        }

        public LordInfo Find(MonsterDefinition def) => _lords.FirstOrDefault(l => l.Def == def);
        public bool IsCleansed(Region r) => _cleansed.Contains(r);

        /// <summary>Cleansed land: a cleansed region's ground beyond the inner edge (P4-83; the inner hunting grounds stay).</summary>
        public bool IsSealedAt(Vector2 p)
        {
            if (_cleansed.Count == 0) return false;
            var cell = GridMap.WorldToCell(p);
            if (!_world.Grid.InBounds(cell)) return false;
            return RegionLords.IsSealed(_world.Map.RegionOf(cell.x, cell.y), Vector2.Distance(p, _world.TownCenterWorld), _cleansed, EcosystemManager.InnerEdge);
        }

        void OnDamaged(UnitDamagedEvent e)
        {
            if (e.Target is Monster { IsRegionLord: true } lord && e.Source is Adventurer a && a.Data != null)
                Find(lord.Def)?.Hitters.Add(a.Data.id);
        }

        void OnDied(UnitDiedEvent e)
        {
            if (e.Victim is not Monster { IsRegionLord: true } lord) return;
            Slay(Find(lord.Def), lord.Position, e.Killer);
        }

        /// <summary>The lord is dead: its region's outer land is sealed, those who fought it carry its title.</summary>
        internal void Slay(LordInfo info, Vector2 at, Unit killer)
        {
            if (info == null || info.Slain) return;
            info.Slain = true;
            info.SlainHour = _clock.TotalHours;
            _cleansed.Add(info.Region);
            Seal(info.Region);
            GiveTitles(info, at);
            bool observed = killer != null && killer.Faction == Faction.Guild
                            || Services.TryGet<FogOfWar>(out var fog) && fog.IsVisibleRaw(at);
            Debug.Log($"[Lords] {info.Def.displayName} fell (by {killer?.DisplayName ?? "?"}, observed {observed}); {RegionLords.RegionName(info.Region)} cleansed");
            if (observed) Announce(info, at);
        }

        void Seal(Region region)
        {
            foreach (var n in _eco.Nests)
                if (RegionLords.IsSealed(n.Region, Vector2.Distance(n.WorldPosition, _world.TownCenterWorld), _cleansed, EcosystemManager.InnerEdge))
                    n.Sealed = true;
        }

        /// <summary>Members alive who hit it, or stood by when it fell (healers, the ones it was hitting).</summary>
        void GiveTitles(LordInfo info, Vector2 at)
        {
            if (!Services.TryGet<GuildRoster>(out var roster)) return;
            string title = RegionLords.Title(info.Def.displayName);
            foreach (var a in roster.Members)
            {
                if (a == null || !a.IsAlive || a.Data == null) continue;
                bool hit = info.Hitters.Contains(a.Data.id);
                bool near = Vector2.Distance(a.Position, at) <= RegionLords.ParticipantRadius;
                if (!hit && !near) continue;
                if (!a.Data.titles.Contains(title)) a.Data.titles.Add(title);
                AdventurerMemory.Add(a.Data.memories, _clock.TotalHours, 3, $"동료들과 함께 {info.Def.displayName}{MemoryRecorder.Josa(info.Def.displayName, "을", "를")} 쓰러뜨렸다", "lord:" + info.Def.id);
                info.Participants.Add(a.Data.name);
            }
            info.Hitters.Clear();
        }

        /// <summary>A lord that fell out of sight is learned of when the guild next sees its lair.</summary>
        void OnHour(HourPassedEvent e)
        {
            if (!Services.TryGet<FogOfWar>(out var fog)) return;
            foreach (var l in _lords)
                if (l.Slain && !l.Known && l.Lair != null && fog.IsVisibleRaw(l.Lair.Cell)) Announce(l, l.Lair.WorldPosition);
        }

        void Announce(LordInfo info, Vector2 at)
        {
            info.Known = true;
            EventBus.Publish(new RegionCleansedEvent
            {
                Region = info.Region, Lord = info.Def, Position = at, Participants = info.Participants, Fallen = KnownFallen, Total = _lords.Count,
            });
            if (Victory || _lords.Count == 0 || _lords.Any(l => !l.Known)) return;
            Victory = true;
            EventBus.Publish(new AllLordsSlainEvent { Day = _clock.Day, Lords = _lords.Count });
        }

        // ---------------- Save (P5-4) ----------------

        [Serializable]
        class SavedLord
        {
            public string lord;
            public bool slain;
            public float slain_hour;
            public bool known;
            public List<string> participants = new();
            public List<string> hitters = new();
        }

        [Serializable]
        class SaveData
        {
            public List<SavedLord> lords = new();
            public bool victory;
        }

        public string SaveKey => "lords";

        public string Capture()
        {
            var s = new SaveData { victory = Victory };
            foreach (var l in _lords)
            {
                var sl = new SavedLord { lord = l.Def.id, slain = l.Slain, slain_hour = l.SlainHour, known = l.Known };
                sl.participants.AddRange(l.Participants);
                sl.hitters.AddRange(l.Hitters.OrderBy(h => h, StringComparer.Ordinal));
                s.lords.Add(sl);
            }
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            Victory = s.victory;
            _cleansed.Clear();
            foreach (var sl in s.lords)
            {
                var l = _lords.FirstOrDefault(x => x.Def.id == sl.lord);
                if (l == null) { ctx.Warnings.Add("lords: no lair for " + sl.lord); continue; }
                l.Slain = sl.slain;
                l.SlainHour = sl.slain_hour;
                l.Known = sl.known;
                l.Participants.Clear();
                l.Participants.AddRange(sl.participants);
                l.Hitters.Clear();
                foreach (var h in sl.hitters) l.Hitters.Add(h);
                if (l.Slain) _cleansed.Add(l.Region);
            }
            foreach (var region in _cleansed) Seal(region);
        }
    }
}
