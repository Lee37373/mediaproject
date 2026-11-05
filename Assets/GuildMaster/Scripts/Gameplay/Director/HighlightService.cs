using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Incidents;
using GuildMaster.Legacy;
using GuildMaster.Monsters;
using GuildMaster.Town;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Director
{
    /// <summary>A moment worth watching, while it lasts.</summary>
    public class Highlight
    {
        public string Key;
        public HighlightKind Kind;
        public float Score;
        /// <summary>Where to look (follows the fight).</summary>
        public Vector2 Position;
        public string Title;
        public string Subtitle;
        /// <summary>Who or what it was against as the guild saw it ("고블린", the raid's kind), or null (the newspaper, P4-130).</summary>
        public string FoeName;
        /// <summary>The foe of a big fight, or the member in trouble; its id at the start (pooled units are reused).</summary>
        public Unit Focus;
        public string FocusId;
        /// <summary>Real time it began and was last seen going on.</summary>
        public float StartedAt, LastActive;
        /// <summary>Moments that are one beat (a last stand, a broken gate) end at this real time; 0 = while it goes on.</summary>
        public float ExpiresAt;
        public bool Ended;
        /// <summary>How it ended, when that is worth a card ("모래 폭군 카르샤 쓰러짐!"), else null.</summary>
        public string Outcome;
        /// <summary>Members who took part (their chronicles have the line).</summary>
        public readonly List<Adventurer> Members = new();
    }

    public struct HighlightStartedEvent { public Highlight Highlight; }
    public struct HighlightEndedEvent { public Highlight Highlight; }

    /// <summary>
    /// Finds the moments worth watching (P4-129, H7 — user decision 2026-11-05): fights with big foes (dragon, region
    /// lord, nemesis, rare monster, boss), members in trouble (low health in a fight, several about to fall together, a
    /// last stand, a retreat) and the town's defence (a raid at a gate, a horde, the dragon over the town, a broken
    /// gate). Only what the guild sees (monsters in the fog are never a highlight). Every member in one gets a line in
    /// its chronicle (<see cref="AdventurerData.chronicle"/>); <see cref="SpectatorCamera"/> shows the best of them.
    /// Nothing here is saved: the moments are found again after loading.
    /// </summary>
    public class HighlightService : MonoBehaviour
    {
        const float ScanInterval = 0.4f;

        readonly Dictionary<string, Highlight> _active = new();
        readonly List<Highlight> _list = new();
        readonly HashSet<string> _touched = new();
        readonly Dictionary<int, string> _raidDirection = new();
        GuildRoster _roster;
        WorldService _world;
        GameClock _clock;
        float _timer;

        /// <summary>The moments going on now, oldest first.</summary>
        public IReadOnlyList<Highlight> Active => _list;
        /// <summary>Highlights begun since the game started (for debugging).</summary>
        public int Started { get; private set; }

        public void Init(GameClock clock, GuildRoster roster, WorldService world)
        {
            _clock = clock;
            _roster = roster;
            _world = world;
            EventBus.Subscribe<LastStandEvent>(OnLastStand);
            EventBus.Subscribe<AdventurerRetreatedEvent>(OnRetreat);
            EventBus.Subscribe<GateBrokenEvent>(OnGateBroken);
            EventBus.Subscribe<RaidSightedEvent>(OnRaidSighted);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<LastStandEvent>(OnLastStand);
            EventBus.Unsubscribe<AdventurerRetreatedEvent>(OnRetreat);
            EventBus.Unsubscribe<GateBrokenEvent>(OnGateBroken);
            EventBus.Unsubscribe<RaidSightedEvent>(OnRaidSighted);
        }

        float Hour => _clock != null ? _clock.TotalHours : 0f;

        void Update()
        {
            // The world stands still: so do its moments (a paused fight is not over).
            if (Time.deltaTime <= 0f) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = ScanInterval;
            Scan();
        }

        /// <summary>One look at the world (public for tests).</summary>
        public void Scan()
        {
            _touched.Clear();
            Services.TryGet<FogOfWar>(out var fog);
            var members = new List<Adventurer>();
            if (_roster != null)
                foreach (var m in _roster.Members)
                    if (m != null && m.IsAlive && m.IsGuildMember && !m.IsVisitor) members.Add(m);

            ScanFights(members, fog);
            ScanWipes(members);
            ScanTown(members, fog);
            EndQuiet();
        }

        static bool Seen(FogOfWar fog, Unit u) => u != null && u.IsAlive && (fog == null || fog.IsVisible(u.Position));

        static bool InFight(Adventurer a)
            => (a.CurrentTarget != null && a.CurrentTarget.IsAlive) || Time.time - a.LastAttackedTime < HighlightRules.CombatMemory;

        /// <summary>The monster a member fights: its target, or the one that just hit it.</summary>
        static Monster FoeOf(Adventurer a)
        {
            if (a.CurrentTarget is Monster t && t.IsAlive) return t;
            return a.LastAttacker is Monster m && m.IsAlive && Time.time - a.LastAttackedTime < HighlightRules.CombatMemory ? m : null;
        }

        void ScanFights(List<Adventurer> members, FogOfWar fog)
        {
            var byFoe = new Dictionary<Monster, List<Adventurer>>();
            foreach (var a in members)
            {
                var foe = FoeOf(a);
                if (foe != null && Seen(fog, foe)
                    && HighlightRules.BigFoe(foe.IsDragon, foe.IsRegionLord, foe.IsNemesis, foe.IsRare, foe.IsBoss) != null)
                {
                    if (!byFoe.TryGetValue(foe, out var list)) byFoe[foe] = list = new List<Adventurer>();
                    list.Add(a);
                }

                // A member in trouble: low in a fight, until it is well again or out of it.
                if (!InFight(a)) continue;
                string key = "hp:" + a.Data.id;
                bool going = _active.ContainsKey(key);
                if (a.HpRatio <= HighlightRules.LowHpRatio || (going && a.HpRatio <= HighlightRules.RecoveredRatio))
                {
                    string foeName = foe != null && Seen(fog, foe) ? foe.DisplayName : null;
                    int pct = Mathf.RoundToInt(a.HpRatio * 100f);
                    Touch(key, HighlightKind.LowHp, HighlightRules.Score(HighlightKind.LowHp), a.Position, a,
                        $"{a.Data.name}, 체력 {pct}%" + (foeName != null ? $" — {foeName}" : ""),
                        new[] { a },
                        foeName != null ? $"{foeName}{Josa(foeName, "과", "와")} 싸우다 체력 {pct}%까지 몰렸다" : $"싸우다 체력 {pct}%까지 몰렸다")
                        .FoeName = foeName;
                }
            }

            foreach (var kv in byFoe)
            {
                var foe = kv.Key;
                var kind = HighlightRules.BigFoe(foe.IsDragon, foe.IsRegionLord, foe.IsNemesis, foe.IsRare, foe.IsBoss).Value;
                var fighters = kv.Value;
                // Everyone near who is in it too (a healer behind, an archer at range).
                foreach (var a in members)
                    if (!fighters.Contains(a) && InFight(a) && (a.Position - foe.Position).sqrMagnitude <= HighlightRules.WitnessRadius * HighlightRules.WitnessRadius)
                        fighters.Add(a);
                Vector2 centre = foe.Position;
                foreach (var a in fighters) centre += a.Position;
                centre /= fighters.Count + 1;
                string names = HighlightRules.Names(fighters.Select(f => f.Data.name).ToList());
                string foeLabel = $"{foe.DisplayName}(Lv{foe.Level})";
                Touch("big:" + foe.UnitId, kind, HighlightRules.Score(kind), centre, foe,
                    $"{names} vs {foeLabel}", fighters,
                    $"{HighlightRules.Title(kind)}: {foeLabel}{Josa(foe.DisplayName, "과", "와")} 맞섰다").FoeName = foe.DisplayName;
            }
        }

        void ScanWipes(List<Adventurer> members)
        {
            var low = members.Where(a => InFight(a) && a.HpRatio <= HighlightRules.WipeRatio).ToList();
            var done = new HashSet<Adventurer>();
            float r2 = HighlightRules.WipeRadius * HighlightRules.WipeRadius;
            foreach (var a in low)
            {
                if (done.Contains(a)) continue;
                var group = low.Where(b => !done.Contains(b) && (b.Position - a.Position).sqrMagnitude <= r2).ToList();
                if (group.Count < 2) continue;
                foreach (var g in group) done.Add(g);
                var ids = group.Select(g => g.Data.id).OrderBy(id => id, System.StringComparer.Ordinal).ToList();
                Vector2 centre = Vector2.zero;
                foreach (var g in group) centre += g.Position;
                centre /= group.Count;
                // The same lot keeps its moment while one more joins or leaves it: keyed by who started it.
                string key = _active.Keys.FirstOrDefault(k => k.StartsWith("wipe:") && ids.Any(id => k.Contains("|" + id + "|")))
                             ?? "wipe:|" + string.Join("|", ids) + "|";
                string names = HighlightRules.Names(group.Select(g => g.Data.name).ToList());
                Touch(key, HighlightKind.Wipe, HighlightRules.Score(HighlightKind.Wipe), centre, null,
                    $"{names}{Josa(names, "이", "가")} 몰리고 있다", group, $"전멸 위기: {names}{Josa(names, "이", "가")} 함께 몰렸다");
            }
        }

        void ScanTown(List<Adventurer> members, FogOfWar fog)
        {
            // A raid at a gate: one moment per sighted raid, where its seen raiders are.
            if (Services.TryGet<WaveManager>(out var waves))
                foreach (var front in waves.Fronts)
                {
                    var near = waves.Raiders.Where(m => Seen(fog, m) && (m.Position - front.Point).sqrMagnitude <= 30f * 30f).ToList();
                    if (near.Count == 0) continue;
                    Vector2 centre = Vector2.zero;
                    foreach (var m in near) centre += m.Position;
                    centre /= near.Count;
                    string species = MostCommon(near);
                    string dir = _raidDirection.TryGetValue(front.RaidId, out var d) ? $"{d}쪽 " : "";
                    Touch("raid:" + front.RaidId, HighlightKind.Raid, HighlightRules.RaidScore(near.Count), centre, null,
                        $"{species} {near.Count}마리가 {dir}성문으로 몰려온다", Near(members, centre),
                        $"마을 방어전: {dir}성문에서 {species} 무리를 막아섰다").FoeName = species;
                }

            // A horde on the march, where the guild sees it.
            if (Services.TryGet<IncidentService>(out var incidents))
                foreach (var inc in incidents.Active)
                {
                    if (inc.Kind != IncidentKind.Horde || inc.Over) continue;
                    var seen = inc.Members.Where(m => Seen(fog, m)).ToList();
                    if (seen.Count == 0) continue;
                    Vector2 centre = Vector2.zero;
                    foreach (var m in seen) centre += m.Position;
                    centre /= seen.Count;
                    string species = MostCommon(seen);
                    Touch("horde:" + inc.Id, HighlightKind.Horde, HighlightRules.Score(HighlightKind.Horde), centre, null,
                        $"{species} {seen.Count}마리가 몰려온다", Near(members, centre), $"대규모 무리({species})와 마주쳤다").FoeName = species;
                }

            // The dragon over the town.
            if (_world != null)
                foreach (var u in UnitRegistry.All)
                {
                    if (u is not Monster m || !m.IsDragon || !Seen(fog, m)) continue;
                    if ((m.Position - _world.TownCenterWorld).sqrMagnitude > HighlightRules.OverTownRadius * HighlightRules.OverTownRadius) continue;
                    Touch("dragon-town:" + m.UnitId, HighlightKind.DragonOverTown, HighlightRules.Score(HighlightKind.DragonOverTown), m.Position, m,
                        $"{m.DisplayName}{Josa(m.DisplayName, "이", "가")} 마을 위에 나타났다", Near(members, m.Position, 30f), "마을을 덮친 드래곤을 보았다")
                        .FoeName = m.DisplayName;
                }
        }

        static List<Adventurer> Near(List<Adventurer> members, Vector2 at, float radius = 15f)
            => members.Where(a => (a.Position - at).sqrMagnitude <= radius * radius).ToList();

        static string MostCommon(List<Monster> monsters)
            => monsters.GroupBy(m => m.Def != null ? m.Def.displayName : m.DisplayName).OrderByDescending(g => g.Count()).First().Key;

        // ---------------- Beats (from events) ----------------

        void OnLastStand(LastStandEvent e)
        {
            var a = e.Adventurer;
            if (a == null || a.Data == null) return;
            Services.TryGet<FogOfWar>(out var fog);
            string by = Seen(fog, e.Killer) ? e.Killer.DisplayName : null;
            Beat("last:" + a.Data.id, HighlightKind.LastStand, a.Position, a, $"{a.Data.name}{Josa(a.Data.name, "이", "가")} 죽음의 문턱에서 버텼다",
                new[] { a }, by != null ? $"구사일생: {by}의 일격을 버텨 냈다" : "구사일생: 죽음의 문턱에서 살아났다").FoeName = by;
        }

        void OnRetreat(AdventurerRetreatedEvent e)
        {
            var a = e.Adventurer;
            if (a == null || a.Data == null || !a.IsGuildMember || a.IsVisitor) return;
            Services.TryGet<FogOfWar>(out var fog);
            string threat = Seen(fog, e.Threat) ? e.Threat.DisplayName : "적";
            string who = e.WithParty ? $"{a.Data.name} 일행" : a.Data.name;
            Beat("retreat:" + a.Data.id, HighlightKind.Retreat, a.Position, a, $"{who}{Josa(who, "이", "가")} {threat}에게서 물러난다",
                new[] { a }, $"{threat}에게서 후퇴했다").FoeName = Seen(fog, e.Threat) ? threat : null;
        }

        void OnGateBroken(GateBrokenEvent e)
        {
            if (e.Gate == null) return;
            var members = _roster != null ? _roster.Members.Where(m => m != null && m.IsAlive && !m.IsVisitor).ToList() : new List<Adventurer>();
            Beat("gate:" + e.Gate.CenterCell, HighlightKind.GateBroken, e.Gate.Center, null, "적이 성 안으로 들어온다",
                Near(members, e.Gate.Center), "성문이 뚫리는 것을 보았다");
        }

        void OnRaidSighted(RaidSightedEvent e) => _raidDirection[e.RaidId] = e.Direction;

        Highlight Beat(string key, HighlightKind kind, Vector2 at, Unit focus, string subtitle, IEnumerable<Adventurer> members, string line)
        {
            var h = Touch(key, kind, HighlightRules.Score(kind), at, focus, subtitle, members, line);
            h.ExpiresAt = Time.unscaledTime + HighlightRules.BeatSeconds;
            return h;
        }

        // ---------------- Bookkeeping ----------------

        Highlight Touch(string key, HighlightKind kind, float score, Vector2 at, Unit focus, string subtitle, IEnumerable<Adventurer> members, string line)
        {
            float now = Time.unscaledTime;
            _touched.Add(key);
            if (!_active.TryGetValue(key, out var h))
            {
                h = new Highlight
                {
                    Key = key, Kind = kind, Title = HighlightRules.Title(kind), Focus = focus, FocusId = focus != null ? focus.UnitId : null,
                    StartedAt = now,
                };
                _active[key] = h;
                _list.Add(h);
                Started++;
                h.Score = score;
                h.Position = at;
                h.Subtitle = subtitle;
                h.LastActive = now;
                Join(h, members, line);
                EventBus.Publish(new HighlightStartedEvent { Highlight = h });
                return h;
            }
            h.Score = score;
            h.Position = at;
            h.Subtitle = subtitle;
            h.LastActive = now;
            Join(h, members, line);
            return h;
        }

        void Join(Highlight h, IEnumerable<Adventurer> members, string line)
        {
            if (members == null) return;
            foreach (var a in members)
            {
                if (a == null || a.Data == null || h.Members.Contains(a)) continue;
                h.Members.Add(a);
                HighlightRules.Write(a.Data.chronicle, Hour, line);
            }
        }

        void EndQuiet()
        {
            float now = Time.unscaledTime;
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                var h = _list[i];
                bool over = h.ExpiresAt > 0f
                    ? now >= h.ExpiresAt
                    : !_touched.Contains(h.Key) && (HighlightRules.IsBigFoe(h.Kind)
                        ? !FocusAlive(h) || now - h.LastActive >= HighlightRules.FightGoneSeconds
                        : now - h.LastActive >= HighlightRules.StateGrace);
                if (!over) continue;
                if (HighlightRules.IsBigFoe(h.Kind) && !FocusAlive(h))
                {
                    string name = h.Focus != null ? h.Focus.DisplayName : "적";
                    h.Outcome = $"{name} 쓰러짐!";
                    foreach (var a in h.Members)
                        if (a != null && a.Data != null) HighlightRules.Write(a.Data.chronicle, Hour, $"{name}{Josa(name, "을", "를")} 쓰러뜨렸다");
                }
                h.Ended = true;
                _list.RemoveAt(i);
                _active.Remove(h.Key);
                EventBus.Publish(new HighlightEndedEvent { Highlight = h });
            }
        }

        /// <summary>Its foe still the same living unit (pooled monsters come back as others).</summary>
        static bool FocusAlive(Highlight h) => h.Focus != null && h.Focus.IsAlive && h.Focus.UnitId == h.FocusId;

        static string Josa(string w, string withBatchim, string without) => MemoryRecorder.Josa(w, withBatchim, without);

        /// <summary>For DebugCommands: the moments going on.</summary>
        public string Describe()
            => _list.Count == 0 ? "없음"
                : string.Join("\n", _list.Select(h => $"{h.Title} [{h.Kind} {h.Score:0}] {h.Subtitle} @({h.Position.x:0},{h.Position.y:0}) · {h.Members.Count}명"
                                                     + $" · {Time.unscaledTime - h.StartedAt:0}초"));
    }
}
