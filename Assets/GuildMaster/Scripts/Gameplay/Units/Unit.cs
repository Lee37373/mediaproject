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
        float _speedBuff = 1f;
        float _speedBuffUntil;
        Vector2 _knockVelocity;
        float _knockUntil;

        /// <summary>Combat intelligence tier (target choice, chase give-up, skill timing).</summary>
        public virtual CombatTier Tier => CombatTier.Novice;

        /// <summary>Fight behaviour, if this unit has one (debug gizmos).</summary>
        public virtual Engagement CombatEngagement => null;
        /// <summary>Current behaviour state for debug labels.</summary>
        public virtual string DebugState => "";

        void OnDrawGizmos() => UnitGizmos.Draw(this, false);
        void OnDrawGizmosSelected() => UnitGizmos.Draw(this, true);

        /// <summary>Unit currently targeted by this unit (for UI, taunts, focus fire).</summary>
        public Unit CurrentTarget { get; internal set; }

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

        /// <summary>Forces this unit to attack a specific target (taunts, pack calls). Override per AI.</summary>
        public virtual void ForceTarget(Unit target) { }

        protected void TickStatus(float dt)
        {
            Mover.BuffMultiplier = Time.time < _speedBuffUntil ? _speedBuff : 1f;
            if (Time.time < _knockUntil)
            {
                var next = Position + _knockVelocity * dt;
                if (Core.Services.TryGet<WorldService>(out var world) && world.Grid.IsWalkable(GridMap.WorldToCell(next)))
                    transform.position = new Vector3(next.x, next.y, transform.position.z);
            }
            foreach (var s in Skills) s.Tick(this, dt);
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
            if (Tier < CombatTier.Heroic || s.AreaRadius <= 0f || target == null) return false;
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
        protected virtual void OnDisable() => UnitRegistry.Unregister(this);

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
            CurrentTarget = null;
            foreach (var s in Skills) s.ResetCooldown(GameRandomStagger());
            if (Mover) Mover.Speed = Stats.MoveSpeed;
        }

        static float GameRandomStagger() => Core.GameRandom.Shared.Range(0f, 2f);

        public abstract bool IsHostileTo(Unit other);

        /// <summary>
        /// Line of sight to another unit: mountains, walls and buildings block it; up to two trees
        /// are fine (same rule as ranged attacks). Use when picking new targets by sight.
        /// </summary>
        public bool CanSee(Unit other)
            => other != null && (!Core.Services.TryGet<WorldService>(out var world) || world.HasLineOfSight(Position, other.Position, 2));

        /// <summary>True if the other unit hit us within the given seconds (retaliation).</summary>
        public bool WasRecentlyAttackedBy(Unit other, float seconds = 8f)
            => other != null && LastAttacker == other && Time.time - LastAttackedTime < seconds;

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

        internal void ReceiveDamage(float amount, Unit source, bool critical)
        {
            if (!IsAlive) return;
            if (GuildInvulnerable && Faction == Faction.Guild) amount = 0f;
            Hp -= amount;
            if (source != null)
            {
                LastAttacker = source;
                LastAttackedTime = Time.time;
            }
            Visual?.Flash();
            EventBus.Publish(new UnitDamagedEvent { Target = this, Source = source, Amount = amount, Critical = critical });
            OnDamaged(amount, source);
            if (Hp <= 0f)
            {
                Hp = 0f;
                _dead = true;
                Mover.Stop();
                OnDeath(source);
                Died?.Invoke(this);
                EventBus.Publish(new UnitDiedEvent { Victim = this, Killer = source });
            }
        }

        protected virtual void OnDamaged(float amount, Unit source) { }
        protected abstract void OnDeath(Unit killer);
    }
}
