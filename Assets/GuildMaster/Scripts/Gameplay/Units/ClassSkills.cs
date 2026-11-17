using System.Collections.Generic;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Units
{
    // ============================================================ Adept and master grades (P4-126)
    //
    // User request 2026-11-04 "스킬 이펙트 에셋 활용해서 스킬 많이 만들어줘": three more for each class, opened at Lv8 (adept)
    // and Lv15 (master) — see SkillCatalog. Each shows an effect from the Cartoon Casual VFX Pack, or a plain ring, spark or
    // beam when the pack is missing.

    /// <summary>Warrior (adept): braces when badly hurt and under attack — blows land at half for a few seconds.</summary>
    public class IronWallSkill : Skill
    {
        public const float Share = 0.5f, Seconds = 4f;
        public IronWallSkill() { Cooldown = 16f; }
        public override string Name => "철벽";
        public override string Description => "체력 50% 이하에서 공격받는 중이면 4초간 받는 피해가 절반이 됩니다.";
        public override Color Color => new(0.55f, 0.75f, 1f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (self.HpRatio > 0.5f || Time.time - self.LastAttackedTime > 2f) return false;
            self.Boons.Brace(Share, Seconds);
            if (!Vfx("Shield", self.Position + new Vector2(0f, 0.35f), 1.1f)) Fx?.Ring(self.Position, 0.9f, Color, 0.5f);
            Announce(self);
            return true;
        }
    }

    /// <summary>Warrior (adept): a finishing blow on a foe in reach that is nearly down.</summary>
    public class ExecuteSkill : Skill
    {
        public const float Below = 0.3f, Multiplier = 2.5f;
        public ExecuteSkill() { Cooldown = 9f; }
        public override string Name => "처형";
        public override string Description => "체력 30% 이하로 몰린 근접한 적에게 공격력 250%의 마무리 일격.";
        public override Color Color => new(1f, 0.3f, 0.25f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (target == null || !target.IsAlive || target.HpRatio > Below || !Combat.InRange(self, target)) return false;
            self.Visual?.FaceTowards(target.Position);
            Damage(self, target, Multiplier);
            if (!Vfx("Hit10_Red", target.Position + new Vector2(0f, 0.35f), 1.2f)) Fx?.Spark(target.Position, Color, 1.2f);
            ShakeIfVisible(target.Position, 0.2f);
            Announce(self);
            return true;
        }
    }

    /// <summary>
    /// Warrior (master) and the goblin warrior: a cry that raises the attack of every ally around for a few seconds.
    /// Used once a fight is on and someone besides the crier is in it.
    /// </summary>
    public class WarCrySkill : Skill
    {
        public const float Radius = 5f, Share = 0.25f, Seconds = 6f;
        static readonly List<Unit> s_allies = new();

        public WarCrySkill() { Cooldown = 20f; }
        public override string Name => "전투 함성";
        public override string Description => "싸움이 붙으면 함성으로 주변(반경 5) 동료의 공격력을 6초간 25% 올립니다.";
        public override Color Color => new(1f, 0.45f, 0.3f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (target == null) return false;
            s_allies.Clear();
            UnitRegistry.Collect(self.Position, Radius, u => TargetSelector.IsAlly(self, u), s_allies);
            bool company = false;
            foreach (var u in s_allies) if (u.CurrentTarget != null) { company = true; break; }
            if (!company) return false;
            self.Boons.Empower(Share, Seconds);
            foreach (var u in s_allies)
            {
                u.Boons.Empower(Share, Seconds);
                Vfx("Booster_ArrowUp", u.Position + new Vector2(0f, 0.4f), 0.7f);
            }
            if (!Vfx("Buff2_Red", self.Position, 1.4f)) Fx?.Ring(self.Position, Radius, Color, 0.5f);
            Announce(self);
            return true;
        }
    }

    /// <summary>Ranger (adept): an arrow that poisons — a hit, then more burning away over six seconds.</summary>
    public class PoisonArrowSkill : Skill
    {
        public const float Seconds = 6f, PoisonShare = 1.5f;
        public PoisonArrowSkill() { Cooldown = 10f; }
        public override string Name => "독화살";
        public override string Description => "중독되지 않은 적에게 독화살: 공격력 100% 피해와 6초간 공격력 150%만큼의 독.";
        public override Color Color => new(0.55f, 0.9f, 0.3f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (!InShot(self, target) || target.Afflictions.Poisoned) return false;
            if (!Services.TryGet<ProjectileService>(out var projectiles)) return false;
            var shooter = self;
            var spec = ProjectileSpec.Of(ProjectileService.LookOf(shooter).WithTail(Color));
            spec.OnImpact = (point, hit) =>
            {
                if (shooter == null || hit == null || !hit.IsAlive) return;
                Damage(shooter, hit, 1f);
                hit.Afflictions.Poison(shooter.AttackNow * PoisonShare, Seconds, shooter);
                if (!Vfx("Poisoning1", hit.Position + new Vector2(0f, 0.3f), 0.9f)) Fx?.Spark(hit.Position, Color, 0.9f);
            };
            projectiles.Launch(self, target, spec);
            Announce(self);
            return true;
        }
    }

    /// <summary>Ranger (adept): an arrow that pins a charging foe — slowed by half for three seconds.</summary>
    public class SnareArrowSkill : Skill
    {
        public const float Slow = 0.5f, Seconds = 3f;
        public SnareArrowSkill() { Cooldown = 12f; }
        public override string Name => "속박 화살";
        public override string Description => "다가오는 근접 적을 맞혀 공격력 80% 피해와 3초간 이동·공격 속도 50% 감소.";
        public override Color Color => new(1f, 0.9f, 0.35f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (!InShot(self, target) || target.Stats.IsRanged || target.Afflictions.Slow > 0f) return false;
            if (Combat.Distance(self, target) > 5f) return false; // only once it is coming close
            if (!Services.TryGet<ProjectileService>(out var projectiles)) return false;
            var shooter = self;
            var spec = ProjectileSpec.Of(ProjectileService.LookOf(shooter).WithTail(Color));
            spec.OnImpact = (point, hit) =>
            {
                if (shooter == null || hit == null || !hit.IsAlive) return;
                Damage(shooter, hit, 0.8f);
                hit.Afflictions.Chill(Slow, Seconds);
                if (!Vfx("Lightning_Small_Yellow", hit.Position + new Vector2(0f, 0.3f), 0.9f)) Fx?.Spark(hit.Position, Color, 0.9f);
            };
            projectiles.Launch(self, target, spec);
            Announce(self);
            return true;
        }
    }

    /// <summary>Ranger (master): three arrows in quick succession, each at 60%.</summary>
    public class VolleySkill : Skill
    {
        public const int Arrows = 3;
        public const float Multiplier = 0.6f;
        public VolleySkill() { Cooldown = 11f; }
        public override string Name => "속사";
        public override string Description => "화살 3발을 잇달아 쏩니다(각 공격력 60%).";
        public override Color Color => new(0.6f, 1f, 0.75f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (!InShot(self, target) || !Services.TryGet<ProjectileService>(out var projectiles)) return false;
            var shooter = self;
            for (int i = 0; i < Arrows; i++)
            {
                var spec = ProjectileSpec.Of(ProjectileService.LookOf(shooter).WithTail(Color));
                spec.Speed = 9f + i * 2.5f; // the later ones fly faster: they land one after another, not at once
                spec.OnImpact = (point, hit) =>
                {
                    if (shooter == null || hit == null || !hit.IsAlive) return;
                    Damage(shooter, hit, Multiplier);
                    Vfx("Hit1_Green", hit.Position + new Vector2(0f, 0.35f), 0.7f);
                };
                projectiles.Launch(self, target, spec);
            }
            Announce(self);
            return true;
        }
    }

    /// <summary>Priest (adept): a shield on the ally in most danger that is being struck — soaks a few blows.</summary>
    public class HolyShieldSkill : Skill
    {
        public const float Range = 6f, Below = 0.7f, Seconds = 6f, Strength = 2.5f;
        public HolyShieldSkill() { Cooldown = 12f; }
        public override string Name => "보호막";
        public override string Description => "공격받는 동료 중 체력 70% 미만인 가장 위험한 한 명(반경 6)에게 6초간 공격력 250%만큼 피해를 흡수하는 막.";
        public override Color Color => new(0.75f, 0.9f, 1f);
        public override bool IsSupport => true;

        public override bool TryUse(Unit self, Unit target)
        {
            var ally = MostEndangeredAlly(self, Range, Below);
            if (ally == null || ally.Boons.Shield > 0f || Time.time - ally.LastAttackedTime > 2.5f) return false;
            ally.Boons.Ward(Mathf.Round(Combat.ScaledHp(self.Stats.Attack * Strength)), Seconds);
            if (ally != self) Fx?.Beam(self.Position, ally.Position, Color, 0.06f, 0.25f);
            if (!Vfx("Shield", ally.Position + new Vector2(0f, 0.35f), 1.1f)) Fx?.Ring(ally.Position, 0.9f, Color, 0.5f);
            Announce(self);
            return true;
        }
    }

    /// <summary>Priest (adept): a bolt from above — twice as hard on the undead.</summary>
    public class SmiteSkill : Skill
    {
        public const float Multiplier = 1.6f;
        public SmiteSkill() { Cooldown = 8f; }
        public override string Name => "심판";
        public override string Description => "적에게 번개를 내려 공격력 160% 피해. 해골 같은 언데드에게는 두 배.";
        public override Color Color => new(1f, 0.95f, 0.5f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (!InShot(self, target)) return false;
            Damage(self, target, IsUndead(target) ? Multiplier * 2f : Multiplier);
            if (!Vfx("Lightning_Small_Yellow", target.Position + new Vector2(0f, 0.4f), 1.1f)) Fx?.Beam(target.Position + new Vector2(0f, 3f), target.Position, Color);
            Announce(self);
            return true;
        }

        public static bool IsUndead(Unit u) => u is Monsters.Monster m && m.Def != null && m.Def.id.Contains("skull");
    }

    /// <summary>Priest (master): a blessing on every ally around in a fight — defence up by 30% for eight seconds.</summary>
    public class BlessSkill : Skill
    {
        public const float Radius = 4f, Share = 0.3f, Seconds = 8f;
        public const int MinFighting = 2;
        static readonly List<Unit> s_allies = new();

        public BlessSkill() { Cooldown = 22f; }
        public override string Name => "축복";
        public override string Description => "주변(반경 4)에서 동료 2명 이상이 싸우고 있으면 모두의 방어력을 8초간 30% 올립니다.";
        public override Color Color => new(1f, 0.9f, 0.45f);
        public override bool IsSupport => true;

        public override bool TryUse(Unit self, Unit target)
        {
            s_allies.Clear();
            UnitRegistry.Collect(self.Position, Radius, u => u == self || TargetSelector.IsAlly(self, u), s_allies);
            int fighting = 0;
            foreach (var u in s_allies) if (u.CurrentTarget != null && u.CurrentTarget.IsAlive) fighting++;
            if (fighting < MinFighting) return false;
            foreach (var u in s_allies)
            {
                u.Boons.Fortify(Share, Seconds);
                if (!Vfx("Buff3_Yellow", u.Position, 0.9f)) Fx?.Ring(u.Position, 0.8f, Color, 0.5f);
            }
            Announce(self);
            return true;
        }
    }

    /// <summary>Mage (adept): lightning that leaps from the target to two more foes close by, weaker each time.</summary>
    public class ChainLightningSkill : Skill
    {
        public const float Jump = 3f;
        static readonly float[] s_multipliers = { 1.3f, 1f, 0.7f };
        static readonly List<Unit> s_hit = new();

        public ChainLightningSkill() { Cooldown = 9f; }
        public override string Name => "연쇄 번개";
        public override string Description => "번개가 목표에서 가까운 적(3칸)으로 두 번 더 튑니다(공격력 130% → 100% → 70%).";
        public override Color Color => new(0.55f, 0.75f, 1f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (!InShot(self, target)) return false;
            s_hit.Clear();
            var from = self.Position;
            var current = target;
            for (int i = 0; i < s_multipliers.Length && current != null; i++)
            {
                s_hit.Add(current);
                Fx?.Beam(from, current.Position, Color, 0.08f, 0.2f);
                Damage(self, current, s_multipliers[i]);
                Vfx("Lightning_Small_Blue", current.Position + new Vector2(0f, 0.35f), 0.9f);
                from = current.Position;
                var last = current;
                current = UnitRegistry.FindNearest(from, Jump, u => u.IsAlive && !s_hit.Contains(u) && self.IsHostileTo(u)
                                                                    && World.HasLineOfSight(last.Position, u.Position, 1));
            }
            Announce(self);
            return true;
        }
    }

    /// <summary>Mage (adept): a ring of frost round the caster when foes press in — hurts and slows them.</summary>
    public class FrostRingSkill : Skill
    {
        public const float Radius = 2f, Slow = 0.4f, Seconds = 3f;
        public FrostRingSkill() { Cooldown = 11f; }
        public override float AreaRadius => Radius;
        public override bool AreaOnSelf => true;
        public override string Name => "서리 고리";
        public override string Description => "근접한 적이 붙으면 주변(반경 2)에 공격력 80% 피해와 3초간 40% 감속.";
        public override Color Color => new(0.6f, 0.85f, 1f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (Tactics.NearestMeleeThreat(self, Radius) == null) return false;
            foreach (var u in HostilesAround(self, self.Position, Radius).ToArray())
            {
                Damage(self, u, 0.8f);
                u.Afflictions.Chill(Slow, Seconds);
            }
            if (!Vfx("Explosion_Effect_Blue", self.Position, 1.6f)) Fx?.Ring(self.Position, Radius, Color, 0.45f);
            Announce(self);
            return true;
        }
    }

    /// <summary>Mage (master): a meteor called down on the target's spot — a red circle, then a heavy blast.</summary>
    public class MeteorSkill : Skill
    {
        public const float Radius = 2.2f, Windup = 1.2f, Multiplier = 2.2f;
        public MeteorSkill() { Cooldown = 18f; }
        public override float AreaRadius => Radius;
        public override string Name => "운석";
        public override string Description => "1.2초 예고 뒤 목표 자리 반경 2.2에 운석이 떨어져 공격력 220% 피해. 붉은 장판을 피하세요.";
        public override Color Color => new(1f, 0.45f, 0.2f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (!InShot(self, target, 2f) || !Services.TryGet<TelegraphService>(out var telegraphs)) return false;
            var caster = self;
            var centre = target.Position;
            telegraphs.Create(self, centre, Radius, Windup, () =>
            {
                if (caster == null) return;
                foreach (var u in HostilesAround(caster, centre, Radius).ToArray()) Damage(caster, u, Multiplier);
                if (!Vfx("Explosion_Large", centre, 1.4f)) Fx?.Ring(centre, Radius, Color, 0.45f);
                ShakeIfVisible(centre, 0.3f);
            });
            Announce(self);
            return true;
        }
    }
}
