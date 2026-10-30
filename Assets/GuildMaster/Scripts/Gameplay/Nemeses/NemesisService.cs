using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Ecosystem;
using GuildMaster.Fog;
using GuildMaster.Incidents;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Nemeses
{
    /// <summary>
    /// A monster did something to a member that the guild saw (P4-109): it became a nemesis (<see cref="IsNew"/>), or one did
    /// more. <see cref="OldName"/> is what it was called a moment ago.
    /// </summary>
    public struct NemesisDeedEvent
    {
        public Nemesis Nemesis;
        public NemesisDeed Deed;
        public bool IsNew;
        public string OldName;
        public int LevelBefore;
    }

    /// <summary>A nemesis fell (P4-109). <see cref="Slayer"/>: the member who brought it down, null if it fell to anything else.</summary>
    public struct NemesisFellEvent { public Nemesis Nemesis; public Adventurer Slayer; }

    /// <summary>
    /// Nemesis monsters (P4-109, G4 — user decisions 2026-10-30; numbers and names in <see cref="NemesisRules"/>). A monster
    /// the guild sees kill a member, or drive one off near death, is named for it ("레오 잡이 고블린 그릭") and grows a level or
    /// two; it goes on as its kind always did. The friends of the one it killed, or the one it drove off, hold a grudge (a
    /// thought) that makes a quest against it worth more to them (<see cref="Avenges"/>); once it is out of the fight the town
    /// puts a bounty on it (<see cref="IncidentService.StartNemesisBounty"/>). When it falls the grudges are settled — a
    /// title and a glad thought for the member who brought it down. Saved as "nemeses".
    /// </summary>
    public class NemesisService : MonoBehaviour, ISaveable, ISaveLink
    {
        /// <summary>Tests: off keeps the monsters a test fights as they are (a member's death would rename and grow them).</summary>
        public static bool Enabled = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Enabled = true;

        const float TickInterval = 0.5f;

        GameClock _clock;
        FogOfWar _fog;
        GuildRoster _roster;
        GuildIntel _intel;
        IncidentService _incidents;
        readonly List<Nemesis> _living = new();
        readonly List<Nemesis> _fallen = new();
        float _timer;

        /// <summary>A deed seen this frame, recorded at its end (after the death has been told under the monster's old name).</summary>
        struct Witnessed
        {
            public Monster Monster;
            public string UnitId, VictimId, Victim;
            public DeedKind Kind;
            public List<Adventurer> Grudging;
        }

        readonly List<Witnessed> _pending = new();

        public IReadOnlyList<Nemesis> Living => _living;
        public IReadOnlyList<Nemesis> FallenOnes => _fallen;

        float Now => _clock != null ? _clock.TotalHours : 0f;

        public void Init(GameClock clock, FogOfWar fog, GuildRoster roster, GuildIntel intel, IncidentService incidents)
        {
            _clock = clock;
            _fog = fog;
            _roster = roster;
            _intel = intel;
            _incidents = incidents;
            EventBus.Subscribe<AdventurerDiedEvent>(OnMemberDied);
            EventBus.Subscribe<AdventurerRetreatedEvent>(OnRetreated);
            EventBus.Subscribe<UnitDiedEvent>(OnUnitDied);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<AdventurerDiedEvent>(OnMemberDied);
            EventBus.Unsubscribe<AdventurerRetreatedEvent>(OnRetreated);
            EventBus.Unsubscribe<UnitDiedEvent>(OnUnitDied);
        }

        // ---------------- Reading ----------------

        /// <summary>The nemesis this monster is, or null.</summary>
        public Nemesis Of(Monster m)
        {
            if (m == null || !m.IsNemesis) return null;
            foreach (var n in _living) if (n.Monster == m) return n;
            return null;
        }

        /// <summary>A living nemesis by its monster's id.</summary>
        public Nemesis Find(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return null;
            foreach (var n in _living) if (n.UnitId == unitId) return n;
            return null;
        }

        /// <summary>The nemesis of this kind, living first, then the fallen (latest first).</summary>
        public IEnumerable<Nemesis> OfKind(Data.MonsterDefinition species)
            => _living.Where(n => n.Species == species).Concat(Enumerable.Reverse(_fallen).Where(n => n.Species == species));

        /// <summary>
        /// This member holds a grudge against a nemesis this quest goes after: the bounty on it, the hunt on the den it lives in,
        /// or the dragon's or a lord's hunt when that is the nemesis.
        /// </summary>
        public static bool Avenges(Adventurer a, Quest q)
        {
            if (a?.Data == null || q == null || q.Type != QuestType.Hunt || a.Data.thoughts.Count == 0
                || !Services.TryGet<NemesisService>(out var service)) return false;
            foreach (var t in a.Data.thoughts)
            {
                if (t.id != ThoughtRules.Grudge.Id) continue;
                var m = service.Find(t.about)?.Monster;
                if (m == null || !m.IsAlive) continue;
                if (q.Incident != null ? q.Incident.IsTarget(m)
                    : m.IsDragon || m.IsRegionLord ? q.Species == m.Def
                    : q.TargetNest != null && q.TargetNest == m.Home) return true;
            }
            return false;
        }

        // ---------------- Deeds ----------------

        void OnMemberDied(AdventurerDiedEvent e)
        {
            var dead = e.Adventurer;
            if (dead == null || dead.Data == null) return;
            // Its friends: worked out now, while the fallen still counts among them.
            var friends = new List<Adventurer>();
            foreach (var m in _roster.Members)
                if (m != dead && m != null && m.IsAlive && m.IsGuildMember && !m.IsVisitor && Relationships.Get(m, dead) >= NemesisRules.GrudgeFrom)
                    friends.Add(m);
            Witness(e.Killer as Monster, dead, DeedKind.Killed, friends);
        }

        void OnRetreated(AdventurerRetreatedEvent e)
            => Witness(e.Threat as Monster, e.Adventurer, DeedKind.DroveOff, e.Adventurer != null ? new List<Adventurer> { e.Adventurer } : null);

        /// <summary>
        /// A deed the guild saw (the monster in its sight that moment), against a guild member: recorded at the end of the frame,
        /// so everything that tells of the death or the retreat does so under the name the monster had.
        /// </summary>
        void Witness(Monster m, Adventurer victim, DeedKind kind, List<Adventurer> grudging)
        {
            if (!Enabled || m == null || !m.IsAlive || m.Def == null || victim?.Data == null || !victim.IsGuildMember || victim.IsVisitor) return;
            if (_fog == null || !_fog.IsVisibleRaw(m.Position)) return;
            _pending.Add(new Witnessed
            {
                Monster = m, UnitId = m.UnitId, VictimId = victim.Data.id, Victim = victim.Data.name, Kind = kind, Grudging = grudging,
            });
        }

        void LateUpdate()
        {
            if (_pending.Count > 0)
            {
                var batch = _pending.ToArray();
                _pending.Clear();
                foreach (var w in batch) Record(w);
            }
            if (Time.deltaTime <= 0f || _living.Count == 0) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = TickInterval;
            Tick();
        }

        /// <summary>
        /// Records a deed now as if the guild had seen it (debug and tests): <paramref name="grudging"/> hold the grudge (by
        /// default the friends of a member killed, or the one driven off).
        /// </summary>
        public Nemesis Record(Monster m, Adventurer victim, DeedKind kind, List<Adventurer> grudging = null)
        {
            if (m == null || !m.IsAlive || m.Def == null || victim?.Data == null) return null;
            if (grudging == null)
            {
                grudging = new List<Adventurer>();
                if (kind == DeedKind.DroveOff) grudging.Add(victim);
                else
                    foreach (var a in _roster.Members)
                        if (a != victim && a != null && a.IsAlive && Relationships.Get(a, victim) >= NemesisRules.GrudgeFrom) grudging.Add(a);
            }
            Record(new Witnessed { Monster = m, UnitId = m.UnitId, VictimId = victim.Data.id, Victim = victim.Data.name, Kind = kind, Grudging = grudging });
            return Of(m);
        }

        void Record(Witnessed w)
        {
            var m = w.Monster;
            // Fallen in the same frame, or its pooled body is already someone else.
            if (m == null || !m.IsAlive || m.UnitId != w.UnitId) return;
            var n = Of(m);
            // The same member again: driven off twice is one deed; a member it drove off and then killed is a new one.
            if (n != null && n.Deeds.Exists(d => d.victim_id == w.VictimId && (d.kind == w.Kind || d.kind == DeedKind.Killed))) return;
            bool isNew = n == null;
            string oldName = m.DisplayName;
            if (isNew)
            {
                bool named = !m.IsRare && !m.IsDragon && !m.IsRegionLord;
                n = new Nemesis
                {
                    UnitId = m.UnitId, Monster = m, Species = m.Def, BaseName = m.DisplayName,
                    Personal = named ? NemesisRules.PersonalName(m.UnitId, m.Def.id, TakenNames(m.Def)) : null,
                    SinceHour = Now,
                };
                _living.Add(n);
            }
            var deed = new NemesisDeed { victim_id = w.VictimId, victim = w.Victim, kind = w.Kind, hour = Now };
            n.Deeds.Add(deed);
            int before = m.Level;
            m.GrowTo(NemesisRules.GrownLevel(m.Level, w.Kind, n.Grows, MonsterLevel.MaxLevel));
            m.MarkNemesis(n.Name);
            n.Level = m.Level;
            n.LastSeen = m.Position;
            n.LastSeenHour = Now;
            // Its bounty once the dust settles — not for a lord or the dragon (their hunts are up already), nor for a rare one
            // whose own hunt is.
            if (n.Bounty != null) n.Bounty.TargetName = n.Name;
            else if (n.Grows && _incidents != null && !_incidents.Active.Any(i => i.Target == m)) n.BountyDue = true;
            if (w.Grudging != null)
                foreach (var a in w.Grudging)
                    if (a != null && a.IsAlive) ThoughtRecorder.Give(a, ThoughtRules.Grudge, n.UnitId, n.ShortName);
            Debug.Log($"[Nemesis] {oldName} → {n.Name} Lv{before}→{m.Level} ({w.Kind} {w.Victim})");
            EventBus.Publish(new NemesisDeedEvent { Nemesis = n, Deed = deed, IsNew = isNew, OldName = oldName, LevelBefore = before });
        }

        /// <summary>Names its kind's living nemeses have (a new one gets another).</summary>
        HashSet<string> TakenNames(Data.MonsterDefinition species)
        {
            var taken = new HashSet<string>();
            foreach (var n in _living)
                if (!string.IsNullOrEmpty(n.Personal) && NemesisRules.NamesFor(n.Species?.id) == NemesisRules.NamesFor(species.id)) taken.Add(n.Personal);
            return taken;
        }

        // ---------------- Living ----------------

        /// <summary>Where it was last seen, and its bounty once it is out of the fight it was named in.</summary>
        void Tick()
        {
            foreach (var n in _living.ToList())
            {
                var m = n.Monster;
                if (m == null || !m.IsAlive) continue; // its fall is heard by OnUnitDied
                if (_fog != null && _fog.IsVisibleRaw(m.Position))
                {
                    n.LastSeen = m.Position;
                    n.LastSeenHour = Now;
                    n.Level = m.Level;
                }
                // Not while it raids, marches or migrates either (measured: five bounties went up on raiders at the gate and
                // came down within the hour) — one that lives through it gets its bounty on the way home. A roaming pack's
                // wandering is its everyday life, not orders.
                bool onOrders = m.DefaultStateOverride != null && m.DefaultStateOverride != m.RoamState;
                if (n.BountyDue && !m.IsInFight() && !onOrders) PostBounty(n);
            }
        }

        /// <summary>
        /// The town's bounty: on its den when the guild knows the den is its kind's (it goes home after a fight), else where it was
        /// last seen; rated by the pack the guild last counted there.
        /// </summary>
        void PostBounty(Nemesis n)
        {
            n.BountyDue = false;
            var m = n.Monster;
            if (_incidents == null || m == null) return;
            var at = n.LastSeen;
            int pack = 1;
            var home = m.Home;
            if (home != null && home.Discovered && _intel != null && _intel.SpeciesOf(home) == m.Def)
            {
                at = home.WorldPosition;
                pack = Mathf.Max(1, _intel.Get(home).LastCount);
            }
            n.Bounty = _incidents.StartNemesisBounty(m, at, pack, NemesisRules.BountyFactor(n.Kills));
        }

        // ---------------- Its fall ----------------

        void OnUnitDied(UnitDiedEvent e)
        {
            if (e.Victim is not Monster m || !m.IsNemesis) return;
            var n = Of(m);
            if (n == null) return;
            _living.Remove(n);
            _fallen.Add(n);
            n.Fallen = true;
            n.FallenHour = Now;
            n.Monster = null;
            n.BountyDue = false;
            var slayer = e.Killer as Adventurer;
            if (slayer != null && (!slayer.IsGuildMember || slayer.IsVisitor || slayer.Data == null)) slayer = null;
            n.SlainBy = slayer?.Data.name;
            // The grudges are settled: glad for those who held one, a little relieved if something else got it.
            foreach (var a in _roster.Members)
            {
                if (a == null || !a.IsAlive || a.Data == null) continue;
                bool held = a.Data.thoughts.RemoveAll(t => t.id == ThoughtRules.Grudge.Id && t.about == n.UnitId) > 0;
                if (held && a != slayer) ThoughtRecorder.Give(a, slayer != null ? ThoughtRules.Avenged : ThoughtRules.NemesisFell, n.UnitId, n.ShortName);
            }
            if (slayer != null)
            {
                ThoughtRecorder.Give(slayer, ThoughtRules.AvengedByHand, n.UnitId, n.ShortName);
                // The dragon's and a lord's fall have their own titles.
                string title = NemesisRules.SlayerTitle(n.ShortName);
                if (n.Grows && !slayer.Data.titles.Contains(title)) slayer.Data.titles.Add(title);
                AdventurerMemory.Add(slayer.Data.memories, Now, 3,
                    $"원수 {n.ShortName}{MemoryRecorder.Josa(n.ShortName, "을", "를")} 쓰러뜨렸다", "nemesis:" + n.UnitId);
            }
            Debug.Log($"[Nemesis] {n.Name} fell" + (slayer != null ? $" to {slayer.Data.name}" : ""));
            EventBus.Publish(new NemesisFellEvent { Nemesis = n, Slayer = slayer });
        }

        // ---------------- What the guild is told ----------------

        /// <summary>The log's line for a deed ("고블린이 레오를 쓰러뜨려 '레오 잡이 고블린 그릭'이 되었습니다 (Lv.4 → 6).").</summary>
        public static string DeedText(NemesisDeedEvent e)
        {
            var d = e.Deed;
            string verb = d.kind == DeedKind.Killed
                ? $"{d.victim}{MemoryRecorder.Josa(d.victim, "을", "를")} 쓰러뜨려"
                : $"{d.victim}{MemoryRecorder.Josa(d.victim, "을", "를")} 죽을 뻔하게 몰아붙여";
            string level = e.Nemesis.Level > e.LevelBefore ? $" (Lv.{e.LevelBefore} → {e.Nemesis.Level})" : "";
            string who = e.IsNew ? e.OldName : "원수 " + e.OldName;
            string name = e.Nemesis.Name;
            return $"{who}{MemoryRecorder.Josa(e.OldName, "이", "가")} {verb} '{name}'{MemoryRecorder.Josa(name, "이", "가")} 되었습니다{level}.";
        }

        /// <summary>The log's line for its fall.</summary>
        public static string FellText(NemesisFellEvent e)
        {
            var n = e.Nemesis;
            return e.Slayer != null
                ? $"{e.Slayer.Data.name}{MemoryRecorder.Josa(e.Slayer.Data.name, "이", "가")} 원수 {n.Name}{MemoryRecorder.Josa(n.Name, "을", "를")} 쓰러뜨렸습니다!"
                  + (n.Grows ? $" 칭호 '{NemesisRules.SlayerTitle(n.ShortName)}'" : "")
                : $"원수 {n.Name}{MemoryRecorder.Josa(n.Name, "이", "가")} 쓰러졌다는 소식입니다.";
        }

        /// <summary>Its story in a line: what it did, when, and its bounty ("레오를 쓰러뜨림(2일 14시) · 현상금 240G").</summary>
        public string Story(Nemesis n)
        {
            var parts = n.Deeds.Select(d => $"{NemesisRules.DeedText(d)}({DayHour(d.hour)})").ToList();
            if (n.Bounty?.Quest != null && n.Bounty.Quest.IsActive) parts.Add($"현상금 {n.Bounty.Quest.RewardGold + n.Bounty.Quest.FeeGold}G");
            return string.Join(" · ", parts);
        }

        static string DayHour(float hour) => $"{Mathf.FloorToInt(hour / 24f) + 1}일 {Mathf.FloorToInt(hour % 24f):00}시";

        // ---------------- Save ----------------

        [Serializable]
        class SavedNemesis
        {
            public string unit;
            public string species;
            public string base_name;
            public string personal;
            public List<NemesisDeed> deeds = new();
            public float since;
            public Vector2 last_seen;
            public float last_seen_hour;
            public int level;
            public float fallen_hour;
            public string slain_by;
            public int bounty = -1;
            public bool bounty_due;
        }

        [Serializable]
        class SaveData
        {
            public List<SavedNemesis> living = new();
            public List<SavedNemesis> fallen = new();
        }

        public string SaveKey => "nemeses";

        static SavedNemesis CaptureOne(Nemesis n) => new()
        {
            unit = n.UnitId, species = n.Species?.id, base_name = n.BaseName, personal = n.Personal, deeds = new List<NemesisDeed>(n.Deeds),
            since = n.SinceHour, last_seen = n.LastSeen, last_seen_hour = n.LastSeenHour, level = n.Level, fallen_hour = n.FallenHour,
            slain_by = n.SlainBy, bounty = n.Bounty != null ? n.Bounty.Id : -1, bounty_due = n.BountyDue,
        };

        public string Capture()
        {
            var s = new SaveData();
            foreach (var n in _living) s.living.Add(CaptureOne(n));
            foreach (var n in _fallen) s.fallen.Add(CaptureOne(n));
            return JsonUtility.ToJson(s);
        }

        SaveData _restoring;
        readonly List<(Nemesis n, SavedNemesis s)> _restored = new();

        Nemesis RestoreOne(SavedNemesis s, bool fallen)
        {
            var n = new Nemesis
            {
                UnitId = s.unit, Species = Services.TryGet<EcosystemManager>(out var eco) ? eco.AllSpecies.FirstOrDefault(d => d != null && d.id == s.species) : null,
                BaseName = s.base_name, Personal = string.IsNullOrEmpty(s.personal) ? null : s.personal, SinceHour = s.since,
                LastSeen = s.last_seen, LastSeenHour = s.last_seen_hour, Level = s.level, Fallen = fallen, FallenHour = s.fallen_hour,
                SlainBy = string.IsNullOrEmpty(s.slain_by) ? null : s.slain_by, BountyDue = s.bounty_due,
            };
            n.Deeds.AddRange(s.deeds);
            _restored.Add((n, s));
            return n;
        }

        /// <summary>The records come back here; their monsters and bounties in <see cref="Link"/>.</summary>
        public void Restore(string json, SaveContext ctx)
        {
            _restoring = JsonUtility.FromJson<SaveData>(json);
            _living.Clear();
            _fallen.Clear();
            _restored.Clear();
            foreach (var s in _restoring.living) _living.Add(RestoreOne(s, false));
            foreach (var s in _restoring.fallen) _fallen.Add(RestoreOne(s, true));
        }

        public void Link(SaveContext ctx)
        {
            foreach (var (n, s) in _restored)
            {
                if (s.bounty >= 0) n.Bounty = ctx.Find<Incident>(Incident.SaveKeyOf(s.bounty));
                if (n.Fallen) continue;
                n.Monster = ctx.Find<Monster>(n.UnitId);
                // The monster comes back under its kind's name: it is given its own again.
                if (n.Monster != null) n.Monster.MarkNemesis(n.Name);
                else ctx.Warnings.Add($"nemesis {n.UnitId}: its monster is not in the save");
            }
            _living.RemoveAll(n => n.Monster == null);
            _restored.Clear();
            _restoring = null;
        }
    }
}
