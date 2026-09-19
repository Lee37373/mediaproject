using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Fog;
using GuildMaster.Units;
using GuildMaster.Visuals;
using UnityEngine;

namespace GuildMaster.Monsters
{
    /// <summary>Receives loot and kill experience when this unit lands a killing blow.</summary>
    public interface ILootReceiver
    {
        void ReceiveKillRewards(MonsterDefinition victim, System.Collections.Generic.List<ItemStack> loot);
    }

    /// <summary>
    /// Pooled monster driven by a small FSM: wander around its nest, engage prey/rivals/humans,
    /// flee from predators, and return home when leashed.
    /// </summary>
    public class Monster : Unit, IProjectileStyle
    {
        public const string PoolKey = "unit.monster";
        const float SenseInterval = 0.4f;
        const float CorpseTime = 1.2f;

        public MonsterDefinition Def { get; private set; }
        public Nest Home { get; private set; }
        public StateMachine<Monster> Fsm { get; private set; }
        public Engagement Engage { get; private set; }

        internal MonsterWanderState WanderState;
        internal MonsterEngageState EngageState;
        internal MonsterFleeState FleeState;
        internal MonsterReturnState ReturnState;

        /// <summary>Optional override (e.g. raid waves) that replaces the default idle behaviour.</summary>
        public State<Monster> DefaultStateOverride { get; set; }

        float _senseTimer;
        float _corpseTimer;
        static int s_counter;
        static readonly System.Collections.Generic.List<ItemStack> s_loot = new();

        protected override void Awake()
        {
            base.Awake();
            Faction = Faction.Monster;
            Fsm = new StateMachine<Monster>(this);
            Engage = new Engagement(this);
            WanderState = new MonsterWanderState();
            EngageState = new MonsterEngageState();
            FleeState = new MonsterFleeState();
            ReturnState = new MonsterReturnState();
        }

        public static GameObject CreatePooledInstance()
        {
            var go = new GameObject("Monster");
            go.AddComponent<World.PathFollower>();
            go.AddComponent<CharacterVisual>();
            go.AddComponent<Monster>();
            go.AddComponent<HealthBar>();
            go.AddComponent<FogHideable>();
            return go;
        }

        public void Setup(MonsterDefinition def, Nest home)
        {
            Def = def;
            Home = home;
            UnitId = $"M_{++s_counter:0000}";
            DisplayName = def.displayName;
            name = $"{def.displayName}[{UnitId}]";
            Stats = new CombatStats
            {
                MaxHp = def.maxHp, Attack = def.attack, Defense = def.defense, AttackRange = def.attackRange,
                AttackCooldown = def.attackCooldown, MoveSpeed = def.moveSpeed, SightRadius = def.sightRadius,
            };
            IsRangedVariant = def.rangedVariantChance > 0f && GameRandom.Shared.Chance(def.rangedVariantChance);
            if (IsRangedVariant)
            {
                Stats.AttackRange = def.rangedVariantRange;
                Stats.Attack *= 0.8f;
                Stats.MaxHp *= 0.8f;
                Stats.AttackCooldown *= 1.2f;
                if (!string.IsNullOrEmpty(def.rangedVariantName)) DisplayName = def.rangedVariantName;
            }
            Radius = 0.28f * def.scale;
            Mass = def.scale * def.scale;
            SkillFactory.Assign(this, def.skillIds);
            ResetVitals();
            Visual.Init(PixelArt.Monster(def), def.scale);
            Visual.SetTint(IsRangedVariant ? new Color(0.85f, 0.9f, 1f) : Color.white);
            GetComponent<HealthBar>().Init(this, 0.75f * def.scale, new Color(0.9f, 0.25f, 0.2f));
            GetComponent<FogHideable>().Refresh();
            Engage.Clear();
            Engage.GiveUpDistance = def.leashRadius;
            DefaultStateOverride = null;
            _spawnPosition = transform.position;
            _corpseTimer = 0f;
            _senseTimer = GameRandom.Shared.Range(0f, SenseInterval);
            Fsm.ChangeState(WanderState);
        }

