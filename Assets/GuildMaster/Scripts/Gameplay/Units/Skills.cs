using System.Collections.Generic;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Units
{
    public struct SkillUsedEvent { public Unit User; public string Name; public Color Color; }

    /// <summary>
    /// Active ability with its own trigger condition. Units try their ready skills before basic
    /// attacks. Multi-frame skills (charge, wind-ups) report IsChanneling to pause other actions.
    /// </summary>
    public abstract class Skill
    {
        float _readyAt;

        public abstract string Name { get; }
        public virtual Color Color => UnityEngine.Color.white;
        public float Cooldown { get; protected set; } = 8f;
        public bool IsReady => Time.time >= _readyAt;
        public virtual bool IsChanneling => false;

        public void ResetCooldown(float stagger) => _readyAt = Time.time + stagger;
        protected void StartCooldown() => _readyAt = Time.time + Cooldown;

        public abstract bool TryUse(Unit self, Unit target);
        /// <summary>Blast radius for area skills (0 = single target); used by heroic skill timing.</summary>
        public virtual float AreaRadius => 0f;
        /// <summary>True if the area is centred on the user rather than the target.</summary>
        public virtual bool AreaOnSelf => false;
        public virtual void Tick(Unit self, float dt) { }

        protected void Announce(Unit self)
        {
            StartCooldown();
            EventBus.Publish(new SkillUsedEvent { User = self, Name = Name, Color = Color });
        }

        // ---- helpers ----
        protected static readonly List<Unit> Buffer = new();

        protected static List<Unit> HostilesAround(Unit self, Vector2 center, float radius)
        {
            UnitRegistry.Collect(center, radius, u => u != self && (self.IsHostileTo(u) || self.WasRecentlyAttackedBy(u)), Buffer);
            return Buffer;
        }

        protected static void Damage(Unit self, Unit target, float multiplier)
        {
            if (target == null || !target.IsAlive) return;
            float dmg = Mathf.Max(1f, self.Stats.Attack * multiplier * GameRandom.Shared.Range(0.9f, 1.1f) - target.Stats.Defense * 0.5f);
            target.ReceiveDamage(Mathf.Round(dmg), self, false);
        }

        protected static FxService Fx => Services.TryGet<FxService>(out var fx) ? fx : null;
        protected static WorldService World => Services.Get<WorldService>();

        protected static void ShakeIfVisible(Vector2 at, float trauma)
        {
            if (Services.TryGet<FogOfWar>(out var fog) && !fog.IsVisible(at)) return;
            if (Services.TryGet<CameraController>(out var cam)) cam.Shake(trauma);
        }
    }

    // ============================================================ Adventurer skills

    /// <summary>Warrior: spin attack when surrounded.</summary>
    public class WhirlwindSkill : Skill
    {
        public WhirlwindSkill() { Cooldown = 7f; }
        public override string Name => "회전베기";
        public override Color Color => new(1f, 0.75f, 0.4f);

        public override bool TryUse(Unit self, Unit target)
        {
            var hostiles = HostilesAround(self, self.Position, 1.9f);
            if (hostiles.Count < 2) return false;
            foreach (var h in hostiles.ToArray())
            {
                Damage(self, h, 1.2f);
                h.Knockback(h.Position - self.Position, 0.6f);
            }
            Fx?.Ring(self.Position, 1.9f, Color);
            Announce(self);
            return true;
        }
    }

    /// <summary>Warrior: pulls monsters that are attacking allies onto himself.</summary>
    public class TauntSkill : Skill
    {
        public TauntSkill() { Cooldown = 14f; }
        public override string Name => "도발";
        public override Color Color => new(1f, 0.9f, 0.3f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (self.HpRatio < 0.5f) return false;
            var hostiles = HostilesAround(self, self.Position, 5f);
            int pulled = 0;
            foreach (var h in hostiles.ToArray())
            {
                if (h.CurrentTarget == null || h.CurrentTarget == self || h.CurrentTarget.Faction != self.Faction) continue;
                h.ForceTarget(self);
                pulled++;
            }
            if (pulled == 0) return false;
            Fx?.Ring(self.Position, 5f, Color, 0.5f);
            Announce(self);
            return true;
        }
    }

    /// <summary>Ranger: arrow that pierces through every enemy on a line (stopped by hard cover).</summary>
    public class PiercingShotSkill : Skill
    {
        public PiercingShotSkill() { Cooldown = 9f; }
        public override string Name => "관통 화살";
        public override Color Color => new(0.7f, 1f, 0.6f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (target == null || Combat.Distance(self, target) > self.Stats.AttackRange + 0.5f) return false;
            var from = self.Position;
            var dir = (target.Position - from).normalized;
            var to = from + dir * (self.Stats.AttackRange + 3f);
            if (World.Raycast(from, to, out var hit, out _)) to = hit;
            // Require at least the primary target on the line.
            if (Vector2.Dot(target.Position - from, dir) > (to - from).magnitude) return false;

            int hits = 0;
            UnitRegistry.Collect((from + to) * 0.5f, (to - from).magnitude * 0.5f + 0.5f, u => u != self && self.IsHostileTo(u), Buffer);
            foreach (var u in Buffer.ToArray())
            {
                var rel = u.Position - from;
                float along = Vector2.Dot(rel, dir);
                if (along < 0f || along > (to - from).magnitude) continue;
                if ((rel - dir * along).sqrMagnitude > 0.45f * 0.45f) continue;
                Damage(self, u, 1.3f);
                hits++;
            }
            if (hits == 0) return false;
            Fx?.Beam(from, to, Color);
            Announce(self);
            return true;
        }
    }

    /// <summary>Mage: slow fireball that explodes in an area (explodes early on cover).</summary>
    public class FireballSkill : Skill
    {
        const float Radius = 1.6f;
        public FireballSkill() { Cooldown = 6f; }
        public override float AreaRadius => Radius;
        public override string Name => "화염구";
        public override Color Color => new(1f, 0.5f, 0.2f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (target == null || Combat.Distance(self, target) > self.Stats.AttackRange + 0.5f) return false;
            if (!World.HasLineOfSight(self.Position, target.Position, 1)) return false;
            if (!Services.TryGet<ProjectileService>(out var projectiles)) return false;
            var caster = self;
            projectiles.Launch(self, target, new ProjectileSpec
            {
                Speed = 7f, Size = 0.6f, Color = Color, TreeBlockChance = 0.6f,
                OnImpact = (point, _) =>
                {
                    if (caster == null) return;
                    foreach (var u in HostilesAround(caster, point, Radius).ToArray()) Damage(caster, u, 1.5f);
                    Fx?.Ring(point, Radius, Color, 0.4f);
                    Fx?.Spark(point, new Color(1f, 0.8f, 0.3f), 1.4f, 0.3f);
                },
            });
            Announce(self);
            return true;
        }
    }

    /// <summary>Mage: teleports away when a melee enemy gets too close.</summary>
    public class BlinkSkill : Skill
    {
        public BlinkSkill() { Cooldown = 12f; }
        public override string Name => "블링크";
        public override Color Color => new(0.6f, 0.7f, 1f);

        public override bool TryUse(Unit self, Unit target)
        {
            var threat = Tactics.NearestMeleeThreat(self, 1.4f);
            if (threat == null || self.HpRatio > 0.75f) return false;
            if (!Tactics.FindKitePosition(self, threat, target, 3.5f, out var point)) return false;
            Fx?.Spark(self.Position, Color, 1f);
            self.Mover.Stop();
            self.transform.position = new Vector3(point.x, point.y, self.transform.position.z);
            Fx?.Spark(point, Color, 1f);
            Announce(self);
            return true;
        }
    }

    // ============================================================ Monster skills

    /// <summary>Straight-line dash that damages, knocks back and stuns on contact.</summary>
    public class ChargeSkill : Skill
    {
        const float SpeedMultiplier = 3.2f;
        const float MaxDuration = 0.7f;

        Unit _target;
        Vector2 _dir;
        float _time;
        bool _active;

        public ChargeSkill() { Cooldown = 10f; }
        public override string Name => "돌진";
        public override Color Color => new(1f, 0.55f, 0.35f);
        public override bool IsChanneling => _active;

        public override bool TryUse(Unit self, Unit target)
        {
            if (target == null) return false;
            float d = Combat.Distance(self, target);
            if (d < 2.5f || d > 7f || !World.HasLineOfSight(self.Position, target.Position, 0)) return false;
            _target = target;
            _dir = (target.Position - self.Position).normalized;
            _time = 0f;
            _active = true;
            self.Mover.Stop();
            Fx?.Spark(self.Position, new Color(0.8f, 0.7f, 0.5f), 0.9f);
            Announce(self);
            return true;
        }

        public override void Tick(Unit self, float dt)
        {
            if (!_active) return;
            _time += dt;
            var next = self.Position + _dir * self.Stats.MoveSpeed * SpeedMultiplier * dt;
            bool blocked = !World.Grid.IsWalkable(GridMap.WorldToCell(next));
            if (!blocked) self.transform.position = new Vector3(next.x, next.y, self.transform.position.z);

            if (_target != null && _target.IsAlive && Combat.Distance(self, _target) <= self.Radius + _target.Radius + 0.35f)
            {
                Damage(self, _target, 1.4f);
                _target.Knockback(_dir, 1.4f);
                _target.Stun(0.5f);
                ShakeIfVisible(_target.Position, 0.15f);
                _active = false;
            }
            else if (blocked || _time >= MaxDuration || !self.IsAlive) _active = false;
        }
    }

    /// <summary>Ogre: telegraphed ground slam — heavy AoE damage after a 1.1s wind-up.</summary>
    public class GroundSlamSkill : Skill
    {
        const float Radius = 2.3f;
        const float Windup = 1.1f;
        float _channelUntil;

        public GroundSlamSkill() { Cooldown = 8f; }
        public override float AreaRadius => Radius;
        public override bool AreaOnSelf => true;
        public override string Name => "내려찍기";
        public override Color Color => new(1f, 0.35f, 0.2f);
        public override bool IsChanneling => Time.time < _channelUntil;

        public override bool TryUse(Unit self, Unit target)
        {
            if (target == null || Combat.Distance(self, target) > 2.2f) return false;
            if (!Services.TryGet<TelegraphService>(out var telegraphs)) return false;
            var center = self.Position;
            self.Mover.Stop();
            _channelUntil = Time.time + Windup;
            telegraphs.Create(self, center, Radius, Windup, () =>
            {
                foreach (var u in HostilesAround(self, center, Radius).ToArray())
                {
                    Damage(self, u, 2f);
                    u.Knockback(u.Position - center, 1.5f);
                }
                Fx?.Ring(center, Radius, Color, 0.45f);
                ShakeIfVisible(center, 0.35f);
            });
            Announce(self);
            return true;
        }
    }

    /// <summary>Wolves: calls idle pack mates onto the target and hastens them.</summary>
    public class PackHowlSkill : Skill
    {
        public PackHowlSkill() { Cooldown = 15f; }
        public override string Name => "동료 부르기";
        public override Color Color => new(0.8f, 0.85f, 1f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (target == null || self is not Monsters.Monster me) return false;
            UnitRegistry.Collect(self.Position, 7f, u => u != self && u is Monsters.Monster m && m.Def == me.Def, Buffer);
            int called = 0;
            foreach (var u in Buffer.ToArray())
            {
                u.ApplySpeedBuff(1.3f, 4f);
                if (u.CurrentTarget == null) { u.ForceTarget(target); called++; }
            }
            self.ApplySpeedBuff(1.3f, 4f);
            if (called == 0 && Buffer.Count == 0) return false;
            Fx?.Ring(self.Position, 3f, Color, 0.6f);
            Announce(self);
            return true;
        }
    }

    public static class SkillFactory
    {
        public static Skill Create(string id) => id switch
        {
            "whirlwind" => new WhirlwindSkill(),
            "taunt" => new TauntSkill(),
            "pierce" => new PiercingShotSkill(),
            "fireball" => new FireballSkill(),
            "blink" => new BlinkSkill(),
            "charge" => new ChargeSkill(),
            "slam" => new GroundSlamSkill(),
            "howl" => new PackHowlSkill(),
            _ => null,
        };

        public static void Assign(Unit unit, IEnumerable<string> ids)
        {
            unit.Skills.Clear();
            if (ids == null) return;
            foreach (var id in ids)
            {
                var s = Create(id);
                if (s != null) unit.Skills.Add(s);
            }
        }
    }
}
