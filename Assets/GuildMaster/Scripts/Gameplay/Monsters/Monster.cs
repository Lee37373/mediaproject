using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Fog;
using GuildMaster.Units;
using GuildMaster.Visuals;
using GuildMaster.World;
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
    /// Pooled monster driven by a small FSM: wander around its nest, engage other species,
    /// territory intruders and (aggressive kinds) humans, flee from predators, and return home
    /// when leashed (see Docs/CODEMAP.md §1 for where these rules live).
    /// </summary>
    public class Monster : Unit, IProjectileStyle, Fog.IFogGhostSubject
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
        internal MonsterRegroupState RegroupState;
        internal MonsterGateBreakState GateBreakState;
        /// <summary>Earliest time the next fall-back-and-regroup may start (skilled+ monsters, P4-28).</summary>
        internal float NextRegroupTime;

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
            RegroupState = new MonsterRegroupState();
            GateBreakState = new MonsterGateBreakState();
            Engage.ApproachSpot = EncircleSpot;
            // Town gates stay shut to monsters until broken (P4-39).
            if (Mover) Mover.PassesGates = false;
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
                MaxHp = Combat.ScaledHp(def.maxHp), Attack = def.attack, Defense = def.defense, AttackRange = def.attackRange,
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
            NextRegroupTime = 0f;
            _pickupTimer = GameRandom.Shared.Range(0f, PickupCheckInterval);
            foreach (var g in def.startingGear)
                if (g.item != null && GameRandom.Shared.Chance(g.chance)) Equipment.Equip(g.item);
            RefreshStats(fullHeal: true);
            // Imported Casual Monsters art when available (archer variants have their own image).
            var art = ArtLibrary.Monster(IsRangedVariant && !string.IsNullOrEmpty(def.rangedArtKey) ? def.rangedArtKey : def.artKey);
            Visual.Init(art != null ? art : PixelArt.Monster(def), def.scale);
            // Coloured by the region it lives in (P4-55; stands in for attributes until they are added).
            var baseTint = IsRangedVariant && art == null ? new Color(0.85f, 0.9f, 1f) : Color.white;
            Visual.SetTint(baseTint * TerrainRegions.MonsterTint(RegionOfHome(home)));
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

        /// <summary>The terrain region of its nest (or of where it appeared, when it has none).</summary>
        Region RegionOfHome(Nest home)
        {
            if (!Services.TryGet<WorldService>(out var world)) return Region.Grassland;
            var cell = home != null ? home.Cell : GridMap.WorldToCell(transform.position);
            return world.Map.RegionOf(cell.x, cell.y);
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

        /// <summary>Fighting smarts set per species (P4-26): slimes on instinct up to veteran bosses.</summary>
        public override CombatTier Tier => Def == null ? CombatTier.Novice : Def.combatTier;

        /// <summary>
        /// Anything hostile we can see, whoever just hit us, or one we can see fighting a pack mate
        /// (P4-52, user report 2026-09-22: "the rest of the pack does not fight" — even slimes, which
        /// never start a fight, stand by their own).
        /// </summary>
        bool IsTargetCandidate(Unit u)
            => u != this && (WasRecentlyAttackedBy(u) || ((IsHostileTo(u) || FightsAPackMate(u)) && CanSee(u)));

        bool FightsAPackMate(Unit u) => u.IsAlive && u.CurrentTarget is Monster mate && IsPackMate(mate);

        // ---------------- Pack alarm (P4-52) ----------------

        /// <summary>Pack mates this close hear a member being hit (a cry, not sight: forests do not hide it).</summary>
        public const float PackAlarmRadius = 8f;
        const float AlarmInterval = 1f;
        float _nextAlarm;
        static readonly System.Collections.Generic.List<Unit> s_alarmed = new();

        /// <summary>Hit: the pack mates within earshot turn on the attacker too.</summary>
        void AlarmPack(Unit attacker)
        {
            if (attacker == null || !attacker.IsAlive || attacker is Monster m && IsPackMate(m) || Time.time < _nextAlarm) return;
            _nextAlarm = Time.time + AlarmInterval;
            UnitRegistry.Collect(Position, PackAlarmRadius, u => u is Monster o && IsPackMate(o), s_alarmed);
            foreach (var u in s_alarmed) ((Monster)u).HearPackMateHit(attacker);
        }

        /// <summary>
        /// A pack mate's cry: one that is idle about the nest, picking something up or on a raid joins
        /// in; one already fighting keeps its own fight, one fleeing or walking home keeps doing that
        /// (the leash still rules, see <see cref="MonsterLeash"/>).
        /// </summary>
        internal void HearPackMateHit(Unit attacker)
        {
            if (!IsAlive || !attacker.IsTargetableBy(this) || IsStunned) return;
            var s = Fsm.Current;
            bool free = s == WanderState || s == PickupState || (DefaultStateOverride != null && s == DefaultStateOverride);
            if (!free) return;
            Engage.SetTarget(attacker);
            Fsm.ChangeState(EngageState);
        }

        /// <summary>Best target by <see cref="TargetSelector"/> (tier-dependent), or null.</summary>
        public Unit FindTarget() => TargetSelector.Pick(this, Stats.SightRadius, IsTargetCandidate, Engage);

        /// <summary>
        /// What we want is on the other side of the town wall and no path leads there (P4-39): go
        /// and break the nearest standing gate. Callers try the path first, so a gate that is
        /// already broken is simply walked through. Raiders keep at it (<paramref name="untilBroken"/>),
        /// chasers give up after a while.
        /// </summary>
        internal bool TryBreakGateToward(Vector2 goal, bool untilBroken)
        {
            if (!Services.TryGet<Town.GateService>(out var gates) || !Services.TryGet<World.WorldService>(out var world)) return false;
            if (world.Grid.IsInTown(Position) == world.Grid.IsInTown(goal)) return false;
            var gate = gates.NearestStanding(Position);
            if (gate == null) return false;
            GateBreakState.Begin(gate, untilBroken);
            Fsm.ChangeState(GateBreakState);
            return true;
        }

        /// <summary>Bashing a town gate right now (a chaser or a raider, P4-39).</summary>
        public bool IsBreakingGate => Fsm != null && Fsm.Current == GateBreakState;

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
            // Every other species is an enemy (P4-45, user decision 2026-09-22: goblins and skulls
            // walked past each other). Same-species packs only clash over territory (above).
            // Raiders on the march do not start fights on the way; hit, they still hit back.
            if (other is Monster m) return m.Def != Def && DefaultStateOverride == null;
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

        // ---------------- Pack tactics (skilled+, P4-28) ----------------

        static readonly System.Collections.Generic.List<Monster> s_encirclers = new();
        static readonly System.Collections.Generic.List<float> s_bearings = new();

        /// <summary>Another living member of the same pack: same species and nest (or both nest-less).</summary>
        public bool IsPackMate(Monster o) => o != null && o != this && o.IsAlive && o.Def == Def && o.Home == Home;

        /// <summary>Who calls the pack's target: the crown holder / pack leader, else the first member of the nest.</summary>
        Monster CallingLeader
        {
            get
            {
                var leader = PackLeader;
                if (leader != null || Home == null) return leader;
                foreach (var m in Home.Members) if (m.IsAlive) return m;
                return null;
            }
        }

        /// <summary>Skilled+ packs gang up on their leader's target (when the leader is close by).</summary>
        public override Unit CalledTarget
        {
            get
            {
                if (Tier < CombatTier.Skilled || Home == null) return null;
                var leader = CallingLeader;
                if (leader == null || leader == this || (leader.Position - Position).sqrMagnitude > 10f * 10f) return null;
                var t = leader.Engage.Target;
                return t != null && t.IsAlive ? t : null;
            }
        }

        /// <summary>
        /// Encircling: when two or more skilled+ melee pack mates go for the same target, each heads for
        /// its own slot around it (spread evenly, centred on where they come from) instead of the target.
        /// </summary>
        Vector2? EncircleSpot(Unit target)
        {
            if (Tier < CombatTier.Skilled || Stats.IsRanged || target == null) return null;
            s_encirclers.Clear();
            foreach (var u in UnitRegistry.All)
                if (u is Monster o && (o == this || IsPackMate(o)) && !o.Stats.IsRanged && o.Engage.Target == target) s_encirclers.Add(o);
            if (s_encirclers.Count < 2) return null;
            s_bearings.Clear();
            int index = 0;
            for (int i = 0; i < s_encirclers.Count; i++)
            {
                var d = s_encirclers[i].Position - target.Position;
                s_bearings.Add(Mathf.Atan2(d.y, d.x));
                if (s_encirclers[i] == this) index = i;
            }
            float angle = PackTactics.EncircleAngle(s_bearings, index);
            float radius = Stats.AttackRange * 0.9f + target.Radius + Radius;
            if (!Services.TryGet<World.WorldService>(out var world)) return null;
            // A slot on water or rock slides along the ring to the nearest walkable angle.
            for (int k = 0; k < 7; k++)
            {
                float a = angle + (k % 2 == 1 ? 1 : -1) * ((k + 1) / 2) * 20f * Mathf.Deg2Rad;
                var spot = target.Position + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                if (!world.Grid.IsWalkable(World.GridMap.WorldToCell(spot))) continue;
                // At the slot but shoved out of reach by the crowd: close the last step directly.
                return (spot - Position).sqrMagnitude < 0.35f * 0.35f ? null : spot;
            }
            return null;
        }

        /// <summary>
        /// Badly hurt and outnumbered (skilled+): fall back to the nearest group of pack mates away from
        /// the fight, or to the nest, then strike back together (MonsterRegroupState). False if not needed
        /// or there is nowhere to go.
        /// </summary>
        internal bool TryStartRegroup()
        {
            if (Tier < CombatTier.Skilled || HpRatio >= PackTactics.RegroupHp || Time.time < NextRegroupTime) return false;
            const float near = 6f;
            int allies = 0, enemies = 0;
            Monster rally = null;
            float rallyDist = float.MaxValue;
            foreach (var u in UnitRegistry.All)
            {
                if (!u.IsAlive || u == this) continue;
                float d = Vector2.Distance(u.Position, Position);
                if (u is Monster o && IsPackMate(o))
                {
                    bool busy = o.Fsm.Current == o.FleeState || o.Fsm.Current == o.RegroupState;
                    if (d <= near) { if (!busy) allies++; }
                    else if (!busy && d < rallyDist) { rallyDist = d; rally = o; }
                }
                else if ((IsHostileTo(u) || u.IsHostileTo(this)) && (u.CurrentTarget == this || d <= 2.5f)) enemies++;
            }
            // Not needed (or nowhere to go): look again in a moment rather than every frame.
            NextRegroupTime = Time.time + 0.5f;
            if (!PackTactics.ShouldRegroup(HpRatio, enemies, allies)) return false;
            if (rally == null && (Home == null || Vector2.Distance(Position, HomePosition) <= near)) return false;
            NextRegroupTime = Time.time + 20f;
            RegroupState.RallyMate = rally;
            RegroupState.Point = rally != null ? rally.Position : HomePosition;
            RegroupState.Enemy = Engage.Target ?? LastAttacker;
            Fsm.ChangeState(RegroupState);
            return true;
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
            if (Fsm.Current == FleeState || Fsm.Current == LureState || Fsm.Current == RegroupState) return;
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
                g.IsFreeFor(this) && (g.Position - home).sqrMagnitude <= range * range && WantsItem(g.Item) && CanSee(g.Position)
                && (leader == null || leader == this || g.Age >= LeaderFirstSeconds || !leader.WantsItem(g.Item)));
            if (item == null || !item.Reserve(this)) return;
            PickupState.Begin(item);
            Fsm.ChangeState(PickupState);
        }

        protected override void OnDamaged(float amount, Unit source)
        {
            AlarmPack(source);
            // Retaliate immediately unless already fighting something.
            if (source != null && source.IsAlive && source.IsTargetableBy(this) && Fsm.Current != EngageState && Fsm.Current != FleeState
                && Fsm.Current != LureState && Fsm.Current != RegroupState)
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

        // ---------------- Fog memory (P4-23) ----------------

        // The guild remembers a living monster where the fog took it; corpses leave nothing.
        bool Fog.IFogGhostSubject.WantsGhost => IsAlive;
        string Fog.IFogGhostSubject.GhostLabel => DisplayName;
        SpriteRenderer Fog.IFogGhostSubject.GhostBody => Visual != null ? Visual.Body : null;
        float Fog.IFogGhostSubject.GhostHpRatio => HpRatio;

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
