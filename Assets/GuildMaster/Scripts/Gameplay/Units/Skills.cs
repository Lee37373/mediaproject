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
    /// <summary>A support skill restored HP (P4-29): floating "+N" and effects.</summary>
    public struct UnitHealedEvent { public Unit Target; public Unit Healer; public float Amount; }
    /// <summary>A disaster monster's skill struck the ground (P4-72): on the right ground it may start a natural disaster.</summary>
    public struct HazardStrikeEvent { public Unit User; public Vector2 Point; public Disasters.DisasterKind Kind; }

    /// <summary>
    /// Active ability with its own trigger condition. Units try their ready skills before basic
    /// attacks. Multi-frame skills (charge, wind-ups) report IsChanneling to pause other actions.
    /// </summary>
    public abstract class Skill
    {
        float _readyAt;

        public abstract string Name { get; }
        /// <summary>Player-facing explanation (tooltips).</summary>
        public virtual string Description => "";
        /// <summary>Seconds until the skill can be used again (0 = ready).</summary>
        public float CooldownLeft => Mathf.Max(0f, _readyAt - Time.time);
        public virtual Color Color => UnityEngine.Color.white;
        public float Cooldown { get; protected set; } = 8f;
        public bool IsReady => Time.time >= _readyAt;
        public virtual bool IsChanneling => false;

        public void ResetCooldown(float stagger) => _readyAt = Time.time + stagger;
        protected void StartCooldown() => _readyAt = Time.time + Cooldown;

        public abstract bool TryUse(Unit self, Unit target);

        /// <summary>
        /// Asked for on purpose by tactics (P4-19), rather than fired by its own trigger. The
        /// cooldown still applies; skills whose automatic condition is about survival override
        /// this so a deliberate escape is not blocked by "you are not hurt enough yet".
        /// </summary>
        public virtual bool TryUseDeliberately(Unit self, Unit target) => TryUse(self, target);
        /// <summary>Blast radius for area skills (0 = single target); used by heroic skill timing.</summary>
        public virtual float AreaRadius => 0f;
        /// <summary>True if the area is centred on the user rather than the target.</summary>
        public virtual bool AreaOnSelf => false;
        /// <summary>Helps allies instead of hurting enemies; usable outside fights too (target may be null).</summary>
        public virtual bool IsSupport => false;
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
            float dmg = Combat.Mitigate(self.Stats.Attack * multiplier * GameRandom.Shared.Range(0.9f, 1.1f), target.Stats.Defense);
            target.ReceiveDamage(Mathf.Round(dmg), self, false);
        }

        /// <summary>
        /// The ally (or self) below <paramref name="below"/> HP ratio in reach and in sight that is worst
        /// off — lowest HP, a little worse when under attack right now — or null.
        /// </summary>
        protected static Unit MostEndangeredAlly(Unit self, float radius, float below)
        {
            Unit best = null;
            float bestScore = float.MaxValue;
            foreach (var u in UnitRegistry.All)
            {
                if (!u.IsAlive || u.HpRatio >= below || (u != self && !TargetSelector.IsAlly(self, u))) continue;
                if ((u.Position - self.Position).sqrMagnitude > radius * radius) continue;
                if (u != self && !World.HasLineOfSight(self.Position, u.Position, 2)) continue;
                float score = u.HpRatio - (Time.time - u.LastAttackedTime < 3f ? 0.1f : 0f);
                if (score < bestScore) { bestScore = score; best = u; }
            }
            return best;
        }

        /// <summary>Heals by a listed amount (scaled here by <see cref="Combat.ScaledHp"/>).</summary>
        protected static void HealUnit(Unit healer, Unit patient, float amount)
        {
            float before = patient.Hp;
            patient.Heal(Mathf.Round(Combat.ScaledHp(amount)));
            float gained = patient.Hp - before;
            if (gained <= 0f) return;
            Fx?.Spark(patient.Position + new Vector2(0f, 0.3f), new Color(0.55f, 1f, 0.6f), 0.9f, 0.35f);
            EventBus.Publish(new UnitHealedEvent { Target = patient, Healer = healer, Amount = gained });
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
        public override string Description => "적 2명 이상에게 둘러싸이면 주변(반경 1.9)을 베어 공격력 120% 피해를 주고 밀쳐 냅니다.";
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
        public override string Description => "체력 50% 이상일 때, 반경 5 안에서 동료를 노리는 적을 자신에게 끌어옵니다.";
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
        public override string Description => "일직선으로 꿰뚫는 화살. 선 위의 모든 적에게 공격력 130% 피해(산·벽에서 멈춤).";
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
        public override string Description => "느린 불덩이를 던져 맞은 곳 반경 1.6에 공격력 150% 피해. 나무에 막힐 수 있습니다.";
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
        public override string Description => "체력 75% 이하에서 근접한 적이 붙으면 안전한 곳으로 순간이동합니다.";
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

    /// <summary>Priest (P4-29): heals the ally in the most danger.</summary>
    public class HealSkill : Skill
    {
        const float Range = 6f;
        const float Below = 0.6f;

        public HealSkill() { Cooldown = 6f; }
        public override string Name => "치유";
        public override string Description => "체력 60% 미만인 동료 중 가장 위험한 한 명(자신 포함, 반경 6)을 공격력의 300%만큼 회복합니다.";
        public override Color Color => new(0.55f, 1f, 0.6f);
        public override bool IsSupport => true;

        public override bool TryUse(Unit self, Unit target)
        {
            var patient = MostEndangeredAlly(self, Range, Below);
            if (patient == null) return false;
            if (patient != self) Fx?.Beam(self.Position, patient.Position, Color, 0.08f, 0.3f);
            HealUnit(self, patient, self.Stats.Attack * 3f);
            Announce(self);
            return true;
        }
    }

    /// <summary>Priest (P4-29): heals every hurt ally around when several need it.</summary>
    public class MassHealSkill : Skill
    {
        const float Radius = 4f;
        const float Below = 0.7f;
        const int MinPatients = 2;
        static readonly List<Unit> s_patients = new();

        public MassHealSkill() { Cooldown = 15f; }
        public override string Name => "광역 치유";
        public override string Description => "주변(반경 4)에 체력 70% 미만인 동료가 2명 이상이면 모두를 공격력의 150%만큼 회복합니다.";
        public override Color Color => new(0.7f, 1f, 0.75f);
        public override bool IsSupport => true;

        public override bool TryUse(Unit self, Unit target)
        {
            s_patients.Clear();
            foreach (var u in UnitRegistry.All)
                if (u.IsAlive && u.HpRatio < Below && (u == self || TargetSelector.IsAlly(self, u))
                    && (u.Position - self.Position).sqrMagnitude <= Radius * Radius)
                    s_patients.Add(u);
            if (s_patients.Count < MinPatients) return false;
            foreach (var u in s_patients.ToArray()) HealUnit(self, u, self.Stats.Attack * 1.5f);
            Fx?.Ring(self.Position, Radius, Color, 0.5f);
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
        public override string Description => "2.5~7칸 떨어진 목표에게 돌진해 공격력 140% 피해, 넉백과 0.5초 기절.";
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
            bool blocked = !World.Grid.IsWalkable(GridMap.WorldToCell(next), self.PassesGates);
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
        public override string Description => "1.1초 예고 뒤 주변(반경 2.3)에 공격력 200% 피해와 강한 넉백. 붉은 장판을 피하세요.";
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
        public override string Description => "주변 같은 무리를 불러 목표를 함께 공격하고 4초간 이동속도 30% 증가.";
        public override Color Color => new(0.8f, 0.85f, 1f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (target == null || self is not Monsters.Monster me) return false;
            // Own pack only: a roaming pack has no nest, so "same nest" also matched every other roaming pack of the kind (P4-76).
            UnitRegistry.Collect(self.Position, 7f, u => u is Monsters.Monster m && me.IsPackMate(m), Buffer);
            // A lone wolf has nobody to call: no howl, and no free haste either (it used to buff
            // itself on every failed try, i.e. permanently while fighting alone).
            if (Buffer.Count == 0) return false;
            foreach (var u in Buffer.ToArray())
            {
                u.ApplySpeedBuff(1.3f, 4f);
                if (u.CurrentTarget == null) u.ForceTarget(target);
            }
            self.ApplySpeedBuff(1.3f, 4f);
            Fx?.Ring(self.Position, 3f, Color, 0.6f);
            Announce(self);
            return true;
        }
    }

    /// <summary>
    /// Ranger (P4-19): slips out of sight. Whoever was chasing loses its target and cannot pick
    /// this unit again while it lasts, which leaves the pursuer standing wherever it stopped —
    /// inside someone else's territory, if it was lured there. Attacking gives the position away.
    /// </summary>
    public class StealthSkill : Skill
    {
        public const float Duration = 5f;
        const float ThreatRange = 6f;

        public StealthSkill() { Cooldown = 18f; }
        public override string Name => "은신";
        public override string Description => "5초간 모습을 감춥니다. 쫓던 적은 목표를 잃고 새로 노리지 못합니다(공격하면 바로 풀림).";
        public override Color Color => new(0.55f, 0.55f, 0.75f);

        public override bool TryUse(Unit self, Unit target)
        {
            // On its own: only worth it while actually being chased and already hurt.
            if (self.HpRatio > 0.6f) return false;
            var chaser = Tactics.NearestMeleeThreat(self, ThreatRange);
            if (chaser == null) return false;
            return Hide(self);
        }

        /// <summary>A lure asks for this while unhurt, so the survival condition does not apply.</summary>
        public override bool TryUseDeliberately(Unit self, Unit target) => Hide(self);

        bool Hide(Unit self)
        {
            self.EnterStealth(Duration);
            // Everyone chasing loses the thread right now.
            UnitRegistry.Collect(self.Position, 14f, u => u.IsAlive && u.CurrentTarget == self, Buffer);
            foreach (var u in Buffer)
            {
                u.CombatEngagement?.Clear();
                u.Mover.Stop();
            }
            Fx?.Ring(self.Position, 1.2f, Color, 0.5f);
            Announce(self);
            return true;
        }
    }

    /// <summary>
    /// Mage (P4-19): a long hop that carries the caster clear over ground the pursuer has to walk.
    /// Used to put a hostile territory between the two — the chaser must cross what we skipped.
    /// </summary>
    public class WarpSkill : Skill
    {
        public const float Range = 7f;

        public WarpSkill() { Cooldown = 16f; }
        public override string Name => "차원 도약";
        public override string Description => "최대 7칸 앞으로 뛰어넘습니다. 쫓아오는 적은 그 사이를 걸어서 지나야 합니다.";
        public override Color Color => new(0.75f, 0.6f, 1f);

        public override bool TryUse(Unit self, Unit target)
        {
            if (self.HpRatio > 0.5f) return false;
            var threat = Tactics.NearestMeleeThreat(self, 2f);
            if (threat == null) return false;
            return WarpTowards(self, self.Position + (self.Position - threat.Position).normalized * Range);
        }

        /// <summary>Hops as far towards a point as the ground allows (tactics ask for this).</summary>
        public bool WarpTowards(Unit self, Vector2 towards)
        {
            if (!Services.TryGet<WorldService>(out var world)) return false;
            var dir = (towards - self.Position);
            if (dir.sqrMagnitude < 0.01f) return false;
            float distance = Mathf.Min(Range, dir.magnitude);
            var wanted = self.Position + dir.normalized * distance;
            if (!world.Grid.TryFindNearestWalkable(GridMap.WorldToCell(wanted), 3, out var cell)) return false;
            var landing = GridMap.CellToWorld(cell);
            Fx?.Spark(self.Position, Color, 1f);
            self.Mover.Stop();
            self.transform.position = new Vector3(landing.x, landing.y, self.transform.position.z);
            Fx?.Spark(landing, Color, 1f);
            Announce(self);
            return true;
        }

        /// <summary>A lure asks for a hop towards a point, not away from a threat.</summary>
        public bool WarpToPoint(Unit self, Vector2 point) => WarpTowards(self, point);

        public override bool TryUseDeliberately(Unit self, Unit target)
            => target != null && WarpTowards(self, target.Position);
    }

    /// <summary>
    /// The disaster monsters' skill (P4-72): a strike of their element — sparks, poison, a frost
    /// roar, a sand whirl, a stomp — that hurts what stands around the spot and may, on the right
    /// ground, set off a natural disaster there (<c>Disasters.DisasterService</c> decides).
    /// Spit and whirls land at the target, roars and stomps around the user.
    /// </summary>
    public class HazardStrikeSkill : Skill
    {
        const float Radius = 1.4f;
        const float SpitRange = 4f;
        readonly Disasters.DisasterKind _kind;

        public HazardStrikeSkill(Disasters.DisasterKind kind)
        {
            _kind = kind;
            Cooldown = 9f;
        }

        bool OnSelf => _kind is Disasters.DisasterKind.Avalanche or Disasters.DisasterKind.Quake;
        public override float AreaRadius => Radius;
        public override bool AreaOnSelf => OnSelf;

        public override string Name => _kind switch
        {
            Disasters.DisasterKind.Wildfire => "불씨 뿜기",
            Disasters.DisasterKind.PoisonFog => "독 뱉기",
            Disasters.DisasterKind.Avalanche => "서리 포효",
            Disasters.DisasterKind.Sandstorm => "모래 회오리",
            _ => "땅 울리기",
        };

        public override string Description
            => $"주변(반경 {Radius})에 공격력 120% 피해. 맞는 땅이면 낮은 확률로 {Disasters.DisasterRules.NameOf(_kind)}이(가) 일어납니다.";

        public override Color Color => _kind switch
        {
            Disasters.DisasterKind.Wildfire => new Color(1f, 0.45f, 0.15f),
            Disasters.DisasterKind.PoisonFog => new Color(0.65f, 0.35f, 0.9f),
            Disasters.DisasterKind.Avalanche => new Color(0.7f, 0.9f, 1f),
            Disasters.DisasterKind.Sandstorm => new Color(0.95f, 0.8f, 0.45f),
            _ => new Color(0.55f, 0.45f, 0.4f),
        };

        public override bool TryUse(Unit self, Unit target)
        {
            if (target == null || Combat.Distance(self, target) > (OnSelf ? 2f : SpitRange)) return false;
            if (!OnSelf && !World.HasLineOfSight(self.Position, target.Position, 1)) return false;
            var point = OnSelf ? self.Position : target.Position;
            foreach (var u in HostilesAround(self, point, Radius).ToArray()) Damage(self, u, 1.2f);
            Fx?.Ring(point, Radius, Color, 0.4f);
            Fx?.Spark(point, Color, 1.2f, 0.3f);
            if (OnSelf) ShakeIfVisible(point, 0.2f);
            Announce(self);
            EventBus.Publish(new HazardStrikeEvent { User = self, Point = point, Kind = _kind });
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
            "stealth" => new StealthSkill(),
            "warp" => new WarpSkill(),
            "heal" => new HealSkill(),
            "massheal" => new MassHealSkill(),
            "charge" => new ChargeSkill(),
            "slam" => new GroundSlamSkill(),
            "howl" => new PackHowlSkill(),
            // Disaster monsters (P4-72).
            "ember" => new HazardStrikeSkill(Disasters.DisasterKind.Wildfire),
            "toxic_spit" => new HazardStrikeSkill(Disasters.DisasterKind.PoisonFog),
            "frost_roar" => new HazardStrikeSkill(Disasters.DisasterKind.Avalanche),
            "sand_whirl" => new HazardStrikeSkill(Disasters.DisasterKind.Sandstorm),
            "quake_stomp" => new HazardStrikeSkill(Disasters.DisasterKind.Quake),
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
