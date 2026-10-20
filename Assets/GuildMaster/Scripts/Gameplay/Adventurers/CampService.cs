using System.Collections.Generic;
using System.Linq;
using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Ecosystem;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    public struct CampPitchedEvent { public Camp Camp; }
    /// <summary>Raiders at the fire. <see cref="Warned"/>: the watch saw them coming; false = someone was hit awake.</summary>
    public struct CampRaidedEvent { public Camp Camp; public Unit Raider; public bool Warned; }
    public struct CampBrokenEvent { public Camp Camp; }
    /// <summary>A sleeper at a camp breathing out a "Zz" (for the floating text layer).</summary>
    public struct CampSnoreEvent { public Adventurer Sleeper; }

    /// <summary>One camp out in the wild (P4-66): who sits at it, who keeps watch, where each goes next.</summary>
    public class Camp
    {
        public int Id;
        public Vector2 Position;
        public CampReason Reason;
        public float PitchedAtHour;
        /// <summary>Until this game hour everyone sits round the fire and talks; then the watches begin.</summary>
        public float SitUntilHour;
        /// <summary>Leader first. A member who leaves the camp state leaves the camp.</summary>
        public readonly List<Adventurer> Members = new();
        /// <summary>What each member goes back to when the fire is put out.</summary>
        public readonly Dictionary<Adventurer, State<Adventurer>> ResumeTo = new();
        /// <summary>The one awake while the others sleep; null while sitting, or when alone.</summary>
        public Adventurer Watcher;
        /// <summary>Raiders about: everyone up and fighting until it has been quiet for a while.</summary>
        public bool Alarmed;
        public float QuietSince;
        public bool Talked;
        public int Raids;
        public Campfire Fire;
        /// <summary>Comrades that found no way to the fire (P4-78): they are not called to it again.</summary>
        public readonly HashSet<Adventurer> CannotReach = new();

        public Adventurer Leader => Members.Count > 0 ? Members[0] : null;
        public bool Sitting(float hour) => hour < SitUntilHour;

        /// <summary>Each member's place in the ring round the fire.</summary>
        public Vector2 SeatOf(Adventurer a)
        {
            int i = Mathf.Max(0, Members.IndexOf(a));
            int n = Mathf.Max(2, Members.Count);
            float angle = i / (float)n * Mathf.PI * 2f + 0.4f;
            return Position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.8f) * 1.15f;
        }
    }

    /// <summary>
    /// Camps out in the wild (P4-66, user proposal 2026-09-23: "a campfire when they are worn out —
    /// the feel of an adventure — and raiders now and then"). Decides when a band far from town
    /// stops to make a fire, gathers its comrades round it, rotates the watch, draws the monsters
    /// that see the firelight, starts the talk at the fire and puts the fire out when everyone is
    /// mended. The rules and numbers are in <see cref="CampRules"/>; the members' side in
    /// <see cref="CampState"/>.
    /// </summary>
    public class CampService : MonoBehaviour, ISaveable, ISaveLink
    {
        const float LightInterval = 0.5f;
        /// <summary>A monster the light drew once leaves it be for a while after (seconds).</summary>
        const float DrawnCooldown = 30f;

        readonly List<Camp> _camps = new();
        readonly Dictionary<Adventurer, float> _lastThreat = new();
        readonly Dictionary<Adventurer, (int night, bool camp)> _nightChoice = new();
        readonly Dictionary<Monster, float> _drawnUntil = new();
        static readonly List<Unit> s_units = new();

        GameClock _clock;
        WorldService _world;
        PoolService _pool;
        GuildRoster _roster;
        int _nextId;
        float _lightTimer;

        public IReadOnlyList<Camp> Camps => _camps;
        /// <summary>Monsters the firelight has drawn so far (debug, tests).</summary>
        public int MonstersDrawn { get; private set; }

        public void Init(GameClock clock, WorldService world, PoolService pool, GuildRoster roster)
        {
            _clock = clock;
            _world = world;
            _pool = pool;
            _roster = roster;
            pool.Register(Campfire.PoolKey, Campfire.Create, 1, 16);
        }

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SavedCamp
        {
            public int id;
            public Vector2 position;
            public CampReason reason;
            public float pitched;
            public float sit_until;
            public List<string> members = new();
            /// <summary>Per member (same order): the state it goes back to when the fire is put out.</summary>
            public List<string> resume_to = new();
            public string watcher;
            public bool alarmed;
            public float quiet_since;
            public bool talked;
            public int raids;
        }

        [System.Serializable]
        class SaveData
        {
            public int next_id;
            public int monsters_drawn;
            public List<SavedCamp> camps = new();
            public List<string> night_choice = new();
            public List<int> night_choice_night = new();
            public List<bool> night_choice_camp = new();
        }

        SaveData _restoring;

        public string SaveKey => "camps";

        public string Capture()
        {
            var s = new SaveData { next_id = _nextId, monsters_drawn = MonstersDrawn };
            foreach (var c in _camps)
            {
                var sc = new SavedCamp
                {
                    id = c.Id, position = c.Position, reason = c.Reason, pitched = c.PitchedAtHour, sit_until = c.SitUntilHour,
                    watcher = c.Watcher != null ? c.Watcher.Data.id : null, alarmed = c.Alarmed, quiet_since = SaveTime.Rel(c.QuietSince),
                    talked = c.Talked, raids = c.Raids,
                };
                foreach (var m in c.Members)
                {
                    sc.members.Add(m.Data.id);
                    sc.resume_to.Add(c.ResumeTo.TryGetValue(m, out var next) ? Adventurer.NameOf(next) : null);
                }
                s.camps.Add(sc);
            }
            var choosers = new List<Adventurer>(_nightChoice.Keys);
            choosers.Sort((a, b) => string.CompareOrdinal(a.Data.id, b.Data.id));
            foreach (var a in choosers)
            {
                if (a == null || !a.IsAlive) continue;
                s.night_choice.Add(a.Data.id);
                s.night_choice_night.Add(_nightChoice[a].night);
                s.night_choice_camp.Add(_nightChoice[a].camp);
            }
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            _restoring = JsonUtility.FromJson<SaveData>(json);
            _nextId = _restoring.next_id;
            MonstersDrawn = _restoring.monsters_drawn;
        }

        /// <summary>The fires are lit again with the same people round them (they sit back down when they resume).</summary>
        public void Link(SaveContext ctx)
        {
            foreach (var sc in _restoring.camps)
            {
                var c = new Camp
                {
                    Id = sc.id, Position = sc.position, Reason = sc.reason, PitchedAtHour = sc.pitched, SitUntilHour = sc.sit_until,
                    Watcher = ctx.Find<Adventurer>(sc.watcher), Alarmed = sc.alarmed, QuietSince = SaveTime.Abs(sc.quiet_since),
                    Talked = sc.talked, Raids = sc.raids,
                };
                for (int i = 0; i < sc.members.Count; i++)
                {
                    if (ctx.Find<Adventurer>(sc.members[i]) is not { } m) continue;
                    c.Members.Add(m);
                    var next = i < sc.resume_to.Count ? m.StateNamed(sc.resume_to[i]) : null;
                    c.ResumeTo[m] = next ?? m.ReturnState;
                }
                if (c.Members.Count == 0) continue;
                c.Fire = _pool.Spawn<Campfire>(Campfire.PoolKey, c.Position);
                c.Fire.Light();
                _camps.Add(c);
            }
            for (int i = 0; i < _restoring.night_choice.Count && i < _restoring.night_choice_night.Count && i < _restoring.night_choice_camp.Count; i++)
                if (ctx.Find<Adventurer>(_restoring.night_choice[i]) is { } a)
                    _nightChoice[a] = (_restoring.night_choice_night[i], _restoring.night_choice_camp[i]);
            _restoring = null;
        }

        public Camp CampOf(Adventurer a)
        {
            foreach (var c in _camps) if (c.Members.Contains(a)) return c;
            return null;
        }

        /// <summary>From the town as far as camping goes: past where the walls stood at the start (P4-86: they move out).</summary>
        float DistanceToTown(Vector2 p) => Vector2.Distance(_world.OnTheSurface(p), _world.TownCenterWorld) - _world.TownGrowth;

        /// <summary>Down in a ruin (P4-85): nobody makes camp there — the band walks on out.</summary>
        bool Underground(Adventurer a) => _world.Map.IsUnderground(GridMap.WorldToCell(a.Position));

        // ---------------- Deciding ----------------

        /// <summary>
        /// On the way home (<see cref="ReturnToTownState"/>): far out and hurt or worn, or out in the
        /// dark and the leader's nerve says so, the band stops and makes camp.
        /// </summary>
        public bool TryCampOnTheWay(Adventurer a)
        {
            if (Underground(a) || ComradesAtAFire(a) || !Quiet(a)) return false;
            var group = Companions(a);
            if (!AllQuiet(group)) return false;
            bool alone = group.Count == 1;
            if (!CampRules.FarOut(DistanceToTown(a.Position), alone)) return false;
            if (group.Any(m => CampRules.NeedsRest(m.HpRatio, m.Data.fatigue, alone))) return Pitch(group, CampReason.Recover);
            if (_clock.IsNight && ChoosesNightCamp(group[0], alone)) return Pitch(group, CampReason.Night);
            return false;
        }

        /// <summary>Out on a quest or a hunt when night falls: the leader decides for the band.</summary>
        public bool TryNightCamp(Adventurer a)
        {
            if (!_clock.IsNight || Underground(a)) return false;
            if (Services.TryGet<PartyTactics>(out var tactics) && tactics.LeaderOf(a) is { } leader && leader != a) return false;
            if (ComradesAtAFire(a) || !Quiet(a)) return false;
            var group = Companions(a);
            if (!AllQuiet(group)) return false;
            bool alone = group.Count == 1;
            if (!CampRules.FarOut(DistanceToTown(a.Position), alone)) return false;
            return ChoosesNightCamp(group[0], alone) && Pitch(group, CampReason.Night);
        }

        /// <summary>
        /// Every one of them quiet, not only the one asking (P4-96): a member nothing had reached sat the whole party down by
        /// a fire while the dragon, out of its sight, was still burning their hurt one — who pulled them all back, and a
        /// moment later they sat down again, a dozen times, until it died.
        /// </summary>
        bool AllQuiet(List<Adventurer> group)
        {
            foreach (var m in group) if (!Quiet(m)) return false;
            return true;
        }

        /// <summary>
        /// Its comrades have a fire going already: it joins theirs (<see cref="AdmitLateComers"/>) instead
        /// of lighting a second one (P4-78) — unless it found no way there.
        /// </summary>
        bool ComradesAtAFire(Adventurer a)
        {
            foreach (var camp in _camps)
            {
                if (camp.CannotReach.Contains(a)) continue;
                foreach (var c in camp.Members) if (Comrades(c, a)) return true;
            }
            return false;
        }

        /// <summary>No enemy in sight, nothing hit us, for <see cref="CampRules.QuietSeconds"/>.</summary>
        bool Quiet(Adventurer a)
        {
            float now = Time.time;
            if (a.Engage.Target != null || ThreatInSight(a) != null) _lastThreat[a] = now;
            float last = Mathf.Max(a.LastAttackedTime, _lastThreat.TryGetValue(a, out var t) ? t : -999f);
            return now - last >= CampRules.QuietSeconds;
        }

        /// <summary>A monster in sight that would come for us, or is fighting one of us.</summary>
        internal static Unit ThreatInSight(Adventurer a)
            => UnitRegistry.FindNearest(a.Position, a.Stats.SightRadius,
                u => u is Monster m && m.IsAlive && (m.IsHostileTo(a) || m.CombatEngagement?.Target is Adventurer) && a.CanSee(m));

        /// <summary>Asked once a night per leader: its caution against the dark (<see cref="CampRules.NightCampChance"/>).</summary>
        bool ChoosesNightCamp(Adventurer leader, bool alone)
        {
            int night = Mathf.FloorToInt((_clock.TotalHours + 24f - CampRules.DawnHour) / 24f);
            if (_nightChoice.TryGetValue(leader, out var c) && c.night == night) return c.camp;
            bool camp = GameRandom.Shared.Chance(CampRules.NightCampChance(RouteChoice.ForParty(leader), alone));
            _nightChoice[leader] = (night, camp);
            return camp;
        }

        /// <summary>
        /// Us first (the leader of our party when it is here), then the comrades who are out too, wherever
        /// they are (P4-78, user decision 2026-10-10: only those within 12 cells used to sit down, and the
        /// party broke in two — the rest hunted on or walked home alone). The far ones walk to the fire.
        /// </summary>
        List<Adventurer> Companions(Adventurer a)
        {
            var group = new List<Adventurer> { a };
            foreach (var m in _roster.Members)
            {
                if (m == a || !m.IsAlive || !m.IsGuildMember || CampOf(m) != null || !IsOut(m) || !Comrades(a, m)) continue;
                group.Add(m);
            }
            var leader = Services.TryGet<PartyTactics>(out var tactics) ? tactics.LeaderOf(a) : null;
            if (leader == null && Services.TryGet<GuildParties>(out var parties)) leader = parties.PartyOf(a)?.Leader;
            int li = leader != null ? group.IndexOf(leader) : -1;
            if (li > 0) { group.RemoveAt(li); group.Insert(0, leader); }
            return group;
        }

        /// <summary>On the road: walking out, working a quest or a hunt, or heading home (not a lure run).</summary>
        static bool IsOut(Adventurer m)
        {
            var s = m.Fsm.Current;
            return s == m.TravelState || s == m.HuntState || s == m.FreeHuntState || s == m.ReturnState && !m.ReturnState.Luring;
        }

        /// <summary>
        /// Out on the same business: the same quest, the same hunting band, or the same standing party's
        /// current outing (a party member out on business of its own is not called across the map).
        /// </summary>
        static bool Comrades(Adventurer a, Adventurer b)
        {
            if (a.CurrentQuest != null && a.CurrentQuest == b.CurrentQuest) return true;
            if (Services.TryGet<GuildParties>(out var parties) && parties.PartyOf(a) is { } p && p == parties.PartyOf(b)
                && p.WentOut(a) && p.WentOut(b)) return true;
            return Services.TryGet<HuntBandService>(out var bands) && bands.BandOf(a) is { } band && band == bands.BandOf(b);
        }

        // ---------------- Late comers (P4-78) ----------------

        const float AdmitInterval = 1f;
        float _admitTimer;

        /// <summary>
        /// Comrades who were fighting, luring or busy when the fire was lit join it afterwards: once quiet
        /// and out on the road, a member of the same business comes to the fire too.
        /// </summary>
        void AdmitLateComers(Camp camp)
        {
            if (camp.Members.Count == 0) return;
            foreach (var m in _roster.Members)
            {
                if (!m.IsAlive || !m.IsGuildMember || CampOf(m) != null || !IsOut(m) || camp.CannotReach.Contains(m)) continue;
                if (m.Engage.Target != null || Time.time - m.LastAttackedTime < CampRules.QuietSeconds) continue; // after its fight
                bool comrade = false;
                foreach (var c in camp.Members) if (Comrades(c, m)) { comrade = true; break; }
                if (!comrade) continue;
                camp.Members.Add(m);
                camp.ResumeTo[m] = ResumeFor(m);
                m.Engage.Clear();
                m.ChangeState(m.CampState);
            }
        }

        static readonly List<Adventurer> s_seated = new();

        /// <summary>The one on watch: taken in turns among those sitting at the fire (not one still walking in).</summary>
        static Adventurer WatchOf(Camp camp, float hour)
        {
            if (camp.Sitting(hour)) return null;
            s_seated.Clear();
            foreach (var m in camp.Members) if (m.CampState.Seated) s_seated.Add(m);
            return s_seated.Count >= 2 ? s_seated[CampRules.WatchIndex(hour - camp.SitUntilHour, s_seated.Count)] : null;
        }

        // ---------------- Pitching ----------------

        /// <summary>
        /// A party out together stops right where it is to mend a hurt member (P4-71, <see cref="PartyRecovery"/>):
        /// how far from town does not matter, and afterwards each goes back to its quest or hunt.
        /// </summary>
        public bool PitchToMend(List<Adventurer> group)
        {
            if (group == null || group.Count == 0) return false;
            var resume = new Dictionary<Adventurer, State<Adventurer>>();
            foreach (var m in group) resume[m] = PartyRecovery.ResumeOf(m, this);
            PartyRecovery.PauseBands(group);
            if (Pitch(group, CampReason.Recover, resume)) return true;
            foreach (var m in group) if (Services.TryGet<HuntBandService>(out var bands) && bands.BandOf(m) is { } band) band.Paused = false;
            return false;
        }

        /// <summary>Makes camp for <paramref name="group"/> (leader first) near where the leader stands.</summary>
        public bool Pitch(List<Adventurer> group, CampReason reason, Dictionary<Adventurer, State<Adventurer>> resume = null)
        {
            if (group == null || group.Count == 0) return false;
            var leader = group[0];
            if (!PickSpot(leader.Position, out var spot)) return false;
            float hour = _clock.TotalHours;
            var camp = new Camp
            {
                Id = ++_nextId,
                Position = spot,
                Reason = reason,
                PitchedAtHour = hour,
                SitUntilHour = hour + CampRules.SitHours,
            };
            camp.Fire = _pool.Spawn<Campfire>(Campfire.PoolKey, spot);
            camp.Fire.Light();
            foreach (var m in group)
            {
                camp.Members.Add(m);
                camp.ResumeTo[m] = resume != null && resume.TryGetValue(m, out var next) ? next : ResumeFor(m);
            }
            _camps.Add(camp);
            foreach (var m in group)
            {
                m.Engage.Clear();
                m.ChangeState(m.CampState);
            }
            leader.Say(PitchLine(reason, group.Count == 1));
            EventBus.Publish(new CampPitchedEvent { Camp = camp });
            return true;
        }

        static string PitchLine(CampReason reason, bool alone)
        {
            if (alone) return reason == CampReason.Night ? "밤길은 위험해. 혼자라도 불을 피우자." : "더는 못 걷겠어... 여기서 좀 쉬자.";
            return reason == CampReason.Night ? "날이 저물었어. 여기서 밤을 보내자." : "여기서 불 피우고 좀 쉬었다 가자.";
        }

        /// <summary>
        /// A quest goes on after the night; a party that pulled back to mend goes back to what it was
        /// doing (P4-71, <see cref="ReturnToTownState.ResumeAfter"/>); everything else heads home.
        /// </summary>
        static State<Adventurer> ResumeFor(Adventurer m)
        {
            var s = m.Fsm.Current;
            if (s == m.ReturnState && m.ReturnState.ResumeAfter != null) return m.ReturnState.ResumeAfter;
            bool onQuest = (s == m.TravelState || s == m.HuntState) && m.CurrentQuest != null && m.CurrentQuest.IsActive;
            return onQuest ? m.TravelState : m.ReturnState;
        }

        /// <summary>
        /// Somewhere close to sit down: open ground outside town, as far from the dens the guild knows
        /// as it can find nearby (a fire next to a den is asking for it).
        /// </summary>
        bool PickSpot(Vector2 origin, out Vector2 spot)
        {
            spot = origin;
            var rng = GameRandom.Shared;
            Services.TryGet<GuildIntel>(out var intel);
            float best = float.MinValue;
            bool found = false;
            for (int i = 0; i < 12; i++)
            {
                var p = i == 0 ? origin : origin + rng.InsideUnitCircle() * 5f;
                if (!_world.Grid.TryFindNearestWalkable(GridMap.WorldToCell(p), 2, out var cell) || _world.Grid.IsInTown(cell)) continue;
                var at = GridMap.CellToWorld(cell);
                float nest = 25f;
                if (intel != null)
                    foreach (var n in intel.KnownNests) nest = Mathf.Min(nest, Vector2.Distance(n.WorldPosition, at));
                float score = nest - 0.3f * Vector2.Distance(at, origin);
                if (score <= best) continue;
                best = score;
                spot = at;
                found = true;
            }
            return found;
        }

        // ---------------- Running the camps ----------------

        /// <summary>A member leaves the camp (its state changed): the fire goes out with the last one.</summary>
        internal void Leave(Adventurer a)
        {
            var camp = CampOf(a);
            if (camp == null) return;
            camp.Members.Remove(a);
            if (camp.Watcher == a) camp.Watcher = null;
            if (camp.Members.Count == 0) End(camp);
        }

        /// <summary>Raiders at the fire: everyone up (once per raid).</summary>
        internal void Alarm(Camp camp, Unit raider, bool warned)
        {
            camp.QuietSince = Time.time;
            if (camp.Alarmed) return;
            camp.Alarmed = true;
            camp.Raids++;
            EventBus.Publish(new CampRaidedEvent { Camp = camp, Raider = raider, Warned = warned });
        }

        void Update()
        {
            if (_clock == null) return;
            float hour = _clock.TotalHours;
            for (int i = _camps.Count - 1; i >= 0; i--)
            {
                var camp = _camps[i];
                camp.Members.RemoveAll(m => m == null || !m.IsAlive || m.Fsm.Current != m.CampState);
                if (camp.Members.Count == 0) { End(camp); continue; }

                camp.Watcher = WatchOf(camp, hour);

                if (camp.Alarmed)
                {
                    if (camp.Members.Any(m => m.Engage.Target != null || ThreatInSight(m) != null)) camp.QuietSince = Time.time;
                    else if (Time.time - camp.QuietSince >= CampRules.QuietSeconds) camp.Alarmed = false;
                    // A fight that never quite ends — a den in sight of the fire — does not hold the camp past
                    // its time either (P4-77: the alarm skipped the limit, and a camp ran fourteen hours).
                    if (hour - camp.PitchedAtHour >= CampRules.MaxHours) Break(camp, quietly: true);
                    continue;
                }
                if (!camp.Talked && camp.Sitting(hour) && camp.Members.Count >= 2 && camp.Members.All(m => m.CampState.Seated)) StartTalk(camp);
                if (Finished(camp, hour)) Break(camp);
            }

            _admitTimer -= Time.deltaTime;
            if (_admitTimer <= 0f)
            {
                _admitTimer = AdmitInterval;
                for (int i = _camps.Count - 1; i >= 0; i--) AdmitLateComers(_camps[i]);
            }

            _lightTimer -= Time.deltaTime;
            if (_lightTimer > 0f) return;
            _lightTimer = LightInterval;
            foreach (var camp in _camps) DrawToTheLight(camp);
        }

        bool Finished(Camp camp, float hour)
        {
            float lowest = 1f, tiredest = 0f;
            foreach (var m in camp.Members)
            {
                lowest = Mathf.Min(lowest, m.HpRatio);
                tiredest = Mathf.Max(tiredest, m.Data.fatigue);
            }
            return CampRules.Done(camp.Reason, lowest, tiredest, _clock.IsNight, hour - camp.PitchedAtHour);
        }

        /// <summary>
        /// Everyone mended (or morning come, or the time up): the fire is put out and each goes back to its
        /// road. <paramref name="quietly"/>: broken off mid-fight, without the cheerful send-off.
        /// </summary>
        void Break(Camp camp, bool quietly = false)
        {
            var leader = camp.Leader;
            // Everyone's road first: the first one back on the hunt un-pauses the band (P4-71).
            var going = camp.Members.Select(m => (m, next: NextRoad(camp, m))).ToList();
            foreach (var (m, next) in going) m.ChangeState(next);
            if (!quietly && leader != null && leader.IsAlive) leader.Say(camp.Reason == CampReason.Night ? "날이 밝았다. 가자!" : "이제 좀 살겠다. 출발하자!");
            End(camp);
        }

        internal static State<Adventurer> NextRoad(Camp camp, Adventurer m)
        {
            var next = camp.ResumeTo.TryGetValue(m, out var s) && s != null ? s : m.ReturnState;
            if (next == m.TravelState && (m.CurrentQuest == null || !m.CurrentQuest.IsActive)) return m.ReturnState;
            // Back to the hunt only with the band still waiting for it (P4-71), not to muster a new one out here.
            if (next == m.FreeHuntState && !(Services.TryGet<HuntBandService>(out var bands) && bands.BandOf(m) is { Paused: true })) return m.ReturnState;
            return next;
        }

        void End(Camp camp)
        {
            if (!_camps.Remove(camp)) return;
            camp.Members.Clear();
            if (camp.Fire != null) camp.Fire.PutOut();
            EventBus.Publish(new CampBrokenEvent { Camp = camp });
        }

        // ---------------- The firelight ----------------

        /// <summary>
        /// Monsters that see the fire come for it (user decision: only those that actually see the
        /// light). It shows further at night; a forest in between hides it like it hides anything.
        /// </summary>
        void DrawToTheLight(Camp camp)
        {
            if (camp.Fire == null || !camp.Fire.Burning) return;
            float radius = CampRules.LightRadius(_clock.IsNight);
            UnitRegistry.Collect(camp.Position, radius, u => u is Monster m && m.IsAlive, s_units);
            float now = Time.time;
            foreach (var u in s_units)
            {
                var m = (Monster)u;
                if (_drawnUntil.TryGetValue(m, out var until) && now < until) continue;
                var camper = NearestCamper(camp, m);
                if (camper == null || !m.IsHostileTo(camper)) continue;
                if (!_world.CanSee(m.Position, camp.Position, radius)) continue;
                if (!m.DrawnToFirelight(camper)) continue;
                _drawnUntil[m] = now + DrawnCooldown;
                MonstersDrawn++;
            }
            if (_drawnUntil.Count > 64)
                foreach (var dead in _drawnUntil.Keys.Where(k => k == null || !k.IsAlive || now >= _drawnUntil[k]).ToList()) _drawnUntil.Remove(dead);
        }

        static Adventurer NearestCamper(Camp camp, Monster m)
        {
            Adventurer best = null;
            float bestD = float.MaxValue;
            foreach (var c in camp.Members)
            {
                if (!c.IsAlive || !c.IsTargetableBy(m)) continue;
                float d = (c.Position - m.Position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        // ---------------- Talk at the fire ----------------

        void StartTalk(Camp camp)
        {
            camp.Talked = true;
            if (!Services.TryGet<BanterService>(out var banter)) return;
            var a = camp.Members[GameRandom.Shared.Range(0, camp.Members.Count)];
            var others = camp.Members.Where(m => m != a).ToList();
            var b = others[GameRandom.Shared.Range(0, others.Count)];
            banter.TryStartAt(a, b, x => x != null && x.IsAlive && CampOf(x) == camp);
        }
    }
}
