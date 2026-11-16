using System.Collections.Generic;
using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using GuildMaster.Traits;
using GuildMaster.Units;
using GuildMaster.Visuals;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    public struct AdventurerLevelUpEvent { public Adventurer Adventurer; public int Level; }
    /// <summary>
    /// Retreat because of low HP (not fatigue). Threat = last unit that hit us, if any. WithParty: the
    /// whole party pulled back with it (P4-71), so it did not run off on its own.
    /// </summary>
    public struct AdventurerRetreatedEvent { public Adventurer Adventurer; public Unit Threat; public bool WithParty; }
    /// <summary>NoFeed: the line already appears in a dialogue window, so the bottom-left feed skips it (the speech bubble still shows).</summary>
    public struct AdventurerSpeechEvent { public Adventurer Adventurer; public string Text; public bool NoFeed; }
    public struct AdventurerStateChangedEvent { public Adventurer Adventurer; public string Label; }

    /// <summary>
    /// Guild adventurer. Low-level behaviour (movement, combat, survival) is an FSM;
    /// high-level choices (which quest, rest, shop) come from an IAdventurerDecisionMaker.
    /// </summary>
    public class Adventurer : Unit, IQuestTaker, ITrader, ILootReceiver, IProjectileStyle
    {
        const float SenseInterval = 0.35f;

        public AdventurerData Data { get; private set; }
        public TraitSet Traits { get; private set; }
        public StateMachine<Adventurer> Fsm { get; private set; }
        public Engagement Engage { get; private set; }
        public Quest CurrentQuest { get; set; }
        /// <summary>Reason of the latest high-level decision (shown in the inspector).</summary>
        public string LastThought { get; set; }
        /// <summary>What the latest decision weighed (P4-104a, shown in the inspector; not saved).</summary>
        public ThinkRecord LastThinking { get; set; }
        public bool IsGuildMember { get; set; } = true;

        // ---- Visitor (recruitment candidate) ----
        public bool IsVisitor => Faction == Faction.Visitor;
        /// <summary>Game hour (TotalHours) at which a visitor heads for the gate.</summary>
        public float VisitorLeaveAtHour { get; set; }
        /// <summary>True while the recruitment dialogue with the guild master is open.</summary>
        public bool InConversation { get; set; }
        public Transform ConversationPartner { get; set; }

        /// <summary>Hp ratio below which the adventurer retreats (traits can modify).</summary>
        public float RetreatThreshold
        {
            get
            {
                float t = (Traits?.RetreatThreshold ?? 0.35f)
                          // Fighting a kind it fears (a quirk, P4-104e): it pulls out sooner.
                          + (Data != null && Data.quirks.Count > 0 && Engage?.Target is Monster m && m.Def != null && QuirkRules.Fears(Data.quirks, m.Def.id)
                              ? QuirkRules.FearRetreatShift : 0f);
                // Behind its rival in a race for kills (P4-131): it holds on longer.
                return Services.TryGet<BondService>(out var bonds) && bonds.IsBehind(this) ? BondRules.RetreatWhenBehind(t) : t;
            }
        }

        internal TownIdleState IdleState;
        internal DecidingState DecidingState;
        internal TravelToQuestState TravelState;
        internal HuntState HuntState;
        /// <summary>Hunting without a quest (P4-37).</summary>
        internal FreeHuntState FreeHuntState;
        internal EscortState EscortState;
        internal PartyGatherState GatherState;
        internal DefendState DefendState;
        internal ReturnToTownState ReturnState;
        internal GuildVisitState VisitState;
        internal RestState RestState;
        /// <summary>A newcomer at the training ground (P4-99).</summary>
        internal TrainState TrainState;
        /// <summary>At a campfire out in the wild (P4-66).</summary>
        internal CampState CampState;
        internal DeadState DeadState;
        internal VisitorWanderState VisitorWanderState;
        internal VisitorLeaveState VisitorLeaveState;
        /// <summary>Out in front of another village as one of its candidates (P4-84).</summary>
        internal VillageCandidateState VillageCandidateState;
        /// <summary>Fleeing a falling village to our town (P4-84).</summary>
        internal RefugeeState RefugeeState;
        internal LureState LureState;
        internal TownGuardState TownGuardState;
        /// <summary>Hit in town too hurt to fight: running for cover (P4-100).</summary>
        internal TakeCoverState TakeCoverState;
        /// <summary>Sent out to bring a fallen comrade's relics home (F-6).</summary>
        internal Legacy.RelicRetrievalState RetrievalState;
        /// <summary>Acting out a mental break (P4-104d): wandering, a spree, a fistfight, berserk.</summary>
        internal BreakState BreakState;
        /// <summary>Walked off its work but trailing its party home (P4-104d, user decision 2026-10-21).</summary>
        internal TagAlongState TagAlongState;
        /// <summary>Gone to a place worth going to (P4-105b): a hot spring, a shrine, an ore vein or a herb patch.</summary>
        internal PlaceVisitState PlaceVisitState;
        /// <summary>Berserk (P4-104d): it goes for anyone near, comrades too, and everyone turns on it.</summary>
        public bool Berserk { get; internal set; }
        /// <summary>The one it is in a fistfight with (P4-104d): hostile to each other only, and no blow wounds.</summary>
        public Adventurer BrawlFoe { get; internal set; }
        /// <summary>Picking loot off the ground (P4-21).</summary>
        internal AdventurerLooting Looting;
        /// <summary>Quest for which the heroic drive hunt was already tried (once per quest).</summary>
        internal Quests.Quest DroveForQuest;
        /// <summary>What this member dropped on the ground when they died (F-6: tracked as relics by GuildLegacy).</summary>
        public IReadOnlyList<Items.GroundItem> DeathDrops { get; private set; }

        float _senseTimer;
        bool _dodging;
        GameClock _clock;

        // ---- Interfaces ----
        public string TakerId => Data.id;
        public string TakerName => Data.name;
        public bool IsAvailableForQuest => IsAlive && IsGuildMember && CurrentQuest == null;
        public string TraderName => Data.name;
        public int Gold { get => Data.gold; set => Data.gold = value; }
        public List<ItemStack> CarriedLoot => Data.loot;
        public Color ProjectileColor => Data.cls switch
        {
            AdventurerClass.Mage => new Color(0.5f, 0.7f, 1f),
            AdventurerClass.Priest => new Color(1f, 0.97f, 0.7f),   // holy light
            _ => new Color(0.95f, 0.85f, 0.55f),
        };

        /// <summary>Idling on the plaza or making up its mind: free to be asked along on something.</summary>
        public bool IsIdleInTown => Fsm != null && (Fsm.Current == IdleState || Fsm.Current == DecidingState);

        /// <summary>In town with nothing on: idling, resting, training (P4-99), or at the guild counter.</summary>
        public bool IsAtLeisureInTown => IsIdleInTown || Fsm != null && (Fsm.Current == RestState || Fsm.Current == VisitState || Fsm.Current == TrainState);

        /// <summary>
        /// Weaker than the monsters round the town (P4-99, <see cref="NewcomerRules"/>): it trains while the training ground
        /// teaches, else hunts alone close in, and does not go out with a stronger band.
        /// </summary>
        public bool IsNewcomer => Data != null && IsGuildMember && !IsVisitor && Services.TryGet<Ecosystem.EcosystemManager>(out var eco)
                                  && NewcomerRules.IsNewcomer(AdventurerStats.Power(Data), eco.BirthLevelAtDistance(NewcomerRules.NearRing));

        /// <summary>A newcomer's answers to quests beyond its ground, by quest and risk to it (P4-100; not saved — asked afresh after a load).</summary>
        readonly Dictionary<(Quests.Quest, string), bool> _dares = new();

        /// <summary>
        /// Whether this newcomer dares the quest at this risk to it (<see cref="QuestRisk.NewcomerHoldsBack"/>): rolled once with
        /// <paramref name="chance"/>, then the same answer — a lower risk (company came) is a new question.
        /// </summary>
        internal bool Dares(Quests.Quest q, string risk, float chance)
        {
            if (_dares.TryGetValue((q, risk), out var dare)) return dare;
            if (_dares.Count >= 16)
                foreach (var key in new List<(Quests.Quest, string)>(_dares.Keys))
                    if (!key.Item1.IsActive) _dares.Remove(key);
            dare = GameRandom.Shared.Chance(chance);
            _dares[(q, risk)] = dare;
            return dare;
        }

        /// <summary>How far from the town this newcomer hunts on its own (P4-99).</summary>
        internal float NewcomerReach
            => Services.TryGet<Ecosystem.EcosystemManager>(out var eco)
                ? NewcomerRules.Reach(AdventurerStats.Power(Data), eco.InnermostDistance, eco.BirthLevelAtDistance)
                : NewcomerRules.NearRing;

        public string StateLabel => Fsm?.Current?.Label ?? "-";
        public override string DebugState => StateLabel;
        public override Engagement CombatEngagement => Engage;

        public static Adventurer Create(AdventurerData data, Vector2 position, Transform parent)
        {
            var go = new GameObject($"Adventurer {data.name}");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.AddComponent<World.PathFollower>();
            go.AddComponent<CharacterVisual>();
            var a = go.AddComponent<Adventurer>();
            go.AddComponent<HealthBar>();
            go.AddComponent<VisionSource>();
            a.Setup(data);
            return a;
        }

        protected override void Awake()
        {
            base.Awake();
            Faction = Faction.Guild;
            Fsm = new StateMachine<Adventurer>(this);
            Engage = new Engagement(this) { GiveUpDistance = 14f };
            IdleState = new TownIdleState();
            DecidingState = new DecidingState();
            TravelState = new TravelToQuestState();
            HuntState = new HuntState();
            FreeHuntState = new FreeHuntState();
            EscortState = new EscortState();
            GatherState = new PartyGatherState();
            DefendState = new DefendState();
            ReturnState = new ReturnToTownState();
            VisitState = new GuildVisitState();
            RestState = new RestState();
            TrainState = new TrainState();
            CampState = new CampState();
            DeadState = new DeadState();
            VisitorWanderState = new VisitorWanderState();
            VisitorLeaveState = new VisitorLeaveState();
            VillageCandidateState = new VillageCandidateState();
            RefugeeState = new RefugeeState();
            LureState = new LureState();
            TownGuardState = new TownGuardState();
            TakeCoverState = new TakeCoverState();
            RetrievalState = new Legacy.RelicRetrievalState();
            BreakState = new BreakState();
            TagAlongState = new TagAlongState();
            PlaceVisitState = new PlaceVisitState();
            Looting = new AdventurerLooting(this);
            // Party back line (P4-27): ranged members fall back behind a front-liner while reloading.
            Engage.HoldSpot = target => Services.TryGet<PartyTactics>(out var party) ? party.BackLineSpot(this, target) : null;
            Engage.LostSight = ShootFlare;
        }

        // ---------------- Party roles (P4-27) ----------------

        public PartyRole Role => Data != null ? PartyTactics.RoleOf(Data.cls) : PartyRole.Back;
        /// <summary>Its party's back line: a ranged role, or one fallen back hurt (P4-96 — the front-liners guard it).</summary>
        public override bool IsBackLine => Role == PartyRole.Back || FallingBack;
        /// <summary>Skilled+ warriors protect the back line.</summary>
        public override bool GuardsBackLine => Role == PartyRole.Front && Tier >= CombatTier.Skilled;
        /// <summary>The guild master's focus order first (P4-132), then its party leader's call (P4-27).</summary>
        public override Unit CalledTarget => OrderedTarget ?? (Services.TryGet<PartyTactics>(out var party) ? party.CalledTargetFor(this) : null);

        // ---------------- The guild master's orders (P4-132) ----------------

        Unit _ordered;
        float _orderedUntil;

        /// <summary>The monster the guild master told it to bring down (a focus order), while the order stands; null otherwise.</summary>
        public Unit OrderedTarget => _ordered != null && _ordered.IsAlive && Time.time < _orderedUntil ? _ordered : null;

        /// <summary>
        /// A focus order (P4-132): on <paramref name="target"/> for <paramref name="seconds"/> or until it falls — past its party
        /// leash and the calling state's zone (<see cref="HandleCombat"/>). A change of state or falling back calls it off.
        /// </summary>
        internal void OrderTarget(Unit target, float seconds)
        {
            _ordered = target;
            _orderedUntil = Time.time + seconds;
            if (target != null && target.IsAlive) Engage.Force(target, OrderLockSeconds);
        }

        internal void CallOffOrder() => _ordered = null;

        /// <summary>The engagement is held on an ordered target this long at a time (kept up while the order stands).</summary>
        const float OrderLockSeconds = 0.5f;
        /// <summary>An ordered target further than this is let go (cells): it ran, or the member was carried off.</summary>
        const float OrderReach = 18f;

        /// <summary>Called home by the guild master's horn (P4-132): not out to the town's defence before this game hour.</summary>
        public float HeldHomeUntilHour { get; private set; } = float.NegativeInfinity;

        /// <summary>
        /// The horn (P4-132): it stops fighting and runs home now — its quest kept, its party's own call not waited for (an order
        /// is an order) — and stays out of the town's defence until <paramref name="untilHour"/>.
        /// </summary>
        internal void RetreatOnOrder(float untilHour)
        {
            HeldHomeUntilHour = untilHour;
            CallOffOrder();
            Engage.Clear();
            Looting.Cancel();
            ReturnState.RequestRetreat();
            ChangeState(ReturnState);
        }

        /// <summary>Turns a freshly created adventurer into a town visitor (not attacked, no quests).</summary>
        public void BecomeVisitor(float leaveAtHour)
        {
            Faction = Faction.Visitor;
            IsGuildMember = false;
            VisitorLeaveAtHour = leaveAtHour;
            RecalculateStats(); // not the guild's yet: no facility helps it (P4-87; health stays full, no dice drawn)
            ChangeState(VisitorWanderState);
        }

        /// <summary>A refugee on its way to our town (P4-84): monsters that go for people fall on it.</summary>
        public override bool ExposedToMonsters => IsVisitor && Fsm != null && Fsm.Current == RefugeeState;

        /// <summary>Visitor accepted the guild's offer. Call through <see cref="GuildRoster.AddExisting"/>.</summary>
        internal void JoinGuild()
        {
            Faction = Faction.Guild;
            IsGuildMember = true;
            InConversation = false;
            ConversationPartner = null;
            RecalculateStats(); // the guild's facilities now count (P4-87)
            ChangeState(IdleState);
        }

        void Setup(AdventurerData data)
        {
            Data = data;
            UnitId = data.id;
            DisplayName = data.name;
            Radius = 0.3f;
            if (data.skillIds == null || data.skillIds.Count == 0)
            {
                data.skillIds = new System.Collections.Generic.List<string>(ClassSkills(data.cls));
            }
            SkillFactory.Assign(this, data.skillIds);
            Traits = TraitSet.FromIds(data.traitIds, Services.TryGet<GameDatabase>(out var db) ? db : null);
            RecalculateStats(fullHeal: true);
            // People are drawn with the CharacterMaker rig when that pack is imported (2026-10-30); otherwise the Cute
            // Adventurers skin, and generated pixel art without any pack.
            if (!Visual.InitHuman(HumanLook.Adventurer(data.id, data.cls, data.skinColor, data.hairColor)))
            {
                var anim = ArtLibrary.Adventurer(SkinFor(data.cls, data.id));
                if (anim != null) Visual.InitAnimated(anim);
                else Visual.Init(ClassSprite(data));
            }
            GetComponent<HealthBar>().Init(this, 0.75f, new Color(0.3f, 0.85f, 0.35f));
            GetComponent<HealthBar>().AlwaysVisible = false;
            GetComponent<VisionSource>().SightRadius = Stats.SightRadius;
            _clock = Services.Get<GameClock>();
            Fsm.ChangeState(IdleState);
        }

        public static string[] ClassSkills(AdventurerClass cls) => cls switch
        {
            AdventurerClass.Warrior => new[] { "taunt", "whirlwind" },
            AdventurerClass.Ranger => new[] { "pierce" },
            // Mass heal first: when several are hurt it beats a single heal (P4-29).
            AdventurerClass.Priest => new[] { "massheal", "heal" },
            _ => new[] { "blink", "fireball" },
        };

        /// <summary>
        /// Learns a skill (P4-34), giving up <paramref name="forgetId"/> when the cap is reached.
        /// Returns false when there is nothing to learn or no room and nothing named to drop.
        /// </summary>
        public bool LearnSkill(string learnId, string forgetId)
        {
            if (string.IsNullOrEmpty(learnId) || SkillFactory.Create(learnId) == null) return false;
            if (Data.skillIds.Contains(learnId)) return false;
            if (Data.skillIds.Count >= SkillCatalog.MaxSkills)
            {
                if (string.IsNullOrEmpty(forgetId) || !Data.skillIds.Remove(forgetId)) return false;
            }
            Data.skillIds.Add(learnId);
            // The skills are dealt anew: the ones kept stay on their cooldown (P4-76: a level-up in a
            // fight made every skill ready again — a whirlwind just used could be spun at once).
            var left = new Dictionary<System.Type, float>();
            foreach (var s in Skills) left[s.GetType()] = s.CooldownLeft;
            SkillFactory.Assign(this, Data.skillIds);
            foreach (var s in Skills) if (left.TryGetValue(s.GetType(), out var t)) s.ResetCooldown(t);
            return true;
        }

        public override void ForceTarget(Unit target)
        {
            if (!IsAlive || target == null || !target.IsAlive) return;
            Engage.Force(target, 3f);
        }

        /// <summary>Level band plus birth grade (see CombatTiers.FromLevel).</summary>
        public override CombatTier Tier => Data == null ? CombatTier.Novice : CombatTiers.FromLevel(Data.level, (int)Data.grade);

        /// <summary>
        /// Cute Adventurers skins per class; the penguin king is reserved for the guild master and the
        /// robed frog for priests (user decision, P4-29).
        /// </summary>
        public static string[] SkinsFor(AdventurerClass cls) => cls switch
        {
            AdventurerClass.Warrior => new[] { "knight", "raccoon" },
            AdventurerClass.Ranger => new[] { "fox" },
            AdventurerClass.Priest => new[] { "frog" },
            _ => new[] { "robot", "skeleton" },
        };

        /// <summary>Stable skin choice for an adventurer (same id → same look).</summary>
        public static string SkinFor(AdventurerClass cls, string id)
        {
            var skins = SkinsFor(cls);
            int h = 0;
            foreach (char c in id ?? "") h = h * 31 + c;
            return skins[Mathf.Abs(h % skins.Length)];
        }

        static Sprite ClassSprite(AdventurerData d)
        {
            var (body, accent, legs) = d.cls switch
            {
                AdventurerClass.Warrior => (new Color(0.62f, 0.64f, 0.70f), new Color(0.75f, 0.2f, 0.2f), new Color(0.35f, 0.3f, 0.3f)),
                AdventurerClass.Ranger => (new Color(0.30f, 0.52f, 0.28f), new Color(0.55f, 0.38f, 0.2f), new Color(0.3f, 0.25f, 0.18f)),
                AdventurerClass.Priest => (new Color(0.92f, 0.9f, 0.84f), new Color(0.95f, 0.8f, 0.35f), new Color(0.55f, 0.5f, 0.45f)),
                _ => (new Color(0.25f, 0.35f, 0.75f), new Color(0.9f, 0.85f, 0.4f), new Color(0.2f, 0.22f, 0.45f)),
            };
            return PixelArt.Humanoid(d.id, d.hairColor, d.skinColor, body, accent, legs);
        }

        /// <summary>Under a shrine's blessing (P4-105b): more health until it ends.</summary>
        public bool IsBlessed => Data != null && Data.blessedUntil > 0f
                                 && Data.blessedUntil > (_clock != null ? _clock.TotalHours : Services.TryGet<GameClock>(out var c) ? c.TotalHours : 0f);

        public void RecalculateStats(bool fullHeal = false)
        {
            float ratio = Stats.MaxHp > 0 ? HpRatio : 1f;
            // P4-87: weakened, raised for members by the guild's stat buildings.
            Stats = AdventurerStats.Compute(Data, Traits?.Modifiers,
                Services.TryGet<Town.FacilityService>(out var facilities) ? facilities.ScaleFor(this) : null);
            // Relics of fallen comrades it carries (2026-11-15, the memorial wall).
            Stats = Legacy.MemorialRules.WithHeirlooms(Stats, Data);
            // A combat inspiration (P4-104d) while it lasts.
            if (Data.inspiration == InspirationKind.Combat)
            {
                var s = Stats;
                s.Attack *= BreakRules.CombatAttack;
                Stats = s;
            }
            // A shrine's blessing (P4-105b) while it lasts.
            if (IsBlessed)
            {
                var s = Stats;
                s.MaxHp = Mathf.Round(s.MaxHp * Places.PlaceRules.BlessingHp);
                Stats = s;
            }
            if (fullHeal) ResetVitals();
            else
            {
                // Never revives: a projectile still in flight can land the killing blow (and a
                // level-up) after its shooter died, and ResetVitals would bring them back.
                Mover.Speed = Stats.MoveSpeed;
                SetHp(Mathf.Max(1f, Stats.MaxHp * ratio));
            }
        }

        // ---------------- Per-frame ----------------

        void Update()
        {
            if (!IsAlive && Fsm.Current != DeadState) return;
            float dt = Time.deltaTime;
            // Paused: nothing acts. A cooldown run out still struck once at dt 0, and a save taken in the
            // pause menu did not match the game it loaded into (P4-80).
            if (dt <= 0f) return;
            float hours = dt / (_clock != null ? _clock.RealSecondsPerGameHour : 10f);

            bool inTown = Services.TryGet<Town.TownService>(out var town) && town.IsInTown(Position);
            if (IsAlive)
            {
                float fatigueRate = inTown ? 0.5f : 3f * (Traits?.FatigueMultiplier ?? 1f);
                // Resting in town or by a campfire (P4-66) wears nobody out.
                if (Fsm.Current != RestState && Fsm.Current != CampState) Data.fatigue = Mathf.Min(100f, Data.fatigue + fatigueRate * hours);
                if (inTown && Fsm.Current != RestState) Heal(Stats.MaxHp * 0.04f * hours);
                // A dormitory makes the town more restful (P4-92).
                Data.stress = Mathf.Max(0f, Data.stress - (inTown ? 4f * Town.FacilityService.TownStressRelief : 0.5f) * hours);
                TickStatus(dt);
                if (IsStunned) return;
            }

            Fsm.Tick(dt);
        }

        /// <summary>
        /// A comrade fighting this close is heard, not seen (P4-54, user request: fights at forests):
        /// one ambushed inside a forest calls the rest in, though they cannot see in from outside.
        /// Same range as a monster pack's alarm (P4-52).
        /// </summary>
        public const float ComradeCryRadius = Monster.PackAlarmRadius;

        /// <summary>Debug A/B switch (P4-54 forest checks): off = only what is seen.</summary>
        public static bool HearComrades = true;

        internal bool HearsFightingAComrade(Monster m)
            => HearComrades && m.IsAlive && m.CombatEngagement?.Target is Adventurer { IsGuildMember: true } comrade && comrade != this
               // A monster whose fight is not ticking (homing, fleeing) may still hold a comrade who died and was destroyed.
               && comrade != null && comrade.IsAlive
               && (comrade.Position - Position).sqrMagnitude <= ComradeCryRadius * ComradeCryRadius;

        /// <summary>
        /// Shared combat logic for out-of-town states. Returns true while fighting
        /// (the calling state should skip its own movement that frame).
        /// </summary>
        internal bool HandleCombat(float dt, System.Func<Unit, bool> extraTargets = null, System.Func<Unit, bool> allowed = null)
        {
            // Highest priority: step out of enemy area-attack telegraphs.
            if (Services.TryGet<TelegraphService>(out var telegraphs) && telegraphs.TryGetEscape(this, out var safe))
            {
                if (!Mover.IsMoving || Vector2.Distance(Mover.Destination, safe) > 0.5f)
                {
                    Mover.SpeedMultiplier = 1.3f;
                    Mover.SetDestination(safe, 0.1f);
                    if (!_dodging) Say("피해!");
                }
                _dodging = true;
                return true;
            }
            if (_dodging)
            {
                _dodging = false;
                Mover.SpeedMultiplier = 1f;
            }
            // Badly hurt with the party fighting on (P4-96): behind them, out of the fight, until mended.
            if (TickFallingBack(dt)) return true;

            // What may be (re)targeted this frame: threats (aggressive monsters we see or that hit us)
            // and the caller's optional extra targets, limited to the optional zone.
            // Party members only take fights near their leader (P4-38); what hits them is fought anyway.
            var leash = PartyLeashCentre();
            // The filters read these (P5-2: they used to be fresh closures every frame).
            _combatLeash = leash;
            _combatAllowed = allowed;
            _combatExtraTargets = extraTargets;
            var threat = _threatFilter ??= IsThreatCandidate;
            var extra = extraTargets == null ? null : _extraFilter ??= IsExtraCandidate;
            Engage.CandidateFilter = extra == null ? threat : _anyFilter ??= IsAnyCandidate;
            Engage.SearchRadius = Mathf.Max(Stats.SightRadius, ComradeCryRadius);

            // A comrade fought only while it was berserk (or in the fistfight): once it comes to, it is a comrade again and the
            // fight is off — the engagement used to run on, and the one who had stopped a berserker shot it dead on the way out.
            if (Engage.Target != null && !IsHostileTo(Engage.Target)) Engage.Clear();
            // The guild master's focus order (P4-132): it goes past the zone and the party leash below.
            var ordered = OrderedTarget;
            // Optional zone (town defense): drop targets outside it instead of chasing them.
            if (Engage.Target != null && Engage.Target != ordered && allowed != null && !allowed(Engage.Target)) Engage.Clear();
            // A chase that pulls a member away from its party is called off (P4-38).
            if (Engage.Target != null && Engage.Target != ordered && !WasRecentlyAttackedBy(Engage.Target)
                && !PartyTactics.InLeash(leash, Engage.Target.Position))
                Engage.Clear();
            if (ordered != null) KeepToTheOrder(ordered);
            if (Engage.Target != null)
            {
                if (Engage.Tick(dt) != EngageStatus.Lost) return Fighting();
            }

            _senseTimer -= dt;
            if (_senseTimer > 0f) return false;
            _senseTimer = SenseInterval;
            // Between fights: patch up the party (priests, P4-29). In fights the engagement uses skills.
            TryUseSupportSkills();

            // The leader's call first (anywhere in sight, P4-27), then threats (aggressive monsters or
            // anything that hit us), then optional quest targets.
            var target = CalledCandidate(threat, extra)
                         ?? TargetSelector.Pick(this, Mathf.Max(Stats.SightRadius * 0.8f, ComradeCryRadius), threat, Engage)
                         ?? (extra != null ? TargetSelector.Pick(this, Stats.SightRadius, extra, Engage) : null);
            if (target == null) return false;
            Engage.SetTarget(target);
            return Engage.Tick(dt) != EngageStatus.Lost && Fighting();
        }

        // What HandleCombat's last call set for its target filters (party leash, zone, extra targets).
        Vector2? _combatLeash;
        System.Func<Unit, bool> _combatAllowed, _combatExtraTargets;
        System.Func<Unit, bool> _threatFilter, _extraFilter, _anyFilter;

        /// <summary>
        /// A threat: a monster (inside the zone, if any) that hit us, or that is hostile or fighting one
        /// of ours and in sight within the party leash. Also one heard fighting a comrade: a slime never
        /// starts a fight, but once it hits back at one of us the rest pitch in instead of looting
        /// around it (user report 2026-09-22).
        /// </summary>
        bool IsThreatCandidate(Unit u)
            => u is Monster m && (_combatAllowed == null || _combatAllowed(m))
               && (WasRecentlyAttackedBy(m)
                   || ((m.IsHostileTo(this) || m.CombatEngagement?.Target is Adventurer { IsGuildMember: true })
                       && CanSee(m) && PartyTactics.InLeash(_combatLeash, m.Position))
                   || HearsFightingAComrade(m) && PartyTactics.InLeash(_combatLeash, m.Position))
               || IsBerserkNear(u);

        /// <summary>A comrade gone berserk (P4-104d) that hit us, or that we see close by: it is stopped by force.</summary>
        internal bool IsBerserkNear(Unit u)
            => u is Adventurer { Berserk: true } b && b != this && b.IsAlive
               && (WasRecentlyAttackedBy(b) || CanSee(b) && (b.Position - Position).sqrMagnitude <= BreakRules.BerserkReach * BreakRules.BerserkReach);

        /// <summary>One of the calling state's extra targets (inside the zone, in sight, within the leash).</summary>
        bool IsExtraCandidate(Unit u)
            => _combatExtraTargets != null && _combatExtraTargets(u) && (_combatAllowed == null || _combatAllowed(u))
               && CanSee(u) && PartyTactics.InLeash(_combatLeash, u.Position);

        bool IsAnyCandidate(Unit u) => IsThreatCandidate(u) || IsExtraCandidate(u);

        /// <summary>Where this member's party leash is tied (its leader), or null when it has none.</summary>
        internal Vector2? PartyLeashCentre()
            => Services.TryGet<PartyTactics>(out var party) ? party.LeashCentre(this) : null;

        /// <summary>A point this member may walk to on its own business (a pickup, a target).</summary>
        internal bool WithinPartyLeash(Vector2 point) => PartyTactics.InLeash(PartyLeashCentre(), point);

        /// <summary>The party leader's called target when it is in sight and allowed here, or null.</summary>
        Unit CalledCandidate(System.Func<Unit, bool> threat, System.Func<Unit, bool> extra)
        {
            var called = CalledTarget;
            if (called == null || !called.IsAlive || Engage.IsIgnoring(called)) return null;
            if (Vector2.Distance(Position, called.Position) > Stats.SightRadius) return null;
            return threat(called) || (extra != null && extra(called)) ? called : null;
        }

        /// <summary>
        /// Holds the engagement on the guild master's ordered target (P4-132) while it can be got at — not one it gave up chasing
        /// (ignored a while), out of its reach (on the wing) or gone far off: then the order is off. A novice something else is
        /// hitting turns on that first, as novices do (<see cref="CombatTiers.RetaliatesOnly"/>), and back to the order once it
        /// lets up — the two would otherwise take the target from each other every frame.
        /// </summary>
        void KeepToTheOrder(Unit ordered)
        {
            if (Engage.IsIgnoring(ordered) || !Combat.CanReach(this, ordered)
                || (ordered.Position - Position).sqrMagnitude > OrderReach * OrderReach)
            {
                CallOffOrder();
                return;
            }
            var now = Engage.Target;
            if (now != null && now != ordered && CombatTiers.RetaliatesOnly(Tier) && WasRecentlyAttackedBy(now, 1.5f)) return;
            Engage.Force(ordered, OrderLockSeconds);
        }

        /// <summary>A fight breaks formation pacing (catch-up / waiting speeds): fight at normal speed.</summary>
        bool Fighting()
        {
            Mover.SpeedMultiplier = 1f;
            return true;
        }

        // ---------------- Flares (P4-94) ----------------

        float _flareReadyAt;

        /// <summary>
        /// A foe it was fighting slipped out of sight: shoots a flare over where it was last seen when it carries one, its
        /// last shot is long enough ago, that spot is not in a flare's light already, and the foe is a threat (hostile by
        /// nature, or hit it lately) — a slime that wandered off into the trees is not worth one.
        /// </summary>
        void ShootFlare(Unit foe, Vector2 lastSeen)
        {
            if (!IsGuildMember || IsVisitor || foe == null || !Services.TryGet<Flares.FlareService>(out var flares)) return;
            bool threat = foe.IsHostileTo(this) || WasRecentlyAttackedBy(foe, Flares.FlareRules.ThreatMemorySeconds);
            bool lit = Flares.FlareService.Covers(lastSeen, Flares.FlareRules.CoveredWithin);
            if (!Flares.FlareRules.WouldFire(Data.flares, Time.time, _flareReadyAt, lit, threat)) return;
            Data.flares--;
            _flareReadyAt = Time.time + Flares.FlareRules.CooldownSeconds;
            flares.Fire(this, Flares.FlareRules.AimPoint(Position, lastSeen));
            Say("조명탄!", toFeed: false);
        }

        /// <summary>Drinks a potion or signals retreat when badly hurt/exhausted. Returns true if retreating.</summary>
        /// <param name="wearinessCounts">
        /// False while defending the town: going home to rest makes no sense at home (user report
        /// 2026-09-22: exhausted defenders "retreated" the moment a raid came, one member fought alone).
        /// </param>
        internal bool CheckRetreat(bool wearinessCounts = true)
        {
            if (HpRatio < RetreatThreshold && Data.potions > 0 && Services.TryGet<GameDatabase>(out var db))
            {
                Data.potions--;
                Heal(Combat.ScaledHp(db.Item(DefaultContent.Potion)?.healAmount ?? 40f));
                Say("물약을 마셨다!");
            }
            bool hurt = HpRatio < RetreatThreshold;
            if (!hurt && !(wearinessCounts && Data.fatigue >= 95f)) return false;
            // Badly hurt with its party out (P4-96, user decision 2026-10-20): it falls back behind them to be healed while
            // they fight on (HandleCombat walks it there), or they all pull out with it when the fight is lost — never alone.
            if (hurt)
            {
                switch (PartyRecovery.WhenHurt(this))
                {
                    case PartyRecovery.HurtCall.FallBack:
                        return false;
                    case PartyRecovery.HurtCall.PartyPulledOut:
                        Data.stress = Mathf.Min(100f, Data.stress + 15f);
                        if (Fsm.Current == ReturnState && ReturnState.Retreating)
                            EventBus.Publish(new AdventurerRetreatedEvent { Adventurer = this, Threat = LastAttacker, WithParty = true });
                        return true;
                }
            }
            Data.stress = Mathf.Min(100f, Data.stress + 15f);
            // Worn out with a party: it stops to mend or pulls back with us (P4-71), nobody leaves alone.
            if (!hurt && PartyRecovery.Handle(this)) return true;
            if (hurt) EventBus.Publish(new AdventurerRetreatedEvent { Adventurer = this, Threat = LastAttacker });
            RetreatAlone();
            return true;
        }

        /// <summary>Home on its own, running (nobody of its party out with it to fall back behind).</summary>
        void RetreatAlone()
        {
            Engage.Clear();
            Looting.Cancel();
            ReturnState.RequestRetreat();
            ChangeState(ReturnState);
        }

        // ---------------- Falling back (P4-96) ----------------

        /// <summary>
        /// Badly hurt with its party fighting on (P4-96, user decision 2026-10-20: "파티 뒤로 빠져 치료"): it stands behind them,
        /// by the healer and away from the foe, out of the fight, until mended (<see cref="FallBackRules.RejoinAt"/>) or the
        /// fight is over. It keeps its state (its quest, its hunt, its camp), so it stays one of the party.
        /// </summary>
        public bool FallingBack { get; private set; }
        State<Adventurer> _fellBackIn;
        float _fallBackRepath, _fallBackStressAt = float.NegativeInfinity;
        static readonly List<Adventurer> s_fallBackParty = new();

        /// <summary>Falls back behind its party (the party's call, <see cref="PartyRecovery.WhenHurt"/>; again while it lasts is nothing).</summary>
        internal void FallBack()
        {
            if (FallingBack) return;
            FallingBack = true;
            CallOffOrder(); // hurt, it is out of the fight (P4-132)
            _fellBackIn = Fsm.Current;
            _fallBackRepath = 0f;
            Engage.Clear();
            Looting.Cancel();
            if (Time.time - _fallBackStressAt >= FallBackRules.StressEverySeconds)
            {
                _fallBackStressAt = Time.time;
                Data.stress = Mathf.Min(100f, Data.stress + FallBackRules.Stress);
            }
            Say("뒤로 빠질게! 치료 좀 부탁해!");
        }

        void StopFallingBack()
        {
            if (!FallingBack) return;
            FallingBack = false;
            _fellBackIn = null;
            Mover.SpeedMultiplier = 1f;
        }

        /// <summary>
        /// Walks it behind its party while it is falling back: true this frame (the caller does no fighting or walking of its
        /// own). Over once mended, with no foe about (the party mends as it must, P4-71), when its party is gone (then it goes
        /// home alone, <see cref="CheckRetreat"/>), or when its state changed under it.
        /// </summary>
        bool TickFallingBack(float dt)
        {
            if (!FallingBack) return false;
            var foe = FoeToFallBackFrom();
            bool partyLeft = PartyRecovery.GroupWith(this, s_fallBackParty).Count < 2;
            if (Fsm.Current != _fellBackIn || foe == null || partyLeft || HpRatio >= FallBackRules.RejoinAt(RetreatThreshold))
            {
                bool mended = foe != null && !partyLeft && HpRatio >= FallBackRules.RejoinAt(RetreatThreshold);
                StopFallingBack();
                if (mended) Say("이제 괜찮아, 다시 간다!");
                return false;
            }
            Visual?.FaceTowards(foe.Position);
            _fallBackRepath -= dt;
            if (_fallBackRepath > 0f) return true;
            _fallBackRepath = FallBackRules.RepathSeconds;

            Adventurer healer = null;
            float healerDist = float.MaxValue;
            Vector2 centre = default;
            int fighters = 0;
            foreach (var m in s_fallBackParty)
            {
                if (m == this || !m.IsAlive) continue;
                if (!m.FallingBack) { centre += m.Position; fighters++; }
                float d = (m.Position - Position).sqrMagnitude;
                if (d < healerDist && Heals(m)) { healer = m; healerDist = d; }
            }
            centre = fighters > 0 ? centre / fighters : Position;
            var spot = FallBackRules.Spot(Position, healer != null ? healer.Position : (Vector2?)null, centre, foe.Position);
            if (Services.TryGet<World.WorldService>(out var world)
                && world.Grid.TryFindNearestWalkable(World.GridMap.WorldToCell(spot), 4, out var cell))
                spot = World.GridMap.CellToWorld(cell);
            Mover.SpeedMultiplier = 1.2f;
            if ((spot - Position).sqrMagnitude > 0.6f * 0.6f) Mover.SetDestination(spot, 0.4f);
            else if (Mover.IsMoving) Mover.Stop();
            return true;
        }

        static bool Heals(Adventurer m)
        {
            foreach (var s in m.Skills) if (s is HealSkill || s is MassHealSkill) return true;
            return false;
        }

        /// <summary>What it falls back from: the nearest monster in the fight round it (after it, or after one of ours), or null.</summary>
        /// <summary>
        /// In a fight, its own or its party's (P4-96): fighting or hit lately, fallen back, or a monster in the fight round it —
        /// a dragon on the wing clears everyone's target for a moment, and that is no lull to walk off in.
        /// </summary>
        public bool InTheThickOfIt => IsInFight() || FallingBack || FoeToFallBackFrom() != null;

        internal Unit FoeToFallBackFrom()
            => UnitRegistry.FindNearest(Position, FallBackRules.ThreatRange, this,
                (u, me) => u is Monster m && m.IsAlive && (m.IsHostileTo(me) || m.CombatEngagement?.Target is Adventurer { IsGuildMember: true }));

        public void ChangeState(State<Adventurer> s)
        {
            StopFallingBack();
            CallOffOrder(); // a focus order was for what it was doing (P4-132); one into town guard is given after the change
            Looting.Cancel();
            Fsm.ChangeState(s);
            EventBus.Publish(new AdventurerStateChangedEvent { Adventurer = this, Label = s.Label });
        }

        /// <summary>A line said again within this many game hours is skipped (small models repeat themselves).</summary>
        const float RepeatWindowHours = 6f;
        readonly List<(string key, float hour)> _recentLines = new();

        /// <param name="toFeed">False for lines that belong to a dialogue window (recruitment): only the speech bubble shows them, not the bottom-left feed.</param>
        public void Say(string text, bool toFeed = true)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            float now = Services.TryGet<GameClock>(out var clock) ? clock.TotalHours : Time.time / 60f;
            string key = LineKey(text);
            _recentLines.RemoveAll(l => now - l.hour > RepeatWindowHours);
            if (_recentLines.Exists(l => l.key == key)) return;
            _recentLines.Add((key, now));
            if (_recentLines.Count > 8) _recentLines.RemoveAt(0);
            EventBus.Publish(new AdventurerSpeechEvent { Adventurer = this, Text = text, NoFeed = !toFeed });
        }

        /// <summary>Letters/digits only, so "좀 쉬어야겠어..." and "좀 쉬어야겠어!" count as the same line.</summary>
        static string LineKey(string text)
        {
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char c in text) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        // ---------------- Unit overrides ----------------

        public override bool IsHostileTo(Unit other)
            => other != null && other != this && other.IsAlive
               && (other is Monster
                   // A mental break (P4-104d): a berserker goes for any comrade and every comrade turns on it; a fistfight is the two's.
                   || other is Adventurer { IsVisitor: false } m && (Berserk || m.Berserk || BrawlFoe == m));

        protected override void OnDamaged(float amount, Unit source)
        {
            // Just after a last stand (F-6): a few seconds in which nothing can finish the member off, so they can get away.
            if (Time.time < GraceUntil)
            {
                Hp = Mathf.Min(Stats.MaxHp, Hp + amount);
                return;
            }
            // A fistfight's blows bruise, they do not wound (P4-104d): never below its floor.
            if (source != null && source == BrawlFoe)
            {
                Hp = Mathf.Max(Hp, Mathf.Min(Hp + amount, Stats.MaxHp * BreakRules.BrawlFloor));
                return;
            }
            // 0.3 per point of the listed (unscaled) health: P4-42 tripled health and fights, and the
            // raw damage tripled the stress of losing a health bar with it (P4-73: members sat at 100).
            Data.stress = Mathf.Min(100f, Data.stress + Combat.ListedHp(amount) * 0.3f);
            // F-6: a lethal blow may leave a guild member alive at 1 HP; they are then wounded and flee (see GuildLegacy.TryLastStand).
            if (Hp <= 0f && IsGuildMember && !IsVisitor && Services.TryGet<Legacy.GuildLegacy>(out var legacy) && legacy.TryLastStand(this, source))
            {
                Hp = 1f;
                GraceUntil = Time.time + Legacy.LegacyRules.GraceSeconds;
                Engage.Clear();
                Looting.Cancel();
                ApplySpeedBuff(1.4f, Legacy.LegacyRules.GraceSeconds + 2f);
                // Away from the blow (P4-96: it used to run home alone from the middle of its party's fight): behind its
                // party if they fight on, with them if the fight is lost — home on its own only with nobody out with it.
                if (PartyRecovery.WhenHurt(this) == PartyRecovery.HurtCall.Alone) RetreatAlone();
            }
        }

        protected override void OnDeath(Unit killer)
        {
            Looting.Cancel();
            // P4-21 (Q-B): the fallen drop their guild gear and whatever loot they carried.
            var drops = new List<ItemStack>(Data.loot);
            if (Data.gearTier > 0) drops.Add(new ItemStack(GearShopping.GearItemId(Data.gearTier), 1));
            if (drops.Count > 0 && Services.TryGet<Items.GroundItemService>(out var ground)) DeathDrops = ground.Drop(Position, drops);
            Data.loot.Clear();
            // Monsters learn from their kills too (user decision 2026-09-22).
            if (killer is Monster slayer && slayer.IsAlive) slayer.GainKillExp(MonsterLevel.ExpForAdventurer(Data.level));
            Fsm.ChangeState(DeadState);
            if (Services.TryGet<GuildRoster>(out var roster)) roster.NotifyDeath(this, killer);
        }

        /// <summary>Guild members this close to a kill share its EXP (P4-40).</summary>
        public const float ExpShareRadius = 10f;
        static readonly List<Unit> s_expShare = new();

        public void ReceiveKillRewards(MonsterDefinition victim, int exp, List<ItemStack> loot)
        {
            Data.kills++;
            foreach (var s in loot) Data.AddLoot(s.itemId, s.count);
            GainExp(Mentored(this, exp)); // the victim's own level counts (user decision 2026-09-22)
            // Everyone fighting alongside gets the full EXP too (P4-40, user report 2026-09-21:
            // "levelling is too slow"). Parties focus one target, so the last hit is luck, and a
            // priest that heals the whole fight never landed one at all — Lv1 after three days.
            UnitRegistry.Collect(Position, ExpShareRadius, u => u != this && u is Adventurer m && m.IsGuildMember, s_expShare);
            foreach (var u in s_expShare) ((Adventurer)u).GainExp(Mentored((Adventurer)u, exp));
        }

        /// <summary>A kill's EXP with a mentor close by: a little more (P4-131).</summary>
        static int Mentored(Adventurer a, int exp) => Services.TryGet<BondService>(out var bonds) ? bonds.Mentored(a, exp) : exp;

        // ---------------- Save (P5-4) ----------------

        /// <summary>A member or visitor as saved: its data, its standing as a unit, and what it was doing.</summary>
        [System.Serializable]
        public class AdventurerSave
        {
            public AdventurerData data;
            public UnitSave unit;
            public bool visitor;
            public bool member;
            public float visitor_leave_hour;
            public string quest;
            public string drove_for_quest;
            public string last_thought;
            public Engagement.EngageSave engage;
            public string state;
            public float state_time;
            public string state_data;
        }

        State<Adventurer>[] _allStates;

        State<Adventurer>[] AllStates => _allStates ??= new State<Adventurer>[]
        {
            IdleState, DecidingState, TravelState, HuntState, FreeHuntState, EscortState, GatherState, DefendState,
            ReturnState, VisitState, RestState, CampState, DeadState, VisitorWanderState, VisitorLeaveState, LureState,
            TownGuardState, RetrievalState, VillageCandidateState, RefugeeState, TrainState, TakeCoverState, BreakState, TagAlongState, PlaceVisitState,
        };

        /// <summary>One of this member's states by class name (saved states refer to each other this way), or null.</summary>
        internal State<Adventurer> StateNamed(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var s in AllStates) if (s.GetType().Name == name) return s;
            return null;
        }

        internal static string NameOf(State<Adventurer> state) => state?.GetType().Name;

        public AdventurerSave CaptureSave()
        {
            var current = Fsm.Current;
            return new AdventurerSave
            {
                data = Data, unit = CaptureUnit(), visitor = IsVisitor, member = IsGuildMember,
                visitor_leave_hour = VisitorLeaveAtHour, quest = CurrentQuest?.Id, drove_for_quest = DroveForQuest?.Id,
                last_thought = LastThought, engage = Engage.Capture(),
                state = NameOf(current), state_time = Mathf.Round((current?.Elapsed ?? 0f) * 100f) / 100f,
                state_data = current?.SaveState(),
            };
        }

        /// <summary>First pass: the member is created where it stood, as it was (no join event).</summary>
        public static Adventurer RestoreSave(AdventurerSave s, Transform parent, SaveContext ctx)
        {
            AdventurerGenerator.NoteId(s.data.id);
            var a = Create(s.data, new Vector2(s.unit.x, s.unit.y), parent);
            a.IsGuildMember = s.member;
            if (s.visitor)
            {
                a.Faction = Faction.Visitor;
                a.VisitorLeaveAtHour = s.visitor_leave_hour;
            }
            if (!s.member || s.visitor) a.RecalculateStats(); // facilities are the guild's only (P4-87)
            a.RestoreUnit(s.unit);
            a.LastThought = s.last_thought;
            ctx.Register(a.UnitId, a);
            return a;
        }

        /// <summary>Second pass: what it points at (its quest, who hit it last).</summary>
        public void LinkSave(AdventurerSave s, SaveContext ctx)
        {
            LinkUnit(s.unit, ctx);
            CurrentQuest = ctx.Find<Quest>(s.quest);
            DroveForQuest = ctx.Find<Quest>(s.drove_for_quest);
        }

        /// <summary>Third pass: back to what it was doing, and to whom it was fighting.</summary>
        public void ResumeSave(AdventurerSave s, SaveContext ctx)
        {
            var state = StateNamed(s.state);
            if (state == null || state == DeadState)
            {
                if (!string.IsNullOrEmpty(s.state)) ctx.Warnings.Add($"{Data.name}: unknown state {s.state}");
                state = IsVisitor ? VisitorWanderState : IdleState;
            }
            Fsm.Resume(state, s.state_time, s.state_data, ctx);
            Engage.Restore(s.engage, ctx);
        }

        public void GainExp(int amount)
        {
            // The fallen gain nothing (a posthumous kill would otherwise level up a corpse).
            if (amount <= 0 || !IsAlive) return;
            // A training inspiration (P4-104d) makes every lesson count more.
            float inspired = Data.inspiration == InspirationKind.Training ? BreakRules.TrainingExp : 1f;
            Data.exp += Mathf.RoundToInt(amount * (Traits?.ExpMultiplier ?? 1f) * inspired);
            bool leveled = false;
            while (Data.exp >= Data.ExpToNextLevel)
            {
                Data.exp -= Data.ExpToNextLevel;
                Data.level++;
                leveled = true;
            }
            if (!leveled) return;
            RecalculateStats();
            Heal(Stats.MaxHp * 0.3f);
            EventBus.Publish(new AdventurerLevelUpEvent { Adventurer = this, Level = Data.level });
        }
    }
}
