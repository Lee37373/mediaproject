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
    public struct AdventurerSpeechEvent { public Adventurer Adventurer; public string Text; }
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
        internal EscortState EscortState;
        internal PartyGatherState GatherState;
        internal DefendState DefendState;
        internal ReturnToTownState ReturnState;
        internal GuildVisitState VisitState;
        internal RestState RestState;
        internal DeadState DeadState;
        internal VisitorWanderState VisitorWanderState;
        internal VisitorLeaveState VisitorLeaveState;

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
        public Color ProjectileColor => Data.cls == AdventurerClass.Mage ? new Color(0.5f, 0.7f, 1f) : new Color(0.95f, 0.85f, 0.55f);

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
            EscortState = new EscortState();
            GatherState = new PartyGatherState();
            DefendState = new DefendState();
            ReturnState = new ReturnToTownState();
            VisitState = new GuildVisitState();
            RestState = new RestState();
            DeadState = new DeadState();
            VisitorWanderState = new VisitorWanderState();
            VisitorLeaveState = new VisitorLeaveState();
        }

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
            SkillFactory.Assign(this, ClassSkills(data.cls));
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
            _ => new[] { "blink", "fireball" },
        };

        public override void ForceTarget(Unit target)
        {
            if (!IsAlive || target == null || !target.IsAlive) return;
            Engage.Force(target, 3f);
        }

        /// <summary>Level band plus birth grade (see CombatTiers.FromLevel).</summary>
        public override CombatTier Tier => Data == null ? CombatTier.Novice : CombatTiers.FromLevel(Data.level, (int)Data.grade);

        /// <summary>Cute Adventurers skins per class; the penguin king is reserved for the guild master.</summary>
        public static string[] SkinsFor(AdventurerClass cls) => cls switch
        {
            AdventurerClass.Warrior => new[] { "knight", "raccoon" },
            AdventurerClass.Ranger => new[] { "fox", "frog" },
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
                _ => (new Color(0.25f, 0.35f, 0.75f), new Color(0.9f, 0.85f, 0.4f), new Color(0.2f, 0.22f, 0.45f)),
            };
            return PixelArt.Humanoid(d.id, d.hairColor, d.skinColor, body, accent, legs);
        }

        public void RecalculateStats(bool fullHeal = false)
        {
            float ratio = Stats.MaxHp > 0 ? HpRatio : 1f;
            Stats = AdventurerStats.Compute(Data, Traits?.Modifiers);
            if (fullHeal || !IsAlive) ResetVitals();
            else
            {
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
                if (Fsm.Current != RestState) Data.fatigue = Mathf.Min(100f, Data.fatigue + fatigueRate * hours);
                if (inTown && Fsm.Current != RestState) Heal(Stats.MaxHp * 0.04f * hours);
                Data.stress = Mathf.Max(0f, Data.stress - (inTown ? 4f : 0.5f) * hours);
                TickStatus(dt);
                if (IsStunned) return;
            }

            Fsm.Tick(dt);
        }

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
            System.Func<Unit, bool> threat = u => u is Monster m && (allowed == null || allowed(m))
                                                   && (WasRecentlyAttackedBy(m) || (m.IsHostileTo(this) && CanSee(m)));
            System.Func<Unit, bool> extra = extraTargets == null ? null : u => extraTargets(u) && (allowed == null || allowed(u)) && CanSee(u);
            Engage.CandidateFilter = extra == null ? threat : u => threat(u) || extra(u);
            Engage.SearchRadius = Stats.SightRadius;

            // Optional zone (town defense): drop targets outside it instead of chasing them.
            if (Engage.Target != null && allowed != null && !allowed(Engage.Target)) Engage.Clear();
            if (Engage.Target != null)
            {
                if (Engage.Tick(dt) != EngageStatus.Lost) return true;
            }

            _senseTimer -= dt;
            if (_senseTimer > 0f) return false;
            _senseTimer = SenseInterval;

            // Threats first (aggressive monsters or anything that hit us), then optional quest targets.
            var target = TargetSelector.Pick(this, Stats.SightRadius * 0.8f, threat, Engage)
                         ?? (extra != null ? TargetSelector.Pick(this, Stats.SightRadius, extra, Engage) : null);
            if (target == null) return false;
            Engage.SetTarget(target);
            return Engage.Tick(dt) != EngageStatus.Lost;
        }

        /// <summary>Drinks a potion or signals retreat when badly hurt/exhausted. Returns true if retreating.</summary>
        internal bool CheckRetreat()
        {
            if (HpRatio < RetreatThreshold && Data.potions > 0 && Services.TryGet<GameDatabase>(out var db))
            {
                Data.potions--;
                Heal(db.Item(DefaultContent.Potion)?.healAmount ?? 40f);
                Say("물약을 마셨다!");
            }
            if (HpRatio < RetreatThreshold || Data.fatigue >= 95f)
            {
                if (HpRatio < RetreatThreshold)
                    EventBus.Publish(new AdventurerRetreatedEvent { Adventurer = this, Threat = LastAttacker });
                Engage.Clear();
                Data.stress = Mathf.Min(100f, Data.stress + 15f);
                ReturnState.Retreating = true;
                Fsm.ChangeState(ReturnState);
                return true;
            }
            return false;
        }

        public void ChangeState(State<Adventurer> s)
        {
            Fsm.ChangeState(s);
            EventBus.Publish(new AdventurerStateChangedEvent { Adventurer = this, Label = s.Label });
        }

        /// <summary>A line said again within this many game hours is skipped (small models repeat themselves).</summary>
        const float RepeatWindowHours = 6f;
        readonly List<(string key, float hour)> _recentLines = new();

        public void Say(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            float now = Services.TryGet<GameClock>(out var clock) ? clock.TotalHours : Time.time / 60f;
            string key = LineKey(text);
            _recentLines.RemoveAll(l => now - l.hour > RepeatWindowHours);
            if (_recentLines.Exists(l => l.key == key)) return;
            _recentLines.Add((key, now));
            if (_recentLines.Count > 8) _recentLines.RemoveAt(0);
            EventBus.Publish(new AdventurerSpeechEvent { Adventurer = this, Text = text });
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
            Data.stress = Mathf.Min(100f, Data.stress + amount * 0.3f);
        }

        protected override void OnDeath(Unit killer)
        {
            Fsm.ChangeState(DeadState);
            if (Services.TryGet<GuildRoster>(out var roster)) roster.NotifyDeath(this, killer);
        }

        public void ReceiveKillRewards(MonsterDefinition victim, List<ItemStack> loot)
        {
            Data.kills++;
            foreach (var s in loot) Data.AddLoot(s.itemId, s.count);
            GainExp(victim.expValue);
        }

        public void GainExp(int amount)
        {
            if (amount <= 0) return;
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
