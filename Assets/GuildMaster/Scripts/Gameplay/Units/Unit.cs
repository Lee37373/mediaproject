using System;
using GuildMaster.Core;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Units
{
    public enum Faction { Guild, Monster, Visitor }

    [Serializable]
    public struct CombatStats
    {
        public float MaxHp;
        public float Attack;
        public float Defense;
        public float AttackRange;
        public float AttackCooldown;
        public float MoveSpeed;
        public float SightRadius;

        public bool IsRanged => AttackRange > 2f;
    }

    public struct UnitDamagedEvent
    {
        public Unit Target;
        public Unit Source;
        public float Amount;
        public bool Critical;
        /// <summary>A step of poison (P4-87), not a blow: it leaves no new mark and shows as a small green number.</summary>
        public bool OverTime;
    }

    public struct UnitDiedEvent
    {
        public Unit Victim;
        public Unit Killer;
    }

    /// <summary>
    /// Base for every combat-capable actor (monsters, adventurers). Handles health,
    /// hostility, registration, and shared components. Behaviour lives in subclasses' FSMs.
    /// </summary>
    [RequireComponent(typeof(PathFollower))]
    public abstract class Unit : MonoBehaviour
    {
        public string UnitId { get; protected set; }
        public string DisplayName { get; protected set; }
        public Faction Faction { get; protected set; }
        /// <summary>
        /// Not of the guild, but out on the road where monsters that go for people fall on it (P4-84: a refugee
        /// from another village). Visitors in town and the merchant stay untouched.
        /// </summary>
        public virtual bool ExposedToMonsters => false;
        public CombatStats Stats;
        public float Hp { get; protected set; }
        public bool IsAlive => !_dead && Hp > 0f;
        public float HpRatio => Stats.MaxHp <= 0f ? 0f : Hp / Stats.MaxHp;

        public PathFollower Mover { get; private set; }
        public CharacterVisual Visual { get; private set; }

        /// <summary>Last unit that damaged this one and when (scaled time).</summary>
        public Unit LastAttacker { get; private set; }
        public float LastAttackedTime { get; private set; } = -999f;

        /// <summary>Scaled time at which the next attack is allowed.</summary>
        public float NextAttackTime { get; set; }

        public event Action<Unit> Died;

        bool _dead;

        public Vector2 Position => transform.position;

        // ---------------- Tactical state ----------------

        /// <summary>Body radius for crowd separation and melee spacing.</summary>
        public float Radius { get; protected set; } = 0.3f;
        /// <summary>Heavier units are pushed less by crowding.</summary>
        public float Mass { get; protected set; } = 1f;
        public readonly System.Collections.Generic.List<Skill> Skills = new();

        public float StunnedUntil { get; private set; }
        public bool IsStunned => Time.time < StunnedUntil;

        /// <summary>Poison, broken armour and chill left by the guild's facilities (P4-87).</summary>
        public Afflictions Afflictions { get; } = new();
        /// <summary>Defence as blows meet it (broken armour taken off).</summary>
        public float EffectiveDefense => Stats.Defense * (1f - Afflictions.ArmorBreak);
        /// <summary>Seconds between attacks as they are now (a chill draws them out).</summary>
        public float AttackCooldownNow => Stats.AttackCooldown / Mathf.Max(0.1f, 1f - Afflictions.Slow);

        float _speedBuff = 1f;
        float _speedBuffUntil;
        Vector2 _knockVelocity;
        float _knockUntil;

        /// <summary>Combat intelligence tier (target choice, chase give-up, skill timing).</summary>
        public virtual CombatTier Tier => CombatTier.Novice;

        /// <summary>
        /// In the air (P4-93: the dragon on the wing): only shots reach it (<see cref="Combat.CanReach"/>), crowds do not
        /// push it, and ground hazards do not touch it.
        /// </summary>
        public virtual bool IsAirborne => false;

        /// <summary>Fight behaviour, if this unit has one (debug gizmos).</summary>
        public virtual Engagement CombatEngagement => null;
        /// <summary>Current behaviour state for debug labels.</summary>
        public virtual string DebugState => "";

        void OnDrawGizmos() => UnitGizmos.Draw(this, false);
        void OnDrawGizmosSelected() => UnitGizmos.Draw(this, true);

        /// <summary>Unit currently targeted by this unit (for UI, taunts, focus fire).</summary>
        public Unit CurrentTarget
        {
            get => _currentTarget;
            internal set
            {
                if (ReferenceEquals(_currentTarget, value)) return;
                if (!ReferenceEquals(_currentTarget, null)) _currentTarget._targetedBy.Remove(this);
                _currentTarget = value;
                if (!ReferenceEquals(value, null)) value._targetedBy.Add(this);
            }
        }

        Unit _currentTarget;
        readonly System.Collections.Generic.List<Unit> _targetedBy = new();

        /// <summary>
        /// Units whose <see cref="CurrentTarget"/> is this one, alive or not (P5-2: counting who
        /// piles onto a target used to scan every unit around it).
        /// </summary>
        public Core.ListView<Unit> TargetedBy => new(_targetedBy);

        // ---------------- Group roles (P4-27) ----------------

        /// <summary>Target a leader called for focus fire (party leader, pack leader); scored higher by TargetSelector.</summary>
        public virtual Unit CalledTarget => null;
        /// <summary>Fights from the back line (ranged roles); allied front-liners protect it.</summary>
        public virtual bool IsBackLine => Stats.IsRanged;
        /// <summary>Front-liner that goes for enemies attacking its back line.</summary>
        public virtual bool GuardsBackLine => false;

        public void Stun(float seconds)
        {
            StunnedUntil = Mathf.Max(StunnedUntil, Time.time + seconds);
            Mover.Stop();
        }

        public void ApplySpeedBuff(float multiplier, float seconds)
        {
            _speedBuff = multiplier;
            _speedBuffUntil = Time.time + seconds;
        }

        /// <summary>Pushes the unit over ~0.15s, stopping at unwalkable terrain.</summary>
        public void Knockback(Vector2 direction, float distance)
        {
            if (!IsAlive || distance <= 0f) return;
            const float duration = 0.15f;
            _knockVelocity = direction.normalized * (distance / duration) / Mathf.Max(0.5f, Mass);
            _knockUntil = Time.time + duration;
        }

        /// <summary>Whether intact town gates open for this unit (P4-39): monsters must break them.</summary>
        public bool PassesGates => Mover == null || Mover.PassesGates;

        /// <summary>Forces this unit to attack a specific target (taunts, pack calls). Override per AI.</summary>
        public virtual void ForceTarget(Unit target) { }

        protected void TickStatus(float dt)
        {
            Mover.BuffMultiplier = (Time.time < _speedBuffUntil ? _speedBuff : 1f) * (1f - Afflictions.Slow);
            Afflictions.Tick(this, dt);
            if (Visual != null) Visual.StatusTint = Afflictions.Tint;
            if (Time.time < _knockUntil)
            {
                var next = Position + _knockVelocity * dt;
                if (Core.Services.TryGet<WorldService>(out var world) && world.Grid.IsWalkable(GridMap.WorldToCell(next), PassesGates))
                    transform.position = new Vector3(next.x, next.y, transform.position.z);
            }
            // By index: a skill's tick can end in a level-up that re-deals the skills (SkillFactory.Assign),
            // which threw "collection was modified" in a 5-day run (2026-09-23).
            for (int i = 0; i < Skills.Count; i++) Skills[i].Tick(this, dt);
            // Stealth runs out on its own; put the sprite back when it does (P4-19).
            if (!IsHidden && Visual != null && Visual.Fade < 1f) BreakStealth();
        }

        /// <summary>Tries each ready skill in order; returns true if one was used.</summary>
        public bool TryUseSkills(Unit target)
        {
            if (IsStunned) return false;
            foreach (var s in Skills)
                if (s.IsReady && !HoldAreaSkill(s, target) && s.TryUse(this, target)) return true;
            return false;
        }

        /// <summary>Support skills (heals) outside fights: returns true if one was used.</summary>
        public bool TryUseSupportSkills()
        {
            if (IsStunned) return false;
            foreach (var s in Skills)
                if (s.IsSupport && s.IsReady && s.TryUse(this, null)) return true;
            return false;
        }

        /// <summary>
        /// Heroic units save area skills for groups: skip while the blast would hit a single enemy
        /// although several are nearby (they may bunch up in a moment).
        /// </summary>
        bool HoldAreaSkill(Skill s, Unit target)
        {
            if (Tier < CombatTier.Heroic || s.AreaRadius <= 0f || target == null || !s.SavedForGroups) return false;
            var center = s.AreaOnSelf ? Position : target.Position;
            int inBlast = UnitRegistry.CountInRadius(center, s.AreaRadius, u => u != this && IsHostileTo(u));
            int nearby = UnitRegistry.CountInRadius(Position, 6f, u => u != this && IsHostileTo(u));
            return inBlast < 2 && nearby >= 2;
        }

        protected virtual void Awake()
        {
            Mover = GetComponent<PathFollower>();
            Visual = GetComponent<CharacterVisual>();
            if (Visual == null) Visual = gameObject.AddComponent<CharacterVisual>();
        }

        protected virtual void OnEnable() => UnitRegistry.Register(this);

        protected virtual void OnDisable()
        {
            UnitRegistry.Unregister(this);
            // Off its target's list of attackers: a destroyed member was still counted round a monster it had been
            // fighting, and scoring that monster read the destroyed object (a trial run threw 1700 times, P4-91).
            CurrentTarget = null;
        }

        /// <summary>
        /// Leaves the world for good without dying (a member who quits, a visitor who walks off): call just before its
        /// object is destroyed, so whoever still points at it reads it as gone rather than touching a destroyed object.
        /// </summary>
        public void Vanish()
        {
            _dead = true;
            CurrentTarget = null;
        }

        /// <summary>Resets health/state for (re)spawn.</summary>
        protected void ResetVitals()
        {
            _dead = false;
            Hp = Stats.MaxHp;
            LastAttacker = null;
            LastAttackedTime = -999f;
            NextAttackTime = 0f;
            StunnedUntil = 0f;
            _speedBuffUntil = 0f;
            _knockUntil = 0f;
            Afflictions.Clear();
            if (Visual != null) Visual.StatusTint = Color.white;
            CurrentTarget = null;
            foreach (var s in Skills) s.ResetCooldown(GameRandomStagger());
            if (Mover) Mover.Speed = Stats.MoveSpeed;
        }

        static float GameRandomStagger() => Core.GameRandom.Shared.Range(0f, 2f);

        public abstract bool IsHostileTo(Unit other);

        /// <summary>
        /// Sight of another unit: within the sight radius (a third of it inside a forest), and no
        /// mountain, wall, building or — seen from outside — forest in between; up to two trees are
        /// fine (P4-48, <see cref="ForestSight"/>). Use when picking new targets by sight. The guild also sees whatever
        /// stands in a flare's light near it (P4-94, <see cref="Flares.FlareService.LightsFor"/>).
        /// </summary>
        public bool CanSee(Unit other)
            => other != null && (!Core.Services.TryGet<WorldService>(out var world) || world.CanSee(Position, other.Position, Stats.SightRadius)
                                 || Faction == Faction.Guild && Flares.FlareService.LightsFor(Position, other.Position));

        /// <summary>Same sight rule for a point (e.g. an item on the ground).</summary>
        public bool CanSee(Vector2 point)
            => !Core.Services.TryGet<WorldService>(out var world) || world.CanSee(Position, point, Stats.SightRadius)
               || Faction == Faction.Guild && Flares.FlareService.LightsFor(Position, point);

        /// <summary>How far this unit sees where it stands now (a third of its sight inside a forest).</summary>
        public float EffectiveSightRadius
            => Core.Services.TryGet<WorldService>(out var world) ? ForestSight.Radius(world, Position, Stats.SightRadius) : Stats.SightRadius;

        /// <summary>True if the other unit hit us within the given seconds (retaliation).</summary>
        public bool WasRecentlyAttackedBy(Unit other, float seconds = 8f)
            => other != null && LastAttacker == other && Time.time - LastAttackedTime < seconds;

        /// <summary>Fighting right now: a live target, or hit within the last <paramref name="grace"/> seconds.</summary>
        public bool IsInFight(float grace = 3f)
            => CurrentTarget != null && CurrentTarget.IsAlive
               || LastAttacker != null && WasRecentlyAttackedBy(LastAttacker, grace);

        /// <summary>Directly sets HP (e.g. keeping the ratio after a max-HP change). Does not kill.</summary>
        protected void SetHp(float hp)
        {
            if (!IsAlive) return;
            Hp = Mathf.Clamp(hp, 1f, Stats.MaxHp);
        }

        public void Heal(float amount)
        {
            if (!IsAlive) return;
            Hp = Mathf.Min(Stats.MaxHp, Hp + amount);
        }

        /// <summary>
        /// Debug/testing (F6, DebugCommands.Invulnerable): guild-side units (guild master, members) take
        /// no damage. Hits still count as attacks, so retaliation and targeting behave as usual.
        /// </summary>
        public static bool GuildInvulnerable;
        /// <summary>
        /// Debug/testing (F6): with <see cref="GuildInvulnerable"/>, enemies cannot target guild units at
        /// all (no picking, retaliation or taunts). Off = damage immunity only (e.g. lure tests).
        /// </summary>
        public static bool GuildUntargetable;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetDebugStatics() => GuildInvulnerable = GuildUntargetable = false;

        // ---------------- Stealth (P4-19) ----------------

        float _hiddenUntil;

        /// <summary>Hidden right now: enemies cannot pick it as a target and it is drawn faded.</summary>
        public bool IsHidden => Time.time < _hiddenUntil;

        /// <summary>Disappears for a few seconds; whoever was chasing it loses the thread.</summary>
        public void EnterStealth(float seconds)
        {
            _hiddenUntil = Time.time + Mathf.Max(0f, seconds);
            if (Visual != null) Visual.Fade = 0.35f;
        }

        /// <summary>Back in plain sight (it attacked, or the time ran out).</summary>
        public void BreakStealth()
        {
            _hiddenUntil = 0f;
            if (Visual != null) Visual.Fade = 1f;
        }

        /// <summary>False when the attacker may not pick this unit as a target (hidden, or debug untargetable).</summary>
        public bool IsTargetableBy(Unit attacker)
        {
            // Hidden from anyone not on its own side; allies still see it.
            if (IsHidden && attacker != null && attacker.Faction != Faction) return false;
            return !(GuildInvulnerable && GuildUntargetable && Faction == Faction.Guild
                     && attacker != null && attacker.Faction != Faction.Guild);
        }

        /// <summary>A known skill of this type that is off cooldown, or null (tactics ask for these).</summary>
        public T FindReadySkill<T>() where T : Skill
        {
            if (IsStunned) return null;
            foreach (var s in Skills)
                if (s is T typed && s.IsReady) return typed;
            return null;
        }

        /// <summary>Uses a known skill of this type on purpose (tactics ask for it); false when it cannot.</summary>
        public bool TryUseSkill<T>(Unit target) where T : Skill
        {
            var skill = FindReadySkill<T>();
            return skill != null && skill.TryUseDeliberately(this, target);
        }

        /// <summary>
        /// How much of a direct hit from <c>source</c> lands on <c>target</c> (P4-107: a kind's weakness and strength by class,
        /// and the guild's mastery of a kind); null = all of it. Set by the bestiary; marks over time are not touched.
        /// </summary>
        public static System.Func<Unit, Unit, float> DamageFactor;

        internal void ReceiveDamage(float amount, Unit source, bool critical, bool overTime = false)
        {
            if (!IsAlive) return;
            if (!overTime && source != null && amount > 0f && DamageFactor != null)
                amount = Mathf.Max(1f, Mathf.Round(amount * DamageFactor(source, this)));
            if (GuildInvulnerable && Faction == Faction.Guild) amount = 0f;
            Hp -= amount;
            if (source != null)
            {
                LastAttacker = source;
                LastAttackedTime = Time.time;
            }
            if (!overTime) Visual?.Flash();
            EventBus.Publish(new UnitDamagedEvent { Target = this, Source = source, Amount = amount, Critical = critical, OverTime = overTime });
            OnDamaged(amount, source);
            if (Hp <= 0f)
            {
                Hp = 0f;
                _dead = true;
                // Marks end with the life (the corpse fades from its own colour, not the poison's).
                Afflictions.Clear();
                if (Visual != null) Visual.StatusTint = Color.white;
                Mover.Stop();
                OnDeath(source);
                Died?.Invoke(this);
                EventBus.Publish(new UnitDiedEvent { Victim = this, Killer = source });
            }
        }

        protected virtual void OnDamaged(float amount, Unit source) { }
        protected abstract void OnDeath(Unit killer);

        // ---------------- Save (P5-4) ----------------

        /// <summary>
        /// What every unit carries besides its own data: where it stands, its health, and the
        /// moments it waits for (as seconds from now, see <see cref="SaveTime"/>). Only the living are saved.
        /// </summary>
        [Serializable]
        public class UnitSave
        {
            public float x;
            public float y;
            public float hp;
            public string last_attacker;
            public float last_attacked;
            public float next_attack;
            public float stunned_until;
            public float speed_buff = 1f;
            public float speed_buff_until;
            public float hidden_until;
            public float grace_until;
            /// <summary>Seconds left on each skill, in the order of <see cref="Skills"/>.</summary>
            public System.Collections.Generic.List<float> skill_cooldowns = new();
            /// <summary>Marks from the guild's facilities (P4-87); all zero when none.</summary>
            public Afflictions.SaveData afflictions;
        }

        public UnitSave CaptureUnit()
        {
            var p = Position;
            var s = new UnitSave
            {
                x = Round(p.x), y = Round(p.y), hp = Round(Hp),
                last_attacker = LastAttacker != null && LastAttacker.IsAlive ? LastAttacker.UnitId : null,
                last_attacked = SaveTime.Rel(LastAttackedTime), next_attack = SaveTime.Rel(NextAttackTime),
                stunned_until = SaveTime.Rel(StunnedUntil), speed_buff = _speedBuff, speed_buff_until = SaveTime.Rel(_speedBuffUntil),
                hidden_until = SaveTime.Rel(_hiddenUntil), grace_until = SaveTime.Rel(GraceUntil),
                afflictions = Afflictions.Capture(),
            };
            foreach (var skill in Skills) s.skill_cooldowns.Add(SaveTime.Hundredths(skill.CooldownLeft));
            return s;
        }

        /// <summary>Puts the saved standing back (call after the unit is set up; the attacker comes in <see cref="LinkUnit"/>).</summary>
        public void RestoreUnit(UnitSave s)
        {
            transform.position = new Vector3(s.x, s.y, transform.position.z);
            // Saved to the hundredth: a unit on its last sliver (under 1) comes back as it was, not topped up to 1.
            Hp = Mathf.Clamp(s.hp, 0.01f, Stats.MaxHp);
            LastAttackedTime = SaveTime.Abs(s.last_attacked);
            NextAttackTime = SaveTime.Abs(s.next_attack);
            StunnedUntil = SaveTime.Abs(s.stunned_until);
            _speedBuff = s.speed_buff;
            _speedBuffUntil = SaveTime.Abs(s.speed_buff_until);
            _hiddenUntil = SaveTime.Abs(s.hidden_until);
            if (Visual != null) Visual.Fade = IsHidden ? 0.35f : 1f;
            GraceUntil = SaveTime.Abs(s.grace_until);
            for (int i = 0; i < Skills.Count && i < s.skill_cooldowns.Count; i++) Skills[i].ResetCooldown(s.skill_cooldowns[i]);
            Afflictions.Restore(s.afflictions);
        }

        public void LinkUnit(UnitSave s, SaveContext ctx)
        {
            if (!string.IsNullOrEmpty(s.last_attacker)) LastAttacker = ctx.Find<Unit>(s.last_attacker);
            Afflictions.Link(s.afflictions, ctx);
        }

        /// <summary>Until this moment (Time.time) nothing can hurt the unit (a last stand's grace, F-6).</summary>
        protected float GraceUntil { get; set; }

        /// <summary>
        /// Positions, health and timers to the hundredth: enough to stand in the same place, and stable
        /// through a save → load → save. A timer still running is never rounded to zero (it would fire
        /// on load, see <see cref="SaveTime.Hundredths"/>).
        /// </summary>
        protected static float Round(float v) => SaveTime.Hundredths(v);
    }
}