        public bool IsRangedVariant { get; private set; }
        public Color ProjectileColor => new(0.8f, 0.7f, 0.45f);

        public Vector2 HomePosition => Home != null ? Home.WorldPosition : _spawnPosition;
        Vector2 _spawnPosition;

        /// <summary>Pack leader = first living member of the nest (packs flock around it).</summary>
        public Monster PackLeader
        {
            get
            {
                if (!Def.packHunter || Home == null) return null;
                foreach (var m in Home.Members) if (m.IsAlive) return m;
                return null;
            }
        }

        public override void ForceTarget(Unit target)
        {
            if (!IsAlive || target == null || !target.IsAlive || Fsm.Current == FleeState) return;
            Engage.SetTarget(target);
            if (Fsm.Current != EngageState) Fsm.ChangeState(EngageState);
        }

        public void GoToDefaultState() => Fsm.ChangeState(DefaultStateOverride ?? WanderState);

        public override bool IsHostileTo(Unit other)
        {
            if (other == null || other == this || !other.IsAlive) return false;
            if (other is Monster m) return Def.IsPredatorOf(m.Def) || Def.IsRivalOf(m.Def);
            return other.Faction == Faction.Guild && Def.aggressiveToHumans;
        }

        /// <summary>A living predator (a monster that preys on us) within sight, if any.</summary>
        public Monster FindPredator()
        {
            if (!Def.fleesFromPredators) return null;
            return UnitRegistry.FindNearest(Position, Stats.SightRadius,
                u => u is Monster m && m.Def.IsPredatorOf(Def) && CanSee(m)) as Monster;
        }

        void Update()
        {
            if (!IsAlive)
            {
                _corpseTimer += Time.deltaTime;
                var c = Visual.Body.color;
                Visual.SetTint(new Color(c.r, c.g, c.b, Mathf.Clamp01(1f - _corpseTimer / CorpseTime)));
                if (_corpseTimer >= CorpseTime && Services.TryGet<PoolService>(out var pool)) pool.Despawn(gameObject);
                return;
            }

            TickStatus(Time.deltaTime);
            if (IsStunned) return;

            _senseTimer -= Time.deltaTime;
            if (_senseTimer <= 0f)
            {
                _senseTimer = SenseInterval;
                Sense();
            }
            Fsm.Tick(Time.deltaTime);
        }

        void Sense()
        {
            if (Fsm.Current == FleeState) return;
            var predator = FindPredator();
            if (predator != null && !WasRecentlyAttackedBy(predator, 2f))
            {
                FleeState.From = predator;
                Fsm.ChangeState(FleeState);
                return;
            }
            if (Fsm.Current == EngageState || Fsm.Current == ReturnState) return;
            var target = UnitRegistry.FindNearestHostile(this, Stats.SightRadius);
            if (target != null)
            {
                Engage.SetTarget(target);
                Fsm.ChangeState(EngageState);
            }
        }

        protected override void OnDamaged(float amount, Unit source)
        {
            // Retaliate immediately unless already fighting something.
            if (source != null && source.IsAlive && Fsm.Current != EngageState && Fsm.Current != FleeState)
            {
                Engage.SetTarget(source);
                Fsm.ChangeState(EngageState);
            }
        }

        protected override void OnDeath(Unit killer)
        {
            if (killer is ILootReceiver receiver)
            {
                s_loot.Clear();
                var rng = GameRandom.Shared;
                foreach (var entry in Def.loot)
                {
                    if (entry.item == null || !rng.Chance(entry.chance)) continue;
                    s_loot.Add(new ItemStack(entry.item.id, rng.Range(entry.min, entry.max + 1)));
                }
                receiver.ReceiveKillRewards(Def, s_loot);
            }
            Home?.OnMemberDied(this, killer);
        }

        /// <summary>Removes the monster without a killer (starvation, despawn).</summary>
        public void Perish()
        {
            if (!IsAlive) return;
            ReceiveDamage(Hp + 999f, null, false);
        }

        public string StateLabel => Fsm.Current?.Label ?? "-";
    }
}
