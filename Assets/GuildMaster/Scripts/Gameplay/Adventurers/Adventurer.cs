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
    /// <summary>Retreat because of low HP (not fatigue). Threat = last unit that hit us, if any.</summary>
    public struct AdventurerRetreatedEvent { public Adventurer Adventurer; public Unit Threat; }
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
        public bool LastThoughtFromLlm { get; set; }
        public bool IsGuildMember { get; set; } = true;

        // ---- Visitor (recruitment candidate) ----
        public bool IsVisitor => Faction == Faction.Visitor;
        /// <summary>Game hour (TotalHours) at which a visitor heads for the gate.</summary>
        public float VisitorLeaveAtHour { get; set; }
        /// <summary>True while the recruitment dialogue with the guild master is open.</summary>
        public bool InConversation { get; set; }
        public Transform ConversationPartner { get; set; }

        /// <summary>Hp ratio below which the adventurer retreats (traits can modify).</summary>
        public float RetreatThreshold => Traits?.RetreatThreshold ?? 0.35f;

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
        /// <summary>At a campfire out in the wild (P4-66).</summary>
        internal CampState CampState;
        internal DeadState DeadState;
        internal VisitorWanderState VisitorWanderState;
        internal VisitorLeaveState VisitorLeaveState;
        internal LureState LureState;
        internal TownGuardState TownGuardState;
        /// <summary>Sent out to bring a fallen comrade's relics home (F-6).</summary>
        internal Legacy.RelicRetrievalState RetrievalState;
        /// <summary>Picking loot off the ground (P4-21).</summary>
        internal AdventurerLooting Looting;
        /// <summary>Quest for which the heroic drive hunt was already tried (once per quest).</summary>
        internal Quests.Quest DroveForQuest;
        /// <summary>What this member dropped on the ground when they died (F-6: tracked as relics by GuildLegacy).</summary>
        public IReadOnlyList<Items.GroundItem> DeathDrops { get; private set; }
        /// <summary>Until this time (Time.time) nothing can hurt the member: a few seconds after a last stand (F-6).</summary>
        float _graceUntil;

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

        /// <summary>In town with nothing on: idling, resting, or at the guild counter.</summary>
        public bool IsAtLeisureInTown => IsIdleInTown || Fsm != null && (Fsm.Current == RestState || Fsm.Current == VisitState);

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
            CampState = new CampState();
            DeadState = new DeadState();
            VisitorWanderState = new VisitorWanderState();
            VisitorLeaveState = new VisitorLeaveState();
            LureState = new LureState();
            TownGuardState = new TownGuardState();
            RetrievalState = new Legacy.RelicRetrievalState();
            Looting = new AdventurerLooting(this);
            // Party back line (P4-27): ranged members fall back behind a front-liner while reloading.
            Engage.HoldSpot = target => Services.TryGet<PartyTactics>(out var party) ? party.BackLineSpot(this, target) : null;
        }

        // ---------------- Party roles (P4-27) ----------------

        public PartyRole Role => Data != null ? PartyTactics.RoleOf(Data.cls) : PartyRole.Back;
        public override bool IsBackLine => Role == PartyRole.Back;
        /// <summary>Skilled+ warriors protect the back line.</summary>
        public override bool GuardsBackLine => Role == PartyRole.Front && Tier >= CombatTier.Skilled;
        public override Unit CalledTarget => Services.TryGet<PartyTactics>(out var party) ? party.CalledTargetFor(this) : null;

        /// <summary>Turns a freshly created adventurer into a town visitor (not attacked, no quests).</summary>
        public void BecomeVisitor(float leaveAtHour)
        {
            Faction = Faction.Visitor;
            IsGuildMember = false;
            VisitorLeaveAtHour = leaveAtHour;
            ChangeState(VisitorWanderState);
        }

        /// <summary>Visitor accepted the guild's offer. Call through <see cref="GuildRoster.AddExisting"/>.</summary>
        internal void JoinGuild()
        {
            Faction = Faction.Guild;
            IsGuildMember = true;
            InConversation = false;
            ConversationPartner = null;
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
            // Imported Cute Adventurers skin when available, generated pixel art otherwise.
            var anim = ArtLibrary.Adventurer(SkinFor(data.cls, data.id));
            if (anim != null) Visual.InitAnimated(anim);
            else Visual.Init(ClassSprite(data));
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
            SkillFactory.Assign(this, Data.skillIds);
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

        public void RecalculateStats(bool fullHeal = false)
        {
            float ratio = Stats.MaxHp > 0 ? HpRatio : 1f;
            Stats = AdventurerStats.Compute(Data, Traits?.Modifiers);
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
            float hours = dt / (_clock != null ? _clock.RealSecondsPerGameHour : 10f);

            bool inTown = Services.TryGet<Town.TownService>(out var town) && town.IsInTown(Position);
            if (IsAlive)
            {
                float fatigueRate = inTown ? 0.5f : 3f * (Traits?.FatigueMultiplier ?? 1f);
                // Resting in town or by a campfire (P4-66) wears nobody out.
                if (Fsm.Current != RestState && Fsm.Current != CampState) Data.fatigue = Mathf.Min(100f, Data.fatigue + fatigueRate * hours);
                if (inTown && Fsm.Current != RestState) Heal(Stats.MaxHp * 0.04f * hours);
                Data.stress = Mathf.Max(0f, Data.stress - (inTown ? 4f : 0.5f) * hours);
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

            // What may be (re)targeted this frame: threats (aggressive monsters we see or that hit us)
            // and the caller's optional extra targets, limited to the optional zone.
            // Party members only take fights near their leader (P4-38); what hits them is fought anyway.
            var leash = PartyLeashCentre();
            // Also one that is fighting a comrade: a slime never starts a fight, but once it hits
            // back at one of us the rest pitch in instead of looting around it (user report 2026-09-22).
            System.Func<Unit, bool> threat = u => u is Monster m && (allowed == null || allowed(m))
                                                   && (WasRecentlyAttackedBy(m)
                                                       || ((m.IsHostileTo(this) || m.CombatEngagement?.Target is Adventurer { IsGuildMember: true })
                                                           && CanSee(m) && PartyTactics.InLeash(leash, m.Position))
                                                       || HearsFightingAComrade(m) && PartyTactics.InLeash(leash, m.Position));
            System.Func<Unit, bool> extra = extraTargets == null ? null
                : u => extraTargets(u) && (allowed == null || allowed(u)) && CanSee(u) && PartyTactics.InLeash(leash, u.Position);
            Engage.CandidateFilter = extra == null ? threat : u => threat(u) || extra(u);
            Engage.SearchRadius = Mathf.Max(Stats.SightRadius, ComradeCryRadius);

            // Optional zone (town defense): drop targets outside it instead of chasing them.
            if (Engage.Target != null && allowed != null && !allowed(Engage.Target)) Engage.Clear();
            // A chase that pulls a member away from its party is called off (P4-38).
            if (Engage.Target != null && !WasRecentlyAttackedBy(Engage.Target) && !PartyTactics.InLeash(leash, Engage.Target.Position))
                Engage.Clear();
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

        /// <summary>A fight breaks formation pacing (catch-up / waiting speeds): fight at normal speed.</summary>
        bool Fighting()
        {
            Mover.SpeedMultiplier = 1f;
            return true;
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
            if (HpRatio < RetreatThreshold || wearinessCounts && Data.fatigue >= 95f)
            {
                Data.stress = Mathf.Min(100f, Data.stress + 15f);
                // Out with a party: it stops to mend or pulls back with us (P4-71), nobody leaves alone.
                if (PartyRecovery.Handle(this))
                {
                    if (HpRatio < RetreatThreshold && Fsm.Current == ReturnState && ReturnState.Retreating)
                        EventBus.Publish(new AdventurerRetreatedEvent { Adventurer = this, Threat = LastAttacker });
                    return true;
                }
                if (HpRatio < RetreatThreshold)
                    EventBus.Publish(new AdventurerRetreatedEvent { Adventurer = this, Threat = LastAttacker });
                Engage.Clear();
                Looting.Cancel();
                ReturnState.RequestRetreat();
                Fsm.ChangeState(ReturnState);
                return true;
            }
            return false;
        }

        public void ChangeState(State<Adventurer> s)
        {
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

        public override bool IsHostileTo(Unit other) => other is Monster && other.IsAlive;

        protected override void OnDamaged(float amount, Unit source)
        {
            // Just after a last stand (F-6): a few seconds in which nothing can finish the member off, so they can get away.
            if (Time.time < _graceUntil)
            {
                Hp = Mathf.Min(Stats.MaxHp, Hp + amount);
                return;
            }
            // 0.3 per point of the listed (unscaled) health: P4-42 tripled health and fights, and the
            // raw damage tripled the stress of losing a health bar with it (P4-73: members sat at 100).
            Data.stress = Mathf.Min(100f, Data.stress + Combat.ListedHp(amount) * 0.3f);
            // F-6: a lethal blow may leave a guild member alive at 1 HP; they are then wounded and flee (see GuildLegacy.TryLastStand).
            if (Hp <= 0f && IsGuildMember && !IsVisitor && Services.TryGet<Legacy.GuildLegacy>(out var legacy) && legacy.TryLastStand(this, source))
            {
                Hp = 1f;
                _graceUntil = Time.time + Legacy.LegacyRules.GraceSeconds;
                Engage.Clear();
                Looting.Cancel();
                ApplySpeedBuff(1.4f, Legacy.LegacyRules.GraceSeconds + 2f);
                ReturnState.RequestRetreat();
                ChangeState(ReturnState);
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
            GainExp(exp); // the victim's own level counts (user decision 2026-09-22)
            // Everyone fighting alongside gets the full EXP too (P4-40, user report 2026-09-21:
            // "levelling is too slow"). Parties focus one target, so the last hit is luck, and a
            // priest that heals the whole fight never landed one at all — Lv1 after three days.
            UnitRegistry.Collect(Position, ExpShareRadius, u => u != this && u is Adventurer m && m.IsGuildMember, s_expShare);
            foreach (var u in s_expShare) ((Adventurer)u).GainExp(exp);
        }

        public void GainExp(int amount)
        {
            // The fallen gain nothing (a posthumous kill would otherwise level up a corpse).
            if (amount <= 0 || !IsAlive) return;
            Data.exp += Mathf.RoundToInt(amount * (Traits?.ExpMultiplier ?? 1f));
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
