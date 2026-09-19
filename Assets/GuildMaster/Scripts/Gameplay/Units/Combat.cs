using System.Collections.Generic;
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
    /// - ranged: keep firing distance, kite away from melee threats between shots (Skilled+),
    ///   and reposition to regain line of sight or find cover from enemy shooters (Veteran+);
    ///   lower tiers simply walk at the target until they have a shot (see <see cref="CombatTiers"/>).
    /// </summary>
    public class Engagement
    {
        /// <summary>How long a target dropped as unreachable is ignored.</summary>
        const float IgnoreSeconds = 6f;
        /// <summary>Closing in by less than this does not count as progress in a chase.</summary>
        const float ChaseProgress = 0.3f;

        readonly Unit _self;
        readonly List<(Unit unit, float until)> _ignored = new();
        float _repathTimer;
        float _tacticTimer;
        float _retargetTimer;
        float _lockedUntil;
        float _stallTime;
        float _bestDist = float.MaxValue;
        Vector2 _lastTargetPos;
        bool _kiting;

        public Unit Target { get; private set; }
        public float GiveUpDistance { get; set; } = 18f;

        /// <summary>Seconds chased without closing in (debug).</summary>
        public float StallTime => _stallTime;

        /// <summary>Targets currently skipped after a failed chase (debug).</summary>
        public IEnumerable<Unit> IgnoredUnits
        {
            get
            {
                foreach (var (unit, until) in _ignored)
                    if (Time.time < until) yield return unit;
            }
        }
        public bool IsKiting => _kiting;

        /// <summary>Which units may be (re)targeted mid-fight; null disables retargeting.</summary>
        public System.Func<Unit, bool> CandidateFilter { get; set; }
        /// <summary>Search radius for retargeting (usually the sight radius).</summary>
        public float SearchRadius { get; set; } = 8f;

        /// <summary>
        /// Formation hint for ranged units (P4-27, party back line): a spot to fall back to while
        /// reloading, or null when the current position is fine.
        /// </summary>
        public System.Func<Unit, Vector2?> HoldSpot { get; set; }

        public Engagement(Unit self) { _self = self; }

        public void SetTarget(Unit target)
        {
            if (target == Target) return;
            if (target != null && !target.IsTargetableBy(_self)) return; // debug untargetable (F6)
            Target = target;
            _self.CurrentTarget = target;
            _repathTimer = 0f;
            _tacticTimer = 0f;
            _retargetTimer = CombatTiers.RetargetInterval(_self.Tier);
            _holdOffTime = 0f;
            ResetChase();
        }

        /// <summary>Taunts / explicit orders: attack this target and do not retarget for a while.</summary>
        public void Force(Unit target, float seconds)
        {
            SetTarget(target);
            _lockedUntil = Time.time + seconds;
        }

        public void Clear()
        {
            Target = null;
            _self.CurrentTarget = null;
            _kiting = false;
            _lockedUntil = 0f;
            ResetChase();
        }

        /// <summary>True while a target is skipped after a failed chase.</summary>
        public bool IsIgnoring(Unit u)
        {
            for (int i = _ignored.Count - 1; i >= 0; i--)
            {
                if (Time.time >= _ignored[i].until || _ignored[i].unit == null) { _ignored.RemoveAt(i); continue; }
                if (_ignored[i].unit == u) return true;
            }
            return false;
        }

        void Ignore(Unit u, float seconds)
        {
            _ignored.RemoveAll(x => x.unit == u);
            _ignored.Add((u, Time.time + seconds));
        }

        void ResetChase()
        {
            _stallTime = 0f;
            _bestDist = float.MaxValue;
        }

        /// <summary>
        /// Chase bookkeeping while out of reach: gives up (and ignores the target for a while) after
        /// the tier's give-up time without closing in — e.g. a faster enemy kiting us.
        /// </summary>
        bool GaveUpChase(float dist, float dt)
        {
            if (dist < _bestDist - ChaseProgress) { _bestDist = dist; _stallTime = 0f; return false; }
            _stallTime += dt;
            if (_stallTime < CombatTiers.ChaseGiveUp(_self.Tier)) return false;
            Ignore(Target, IgnoreSeconds);
            Clear();
            _self.Mover.Stop();
            return true;
        }

        public EngageStatus Tick(float dt)
        {
            if (Target == null || !Target.IsAlive || !_self.IsAlive || !Target.IsTargetableBy(_self)) { Clear(); return EngageStatus.Lost; }

            float dist = Combat.Distance(_self, Target);
            if (dist > GiveUpDistance) { Clear(); _self.Mover.Stop(); return EngageStatus.Lost; }

            if (_self.IsStunned || IsChanneling()) return EngageStatus.Attacking;

            // Periodic re-evaluation (tier sets how often and what counts, see TargetSelector);
            // instinct-driven units only turn on whoever just hit them.
            var tier = _self.Tier;
            _retargetTimer -= dt;
            Unit better = null;
            if (CombatTiers.RetaliatesOnly(tier))
            {
                better = FreshAttacker();
                // Stays on the new attacker a moment, or two attackers taking turns flip it every hit.
                if (better != null && better != Target) _lockedUntil = Time.time + InstinctFocusSeconds;
            }
            else if (_retargetTimer <= 0f)
            {
                _retargetTimer = CombatTiers.RetargetInterval(tier);
                if (CandidateFilter != null && Time.time >= _lockedUntil)
                    better = TargetSelector.Pick(_self, SearchRadius, CandidateFilter, this);
            }
            if (better != null && better != Target)
            {
                SetTarget(better);
                dist = Combat.Distance(_self, Target);
            }

            if (HoldOffTick(dt, dist)) return EngageStatus.Chasing;
            if (_self.TryUseSkills(Target)) return EngageStatus.Attacking;

            return _self.Stats.IsRanged ? RangedTick(dt, dist) : MeleeTick(dt, dist);
        }

        /// <summary>After turning on an attacker, instinct-driven units ignore other hits this long.</summary>
        const float InstinctFocusSeconds = 1.5f;

        /// <summary>Someone other than the target hit us just now (instinct: turn on them), or null.</summary>
        Unit FreshAttacker()
        {
            var hitter = _self.LastAttacker;
            if (hitter == null || hitter == Target || !_self.WasRecentlyAttackedBy(hitter, 0.5f)) return null;
            if (Time.time < _lockedUntil || !hitter.IsAlive || !hitter.IsTargetableBy(_self) || IsIgnoring(hitter)) return null;
            return CandidateFilter == null || CandidateFilter(hitter) ? hitter : null;
        }

        // ---------------- Third-party wait ("어부지리", Skilled+) ----------------

        const float HoldOffMaxSeconds = 12f;
        float _holdOffTime;

        /// <summary>True while waiting at a distance for the target to be worn down by someone else.</summary>
        public bool IsHoldingOff { get; private set; }

        /// <summary>
        /// The target is busy fighting a third party that is also our enemy (e.g. two monster species,
        /// or a monster and the guild) and nobody is hitting us: stay just out of the brawl until the
        /// target is weakened, its opponent falls, or we have waited long enough.
        /// </summary>
        public static bool ShouldHoldOff(Unit self, Unit target, float waited)
        {
            if (self.Tier < CombatTier.Skilled || target == null || waited >= HoldOffMaxSeconds) return false;
            if (Time.time - self.LastAttackedTime < 3f) return false;
            var other = target.CurrentTarget;
            if (other == null || other == self || !other.IsAlive || TargetSelector.IsAlly(self, other)) return false;
            if (!(self.IsHostileTo(other) || other.IsHostileTo(self))) return false;
            return target.HpRatio > 0.4f && other.HpRatio > 0.3f;
        }

        bool HoldOffTick(float dt, float dist)
        {
            if (!ShouldHoldOff(_self, Target, _holdOffTime))
            {
                IsHoldingOff = false;   // the wait budget resets only with a new target (SetTarget)
                return false;
            }
            IsHoldingOff = true;
            _holdOffTime += dt;
            ResetChase();
            float keep = Mathf.Max(_self.Stats.AttackRange + 1.5f, 4.5f);
            if (dist < keep - 0.5f)
            {
                var away = (_self.Position - Target.Position).normalized;
                if (away == Vector2.zero) away = Vector2.right;
                _self.Mover.SetDestination(Target.Position + away * keep, 0.3f);
            }
            else if (dist > keep + 2f) _self.Mover.SetDestination(Target.Position, keep);
            else if (_self.Mover.IsMoving) _self.Mover.Stop();
            _self.Visual?.FaceTowards(Target.Position);
            return true;
        }

        bool IsChanneling()
        {
            foreach (var s in _self.Skills) if (s.IsChanneling) return true;
            return false;
        }

        EngageStatus MeleeTick(float dt, float dist)
        {
            if (Combat.InRange(_self, Target))
            {
                if (_self.Mover.IsMoving) _self.Mover.Stop();
                Combat.TryAttack(_self, Target);
                ResetChase();
                return EngageStatus.Attacking;
            }
            if (GaveUpChase(dist, dt)) return EngageStatus.Lost;

            _repathTimer -= dt;
            if (_repathTimer <= 0f || !_self.Mover.IsMoving || (Target.Position - _lastTargetPos).sqrMagnitude > 1f)
            {
                _repathTimer = 0.5f;
                _lastTargetPos = Target.Position;
                if (!_self.Mover.SetDestination(Target.Position, Mathf.Max(0.1f, _self.Stats.AttackRange * 0.8f)))
                    return DropUnreachable();
            }
            return EngageStatus.Chasing;
        }

        EngageStatus RangedTick(float dt, float dist)
        {
            _tacticTimer -= dt;
            var tier = _self.Tier;
            bool cooldownReady = Time.time >= _self.NextAttackTime;

            // Kite (Skilled+): step away from melee threats while reloading.
            var threat = CombatTiers.CanKite(tier) ? Tactics.NearestMeleeThreat(_self, 2.2f + _self.Radius) : null;
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

            // Formation (party back line): fall back behind the front line while reloading.
            if (!cooldownReady && HoldSpot?.Invoke(Target) is Vector2 hold && (hold - _self.Position).sqrMagnitude > 0.6f * 0.6f)
            {
                if (_tacticTimer <= 0f || !_self.Mover.IsMoving)
                {
                    _tacticTimer = 0.45f;
                    _self.Mover.SetDestination(hold, 0.2f);
                }
                return EngageStatus.Chasing;
            }

            bool inRange = Combat.InRange(_self, Target);
            bool hasShot = inRange && Combat.HasShot(_self, Target);
            if (hasShot)
            {
                if (_self.Mover.IsMoving) _self.Mover.Stop();
                Combat.TryAttack(_self, Target);
                ResetChase();
                return EngageStatus.Attacking;
            }
            if (GaveUpChase(dist, dt)) return EngageStatus.Lost;

            // No shot. Veteran+ look for a firing position (line of sight, away from melee, in cover);
            // the others just walk at the target — to firing range, or right up to it when something
            // blocks the line — and shoot as soon as the line is clear.
            if (_tacticTimer <= 0f || !_self.Mover.IsMoving)
            {
                _tacticTimer = 0.8f;
                bool ok = CombatTiers.SeeksFiringPosition(tier) && Tactics.FindFiringPosition(_self, Target, out var spot)
                    ? _self.Mover.SetDestination(spot, 0.1f)
                    : _self.Mover.SetDestination(Target.Position, inRange ? 1f : _self.Stats.AttackRange * 0.8f);
                if (!ok) return DropUnreachable();
            }
            return EngageStatus.Chasing;
        }

        /// <summary>
        /// No path to the target (e.g. across water): drop it and skip it for a while, or the next
        /// sense tick picks it again and repeats a failed whole-map A* search every few frames.
        /// </summary>
        EngageStatus DropUnreachable()
        {
            Ignore(Target, IgnoreSeconds);
            Clear();
            return EngageStatus.Lost;
        }
    }
}
