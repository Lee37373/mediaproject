using GuildMaster.Core;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Units
{
    /// <summary>Stateless combat rules: attack timing, line of sight, damage formula, projectiles.</summary>
    public static class Combat
    {
        public const float ContactPadding = 0.35f;
        public const float CritChance = 0.1f;
        public const float CritMultiplier = 1.6f;

        public static float Distance(Unit a, Unit b) => Vector2.Distance(a.Position, b.Position);

        public static bool InRange(Unit attacker, Unit target)
            => Distance(attacker, target) <= attacker.Stats.AttackRange + ContactPadding + target.Radius;

        public static bool HasShot(Unit attacker, Unit target)
            => !attacker.Stats.IsRanged || Services.Get<WorldService>().HasLineOfSight(attacker.Position, target.Position, 2);

        /// <summary>Attacks if in range, off cooldown and (for ranged) with line of sight.</summary>
        public static bool TryAttack(Unit attacker, Unit target)
        {
            if (attacker == null || target == null || !attacker.IsAlive || !target.IsAlive || attacker.IsStunned) return false;
            if (Time.time < attacker.NextAttackTime || !InRange(attacker, target) || !HasShot(attacker, target)) return false;

            attacker.NextAttackTime = Time.time + attacker.Stats.AttackCooldown;
            attacker.Visual?.FaceTowards(target.Position);

            if (attacker.Stats.IsRanged && Services.TryGet<ProjectileService>(out var projectiles))
                projectiles.Launch(attacker, target);
            else
                ApplyHit(attacker, target);
            return true;
        }

        public static void ApplyHit(Unit attacker, Unit target)
        {
            if (target == null || !target.IsAlive) return;
            var rng = GameRandom.Shared;
            float atk = attacker != null ? attacker.Stats.Attack : 5f;
            float raw = atk * rng.Range(0.9f, 1.1f);
            bool crit = rng.Chance(CritChance);
            if (crit) raw *= CritMultiplier;
            float dmg = Mathf.Max(1f, raw - target.Stats.Defense * 0.5f);
            target.ReceiveDamage(Mathf.Round(dmg), attacker, crit);
        }
    }

    public enum EngageStatus { Chasing, Attacking, Lost }

    /// <summary>
    /// Reusable fight behaviour for any unit:
    /// - uses ready skills first,
    /// - melee: chase and strike (crowd separation spreads attackers around the target),
    /// - ranged: keep firing distance, kite away from melee threats between shots,
    ///   and reposition to regain line of sight or find cover from enemy shooters.
    /// </summary>
    public class Engagement
    {
        readonly Unit _self;
        float _repathTimer;
        float _tacticTimer;
        Vector2 _lastTargetPos;
        bool _kiting;

        public Unit Target { get; private set; }
        public float GiveUpDistance { get; set; } = 18f;
        public bool IsKiting => _kiting;

        public Engagement(Unit self) { _self = self; }

        public void SetTarget(Unit target)
        {
            if (target == Target) return;
            Target = target;
            _self.CurrentTarget = target;
            _repathTimer = 0f;
            _tacticTimer = 0f;
        }

        public void Clear()
        {
            Target = null;
            _self.CurrentTarget = null;
            _kiting = false;
        }

        public EngageStatus Tick(float dt)
        {
            if (Target == null || !Target.IsAlive || !_self.IsAlive) { Clear(); return EngageStatus.Lost; }

            float dist = Combat.Distance(_self, Target);
            if (dist > GiveUpDistance) { Clear(); _self.Mover.Stop(); return EngageStatus.Lost; }

            if (_self.IsStunned || IsChanneling()) return EngageStatus.Attacking;
            if (_self.TryUseSkills(Target)) return EngageStatus.Attacking;

            return _self.Stats.IsRanged ? RangedTick(dt, dist) : MeleeTick(dt);
        }

        bool IsChanneling()
        {
            foreach (var s in _self.Skills) if (s.IsChanneling) return true;
            return false;
        }

        EngageStatus MeleeTick(float dt)
        {
            if (Combat.InRange(_self, Target))
            {
                if (_self.Mover.IsMoving) _self.Mover.Stop();
                Combat.TryAttack(_self, Target);
                return EngageStatus.Attacking;
            }

            _repathTimer -= dt;
            if (_repathTimer <= 0f || !_self.Mover.IsMoving || (Target.Position - _lastTargetPos).sqrMagnitude > 1f)
            {
                _repathTimer = 0.5f;
                _lastTargetPos = Target.Position;
                if (!_self.Mover.SetDestination(Target.Position, Mathf.Max(0.1f, _self.Stats.AttackRange * 0.8f)))
                {
                    Clear();
                    return EngageStatus.Lost;
                }
            }
            return EngageStatus.Chasing;
        }

        EngageStatus RangedTick(float dt, float dist)
        {
            _tacticTimer -= dt;
            bool cooldownReady = Time.time >= _self.NextAttackTime;

            // Kite: step away from melee threats while reloading.
            var threat = Tactics.NearestMeleeThreat(_self, 2.2f + _self.Radius);
            if (threat != null && !cooldownReady)
            {
                if (_tacticTimer <= 0f || !_self.Mover.IsMoving)
                {
                    _tacticTimer = 0.45f;
                    if (Tactics.FindKitePosition(_self, threat, Target, 2.5f, out var kite))
                    {
                        _self.Mover.SetDestination(kite, 0.1f);
                        _kiting = true;
                    }
                }
                return EngageStatus.Chasing;
            }
            _kiting = false;

            bool inRange = Combat.InRange(_self, Target);
            bool hasShot = inRange && Combat.HasShot(_self, Target);
            if (hasShot)
            {
                if (_self.Mover.IsMoving) _self.Mover.Stop();
                Combat.TryAttack(_self, Target);
                return EngageStatus.Attacking;
            }

            // No shot: find a firing position (line of sight, away from melee, in cover), else close in.
            if (_tacticTimer <= 0f || !_self.Mover.IsMoving)
            {
                _tacticTimer = 0.8f;
                bool ok = Tactics.FindFiringPosition(_self, Target, out var spot)
                    ? _self.Mover.SetDestination(spot, 0.1f)
                    : _self.Mover.SetDestination(Target.Position, _self.Stats.AttackRange * 0.6f);
                if (!ok) { Clear(); return EngageStatus.Lost; }
            }
            return EngageStatus.Chasing;
        }
    }
}
