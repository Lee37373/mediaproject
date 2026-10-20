using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Dragons;
using GuildMaster.Units;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Monsters
{
    /// <summary>
    /// What happens to the dragon when it is hit or taunted (P4-93; <see cref="Monster.OnDamaged"/> and
    /// <see cref="Monster.ForceTarget"/> hand the dragon here instead of the pack rules).
    /// </summary>
    public static class DragonStates
    {
        /// <summary>
        /// Hit: asleep it wakes and fights on the ground; on the wing the blow counts towards bringing it down and it
        /// turns on whoever struck; on the ground it fights back.
        /// </summary>
        internal static void OnHit(Monster m, float amount, Unit source)
        {
            if (!m.IsAlive) return;
            var s = m.Fsm.Current;
            if (s == m.DragonSleep) Wake(m, source);
            else if (s == m.DragonFlight)
            {
                m.AirDamage += amount;
                m.DragonFlight.TurnOn(m, source);
            }
            else if (s == m.DragonLanded && Fresh(m, source) && (m.Engage.Target == null || !m.Engage.Target.IsAlive))
                m.Engage.SetTarget(source);
        }

        /// <summary>Taunted (a warrior's call): it turns on the one calling, however it is.</summary>
        internal static void Provoke(Monster m, Unit target)
        {
            var s = m.Fsm.Current;
            if (s == m.DragonSleep) Wake(m, target);
            else if (s == m.DragonFlight) m.DragonFlight.TurnOn(m, target);
            else if (s == m.DragonLanded) m.Engage.Force(target, 4f);
        }

        /// <summary>
        /// Woken at home (a blow, a raider creeping close or reaching into its hoard): it fights on the ground first, then
        /// flies — round its lair, or after the thief (<see cref="DragonLandedState"/>).
        /// </summary>
        internal static void Wake(Monster m, Unit by)
        {
            if (Fresh(m, by)) m.Engage.SetTarget(by);
            m.DragonLanded.WokenAtHome = true;
            m.Fsm.ChangeState(m.DragonLanded);
            if (Services.TryGet<DragonService>(out var svc)) svc.OnWokenByAttack(by);
        }

        static bool Fresh(Monster m, Unit u) => u != null && u.IsAlive && u.IsTargetableBy(m) && u != m;
    }

    /// <summary>Asleep on its hoard (P4-93): sees nothing, heals by the hour (<see cref="DragonService"/>), wakes when hit or when its day comes.</summary>
    public class DragonSleepState : State<Monster>
    {
        public override string Label => "잠";

        public override void Enter()
        {
            var m = Owner;
            m.ShowPose(DragonPose.Sleeping);
            m.Mover.Stop();
            m.Engage.Clear();
            m.AirDamage = 0f;
        }
    }

    /// <summary>
    /// On the wing (P4-93): flies straight over anything (no paths) along the day's round (<see cref="DragonService"/>),
    /// and whatever it sees from the sky — people, monsters, buildings, a village — it circles and burns for a while
    /// before flying on. Shots that hurt it enough bring it down to fight on the ground (<see cref="Dragon.ShouldLand"/>);
    /// badly hurt, or when the day's flight is over, it flies home and sleeps.
    /// </summary>
    public class DragonFlightState : State<Monster>
    {
        const float ScanSeconds = 0.4f;
        /// <summary>Close enough to a waypoint (it banks round, so it cannot hit a point exactly).</summary>
        const float ArriveDistance = 2f;
        /// <summary>Degrees a second it circles what it burns.</summary>
        const float CircleSpeed = 25f;

        Unit _unit;
        Burnable _structure;
        float _scanTimer, _encounterUntil, _avoidUntil;
        /// <summary>Where the present encounter is, and the last place it left (left alone a while, <see cref="Dragon.AvoidSeconds"/>).</summary>
        Vector2 _encounterAt, _avoidAt;
        bool _landing;
        Vector2 _landingSpot;
        /// <summary>Where it is heading (radians; NaN until it first moves) and the phase of its weaving.</summary>
        float _heading = float.NaN, _weave;

        public override string Label => _landing ? "내려오는 중" : _unit != null || _structure.Exists ? "불 뿜는 중" : "비행";

        /// <summary>What it is burning now (a unit, else a building, gate or village; debug and the inspector).</summary>
        public string TargetName => _unit != null ? _unit.DisplayName : _structure.Name;

        public override void Enter()
        {
            var m = Owner;
            m.ShowPose(DragonPose.Flying);
            m.Mover.Stop();
            m.Engage.Clear();
            m.AirDamage = 0f;
            _unit = null;
            _structure = default;
            _scanTimer = 0f;
            _landing = false;
            _encounterUntil = _avoidUntil = 0f;
            _heading = float.NaN;
        }

        /// <summary>A target here: the encounter going on carries on (its clock is the place's), else one starts here.</summary>
        void Meet(Vector2 at, float seconds)
        {
            if (Time.time < _encounterUntil) return;
            _encounterUntil = Time.time + seconds;
            _encounterAt = at;
        }

        public override void Exit() => _unit = null;

        /// <summary>Struck or taunted on the wing: it goes for that one (unless it is flying home to lick its wounds).</summary>
        internal void TurnOn(Monster m, Unit by)
        {
            if (by == null || !by.IsAlive || !by.IsTargetableBy(m) || by == m || _landing) return;
            if (Services.TryGet<DragonService>(out var svc) && svc.Retreating) return;
            if (_unit == by) return;
            _unit = by;
            _structure = default;
            Meet(by.Position, Dragon.EncounterSeconds);
        }

        public override void Tick(float dt)
        {
            var m = Owner;
            if (!Services.TryGet<DragonService>(out var svc)) return;
            bool channeling = IsChanneling(m);

            // Brought down by shots: glide to open ground and fight there.
            if (!_landing && Dragon.ShouldLand(m.AirDamage, m.Stats.MaxHp) && !svc.Retreating && FindLanding(m, out _landingSpot))
            {
                _landing = true;
                _unit = null;
                _structure = default;
            }
            if (_landing)
            {
                if (channeling) return;
                if (FlyTo(m, _landingSpot, dt, weave: false))
                {
                    m.transform.position = new Vector3(_landingSpot.x, _landingSpot.y, m.transform.position.z);
                    m.Engage.Clear();
                    if (m.LastAttacker != null && m.LastAttacker.IsAlive && m.LastAttacker.IsTargetableBy(m)) m.Engage.SetTarget(m.LastAttacker);
                    m.DragonLanded.WokenAtHome = false;
                    Machine.ChangeState(m.DragonLanded);
                }
                return;
            }

            svc.CheckFlight(m);
            if (svc.Retreating) { _unit = null; _structure = default; }
            else
            {
                _scanTimer -= dt;
                if (_scanTimer <= 0f)
                {
                    _scanTimer = ScanSeconds;
                    Scan(m, svc.Homebound);
                }
            }

            bool burning = TryGetTarget(m, out var aim);
            if (burning)
            {
                if (!channeling) UseFire(m, aim);
                if (channeling || IsChanneling(m))
                {
                    m.Visual.FaceTowards(aim);
                    return;
                }
                var from = m.Position - aim;
                if (from.sqrMagnitude < 0.01f) from = Vector2.right;
                float a = Mathf.Atan2(from.y, from.x) + CircleSpeed * Mathf.Deg2Rad * dt;
                var hover = aim + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Dragon.HoverDistance;
                FlyTo(m, hover, dt, weave: false);
                m.Visual.FaceTowards(aim);
                return;
            }
            if (channeling) return;
            var goal = svc.NextWaypoint;
            if (!FlyTo(m, goal, dt)) return;
            if (svc.IsHome(goal))
            {
                // Home — unless its hoard was robbed meanwhile: then it is off after the thief.
                if (svc.Settle(m)) Machine.ChangeState(m.DragonSleep);
            }
            else svc.NextLeg();
        }

        static bool IsChanneling(Monster m)
        {
            foreach (var s in m.Skills) if (s.IsChanneling) return true;
            return false;
        }

        /// <summary>
        /// What it does to what it circles (P4-95): roars when several are round it (they freeze, and the fire that follows
        /// finds them standing), rains fire on the area, bombs a line through it, and breathes between — one a tick.
        /// </summary>
        static void UseFire(Monster m, Vector2 aim)
        {
            if (m.FindReadySkill<DragonRoarSkill>() is { } roar && roar.TryUse(m, null)) return;
            if (m.FindReadySkill<DragonFireRainSkill>() is { } rain && rain.TryRainOn(m, aim)) return;
            if (m.FindReadySkill<DragonBombingSkill>() is { } bomb && bomb.TryBombAt(m, aim)) return;
            if (m.FindReadySkill<DragonBreathSkill>() is { } breath && Vector2.Distance(m.Position, aim) <= Dragon.BreathRange) breath.TryBreatheAt(m, aim);
        }

        /// <summary>
        /// Flies at the point over anything, like a big flier rather than an arrow (user report 2026-10-20: "너무 직선으로"): it
        /// banks round at most <see cref="Dragon.TurnRate"/> a second, so every change of course is an arc, slows a little in a
        /// hard bank, and on a long way weaves from side to side (<see cref="Dragon.WeaveAngle"/>, fading out near the point so
        /// it still gets there). True once there.
        /// </summary>
        bool FlyTo(Monster m, Vector2 goal, float dt, bool weave = true)
        {
            var pos = m.Position;
            var d = goal - pos;
            float dist = d.magnitude;
            if (dist <= ArriveDistance) return true;
            float want = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            if (weave)
            {
                _weave += Dragon.WeaveSpeed * dt;
                float fade = Mathf.Clamp01((dist - 6f) / 20f);
                want += (Mathf.Sin(_weave) + 0.5f * Mathf.Sin(_weave * 0.37f + 1.3f)) * Dragon.WeaveAngle * fade;
            }
            if (float.IsNaN(_heading)) _heading = want;
            float turn = Mathf.DeltaAngle(_heading, want);
            // Near the point it slows and banks harder, or it would circle round it for ever (its turning circle must stay
            // well inside the distance it calls arriving).
            float near = Mathf.Clamp01(dist / 10f);
            float rate = Dragon.TurnRate * Mathf.Lerp(3f, 1f, near);
            _heading += Mathf.Clamp(turn, -rate * dt, rate * dt);
            float speed = Dragon.FlightSpeed * Mathf.Lerp(1f, 0.7f, Mathf.Abs(turn) / 180f) * Mathf.Lerp(0.4f, 1f, near);
            var dir = new Vector2(Mathf.Cos(_heading * Mathf.Deg2Rad), Mathf.Sin(_heading * Mathf.Deg2Rad));
            var next = pos + dir * speed * dt;
            if (Services.TryGet<WorldService>(out var world))
            {
                next.x = Mathf.Clamp(next.x, 1f, world.Map.Width - 1f);
                next.y = Mathf.Clamp(next.y, 1f, world.Map.SurfaceHeight - 1f);
            }
            m.transform.position = new Vector3(next.x, next.y, m.transform.position.z);
            m.Visual.FaceTowards(next + dir);
            return false;
        }

        /// <summary>
        /// Keeps on what it is burning while the encounter lasts; then flies on and leaves the place alone a while. A unit
        /// gone or out of sight, or a structure fallen, ends only that target (the next one there is taken on the same clock).
        /// </summary>
        bool TryGetTarget(Monster m, out Vector2 aim)
        {
            aim = default;
            if (_unit != null || _structure.Exists)
            {
                bool over = Time.time >= _encounterUntil;
                bool gone = _unit != null
                    ? !_unit.IsAlive || !_unit.IsTargetableBy(m) || (_unit.Position - m.Position).sqrMagnitude > Sq(Dragon.AirSight * 1.5f)
                    : _structure.Fallen;
                if (over || gone)
                {
                    if (over)
                    {
                        _avoidAt = _encounterAt;
                        _avoidUntil = Time.time + Dragon.AvoidSeconds;
                    }
                    _unit = null;
                    _structure = default;
                }
            }
            // Robbed: the thief, wherever it is, no matter how long (user decision 2026-10-20).
            if (Services.TryGet<DragonService>(out var svc) && svc.Quarry is { } quarry && !svc.Retreating)
            {
                _unit = quarry;
                _structure = default;
                aim = quarry.Position;
                return true;
            }
            if (_unit != null) { aim = _unit.Position; return true; }
            if (_structure.Exists) { aim = _structure.Aim(m.Position); return true; }
            return false;
        }

        static float Sq(float x) => x * x;

        /// <summary>
        /// Looks down for something to burn, away from the place it last left: the nearest person in its sight first (the
        /// guild, travellers), then the nearest monster, then the nearest building, gate or village. Flying home it only
        /// minds people (a pack on every hill held it up for hours on the way back).
        /// </summary>
        void Scan(Monster m, bool homebound)
        {
            if (_unit != null || _structure.Exists) return;
            bool avoiding = Time.time < _avoidUntil;
            float avoid2 = Sq(Dragon.AvoidRadius);
            Unit person = null, beast = null;
            float personD = float.MaxValue, beastD = float.MaxValue;
            float r2 = Sq(Dragon.AirSight);
            foreach (var u in UnitRegistry.All)
            {
                if (u == m || !u.IsAlive || u.IsAirborne || !u.IsTargetableBy(m) || !m.IsHostileTo(u)) continue;
                float d = (u.Position - m.Position).sqrMagnitude;
                if (d > r2 || avoiding && (u.Position - _avoidAt).sqrMagnitude < avoid2) continue;
                if (u is Monster) { if (!homebound && d < beastD) { beastD = d; beast = u; } }
                else if (d < personD) { personD = d; person = u; }
            }
            var pick = person ?? beast;
            if (pick != null)
            {
                _unit = pick;
                Meet(pick.Position, person != null ? Dragon.EncounterSeconds : Dragon.MonsterEncounterSeconds);
                return;
            }
            if (!homebound && Burnable.Nearest(m.Position, Dragon.AirSight, out var b) && !(avoiding && (b.Aim(m.Position) - _avoidAt).sqrMagnitude < avoid2))
            {
                _structure = b;
                Meet(b.Aim(m.Position), Dragon.EncounterSeconds);
            }
        }

        /// <summary>The nearest open cell below (not water or rock) to come down on, within a few cells.</summary>
        static bool FindLanding(Monster m, out Vector2 spot)
        {
            spot = m.Position;
            if (!Services.TryGet<WorldService>(out var world)) return false;
            if (!world.Grid.TryFindNearestWalkable(GridMap.WorldToCell(m.Position), 6, out var cell)) return false;
            spot = GridMap.CellToWorld(cell);
            return true;
        }

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class Saved
        {
            public string unit;
            public bool landing;
            public Vector2 landing_spot;
            public float encounter_until;
            public Vector2 encounter_at;
            public float avoid_until;
            public Vector2 avoid_at;
            public float air_damage;
        }

        public override string SaveState() => JsonUtility.ToJson(new Saved
        {
            unit = _unit != null && _unit.IsAlive ? _unit.UnitId : null, landing = _landing, landing_spot = _landingSpot,
            encounter_until = SaveTime.Rel(_encounterUntil), encounter_at = _encounterAt, avoid_until = SaveTime.Rel(_avoidUntil), avoid_at = _avoidAt,
            air_damage = Owner.AirDamage,
        });

        public override void ResumeState(string json, SaveContext ctx)
        {
            Enter();
            var s = JsonUtility.FromJson<Saved>(json);
            _unit = ctx.Find<Unit>(s.unit);
            _landing = s.landing;
            _landingSpot = s.landing_spot;
            _encounterUntil = SaveTime.Abs(s.encounter_until);
            _encounterAt = s.encounter_at;
            _avoidUntil = SaveTime.Abs(s.avoid_until);
            _avoidAt = s.avoid_at;
            Owner.AirDamage = s.air_damage;
        }
    }

    /// <summary>
    /// Down on the ground (P4-93): brought down by shots, or woken at home. It fights like any monster — bites, and its
    /// fire (<see cref="DragonBreathSkill"/>) — and anyone can reach it. After <see cref="Dragon.LandedSeconds"/> it takes off
    /// again: back to its round, or (woken at home) a flight round its lair.
    /// </summary>
    public class DragonLandedState : State<Monster>
    {
        const float LookSeconds = 0.4f;
        float _lookTimer;

        /// <summary>It was asleep at home when the fight came to it (the flight after it circles the lair).</summary>
        internal bool WokenAtHome;

        public override string Label => "땅에서 싸우는 중";

        public override void Enter()
        {
            var m = Owner;
            m.ShowPose(DragonPose.Standing);
            m.Mover.Stop();
            m.Mover.SpeedMultiplier = 1f;
            _lookTimer = 0f;
        }

        public override void Exit() => Owner.Mover.Stop();

        public override void Tick(float dt)
        {
            var m = Owner;
            bool channeling = false;
            foreach (var s in m.Skills) channeling |= s.IsChanneling;
            if (TimeInState >= Dragon.LandedSeconds && !channeling)
            {
                if (Services.TryGet<DragonService>(out var svc)) svc.TakeOff(m, WokenAtHome);
                WokenAtHome = false;
                Machine.ChangeState(m.DragonFlight);
                return;
            }
            if (m.Engage.Target != null && m.Engage.Tick(dt) != EngageStatus.Lost) return;
            _lookTimer -= dt;
            if (_lookTimer > 0f) return;
            _lookTimer = LookSeconds;
            var next = m.FindTarget();
            if (next != null) m.Engage.SetTarget(next);
            else if (m.Mover.IsMoving) m.Mover.Stop();
        }

        [System.Serializable]
        class Saved { public bool woken_at_home; }

        public override string SaveState() => JsonUtility.ToJson(new Saved { woken_at_home = WokenAtHome });

        public override void ResumeState(string json, SaveContext ctx)
        {
            WokenAtHome = JsonUtility.FromJson<Saved>(json).woken_at_home;
            Enter();
        }
    }

    /// <summary>Something standing the dragon can set alight (P4-93): one of our buildings, a town gate, or a village.</summary>
    internal readonly struct Burnable
    {
        readonly Town.Building _building;
        readonly Town.TownGate _gate;
        readonly Villages.Village _village;

        Burnable(Town.Building building, Town.TownGate gate, Villages.Village village)
        {
            _building = building;
            _gate = gate;
            _village = village;
        }

        public bool Exists => _building != null || _gate != null || _village != null;
        public bool Fallen => _building != null ? _building.IsDestroyed : _gate != null ? _gate.IsBroken : _village == null || _village.IsDestroyed;
        public string Name => _building != null ? _building.DisplayName : _gate != null ? _gate.DisplayName : _village != null ? _village.DisplayName : "";

        /// <summary>Where to breathe so the blast takes it: its middle.</summary>
        public Vector2 Aim(Vector2 from) => _building != null ? _building.Center : _gate != null ? _gate.Center : _village.WorldPosition;

        /// <summary>The nearest standing one within <paramref name="radius"/> of <paramref name="at"/>.</summary>
        public static bool Nearest(Vector2 at, float radius, out Burnable best)
        {
            best = default;
            float bestD = radius;
            if (Services.TryGet<Town.TownService>(out var town))
                foreach (var b in town.Buildings)
                {
                    if (b == null || b.IsDestroyed) continue;
                    float d = Vector2.Distance(b.ClosestPoint(at), at);
                    if (d <= bestD) { bestD = d; best = new Burnable(b, null, null); }
                }
            if (Services.TryGet<Town.GateService>(out var gates))
                foreach (var g in gates.Gates)
                {
                    if (g == null || g.IsBroken) continue;
                    float d = Vector2.Distance(g.ClosestPoint(at), at);
                    if (d <= bestD) { bestD = d; best = new Burnable(null, g, null); }
                }
            if (Services.TryGet<Villages.VillageService>(out var villages))
                foreach (var v in villages.Villages)
                {
                    if (v == null || v.IsDestroyed) continue;
                    float d = Vector2.Distance(v.WorldPosition, at);
                    if (d <= bestD) { bestD = d; best = new Burnable(null, null, v); }
                }
            return best.Exists;
        }
    }
}
