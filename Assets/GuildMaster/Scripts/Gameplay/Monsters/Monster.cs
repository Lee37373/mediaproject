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
    /// <summary>A monster wore or swallowed an item from the ground (P4-21).</summary>
    public struct MonsterItemUsedEvent { public Monster Monster; public ItemDefinition Item; public bool Absorbed; }

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
        internal MonsterLureState LureState;
        /// <summary>Earliest time the next lure retreat may start (veteran monsters).</summary>
        internal float NextLureTime;
        internal MonsterReturnState ReturnState;
        internal RaidState RaidState;
        internal MonsterPickupState PickupState;

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
            LureState = new MonsterLureState();
            ReturnState = new MonsterReturnState();
            RaidState = new RaidState();
            PickupState = new MonsterPickupState();
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
            // P4-21: base stats kept apart from gear; some spawn already wearing something.
            _baseStats = Stats;
            Equipment.Clear();
            _crownAura = false;
            NextLureTime = 0f;
            _pickupTimer = GameRandom.Shared.Range(0f, PickupCheckInterval);
            foreach (var g in def.startingGear)
                if (g.item != null && GameRandom.Shared.Chance(g.chance)) Equipment.Equip(g.item);
            RefreshStats(fullHeal: true);
            // Imported Casual Monsters art when available (archer variants have their own image).
            var art = ArtLibrary.Monster(IsRangedVariant && !string.IsNullOrEmpty(def.rangedArtKey) ? def.rangedArtKey : def.artKey);
            Visual.Init(art != null ? art : PixelArt.Monster(def), def.scale);
            Visual.SetTint(IsRangedVariant && art == null ? new Color(0.85f, 0.9f, 1f) : Color.white);
            GetComponent<HealthBar>().Init(this, 0.75f * def.scale, new Color(0.9f, 0.25f, 0.2f));
            GetComponent<FogHideable>().Refresh();
            Engage.Clear();
            Engage.GiveUpDistance = def.leashRadius;
            Engage.SearchRadius = def.sightRadius;
            Engage.CandidateFilter = IsTargetCandidate;
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
                if (Home == null) return null;
                // The crown holder leads, whatever the species (P4-21).
                foreach (var m in Home.Members) if (m.IsAlive && m.Equipment.Has(MonsterEquipment.PackLeaderEffect)) return m;
                if (!Def.packHunter) return null;
                foreach (var m in Home.Members) if (m.IsAlive) return m;
                return null;
            }
        }

        /// <summary>Danger sets fighting smarts: weak species fight like novices, bosses like veterans.</summary>
        public override CombatTier Tier => Def == null ? CombatTier.Novice : Def.danger switch
        {
            DangerLevel.Low => CombatTier.Novice,
            DangerLevel.Medium => CombatTier.Skilled,
            _ => CombatTier.Veteran,
        };

        /// <summary>Anything hostile we can see, or whoever just hit us.</summary>
        bool IsTargetCandidate(Unit u)
            => u != this && (WasRecentlyAttackedBy(u) || (IsHostileTo(u) && CanSee(u)));

        /// <summary>Best target by <see cref="TargetSelector"/> (tier-dependent), or null.</summary>
        public Unit FindTarget() => TargetSelector.Pick(this, Stats.SightRadius, IsTargetCandidate, Engage);

        public override void ForceTarget(Unit target)
        {
            if (!IsAlive || target == null || !target.IsAlive || Fsm.Current == FleeState) return;
            // Taunts and pack calls hold for a few seconds before normal retargeting resumes.
            Engage.Force(target, 4f);
            if (Fsm.Current != EngageState) Fsm.ChangeState(EngageState);
        }

        public void GoToDefaultState() => Fsm.ChangeState(DefaultStateOverride ?? WanderState);

        /// <summary>Walks back to the home position (nest, or spawn point for nest-less monsters).</summary>
        public void GoToReturn() { Engage.Clear(); Fsm.ChangeState(ReturnState); }

        public override bool IsHostileTo(Unit other)
        {
            if (other == null || other == this || !other.IsAlive) return false;
            // Residents at home drive intruders (other species, guild units) out of their territory.
            if (IsDefendingTerritory && Home.IsIntruder(other)) return true;
            if (other is Monster m) return Def.IsPredatorOf(m.Def) || Def.IsRivalOf(m.Def);
            return other.Faction == Faction.Guild && Def.aggressiveToHumans;
        }

        /// <summary>Lives in a nest and is not away raiding (raiders do not guard their home).</summary>
        public bool IsDefendingTerritory => Home != null && DefaultStateOverride == null;

        /// <summary>Runs away from a point (territorial rout) for a few seconds, then resumes.</summary>
        public void FleeFrom(Vector2 point)
        {
            if (!IsAlive) return;
            Engage.Clear();
            FleeState.From = null;
            FleeState.FromPoint = point;
            Fsm.ChangeState(FleeState);
        }

        /// <summary>Migration to another nest (see Nest.TransferMember): heads for the new home.</summary>
        internal void ChangeHome(Nest nest)
        {
            Home = nest;
            if (IsAlive && Fsm.Current != EngageState) GoToReturn();
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
            TickCrownAura(Time.deltaTime);
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
            if (Fsm.Current == FleeState || Fsm.Current == LureState) return;
            // Raiders march together; they do not scatter from predators in their own wave.
            var predator = DefaultStateOverride == null ? FindPredator() : null;
            if (predator != null && !WasRecentlyAttackedBy(predator, 2f))
            {
                FleeState.From = predator;
                Fsm.ChangeState(FleeState);
                return;
            }
            if (Fsm.Current == EngageState || Fsm.Current == ReturnState) return;
            var target = FindTarget();
            if (target != null)
            {
                Engage.SetTarget(target);
                Fsm.ChangeState(EngageState);
                return;
            }
            if (Fsm.Current == WanderState) TryStartPickup();
        }

        const float PickupCheckInterval = 1.5f;
        /// <summary>Pack mates leave a fresh drop to the leader for this long.</summary>
        const float LeaderFirstSeconds = 3f;
        float _pickupTimer;

        /// <summary>
        /// While idling at home: walk to a visible item worth taking (P4-21), only within the home
        /// range (wander radius + 3) so monsters do not roam off after loot.
        /// </summary>
        void TryStartPickup()
        {
            if (Def.pickupMode == MonsterPickupMode.None || DefaultStateOverride != null) return;
            _pickupTimer -= SenseInterval;
            if (_pickupTimer > 0f) return;
            _pickupTimer = PickupCheckInterval;
            if (!Services.TryGet<Items.GroundItemService>(out var ground) || ground.Items.Count == 0) return;
            var home = HomePosition;
            float range = Def.wanderRadius + 3f;
            var leader = PackLeader;
            var item = ground.FindNearest(Position, Stats.SightRadius, g =>
                g.IsFreeFor(this) && (g.Position - home).sqrMagnitude <= range * range && WantsItem(g.Item)
                && (leader == null || leader == this || g.Age >= LeaderFirstSeconds || !leader.WantsItem(g.Item)));
            if (item == null || !item.Reserve(this)) return;
            PickupState.Begin(item);
            Fsm.ChangeState(PickupState);
        }

        protected override void OnDamaged(float amount, Unit source)
        {
            // Retaliate immediately unless already fighting something.
            if (source != null && source.IsAlive && source.IsTargetableBy(this) && Fsm.Current != EngageState && Fsm.Current != FleeState && Fsm.Current != LureState)
            {
                Engage.SetTarget(source);
                Fsm.ChangeState(EngageState);
            }
        }

        protected override void OnDeath(Unit killer)
        {
            // P4-21: everything falls to the ground (worn gear always, the loot table when killed);
            // a killing adventurer still gets the kill and its EXP, but must pick the loot up.
            if (killer != null) MonsterEquipment.BuildDrops(Def, Equipment.Worn, GameRandom.Shared, s_loot);
            else MonsterEquipment.BuildDrops(null, Equipment.Worn, GameRandom.Shared, s_loot);
            if (s_loot.Count > 0 && Services.TryGet<Items.GroundItemService>(out var ground)) ground.Drop(Position, s_loot);
            Equipment.Clear();
            if (killer is ILootReceiver receiver) receiver.ReceiveKillRewards(Def, s_noLoot);
            Home?.OnMemberDied(this, killer);
        }

        static readonly System.Collections.Generic.List<ItemStack> s_noLoot = new();

        // ---------------- Equipment (P4-21) ----------------

        public MonsterEquipment Equipment { get; } = new();
        CombatStats _baseStats;
        bool _crownAura;
        float _auraTimer;
        SpriteRenderer _gearIcon;

        /// <summary>Base stats + worn gear + swallowed HP + crown aura; keeps the HP ratio (or heals fully).</summary>
        public void RefreshStats(bool fullHeal = false)
        {
            float ratio = Stats.MaxHp > 0f ? HpRatio : 1f;
            var s = _baseStats;
            Equipment.Apply(ref s);
            if (_crownAura) s.Attack *= 1.1f;
            Stats = s;
            if (fullHeal) ResetVitals();
            else if (IsAlive) SetHp(Mathf.Max(1f, s.MaxHp * ratio));
            UpdateGearIcon();
        }

        /// <summary>Largest HP a slime can gain by swallowing items.</summary>
        public float AbsorbCap => _baseStats.MaxHp * MonsterEquipment.AbsorbCapShare;

        /// <summary>Would this monster pick the item up (wear it or swallow it)?</summary>
        public bool WantsItem(ItemDefinition item)
            => Def != null && MonsterEquipment.Wants(Def.pickupMode, item, item == null ? null : Equipment.InSlot(item.equipSlot), Equipment.AbsorbedHp, AbsorbCap);

        /// <summary>Takes the item: wear it (dropping what it replaces) or swallow it for HP.</summary>
        public void UseItem(ItemDefinition item)
        {
            if (item == null) return;
            if (item.IsUnique || Def.pickupMode == MonsterPickupMode.Equip)
            {
                var old = Equipment.Equip(item);
                if (old != null && Services.TryGet<Items.GroundItemService>(out var ground))
                    ground.Drop(Position, new[] { new ItemStack(old.id, 1) });
                RefreshStats();
            }
            else
            {
                float gain = Equipment.Absorb(item, AbsorbCap);
                RefreshStats();
                Heal(gain);
            }
            EventBus.Publish(new MonsterItemUsedEvent { Monster = this, Item = item, Absorbed = !item.IsUnique && Def.pickupMode == MonsterPickupMode.Absorb });
        }

        /// <summary>Holders of the crown lead the pack; pack mates near it hit 10% harder.</summary>
        void TickCrownAura(float dt)
        {
            _auraTimer -= dt;
            if (_auraTimer > 0f) return;
            _auraTimer = 1f;
            bool aura = false;
            if (Home != null && !Equipment.Has(MonsterEquipment.PackLeaderEffect))
                foreach (var m in Home.Members)
                    if (m != this && m.IsAlive && m.Equipment.Has(MonsterEquipment.PackLeaderEffect)
                        && (m.Position - Position).sqrMagnitude <= 6f * 6f) { aura = true; break; }
            if (aura == _crownAura) return;
            _crownAura = aura;
            RefreshStats();
        }

        /// <summary>
        /// Small marker for worn gear: a gold crown dot for the leader, else the item's colour.
        /// FogHideable owns the renderer's enabled flag, so "no gear" hides the GameObject instead —
        /// toggling the renderer showed the dot inside the fog (gear picked up out of sight) and left a
        /// stale dot on pooled monsters that respawned without gear.
        /// </summary>
        void UpdateGearIcon()
        {
            ItemDefinition shown = null;
            foreach (var i in Equipment.Worn) if (shown == null || MonsterEquipment.Score(i) > MonsterEquipment.Score(shown)) shown = i;
            if (shown == null) { if (_gearIcon != null) _gearIcon.gameObject.SetActive(false); return; }
            var fog = GetComponent<FogHideable>();
            if (_gearIcon == null)
            {
                var go = new GameObject("GearIcon");
                go.transform.SetParent(transform, false);
                _gearIcon = go.AddComponent<SpriteRenderer>();
                _gearIcon.sprite = SpriteFactory.Circle(10, Color.white, new Color(0.1f, 0.08f, 0.06f, 1f));
                fog.Refresh();
            }
            _gearIcon.gameObject.SetActive(true);
            _gearIcon.enabled = fog.IsFogVisible;
            _gearIcon.color = shown.color;
            float size = shown.IsUnique ? 0.3f : 0.22f;
            float s = size / _gearIcon.sprite.bounds.size.x;
            _gearIcon.transform.localScale = new Vector3(s, s, 1f);
            _gearIcon.transform.localPosition = shown.IsUnique ? new Vector3(0f, 0.95f * Def.scale, 0f) : new Vector3(0.28f * Def.scale, 0.55f * Def.scale, 0f);
            _gearIcon.sortingOrder = CharacterVisual.BaseSortingOrder + 900;
        }

        /// <summary>Removes the monster without a killer (starvation, despawn).</summary>
        public void Perish()
        {
            if (!IsAlive) return;
            ReceiveDamage(Hp + 999f, null, false);
        }

        public string StateLabel => Fsm.Current?.Label ?? "-";
        public override string DebugState => StateLabel;
        public override Engagement CombatEngagement => Engage;
    }
}
