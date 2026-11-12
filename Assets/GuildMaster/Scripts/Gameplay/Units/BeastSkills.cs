using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Units
{
    // ============================================================ Beasts (P4-126)
    //
    // User request 2026-11-04: monster skills with the Cartoon Casual VFX Pack. Given in DefaultContent: poison sting (great
    // bee, sand serpent), drain (vampire bat, leech larva), spores (forest fungus), tongue (swamp toad), frenzy (orc, rhino),
    // slime burst (slime king), blizzard (yeti), pounce (raptor, horn rabbit); the goblin warrior shares the warrior's cry.

    /// <summary>A sting or bite in reach that leaves poison burning.</summary>
    public class PoisonStingSkill : Skill
    {
        public const float Seconds = 5f, PoisonShare = 1.2f;
        public PoisonStingSkill() { Cooldown = 8f; }
        public override string Name => "독침";
        public override string Description => "근접한 적을 찔러 공격력 80% 피해와 5초간 공격력 120%만큼의 독.";
        public override Color Color => new(0.6f, 0.9f, 0.25f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (target == null || !target.IsAlive || !Combat.InRange(self, target) || target.Afflictions.Poisoned) return false;
            self.Visual?.FaceTowards(target.Position);
            Damage(self, target, 0.8f);
            target.Afflictions.Poison(self.AttackNow * PoisonShare, Seconds, self);
            if (!Vfx("Poisoning1", target.Position + new Vector2(0f, 0.3f), 0.9f)) Fx?.Spark(target.Position, Color, 0.8f);
            Announce(self);
            return true;
        }
    }

    /// <summary>A bite that heals the biter by part of what it took, once it is a little hurt.</summary>
    public class DrainSkill : Skill
    {
        public const float Multiplier = 1.2f, HealShare = 0.6f;
        public DrainSkill() { Cooldown = 7f; }
        public override string Name => "흡혈";
        public override string Description => "체력이 깎이면 근접한 적을 물어 공격력 120% 피해, 입힌 피해의 60%만큼 회복합니다.";
        public override Color Color => new(0.9f, 0.25f, 0.35f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (self.HpRatio > 0.9f || target == null || !target.IsAlive || !Combat.InRange(self, target)) return false;
            self.Visual?.FaceTowards(target.Position);
            float before = target.Hp;
            Damage(self, target, Multiplier);
            float dealt = before - target.Hp;
            float hpBefore = self.Hp;
            if (dealt > 0f) self.Heal(Mathf.Round(dealt * HealShare));
            float gained = self.Hp - hpBefore;
            if (gained > 0f) Core.EventBus.Publish(new UnitHealedEvent { Target = self, Healer = self, Amount = gained });
            if (!Vfx("Hit1_Red", target.Position + new Vector2(0f, 0.35f), 0.9f)) Fx?.Spark(target.Position, Color, 0.8f);
            Announce(self);
            return true;
        }
    }

    /// <summary>A cloud of spores round the user when foes come near: everyone in it is poisoned.</summary>
    public class SporeCloudSkill : Skill
    {
        public const float Radius = 2f, Seconds = 5f, PoisonShare = 1.2f;
        public SporeCloudSkill() { Cooldown = 12f; }
        public override float AreaRadius => Radius;
        public override bool AreaOnSelf => true;
        public override string Name => "포자 구름";
        public override string Description => "적이 다가오면 주변(반경 2)에 포자를 뿜어 5초간 공격력 120%만큼의 독.";
        public override Color Color => new(0.75f, 0.85f, 0.4f);

        public override bool TryUse(Unit self, Unit target)
        {
            var near = HostilesAround(self, self.Position, Radius);
            if (near.Count == 0) return false;
            foreach (var u in near.ToArray()) u.Afflictions.Poison(self.AttackNow * PoisonShare, Seconds, self);
            if (!Vfx("Cloud1", self.Position + new Vector2(0f, 0.2f), 1.6f)) Fx?.Ring(self.Position, Radius, Color, 0.6f);
            Vfx("Poisoning2", self.Position, 1.2f);
            Announce(self);
            return true;
        }
    }

    /// <summary>A long tongue that drags a foe a few cells off to the user's side and stuns it a moment.</summary>
    public class TonguePullSkill : Skill
    {
        public const float MinRange = 2.5f, MaxRange = 5f;
        public TonguePullSkill() { Cooldown = 10f; }
        public override string Name => "혀 끌어당기기";
        public override string Description => "2.5~5칸 떨어진 적을 혀로 끌어당겨 공격력 60% 피해와 0.4초 기절.";
        public override Color Color => new(1f, 0.55f, 0.65f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (target == null || !target.IsAlive) return false;
            float d = Combat.Distance(self, target);
            if (d < MinRange || d > MaxRange || !World.HasLineOfSight(self.Position, target.Position, 0)) return false;
            self.Visual?.FaceTowards(target.Position);
            Fx?.Beam(self.Position + new Vector2(0f, 0.3f), target.Position + new Vector2(0f, 0.3f), Color, 0.1f, 0.25f);
            Damage(self, target, 0.6f);
            target.Knockback(self.Position - target.Position, d - self.Radius - target.Radius - 0.3f);
            target.Stun(0.4f);
            Vfx("Hit1_Red", target.Position + new Vector2(0f, 0.35f), 0.8f);
            Announce(self);
            return true;
        }
    }

    /// <summary>Badly hurt, the beast goes wild: harder and faster for a while.</summary>
    public class FrenzySkill : Skill
    {
        public const float Share = 0.4f, Haste = 1.2f, Seconds = 8f;
        public FrenzySkill() { Cooldown = 25f; }
        public override string Name => "광폭화";
        public override string Description => "체력 50% 이하에서 싸우는 중이면 8초간 공격력 40%, 이동 속도 20% 증가.";
        public override Color Color => new(1f, 0.25f, 0.2f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (target == null || self.HpRatio > 0.5f) return false;
            self.Boons.Empower(Share, Seconds);
            self.ApplySpeedBuff(Haste, Seconds);
            if (!Vfx("Buff2_Red", self.Position, 1.3f)) Fx?.Ring(self.Position, 1.2f, Color, 0.5f);
            Vfx("Booster_Lightning", self.Position + new Vector2(0f, 0.5f), 0.8f);
            Announce(self);
            return true;
        }
    }

    /// <summary>The slime king bursts its slime round itself: hurts and slows whoever crowds it.</summary>
    public class SlimeBurstSkill : Skill
    {
        public const float Radius = 2.4f, Slow = 0.45f, Seconds = 3f;
        public SlimeBurstSkill() { Cooldown = 12f; }
        public override float AreaRadius => Radius;
        public override bool AreaOnSelf => true;
        public override string Name => "점액 폭발";
        public override string Description => "주변(반경 2.4)에 적이 2명 이상이면 점액을 터뜨려 공격력 90% 피해와 3초간 45% 감속.";
        public override Color Color => new(0.45f, 0.95f, 0.5f);

        public override bool TryUse(Unit self, Unit target)
        {
            var near = HostilesAround(self, self.Position, Radius);
            if (near.Count < 2) return false;
            foreach (var u in near.ToArray())
            {
                Damage(self, u, 0.9f);
                u.Afflictions.Chill(Slow, Seconds);
            }
            if (!Vfx("Explosion_Effect_Green", self.Position, 1.8f)) Fx?.Ring(self.Position, Radius, Color, 0.5f);
            Vfx("Bubble", self.Position + new Vector2(0f, 0.3f), 1.4f);
            Announce(self);
            return true;
        }
    }

    /// <summary>The yeti's blizzard round itself: cold that hurts and slows for a while.</summary>
    public class BlizzardSkill : Skill
    {
        public const float Radius = 2.8f, Slow = 0.5f, Seconds = 4f;
        public BlizzardSkill() { Cooldown = 14f; }
        public override float AreaRadius => Radius;
        public override bool AreaOnSelf => true;
        public override string Name => "눈보라";
        public override string Description => "주변(반경 2.8)에 눈보라를 일으켜 공격력 70% 피해와 4초간 50% 감속.";
        public override Color Color => new(0.85f, 0.95f, 1f);

        public override bool TryUse(Unit self, Unit target)
        {
            var near = HostilesAround(self, self.Position, Radius);
            if (near.Count == 0) return false;
            foreach (var u in near.ToArray())
            {
                Damage(self, u, 0.7f);
                u.Afflictions.Chill(Slow, Seconds);
            }
            if (!Vfx("Explosion_Effect_White", self.Position, 2f)) Fx?.Ring(self.Position, Radius, Color, 0.5f);
            Vfx("Cloud2", self.Position + new Vector2(0f, 0.3f), 1.6f);
            Announce(self);
            return true;
        }
    }

    /// <summary>A short, quick leap onto a foe a few cells off: a blow and a moment's stun on landing.</summary>
    public class PounceSkill : Skill
    {
        const float SpeedMultiplier = 4.5f, MaxDuration = 0.45f;
        public const float MinRange = 2f, MaxRange = 5f;

        Unit _target;
        Vector2 _dir;
        float _time;
        bool _active;

        public PounceSkill() { Cooldown = 9f; }
        public override string Name => "덮치기";
        public override string Description => "2~5칸 떨어진 적에게 뛰어들어 공격력 130% 피해와 0.6초 기절.";
        public override Color Color => new(1f, 0.8f, 0.4f);
        public override bool IsChanneling => _active;

        public override bool TryUse(Unit self, Unit target)
        {
            if (target == null || !target.IsAlive) return false;
            float d = Combat.Distance(self, target);
            if (d < MinRange || d > MaxRange || !World.HasLineOfSight(self.Position, target.Position, 0)) return false;
            _target = target;
            _dir = (target.Position - self.Position).normalized;
            _time = 0f;
            _active = true;
            self.Mover.Stop();
            self.Visual?.FaceTowards(target.Position);
            if (!Vfx("Smoke", self.Position + new Vector2(0f, 0.15f), 0.6f)) Fx?.Spark(self.Position, new Color(0.8f, 0.7f, 0.5f), 0.7f);
            Announce(self);
            return true;
        }

        public override void Tick(Unit self, float dt)
        {
            if (!_active) return;
            _time += dt;
            var next = self.Position + _dir * self.Stats.MoveSpeed * SpeedMultiplier * dt;
            bool blocked = !World.Grid.IsWalkable(GridMap.WorldToCell(next), self.PassesGates);
            if (!blocked) self.transform.position = new Vector3(next.x, next.y, self.transform.position.z);
            if (_target != null && _target.IsAlive && Combat.Distance(self, _target) <= self.Radius + _target.Radius + 0.35f)
            {
                Damage(self, _target, 1.3f);
                _target.Stun(0.6f);
                if (!Vfx("Hit5_Yellow", _target.Position + new Vector2(0f, 0.35f), 0.9f)) Fx?.Spark(_target.Position, Color, 0.9f);
                _active = false;
            }
            else if (blocked || _time >= MaxDuration || !self.IsAlive) _active = false;
        }
    }
}
