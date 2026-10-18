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
        void ReceiveKillRewards(MonsterDefinition victim, int exp, System.Collections.Generic.List<ItemStack> loot);
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
        /// <summary>On a raid against another village (P4-84).</summary>
        internal VillageRaidState VillageRaidState;
        internal MigrateState MigrateState;
        internal RoamState RoamState;
        /// <summary>The roaming pack it walks with when its pack has no nest (P4-58), else null.</summary>
        internal RoamingPack Roaming;
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
            VillageRaidState = new VillageRaidState();
            MigrateState = new MigrateState();
            RoamState = new RoamState();
            PickupState = new MonsterPickupState();
            RegroupState = new MonsterRegroupState();
            GateBreakState = new MonsterGateBreakState();
            Engage.ApproachSpot = EncircleSpot;
            // Town gates stay shut to monsters until broken (P4-39).
            if (Mover)
            {
                Mover.PassesGates = false;
                Mover.BelievedShut = BelievedShut;
            }
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

        public void Setup(MonsterDefinition def, Nest home, bool boss = false)
        {
            // The random draws stay in this order (seeded worlds must come out the same).
            bool ranged = !boss && def.rangedVariantChance > 0f && GameRandom.Shared.Chance(def.rangedVariantChance);
            Prepare(def, home, boss, ranged, $"M_{++s_counter:0000}");
            _pickupTimer = GameRandom.Shared.Range(0f, PickupCheckInterval);
            foreach (var g in def.startingGear)
                if (g.item != null && GameRandom.Shared.Chance(g.chance)) Equipment.Equip(g.item);
            Finish(home);
            _senseTimer = GameRandom.Shared.Range(0f, SenseInterval);
            Fsm.ChangeState(WanderState);
        }

        /// <summary>
        /// A rare variant (P4-85, <see cref="RareMonster"/>): alone, with no nest, at <paramref name="level"/>. The caller picks
        /// the spot, the kind and the variant; nothing here draws a random number.
        /// </summary>
        public void SetupRare(MonsterDefinition def, int variant, int level)
        {
            Prepare(def, null, false, false, $"M_{++s_counter:0000}", variant);
            Level = Mathf.Clamp(level, 1, MonsterLevel.MaxLevel);
            ApplyLevel();
            Finish(null);
            _senseTimer = SenseInterval;
            Fsm.ChangeState(WanderState);
        }

        /// <summary>Who it is and its base numbers, at its nest's birth level, wearing nothing (no random draws).</summary>
        void Prepare(MonsterDefinition def, Nest home, bool boss, bool ranged, string id, int rare = -1)
        {
            Def = def;
            Home = home;
            Roaming = null;
            UnitId = id;
            RareVariant = rare;
            InHorde = false;
            // A region lord (P4-83) is always its lair's boss, under its own name.
            IsBoss = boss || def.isRegionLord;
            DisplayName = def.isRegionLord ? def.displayName : boss ? MonsterBoss.Name(def.displayName, def.bossName)
                        : rare >= 0 ? RareMonster.Name(rare, def.displayName) : def.displayName;
            name = $"{def.displayName}[{UnitId}]";
            Stats = new CombatStats
            {
                MaxHp = Combat.ScaledHp(def.maxHp), Attack = def.attack, Defense = def.defense, AttackRange = def.attackRange,
                AttackCooldown = def.attackCooldown, MoveSpeed = def.moveSpeed, SightRadius = def.sightRadius,
            };
            IsRangedVariant = ranged;
            if (IsRangedVariant)
            {
                Stats.AttackRange = def.rangedVariantRange;
                Stats.Attack *= 0.8f;
                Stats.MaxHp *= 0.8f;
                Stats.AttackCooldown *= 1.2f;
                if (!string.IsNullOrEmpty(def.rangedVariantName)) DisplayName = def.rangedVariantName;
            }
            if (def.isRegionLord)
            {
                Stats.MaxHp *= RegionLords.HpMultiplier;
                Stats.Attack *= RegionLords.AttackMultiplier;
                Stats.Defense += RegionLords.DefenseBonus;
            }
            else if (boss)
            {
                Stats.MaxHp *= MonsterBoss.HpMultiplier;
                Stats.Attack *= MonsterBoss.AttackMultiplier;
                Stats.Defense += MonsterBoss.DefenseBonus;
            }
            else if (rare >= 0)
            {
                Stats.MaxHp *= RareMonster.HpMultiplier;
                Stats.Attack *= RareMonster.AttackMultiplier;
                Stats.Defense += RareMonster.DefenseBonus;
            }
            Radius = 0.28f * Scale;
            Mass = Scale * Scale;
            SkillFactory.Assign(this, def.skillIds);
            _ownKnownOpenGates.Clear();
            // P4-21: base stats kept apart from gear; some spawn already wearing something.
            // Own level (user decision 2026-09-22): born higher the farther out the nest lies.
            _unlevelledStats = Stats;
            // A lord is born beyond the ordinary cap (P4-83), and never grows (it is already past the cap).
            Level = def.isRegionLord ? RegionLords.Level
                : Mathf.Min(MonsterLevel.MaxLevel, (home != null ? home.BirthLevel : 1) + (boss ? MonsterBoss.LevelBonus : 0));
            _lastFightTime = float.NegativeInfinity;
            Exp = 0;
            TierExp = 0;
            ApplyLevel();
            Equipment.Clear();
            _crownAura = false;
            NextLureTime = 0f;
            NextRegroupTime = 0f;
        }

        /// <summary>Stats with its gear, looks, bars and fighting settings; full health, at home doing nothing.</summary>
        void Finish(Nest home)
        {
            var def = Def;
            RefreshStats(fullHeal: true);
            var art = ImportedArt(def, IsBoss, IsRangedVariant);
            Visual.Init(art != null ? art : PixelArt.Monster(def), Scale);
            // Coloured by the region it lives in (P4-55; stands in for attributes until they are added).
            var baseTint = IsRangedVariant && art == null ? new Color(0.85f, 0.9f, 1f) : Color.white;
            // A rare variant shines in its own colour instead of the land's (P4-85).
            Visual.SetTint(IsRare ? RareMonster.Tint(RareVariant) : baseTint * def.artTint * TerrainRegions.MonsterTint(RegionOfHome(home)));
            GetComponent<HealthBar>().Init(this, 0.75f * Scale, new Color(0.9f, 0.25f, 0.2f));
            GetComponent<FogHideable>().Refresh();
            Engage.Clear();
            Engage.GiveUpDistance = def.leashRadius;
            Engage.SearchRadius = def.sightRadius;
            Engage.CandidateFilter = _isTargetCandidate ??= IsTargetCandidate;
            DefaultStateOverride = null;
            _spawnPosition = transform.position;
            _corpseTimer = 0f;
        }

        /// <summary>Imported Casual Monsters art when available (archer variants and bosses have their own image), else null.</summary>
        public static Sprite ImportedArt(MonsterDefinition def, bool boss, bool ranged)
        {
            var art = boss && !string.IsNullOrEmpty(def.bossArtKey) ? ArtLibrary.Monster(def.bossArtKey) : null;
            return art != null ? art : ArtLibrary.Monster(ranged && !string.IsNullOrEmpty(def.rangedArtKey) ? def.rangedArtKey : def.artKey);
        }

        /// <summary>How this kind is drawn (imported art, or the generated picture).</summary>
        public static Sprite ArtFor(MonsterDefinition def, bool boss, bool ranged)
            => ImportedArt(def, boss, ranged) is { } art ? art : PixelArt.Monster(def);

        /// <summary>The terrain region of its nest (or of where it appeared, when it has none).</summary>
        Region RegionOfHome(Nest home)
        {
            if (!Services.TryGet<WorldService>(out var world)) return Region.Grassland;
            var cell = home != null ? home.Cell : GridMap.WorldToCell(transform.position);
            return world.Map.RegionOf(cell.x, cell.y);
        }

        public bool IsRangedVariant { get; private set; }
        /// <summary>The pack's boss (P4-57, <see cref="MonsterBoss"/>): bigger, tougher, leads the pack.</summary>
        public bool IsBoss { get; private set; }
        /// <summary>One of the four region lords (P4-83, <see cref="RegionLords"/>); always also its lair's boss.</summary>
        public bool IsRegionLord => Def != null && Def.isRegionLord;
        /// <summary>Marching with a sudden incident's horde (P4-85, set and cleared by <see cref="MigrationService"/>).</summary>
        internal bool InHorde;
        /// <summary>Which rare variant it is (P4-85, <see cref="RareMonster"/>), or -1 for an ordinary member of its kind.</summary>
        public int RareVariant { get; private set; } = -1;
        public bool IsRare => RareVariant >= 0;
        /// <summary>Drawn size: the species' scale, bigger for a boss or a rare variant.</summary>
        public float Scale => Def == null ? 1f : Def.scale * (IsBoss ? MonsterBoss.SizeMultiplier : IsRare ? RareMonster.SizeMultiplier : 1f);
        public Color ProjectileColor => new(0.8f, 0.7f, 0.45f);

        public Vector2 HomePosition => Home != null ? Home.WorldPosition : Roaming != null ? Roaming.Center : _spawnPosition;

        /// <summary>Its pack: the nest's members, or the roaming pack's (P4-58); null when alone.</summary>
        internal System.Collections.Generic.List<Monster> PackMembers => Home != null ? Home.MemberList : Roaming?.Members;
        Vector2 _spawnPosition;

        /// <summary>Pack leader = first living member of the nest (packs flock around it).</summary>
        public Monster PackLeader
        {
            get
            {
                var pack = PackMembers;
                if (pack == null) return null;
                // The crown holder leads, whatever the species (P4-21).
                foreach (var m in pack) if (m.IsAlive && m.Equipment.Has(MonsterEquipment.PackLeaderEffect)) return m;
                // Then the pack's boss (P4-57).
                foreach (var m in pack) if (m.IsAlive && m.IsBoss) return m;
                if (!Def.packHunter) return null;
                foreach (var m in pack) if (m.IsAlive) return m;
                return null;
            }
        }

        /// <summary>
        /// Fighting smarts set per species (P4-26): slimes on instinct up to veteran bosses, a step per
        /// few levels, and a step or two more for a pack that has eaten its way over battlefields
        /// nobody cleared (<see cref="TierExp"/>).
        /// </summary>
        public override CombatTier Tier => Def == null ? CombatTier.Novice
            : MonsterLevel.TierAt(IsRare ? RareMonster.SpeciesTier(Def.combatTier) : MonsterBoss.SpeciesTier(Def.combatTier, IsBoss), Level, TierExp);

        // ---------------- Level and EXP (user decision 2026-09-22) ----------------

        public int Level { get; private set; } = 1;
        public int Exp { get; private set; }

        /// <summary>
        /// What it has learned eating the leavings of fights (user decision 2026-09-23). Kept apart
        /// from <see cref="Exp"/>: this buys smarts, not strength, so the numbers a member reads off a
        /// monster stay true to its level.
        /// </summary>
        public int TierExp { get; private set; }

        /// <summary>Steps of tier this one has scavenged, for the guild's own reckoning and the UI.</summary>
        public int ScavengedTierSteps => MonsterLevel.ScavengedTierSteps(TierExp);

        void GainTierExp(int amount)
        {
            if (amount <= 0 || !IsAlive) return;
            TierExp += amount;
        }
        public int ExpToNext => MonsterLevel.ExpToNext(Level);
        /// <summary>What killing this one is worth.</summary>
        public int ExpWorth => Def == null ? 0
            : MonsterLevel.ExpWorth(Def.expValue, Level)
              * (IsRegionLord ? RegionLords.ExpMultiplier : IsBoss ? MonsterBoss.ExpMultiplier : IsRare ? RareMonster.ExpMultiplier : 1);
        CombatStats _unlevelledStats;

        /// <summary>Base stats = the species (and ranged variant) scaled by level; gear goes on top.</summary>
        void ApplyLevel()
        {
            _baseStats = MonsterLevel.Grow(_unlevelledStats, Level);
        }

        /// <summary>Sets the level outright (tests and debug tools; P4-58 the level is the strength). Heals fully.</summary>
        internal void SetLevel(int level)
        {
            Level = Mathf.Clamp(level, 1, MonsterLevel.MaxLevel);
            Exp = 0;
            ApplyLevel();
            RefreshStats(fullHeal: true);
        }

        /// <summary>Danger shown to the guild (P4-58): by level, one step more for a boss or a rare variant.</summary>
        public DangerLevel Danger => (DangerLevel)Mathf.Min((int)DangerLevel.High, (int)MonsterLevel.DangerAt(Level) + (IsBoss || IsRare ? 1 : 0));

        /// <summary>EXP for a kill: this monster's own, and the same for pack mates fighting close by.</summary>
        public void GainKillExp(int amount)
        {
            GainExp(amount);
            var pack = PackMembers;
            if (pack == null) return;
            foreach (var m in pack)
                if (m != this && m.IsAlive && (m.Position - Position).sqrMagnitude <= MonsterLevel.ExpShareRadius * MonsterLevel.ExpShareRadius)
                    m.GainExp(amount);
        }

        public void GainExp(int amount)
        {
            if (amount <= 0 || !IsAlive || Level >= MonsterLevel.MaxLevel) return;
            Exp += amount;
            bool levelled = false;
            while (Level < MonsterLevel.MaxLevel && Exp >= ExpToNext)
            {
                Exp -= ExpToNext;
                Level++;
                levelled = true;
            }
            if (!levelled) return;
            ApplyLevel();
            RefreshStats();
            Heal(Stats.MaxHp * 0.3f);
        }

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

        /// <summary>
        /// A campfire in sight (P4-66): one idling about, picking something up, roaming or migrating
        /// comes for the people at it — and cries for its pack as it goes. One on a raid, in a fight,
        /// fleeing or walking home keeps doing that. The caller has checked that it sees the light.
        /// </summary>
        internal bool DrawnToFirelight(Unit camper)
        {
            if (!IsAlive || IsStunned || camper == null || !camper.IsAlive || !camper.IsTargetableBy(this)) return false;
            var s = Fsm.Current;
            bool free = s == WanderState || s == PickupState
                        || s == DefaultStateOverride && (DefaultStateOverride == RoamState || DefaultStateOverride == MigrateState);
            if (!free) return false;
            Engage.SetTarget(camper);
            Fsm.ChangeState(EngageState);
            UnitRegistry.Collect(Position, PackAlarmRadius, u => u is Monster o && o != this && IsPackMate(o), s_alarmed);
            foreach (var u in s_alarmed) ((Monster)u).HearPackMateHit(camper);
            return true;
        }

        /// <summary>Best target by <see cref="TargetSelector"/> (tier-dependent), or null.</summary>
        public Unit FindTarget() => TargetSelector.Pick(this, Stats.SightRadius, _isTargetCandidate ??= IsTargetCandidate, Engage);

        /// <summary>Created once: a method group passed as a delegate allocates a new one every call (P5-2).</summary>
        System.Func<Unit, bool> _isTargetCandidate;

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

        /// <summary>
        /// A raid or a migration is over (given up, or the band is gone): back to ordinary life — its
        /// nest, or its roaming pack when the nest was lost meanwhile (P4-58). Raiders keep their raid
        /// when their pack takes to the road (<see cref="StartRoaming"/>), so the raid's end has to
        /// hand them back to the pack; clearing the override alone left one wandering with no nest.
        /// </summary>
        internal void EndMarch()
        {
            if (Roaming != null)
            {
                DefaultStateOverride = RoamState;
                Engage.Clear();
                GoToDefaultState();
                return;
            }
            DefaultStateOverride = null;
            GoToReturn();
        }

        public override bool IsHostileTo(Unit other)
        {
            if (other == null || other == this || !other.IsAlive) return false;
            // Residents at home drive intruders (other species, guild units) out of their territory.
            if (IsDefendingTerritory && Home.IsIntruder(other)) return true;
            // Every other species is an enemy (P4-45, user decision 2026-09-22: goblins and skulls
            // walked past each other). Same-species packs only clash over territory (above).
            // Raiders on the march do not start fights on the way; hit, they still hit back. A band
            // on the move to a new home fights whatever it meets (P4-56, user decision 2026-09-22).
            // A region lord and its guards share a lair (P4-83): one pack, though of two kinds.
            // A horde on its march (P4-85) does not start fights either: it came to settle by the town, not to clear the way.
            if (other is Monster m) return m.Def != Def && !(Home != null && m.Home == Home)
                                           && (DefaultStateOverride == null || DefaultStateOverride == MigrateState && !InHorde || DefaultStateOverride == RoamState);
            return (other.Faction == Faction.Guild || other.ExposedToMonsters) && Def.aggressiveToHumans;
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
            // On a raid it finishes the raid first, then walks to the new home.
            if (IsAlive && Fsm.Current != EngageState && DefaultStateOverride == null) GoToReturn();
        }

        /// <summary>Its pack lost its nest with nowhere to go (P4-58): it roams with the pack from now on.</summary>
        internal void StartRoaming(RoamingPack pack)
        {
            Home = null;
            Roaming = pack;
            if (DefaultStateOverride != null && DefaultStateOverride != RoamState) return; // raiders finish their raid
            DefaultStateOverride = RoamState;
            if (IsAlive && Fsm.Current != EngageState) GoToDefaultState();
        }

        /// <summary>A living predator (a monster that preys on us) within sight, if any.</summary>
        public Monster FindPredator()
        {
            if (!Def.fleesFromPredators) return null;
            return UnitRegistry.FindNearest(Position, Stats.SightRadius, this,
                static (u, self) => u is Monster m && m.Def.IsPredatorOf(self.Def) && self.CanSee(m)) as Monster;
        }

        // ---------------- Pack tactics (skilled+, P4-28) ----------------

        static readonly System.Collections.Generic.List<Monster> s_encirclers = new();
        static readonly System.Collections.Generic.List<float> s_bearings = new();

        /// <summary>Another living member of the same pack: same species and nest (or both nest-less).</summary>
        public bool IsPackMate(Monster o) => o != null && o != this && o.IsAlive && o.Home == Home && o.Roaming == Roaming
                                             && (o.Def == Def || Home != null && (IsRegionLord || o.IsRegionLord)) // a lord and its guards (P4-83)
                                             && o.IsRare == IsRare; // a rare variant is nobody's pack mate (P4-85)

        /// <summary>Who calls the pack's target: the crown holder / pack leader, else the first member of the nest.</summary>
        Monster CallingLeader
        {
            get
            {
                var leader = PackLeader;
                var pack = PackMembers;
                if (leader != null || pack == null) return leader;
                foreach (var m in pack) if (m.IsAlive) return m;
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
                    else if (!busy && d < rallyDist && d <= MonsterRegroupState.MaxDistance) { rallyDist = d; rally = o; }
                }
                else if ((IsHostileTo(u) || u.IsHostileTo(this)) && (u.CurrentTarget == this || d <= 2.5f)) enemies++;
            }
            // Not needed (or nowhere to go): look again in a moment rather than every frame.
            NextRegroupTime = Time.time + 0.5f;
            if (!PackTactics.ShouldRegroup(HpRatio, enemies, allies)) return false;
            if (rally == null)
            {
                // No mate in reach: the nest, if it is near enough to fall back to (P4-81).
                if (Home == null) return false;
                float home = Vector2.Distance(Position, HomePosition);
                if (home <= near || home > MonsterRegroupState.MaxDistance) return false;
            }
            NextRegroupTime = Time.time + 20f;
            RegroupState.RallyMate = rally;
            RegroupState.Point = rally != null ? rally.Position : HomePosition;
            RegroupState.Enemy = Engage.Target ?? LastAttacker;
            Fsm.ChangeState(RegroupState);
            return true;
        }

        void Update()
        {
            // Paused: nothing acts (P4-80, see Adventurer.Update).
            if (Time.deltaTime <= 0f) return;
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
                using (s_senseMarker.Auto())
                {
                    LookAtGates();
                    Sense();
                }
            }
            Fsm.Tick(Time.deltaTime);
        }

        static readonly Unity.Profiling.ProfilerMarker s_senseMarker = new("Monster.Sense");

        // ---------------- What it knows of the town gates (user report 2026-09-22) ----------------

        readonly System.Collections.Generic.HashSet<Vector2Int> _ownKnownOpenGates = new();
        /// <summary>Broken gates this monster's pack has seen (its own memory when it has no nest).</summary>
        System.Collections.Generic.HashSet<Vector2Int> KnownOpenGates => Home != null ? Home.KnownOpenGates : _ownKnownOpenGates;

        /// <summary>
        /// Raiders came straight for a breach nobody had seen: paths used the real state of every
        /// gate. A broken gate the pack has not seen is taken to be shut; they bash the gate in front
        /// of them instead, and only head for a breach once one of them has seen it.
        /// </summary>
        bool BelievedShut(int x, int y)
        {
            if (!Services.TryGet<WorldService>(out var world) || !world.Grid.IsGate(x, y)) return false;
            var cell = new Vector2Int(x, y);
            if (world.Grid.IsClosedGate(cell)) return false; // really shut: the grid says so already
            var known = KnownOpenGates;
            foreach (var g in world.Map.Gates)
                if ((g - cell).sqrMagnitude <= 2 && known.Contains(g)) return false;
            return true;
        }

        void LookAtGates()
        {
            if (!Services.TryGet<Town.GateService>(out var gates)) return;
            float r = EffectiveSightRadius;
            foreach (var g in gates.Gates)
            {
                if ((g.Center - Position).sqrMagnitude > r * r || !CanSee(g.Center)) continue;
                if (g.IsBroken) KnownOpenGates.Add(g.CenterCell);
                else KnownOpenGates.Remove(g.CenterCell);
            }
        }

        void Sense()
        {
            if (Fsm.Current == FleeState || Fsm.Current == LureState || Fsm.Current == RegroupState) return;
            // Raiders march together; they do not scatter from predators in their own wave.
            var predator = DefaultStateOverride == null || DefaultStateOverride == RoamState ? FindPredator() : null;
            if (predator != null && !WasRecentlyAttackedBy(predator, 2f))
            {
                FleeState.From = predator;
                Fsm.ChangeState(FleeState);
                return;
            }
            if (Fsm.Current == EngageState)
            {
                _lastFightTime = Time.time;
                _pickupTimer = 0f; // look for the spoils as soon as the fight is over
                return;
            }
            // Breaking a gate is already its answer to "the one I want is behind the wall": seeing
            // them through the gateway must not pull it off the gate (2026-09-23). It used to swap
            // to a chase it could not finish, be blocked by the shut gate, go back to the gate, and
            // swap again — the gate sometimes never gave. Whoever hits it on its own side still
            // comes first (see MonsterGateBreakState).
            if (Fsm.Current == GateBreakState) return;
            // Walking home after a fight: the spoils on the way are still worth a detour (P4-57).
            if (Fsm.Current == ReturnState)
            {
                TryStartPickup(goingHome: true);
                return;
            }
            var target = FindTarget();
            if (target != null)
            {
                Engage.SetTarget(target);
                Fsm.ChangeState(EngageState);
                return;
            }
            if (Fsm.Current == WanderState || Fsm.Current == DefaultStateOverride && LootsOnTheWay) TryStartPickup();
        }

        /// <summary>A migrating or roaming pack stops for loot; raiders on the march do not.</summary>
        bool LootsOnTheWay => DefaultStateOverride == MigrateState || DefaultStateOverride == RoamState;

        const float PickupCheckInterval = 1.5f;
        /// <summary>Pack mates leave a fresh drop to the leader for this long.</summary>
        const float LeaderFirstSeconds = 3f;
        /// <summary>After a fight the spoils within this range of the monster are fair game, wherever it is (P4-57).</summary>
        public const float PostFightPickupRange = 6f;
        /// <summary>How long after its last fight a monster still looks for spoils away from home.</summary>
        public const float PostFightPickupSeconds = 20f;
        float _pickupTimer;

        /// <summary>
        /// One item picked up, the rest of the spoils are looked for at once (2026-09-23). Waiting the
        /// usual interval let it walk out of the post-fight range first: it ate the slime jelly and left
        /// the gear lying two cells away.
        /// </summary>
        internal void LookForMoreSpoilsNow() => _pickupTimer = 0f;
        float _lastFightTime = float.NegativeInfinity;

        /// <summary>
        /// Walk to a visible item worth taking (P4-21): near home while idling (wander radius + 3, so
        /// monsters do not roam off after loot), or — for a while after a fight (P4-57, user report:
        /// "monsters leave the loot of their fights") — near where it stands. Raiders never stop for loot.
        ///
        /// <paramref name="goingHome"/> drops the home range: on the walk back, anything in sight that
        /// it wants is on its way by definition. Without that a monster walked past the spoils of the
        /// fight it had just won — they lay more than a wander radius from its den, and the after-fight
        /// window had closed on the way — which is what left gear on the ground for 30-odd game hours
        /// with a willing taker looking right at it (measured 2026-09-23).
        /// </summary>
        void TryStartPickup(bool goingHome = false)
        {
            if (Def.pickupMode == MonsterPickupMode.None) return;
            if (DefaultStateOverride != null && !LootsOnTheWay) return;
            _pickupTimer -= SenseInterval;
            if (_pickupTimer > 0f) return;
            _pickupTimer = PickupCheckInterval;
            if (!Services.TryGet<Items.GroundItemService>(out var ground) || ground.Items.Count == 0) return;
            var item = FindSpoils(ground, goingHome);
            if (item == null || !item.Reserve(this)) return;
            PickupState.Begin(item);
            Fsm.ChangeState(PickupState);
        }

        /// <summary>
        /// Split from <see cref="TryStartPickup"/> so that its lambda's captures are allocated only
        /// when there is something to look for (P5-2: captured parameters are allocated on entry,
        /// before the early returns, which was garbage on every sense of every monster).
        /// </summary>
        Items.GroundItem FindSpoils(Items.GroundItemService ground, bool goingHome)
        {
            var home = HomePosition;
            float range = Def.wanderRadius + 3f;
            var here = Position;
            bool afterFight = Time.time - _lastFightTime <= PostFightPickupSeconds;
            var leader = PackLeader;
            return ground.FindNearest(Position, Stats.SightRadius, g =>
            {
                if (!g.IsFreeFor(this) || !WantsItem(g.Item) || !CanSee(g.Position)) return false;
                // The spoils of its own fight are its to take; the leader's first pick is for drops at home.
                if (afterFight && (g.Position - here).sqrMagnitude <= PostFightPickupRange * PostFightPickupRange) return true;
                if (goingHome) return true; // on the way back, whatever it can see and wants is on its way
                return (g.Position - home).sqrMagnitude <= range * range
                       && (leader == null || leader == this || g.Age >= LeaderFirstSeconds || !leader.WantsItem(g.Item));
            });
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
            // A lord's table is rolled once: its trophy is certain, and one of it is the point (P4-83).
            if (killer != null) MonsterEquipment.BuildDrops(Def, Equipment.Worn, GameRandom.Shared, s_loot, IsBoss && !IsRegionLord ? MonsterBoss.LootRolls : 1);
            else MonsterEquipment.BuildDrops(null, Equipment.Worn, GameRandom.Shared, s_loot);
            // A rare variant's spoils are certain (P4-85); one that slips away leaves nothing.
            if (killer != null && IsRare) RareMonster.AddSpoils(Level, s_loot);
            if (s_loot.Count > 0 && Services.TryGet<Items.GroundItemService>(out var ground)) ground.Drop(Position, s_loot);
            Equipment.Clear();
            if (killer is ILootReceiver receiver) receiver.ReceiveKillRewards(Def, ExpWorth, s_noLoot);
            else if (killer is Monster rival) rival.GainKillExp(ExpWorth);
            Home?.OnMemberDied(this, killer);
            Roaming?.OnMemberDied(this);
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

        /// <summary>
        /// Takes the item: wears it, or eats it. Gear with a slot goes on (the crown on any picker,
        /// the rest on the humanoids); the leavings of a fight are eaten by everyone, which teaches a
        /// step of smarts (<see cref="TierExp"/>) and, for a slime, still feeds it (P4-21).
        /// </summary>
        public void UseItem(ItemDefinition item)
        {
            if (item == null) return;
            bool wearable = item.equipSlot != EquipSlot.None
                            && (item.IsUnique || Def.pickupMode == MonsterPickupMode.Equip);
            bool absorbed = false;
            if (wearable)
            {
                var old = Equipment.Equip(item);
                if (old != null && Services.TryGet<Items.GroundItemService>(out var ground))
                    ground.Drop(Position, new[] { new ItemStack(old.id, 1) });
                RefreshStats();
            }
            else
            {
                if (Def.pickupMode == MonsterPickupMode.Absorb)
                {
                    float gain = Equipment.Absorb(item, AbsorbCap);
                    RefreshStats();
                    Heal(gain);
                    absorbed = true;
                }
                GainTierExp(MonsterEquipment.TierExpFor(item));
            }
            EventBus.Publish(new MonsterItemUsedEvent { Monster = this, Item = item, Absorbed = absorbed });
        }

        /// <summary>Holders of the crown lead the pack; pack mates near it hit 10% harder.</summary>
        void TickCrownAura(float dt)
        {
            _auraTimer -= dt;
            if (_auraTimer > 0f) return;
            _auraTimer = 1f;
            bool aura = false;
            if (PackMembers != null && !Equipment.Has(MonsterEquipment.PackLeaderEffect))
                foreach (var m in PackMembers)
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
            _gearIcon.transform.localPosition = shown.IsUnique ? new Vector3(0f, 0.95f * Scale, 0f) : new Vector3(0.28f * Scale, 0.55f * Scale, 0f);
            _gearIcon.sortingOrder = CharacterVisual.BaseSortingOrder + 900;
        }

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        public class MonsterSave
        {
            public string id;
            public string species;
            public bool boss;
            public bool ranged;
            /// <summary>Rare variant (P4-85), -1 for none.</summary>
            public int rare = -1;
            public int level;
            public int exp;
            public int tier_exp;
            public System.Collections.Generic.List<string> worn = new();
            public float absorbed;
            public Vector2 spawn;
            public string default_state;
            public System.Collections.Generic.List<Vector2Int> known_gates = new();
            public float next_lure;
            public float next_regroup;
            public float last_fight;
            /// <summary>Seconds (scaled) until the next look round and the next look for spoils.</summary>
            public float sense_timer;
            public float pickup_timer;
            public UnitSave unit;
            public Engagement.EngageSave engage;
            public string state;
            public float state_time;
            public string state_data;
        }

        /// <summary>The next id handed out (saved, so a loaded world never reuses one).</summary>
        public static int IdCounter { get => s_counter; set => s_counter = value; }

        State<Monster>[] _allStates;

        State<Monster>[] AllStates => _allStates ??= new State<Monster>[]
        {
            WanderState, EngageState, FleeState, LureState, ReturnState, RaidState, MigrateState, RoamState,
            PickupState, RegroupState, GateBreakState, VillageRaidState,
        };

        internal State<Monster> StateNamed(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var s in AllStates) if (s.GetType().Name == name) return s;
            return null;
        }

        public MonsterSave CaptureSave()
        {
            var s = new MonsterSave
            {
                id = UnitId, species = Def.id, boss = IsBoss, ranged = IsRangedVariant, rare = RareVariant, level = Level, exp = Exp, tier_exp = TierExp,
                absorbed = Equipment.AbsorbedHp, spawn = _spawnPosition, default_state = DefaultStateOverride?.GetType().Name,
                next_lure = SaveTime.Rel(NextLureTime), next_regroup = SaveTime.Rel(NextRegroupTime),
                last_fight = SaveTime.Rel(_lastFightTime), sense_timer = Round(_senseTimer), pickup_timer = Round(_pickupTimer),
                unit = CaptureUnit(), engage = Engage.Capture(),
            };
            var worn = new System.Collections.Generic.List<ItemDefinition>(Equipment.Worn);
            worn.Sort((a, b) => a.equipSlot.CompareTo(b.equipSlot)); // the same order every time
            foreach (var item in worn) s.worn.Add(item.id);
            var gates = new System.Collections.Generic.List<Vector2Int>(_ownKnownOpenGates);
            gates.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            s.known_gates = gates;
            var current = Fsm.Current;
            s.state = current?.GetType().Name;
            s.state_time = Mathf.Round((current?.Elapsed ?? 0f) * 100f) / 100f;
            s.state_data = current?.SaveState();
            return s;
        }

        /// <summary>
        /// First pass: the monster comes back where it stood, with its level, gear and wounds
        /// (no random draws, no events). Its nest or pack takes it in; what it was doing comes in <see cref="ResumeSave"/>.
        /// </summary>
        public static Monster RestoreSave(MonsterSave s, MonsterDefinition def, Nest home, SaveContext ctx)
        {
            var pool = Services.Get<PoolService>();
            var m = pool.Spawn<Monster>(PoolKey, new Vector2(s.unit.x, s.unit.y));
            m.Prepare(def, home, s.boss, s.ranged, s.id, s.rare);
            m.Level = Mathf.Clamp(s.level, 1, def.isRegionLord ? RegionLords.Level : MonsterLevel.MaxLevel);
            m.Exp = s.exp;
            m.TierExp = s.tier_exp;
            m.ApplyLevel();
            var db = Services.Get<GameDatabase>();
            foreach (var id in s.worn) if (db.Item(id) is { } item) m.Equipment.Equip(item);
            m.Equipment.RestoreAbsorbed(s.absorbed);
            m.Finish(home);
            m._spawnPosition = s.spawn;
            foreach (var g in s.known_gates) m._ownKnownOpenGates.Add(g);
            m.NextLureTime = SaveTime.Abs(s.next_lure);
            m.NextRegroupTime = SaveTime.Abs(s.next_regroup);
            m._lastFightTime = SaveTime.Abs(s.last_fight);
            m._senseTimer = s.sense_timer;
            m._pickupTimer = s.pickup_timer;
            m.Fsm.ChangeState(m.WanderState);
            m.RestoreUnit(s.unit);
            ctx.Register(m.UnitId, m);
            return m;
        }

        public void LinkSave(MonsterSave s, SaveContext ctx) => LinkUnit(s.unit, ctx);

        /// <summary>Third pass: its standing orders (a raid, a march, the road), then what it was doing and to whom.</summary>
        public void ResumeSave(MonsterSave s, SaveContext ctx)
        {
            DefaultStateOverride = StateNamed(s.default_state);
            var state = StateNamed(s.state);
            if (state == null)
            {
                if (!string.IsNullOrEmpty(s.state)) ctx.Warnings.Add($"{UnitId}: unknown state {s.state}");
                state = DefaultStateOverride ?? WanderState;
            }
            Fsm.Resume(state, s.state_time, s.state_data, ctx);
            Engage.Restore(s.engage, ctx);
        }

        /// <summary>Joins a roaming pack while a save is put back (P5-4): no change of orders, no events.</summary>
        internal void RestoreRoaming(RoamingPack pack)
        {
            Home = null;
            Roaming = pack;
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
