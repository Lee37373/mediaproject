using System.Linq;
using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Monsters;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Hunting on the guild's own initiative, with no quest (P4-37). Members gather at the gate,
    /// walk out as a band (user request 2026-09-20), kill what <see cref="FreeHunt.CanTake"/>
    /// allows and bring the loot home. Anything none of them could take is left alone and walked away
    /// from — but whatever hits one of them is fought by them all (P4-121).
    /// </summary>
    public class FreeHuntState : State<Adventurer>
    {
        /// <summary>Close enough to the roam spot to pick the next one.</summary>
        const float SpotReached = 2.5f;
        const float SearchInterval = 0.6f;
        /// <summary>Seconds between "too strong" remarks, so backing off is not a chatterbox.</summary>
        const float BackOffCooldown = 20f;
        /// <summary>
        /// Seconds the band walks the leg it turned onto from a monster before it turns from that one again (P4-121: it
        /// turned every survey, 0.6 s, while the monster stayed in sight — back and forth on the spot).
        /// </summary>
        const float BackOffHold = 5f;
        /// <summary>Cells past a monster's sight the band keeps out of when it would rather not be noticed.</summary>
        const float AvoidMargin = 3f;
        /// <summary>A known den this close counts as "we are standing in their territory".</summary>
        const float NestNearRadius = 12f;

        /// <summary>Surprises on the way to one spot before the band gives up on it and picks another.</summary>
        const int ReconsiderAfterSurprises = 3;
        /// <summary>Scouting legs before the leader thinks again about a den to make for (one it has just found, say).</summary>
        const int ScoutLegsPerLook = 3;

        float _searchTimer;
        float _roamTimer;
        float _startHour;
        float _nextBackOffLine;
        float _formationRepath;
        float _campCheck;
        int _enemies;
        int _nestNearby;
        /// <summary>The known den whose territory we stand in (the one <see cref="_nestNearby"/> counts), null for none.</summary>
        Nest _nestHere;
        /// <summary>Per kind, those from the known dens round here that would pile in (P4-104b), worked out once a survey.</summary>
        readonly System.Collections.Generic.Dictionary<MonsterDefinition, int> _crowd = new();
        HuntBand _band;
        /// <summary>Us and the members close enough to fight beside us, as the fight estimate sees them.</summary>
        readonly System.Collections.Generic.List<CombatStats> _bandStats = new();
        readonly System.Collections.Generic.List<float> _bandHp = new();
        /// <summary>Each one's class, alongside <see cref="_bandStats"/> (P4-107: a researched kind's weakness or strength).</summary>
        readonly System.Collections.Generic.List<AdventurerClass> _bandClasses = new();
        /// <summary>The band as it stands against a researched kind, worked out once a count.</summary>
        readonly System.Collections.Generic.Dictionary<MonsterDefinition, System.Collections.Generic.List<CombatStats>> _bandFor = new();
        static readonly System.Collections.Generic.List<Unit> AllyBuffer = new();
        /// <summary>How far out this band roams (P4-38): grows with what it could take on.</summary>
        float _reach = FreeHunt.Radius;

        // ---- Walking country it does not know (P4-64) ----
        /// <summary>The spot came from scouting, not from a den the guild has seen: head into the fog.</summary>
        bool _scouting;
        /// <summary>Where this leg ends, and how many route corrections the walk had when it was aimed.</summary>
        Vector2 _leg;
        int _correctionsAtAim;
        /// <summary>Times the ground turned out worse than the plan on the way to this spot.</summary>
        int _surprises;

        // ---- Aimed afresh every leg (P4-121) ----
        /// <summary>The leader's goal — a den or a watchtower — or null while scouting, which has no spot: each leg is picked from where the band is.</summary>
        Vector2? _goal;
        /// <summary>The den <see cref="_goal"/> is, looked at again every leg: kept only while the band could still take it.</summary>
        Nest _goalNest;
        bool _needGoal;
        int _scoutLegs;
        /// <summary>The way the last leg went: scouting goes on that way rather than back and forth.</summary>
        Vector2 _heading;
        /// <summary>A spot that went wrong (could not be walked to), kept clear of for a few legs.</summary>
        Vector2? _avoid;
        int _avoidLegs;
        Unit _backedOffFrom;
        float _backOffUntil;
        readonly ExplorationRoute.LegView _view = new();
        static readonly System.Collections.Generic.List<Unit> Nearby = new();
        /// <summary>Legs walked this trip (debug: <see cref="Session.DebugCommands.ExploreReport"/>).</summary>
        public int LegsWalked { get; private set; }
        public Vector2 Leg => _leg;
        public int Surprises => _surprises;
        public bool Scouting => _scouting;

        public override string Label => _band != null && _band.Mustering ? "동료 기다리는 중" : "자율 사냥";

        public override void Enter()
        {
            _startHour = AdvCtx.Clock.TotalHours;
            _searchTimer = 0f;
            _roamTimer = 0f;
            _formationRepath = 0f;
            _nextBackOffLine = 0f;
            _enemies = 1;
            _nestNearby = 0;
            _nestHere = null;
            _crowd.Clear();
            _surprises = 0;
            LegsWalked = 0;
            ForgetTheWay();
            _correctionsAtAim = Owner.Mover.RouteCorrections;
            // Out here the band plans with what the guild has seen and fixes the route as it learns
            // the ground (P4-64); in town and on quests the walkers keep the old omniscient plan.
            Owner.Mover.UnknownToMe = ExplorationRoute.KnowledgeOf(Services.TryGet<Fog.FogOfWar>(out var fog) ? fog : null);
            CountBand(Owner);
            _band = Services.TryGet<HuntBandService>(out var bands) ? bands.Join(Owner) : null;
            // Back on the hunt after mending (P4-71): the band goes on, its time out still counted from the gate.
            if (_band != null && _band.Paused)
            {
                _band.Paused = false;
                _startHour = _band.SetOutHour;
            }
            // With invitations out the leader has already asked (P4-51).
            bool inviting = _band != null && Services.TryGet<PartyService>(out var invites) && invites.IsGathering(_band);
            if (_band != null && _band.Leader == Owner && _band.Members.Count == 1 && !inviting) Owner.Say("사냥 나갈 사람?");
        }

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class Saved
        {
            public float start_hour;
            public bool scouting;
            public int surprises;
        }

        public override string SaveState() => JsonUtility.ToJson(new Saved
        {
            start_hour = _startHour, scouting = _scouting, surprises = _surprises,
        });

        /// <summary>As <see cref="Enter"/>, but back in its band (restored with it) without asking around again.</summary>
        public override void ResumeState(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<Saved>(json);
            _searchTimer = 0f;
            _roamTimer = 0f;
            _formationRepath = 0f;
            _nextBackOffLine = 0f;
            _enemies = 1;
            _nestNearby = 0;
            _nestHere = null;
            _crowd.Clear();
            ForgetTheWay();
            _correctionsAtAim = Owner.Mover.RouteCorrections;
            Owner.Mover.UnknownToMe = ExplorationRoute.KnowledgeOf(Services.TryGet<Fog.FogOfWar>(out var fog) ? fog : null);
            CountBand(Owner);
            // Only the band it was in (restored with the bands): joining would open a new one and
            // ask everyone in town along again.
            _band = Services.TryGet<HuntBandService>(out var bands) ? bands.BandOf(Owner) : null;
            _startHour = s.start_hour;
            _scouting = s.scouting;
            _surprises = s.surprises;
        }

        public override void Exit()
        {
            Owner.Mover.SpeedMultiplier = 1f;
            Owner.Mover.UnknownToMe = null;
            // Pulling back to mend (P4-71): still one of the band, which goes on afterwards.
            if (_band != null && _band.Paused) { _band = null; return; }
            if (_band != null && !_band.Mustering) _band.GoingHome = true;
            if (Services.TryGet<HuntBandService>(out var bands)) bands.Leave(Owner);
            _band = null;
        }

        bool IsLeader => _band == null || _band.Leader == Owner;

        /// <summary>Worth attacking: one we can handle (P4-43: nothing is spared any more).</summary>
        internal bool Huntable(Unit u)
            => u is Monster m && m.IsAlive && m.Def != null
               && FreeHunt.CanTake(BandAgainst(m.Def), _bandHp, FoeAgainst(m.Def, m.Stats),
                   FreeHunt.ExpectedEnemies(_enemies, m.Def, _nestNearby) + CrowdOf(m.Def));

        /// <summary>
        /// The band against a kind the guild has researched (P4-107, user decision 2026-10-27): each one's blows counted with
        /// the kind's weakness or strength against its class, and mastery's edge; as it is for a kind not yet studied.
        /// </summary>
        System.Collections.Generic.List<CombatStats> BandAgainst(MonsterDefinition s)
        {
            if (s == null || Bestiary.BestiaryService.LevelOf(s) < 2) return _bandStats;
            if (_bandFor.TryGetValue(s, out var band)) return band;
            band = new System.Collections.Generic.List<CombatStats>(_bandStats.Count);
            for (int i = 0; i < _bandStats.Count; i++)
            {
                var st = _bandStats[i];
                if (i < _bandClasses.Count) st.Attack *= Bestiary.BestiaryService.KnownAttackFactor(s, _bandClasses[i]);
                band.Add(st);
            }
            _bandFor[s] = band;
            return band;
        }

        /// <summary>A mastered kind's blows, counted lighter.</summary>
        static CombatStats FoeAgainst(MonsterDefinition s, CombatStats stats)
        {
            stats.Attack *= Bestiary.BestiaryService.KnownTakenFactor(s);
            return stats;
        }

        /// <summary>The known dens of that kind round here, the one we stand at left out, that would pile in (P4-104b, as the den choice).</summary>
        int CrowdOf(MonsterDefinition s)
        {
            if (!_crowd.TryGetValue(s, out int n)) _crowd[s] = n = AreaRisk.ExtraFoesAround(s, Owner.Position, _nestHere);
            return n;
        }

        /// <summary>What the guild expects at a den now: its last count, grown back since (P4-42).</summary>
        static int ExpectedAt(GuildIntel intel, Nest n)
        {
            var rec = intel.Get(n);
            float since = Services.TryGet<GameClock>(out var clock) ? clock.TotalHours - rec.LastSeenHour : 0f;
            // Far sites breed slower (a public rule of the world, not hidden state).
            float growth = Services.TryGet<EcosystemManager>(out var eco) ? eco.GrowthFactorAt(n.WorldPosition) : 1f;
            return FreeHunt.ExpectedDenCount(intel.SpeciesOf(n), rec.LastCount, since, growth);
        }

        /// <summary>
        /// What may be fought at all: our own prey, anything that has just hit us, and anything fighting one of ours — the
        /// rest pitch in (P4-121, user report 2026-11-02: one member fought while the others called it "not one for us"
        /// and walked off; this filter kept out the comrade rule <see cref="Adventurer.HandleCombat"/> has for it, P4-42).
        /// </summary>
        bool Allowed(Unit u) => Huntable(u) || Owner.WasRecentlyAttackedBy(u) || FightingOneOfUs(u);

        /// <summary>
        /// A monster fighting another guild member, or one a member of our band has taken on — seen, or heard close by (as
        /// the combat code hears a comrade). Not one that is merely coming at us: that is still walked away from until it
        /// strikes (P4-37).
        /// </summary>
        bool FightingOneOfUs(Unit u)
        {
            if (u is not Monster m || !m.IsAlive) return false;
            bool onAComrade = m.CombatEngagement?.Target is Adventurer { IsGuildMember: true, IsAlive: true } comrade && comrade != Owner;
            bool takenOn = !onAComrade && TakenOnByTheBand(m);
            if (!onAComrade && !takenOn) return false;
            return Owner.CanSee(m) || Owner.HearsFightingAComrade(m)
                   || takenOn && Adventurer.HearComrades && (m.Position - Owner.Position).sqrMagnitude <= Adventurer.ComradeCryRadius * Adventurer.ComradeCryRadius;
        }

        /// <summary>One of our band other than us is fighting it.</summary>
        bool TakenOnByTheBand(Unit m)
        {
            if (_band == null) return false;
            foreach (var member in _band.Members)
                if (member != Owner && member != null && member.IsAlive && member.Engage.Target == m) return true;
            return false;
        }

        /// <summary>
        /// Too strong for every one of the band, each as it judges (a newcomer by itself, P4-99): the band keeps clear of
        /// it. A priest leading newcomers could take nothing alone and turned the band from slimes its ranger hunts.
        /// </summary>
        bool TooStrongForUs(Monster m)
        {
            if (Huntable(m)) return false;
            if (_band == null) return true;
            foreach (var member in _band.Members)
                if (member != Owner && member != null && member.IsAlive && member.Fsm.Current == member.FreeHuntState && member.FreeHuntState.Huntable(m))
                    return false;
            return true;
        }

        /// <summary>Whether this member, as it judges, could take on what lives at a known den (with its neighbours that would join, P4-104b).</summary>
        internal bool CouldTake(GuildIntel intel, Nest n)
        {
            var s = intel.SpeciesOf(n);
            // A region lord's lair is gone to on its quest, not on a whim (P4-83): it is far beyond a den's danger.
            if (s == null || intel.KnownLordAt(n) != null) return false;
            // With the known dens next door that would pile in, and in known deep woods only a fight well within
            // the band's means (P4-104b: bands took a den at a time and died to the zone, or blind in the woods).
            int foes = FreeHunt.ExpectedEnemies(1, s, ExpectedAt(intel, n)) + AreaRisk.ExtraFoesAround(s, n.WorldPosition, n);
            float share = AreaRisk.KnownDeep(n.WorldPosition) ? DeepWoodRules.HuntLossShare : 1f;
            return FreeHunt.CanTake(BandAgainst(s), _bandHp, FoeAgainst(s, FreeHunt.StatsOf(s, n.BirthLevel)), foes, share);
        }

        /// <summary>A known den with someone in it that none of the band could take on (as <see cref="TooStrongForUs"/>).</summary>
        bool DenTooStrongForUs(GuildIntel intel, Nest n)
        {
            if (ExpectedAt(intel, n) <= 0 || CouldTake(intel, n)) return false;
            if (_band == null) return true;
            foreach (var member in _band.Members)
                if (member != Owner && member != null && member.IsAlive && member.Fsm.Current == member.FreeHuntState && member.FreeHuntState.CouldTake(intel, n))
                    return false;
            return true;
        }

        /// <summary>One of the band other than us is in a fight: the band does not walk off from it.</summary>
        bool BandFighting()
        {
            if (_band == null) return false;
            foreach (var member in _band.Members)
                if (member != Owner && member != null && member.IsAlive && member.Engage.Target != null) return true;
            return false;
        }

        /// <summary>The two filters above as delegates, made once (P5-2).</summary>
        System.Func<Unit, bool> _huntable, _allowed;

        public override void Tick(float dt)
        {
            var a = Owner;
            if (a.CheckRetreat()) return;

            // Not a trip worth making: a healer whose company never showed up (or went home) turns
            // back rather than hunt alone (user report 2026-09-20).
            if (_band != null && !_band.Mustering && !FreeHunt.WorthGoing(a.Data.cls, _band.Members.Count))
            {
                a.Say("혼자서는 무리야. 돌아가자.");
                a.ChangeState(a.ReturnState);
                return;
            }

            // Waiting at the gate for company (user request: hunt as a party) — fighting what comes at the town first
            // (P4-93: archers waiting by the gate watched the dragon burn the smithy).
            if (_band != null && _band.Mustering)
            {
                if (a.HandleCombat(dt)) return;
                if (!a.Mover.IsMoving && Vector2.Distance(a.Position, AdvCtx.GuildDoor) > 2f)
                    a.Mover.SetDestination(AdvCtx.GuildDoor, 1.5f);
                return;
            }

            if (a.HandleCombat(dt, _huntable ??= Huntable, _allowed ??= Allowed)) return;
            if (a.Looting.Tick(dt, a.WithinPartyLeash)) return;
            // Night out on a hunt: the band may make camp, then head home in the morning (P4-66).
            if (AdvCtx.TryNightCamp(a, ref _campCheck, dt)) return;
            // A brave or greedy leader may make for the dragon's hoard instead (P4-93); the band follows it there.
            if (Services.TryGet<Dragons.DragonService>(out var dragon) && dragon.RaidTick(a, _startHour, _band == null || _band.Leader == a)) return;

            _searchTimer -= dt;
            if (_searchTimer <= 0f)
            {
                _searchTimer = SearchInterval;
                // These walk collections, so they run on the search tick, not every frame.
                if (ShouldGoHome(a)) { a.ChangeState(a.ReturnState); return; }
                Survey(a);

                var leash = a.PartyLeashCentre();
                var prey = UnitRegistry.FindNearest(a.Position, a.Stats.SightRadius,
                    u => Huntable(u) && !a.Engage.IsIgnoring(u) && a.CanSee(u) && PartyTactics.InLeash(leash, u.Position));
                if (prey != null)
                {
                    a.Engage.SetTarget(prey);
                    return;
                }
                if (BackOffFromSomethingTooStrong(a)) return;
            }

            // Followers keep formation around the leader (P4-27), the leader paces for them.
            if (Services.TryGet<PartyTactics>(out var party))
            {
                if (party.Follow(a, ref _formationRepath, dt, out _)) return;
                if (party.PaceLeader(a)) return;
            }

            // Exploring is walked a leg at a time (P4-64, user request 2026-09-23): the band commits
            // about 18 cells, reads the ground it can see, and aims again — and whenever the terrain
            // it discovers forces the walk to redraw its route, it re-aims at once instead of trudging
            // on along a plan made of guesses. Every leg is aimed afresh from where the band stands,
            // what it sees and how it is doing (P4-121, user intent 2026-11-02).
            if (_goal is { } goal && Reached(a, goal))
            {
                ClimbTowerAt(a, goal);
                DropGoal();
            }
            bool surprised = a.Mover.RouteCorrections != _correctionsAtAim;
            if (a.Mover.IsMoving && !surprised) return;
            _roamTimer -= dt;
            if (_roamTimer > 0f) return;
            _roamTimer = GameRandom.Shared.Range(1f, 2.5f);
            // The way there keeps turning out worse than it looked: somewhere else, then.
            if (surprised && ++_surprises >= ReconsiderAfterSurprises) DropGoal(_goal ?? _leg);
            AimLeg(a);
        }

        /// <summary>
        /// At the spot, or as close as the ground lets: a scouting spot picked in the fog may turn out
        /// to be water or rock, and the walk then ends at the nearest walkable cell up to six away
        /// (P4-73: the band stood there re-aiming at it for hours).
        /// </summary>
        static bool Reached(Adventurer a, Vector2 spot)
        {
            float d = Vector2.Distance(a.Position, spot);
            if (d < SpotReached) return true;
            return !a.Mover.IsMoving && d < 7f && !Services.Get<WorldService>().Grid.IsWalkable(GridMap.WorldToCell(spot));
        }

        /// <summary>At a watchtower it made for (P4-105c): up it goes, and the band with it sees the view.</summary>
        void ClimbTowerAt(Adventurer a, Vector2 spot)
        {
            if (!_scouting || !Services.TryGet<Places.PlaceVisits>(out var visits) || visits.TowerAt(spot) is not { } tower) return;
            visits.Climb(a, tower, _band?.Members);
        }

        /// <summary>
        /// Walks the next leg (P4-64): towards the goal, or — scouting — whichever way looks best from here (P4-121). Either
        /// way it is aimed with what the band sees and how it stands now (<see cref="LookAround"/>). A leg that cannot be
        /// reached even by guesswork counts as a surprise, so the band tries another heading and eventually another goal.
        /// </summary>
        void AimLeg(Adventurer a)
        {
            var world = Services.Get<WorldService>();
            float caution = RouteChoice.ForParty(a);
            var goal = Goal(a);
            var view = LookAround(a, world);
            _leg = goal is { } g
                ? ExplorationRoute.NextLeg(world, a.Mover.UnknownToMe, a.Position, g, caution, _scouting, view)
                : ExplorationRoute.ScoutLeg(world, a.Mover.UnknownToMe, a.Position, caution, view);
            _correctionsAtAim = a.Mover.RouteCorrections;
            // Where the band is headed, for a member out of formation to make for (scouting: the leader's leg).
            if (_band != null && IsLeader)
            {
                _band.Spot = goal ?? _leg;
                _band.HasSpot = true;
            }
            // The band walks the leader's route (P4-63): its nerve decides how wide of the woods it goes.
            var step = _leg - a.Position;
            if (step.sqrMagnitude > 1f && a.Mover.SetDestination(_leg, 1f, caution))
            {
                LegsWalked++;
                if (goal == null) _scoutLegs++;
                _heading = step.normalized;
                if (_avoidLegs > 0 && --_avoidLegs == 0) _avoid = null;
                return;
            }
            // Somewhere else, not the same den again (P4-80: the best den was picked straight back,
            // and the band tried the same unwalkable leg until a scouting roll took it elsewhere).
            if (++_surprises >= ReconsiderAfterSurprises) DropGoal(goal ?? _leg);
        }

        /// <summary>
        /// What the band takes into account for this leg (P4-121): its reach and how worn it is, the way it was going, and
        /// what to keep clear of — monsters any of it sees that none of it could take (unless one is already fighting one
        /// of us: that is everyone's fight), dens the guild knows it could not, and a spot that just went wrong.
        /// </summary>
        ExplorationRoute.LegView LookAround(Adventurer a, WorldService world)
        {
            var v = _view;
            v.Town = world.TownCenterWorld;
            v.Reach = _reach;
            v.Heading = _heading;
            v.Strain = BandStrain(a);
            v.Rng = GameRandom.Shared;
            v.Avoid.Clear();
            UnitRegistry.Collect(a.Position, a.Stats.SightRadius + FreeHunt.AllyRadius, Nearby);
            foreach (var u in Nearby)
                if (u is Monster m && m.IsAlive && SeenByBand(a, m) && TooStrongForUs(m) && !FightingOneOfUs(m))
                    v.Avoid.Add((m.Position, m.Stats.SightRadius + AvoidMargin));
            if (Services.TryGet<GuildIntel>(out var intel))
                foreach (var n in intel.KnownNests)
                    if (n != _goalNest && Vector2.Distance(n.WorldPosition, a.Position) <= ExplorationRoute.LegLength * 2f + NestNearRadius
                        && DenTooStrongForUs(intel, n))
                        v.Avoid.Add((n.WorldPosition, NestNearRadius));
            if (_avoid is { } spot) v.Avoid.Add((spot, ExplorationRoute.LegLength * 0.6f));
            return v;
        }

        /// <summary>In sight of us or of anyone in the band.</summary>
        bool SeenByBand(Adventurer a, Unit u)
        {
            if (a.CanSee(u)) return true;
            if (_band == null) return false;
            foreach (var member in _band.Members)
                if (member != a && member != null && member.IsAlive && member.CanSee(u)) return true;
            return false;
        }

        /// <summary>How worn the band is (<see cref="FreeHunt.Strain"/>): its time out, its fullest bag and its most hurt.</summary>
        float BandStrain(Adventurer a)
        {
            int loot = a.Data.LootCount;
            float hp = a.HpRatio;
            if (_band != null)
                foreach (var member in _band.Members)
                {
                    if (member == null || !member.IsAlive) continue;
                    loot = Mathf.Max(loot, member.Data.LootCount);
                    hp = Mathf.Min(hp, member.HpRatio);
                }
            return FreeHunt.Strain(AdvCtx.Clock.TotalHours - _startHour, loot, hp);
        }

        /// <summary>
        /// The band walks home together: whatever sends one member back sends everyone back, so
        /// nobody trails home alone and nobody is left hunting on its own (P4-38).
        /// </summary>
        bool ShouldGoHome(Adventurer a)
        {
            if (_band != null && _band.GoingHome) return true;
            if (FreeHunt.ShouldGoHome(AdvCtx.Clock.TotalHours - _startHour, a.Data.LootCount, NewQuestWorthTaking(a), a.HpRatio)) return true;
            var leader = _band?.Leader;
            return leader != null && leader != a && leader.Fsm.Current == leader.ReturnState;
        }

        /// <summary>
        /// A quest posted since this hunt set out that this member would consider taking. Any open quest
        /// used to count (P4-77): one nobody would take — too dangerous, waiting for persuasion — sent every
        /// band home the moment it left the guild, to decide on a hunt again, back and forth all day.
        /// </summary>
        bool NewQuestWorthTaking(Adventurer a)
        {
            foreach (var q in AdvCtx.Board.AvailableFor(a))
                if (q.PostedAtHour >= _startHour && RuleBasedDecisionMaker.Score(a, q) > 0f) return true;
            return false;
        }

        /// <summary>
        /// What the guild knows about here, refreshed once per search: how many of us, how many of
        /// them, whose den we are standing in. The den counts come
        /// from <see cref="GuildIntel"/>, so they are what the whole guild has seen between them —
        /// a member walks in already knowing what a colleague found here (user request).
        /// </summary>
        void Survey(Adventurer a)
        {
            CountBand(a);
            _enemies = UnitRegistry.CountInRadius(a.Position, a.Stats.SightRadius, u => u is Monster m && m.IsAlive && a.CanSee(m)); // only what it sees (P4-48)
            _nestNearby = 0;
            _nestHere = null;
            _crowd.Clear();
            if (!Services.TryGet<GuildIntel>(out var intel)) return;
            foreach (var n in intel.KnownNests)
            {
                if (Vector2.Distance(n.WorldPosition, a.Position) > NestNearRadius) continue;
                int seen = intel.Get(n).LastCount;
                if (_nestHere != null && seen <= _nestNearby) continue;
                _nestNearby = Mathf.Max(_nestNearby, seen);
                _nestHere = n;
            }
        }

        /// <summary>Ourselves first, then every living member within <see cref="FreeHunt.AllyRadius"/> — a newcomer only itself (P4-99).</summary>
        void CountBand(Adventurer a)
        {
            _bandStats.Clear();
            _bandHp.Clear();
            _bandClasses.Clear();
            _bandFor.Clear();
            _bandStats.Add(a.Stats);
            _bandHp.Add(a.Hp);
            _bandClasses.Add(a.Data.cls);
            // A newcomer (P4-99, user decision 2026-10-21) hunts what it can take on its own, close in: no further than where
            // the young are born no stronger than it — not out to the near ring every other hunt reaches.
            if (a.IsNewcomer)
            {
                _reach = a.NewcomerReach;
                return;
            }
            UnitRegistry.Collect(a.Position, FreeHunt.AllyRadius, u => u != a && u is Adventurer m && m.IsGuildMember, AllyBuffer);
            foreach (var u in AllyBuffer)
            {
                _bandStats.Add(u.Stats);
                _bandHp.Add(u.Hp);
                _bandClasses.Add(((Adventurer)u).Data.cls);
            }
            // Judged at full health: how far out the band goes is its strength, not today's scratches
            // (being hurt already sends it home, see FreeHunt.ShouldGoHome).
            _reach = Services.TryGet<EcosystemManager>(out var eco)
                ? FreeHunt.RoamRadius(_bandStats, null, eco.Species, eco.OutermostDistance)
                : FreeHunt.Radius;
        }

        /// <summary>
        /// Leaves before a monster none of us could handle notices us (user decision) — once, when it comes into sight and
        /// the way we are walking leads up to it: the next leg is aimed with it in view (<see cref="LookAround"/>), and walked.
        /// One already on us is not avoided, nor one fighting any of us, and the band does not leave while one of it is in
        /// a fight (P4-121, user report 2026-11-02: "not one for us", said over a member's fight) —
        /// <see cref="Adventurer.HandleCombat"/> fights it and <see cref="Adventurer.CheckRetreat"/> pulls us out if it goes badly.
        /// </summary>
        bool BackOffFromSomethingTooStrong(Adventurer a)
        {
            if (!IsLeader || BandFighting()) return false; // the band turns as one; followers keep formation
            var threat = UnitRegistry.FindNearest(a.Position, a.Stats.SightRadius,
                u => u is Monster m && m.IsAlive && a.CanSee(u) && TooStrongForUs(m) && !FightingOneOfUs(m));
            if (threat == null || a.WasRecentlyAttackedBy(threat)) return false;
            // Just turned from it: the band walks the leg it turned onto (P4-121: it used to turn every survey).
            if (threat == _backedOffFrom && Time.time < _backOffUntil) return false;
            // Walking away from it already.
            if (a.Mover.IsMoving && !LeadsUpTo(a.Position, a.Mover.Destination, threat)) return false;
            _backedOffFrom = threat;
            _backOffUntil = Time.time + BackOffHold;
            // Whatever we were making for lies past it: think again, away from it.
            if (_goal is { } goal && ExplorationRoute.ClosestPass(a.Position, goal, threat.Position) < threat.Stats.SightRadius + AvoidMargin)
                DropGoal(threat.Position);
            AimLeg(a);
            if (Time.time >= _nextBackOffLine)
            {
                _nextBackOffLine = Time.time + BackOffCooldown;
                a.Say(_band != null && _band.Members.Count >= 2 ? "저건 우리가 건드릴 상대가 아니야." : "저건 나 혼자 건드릴 상대가 아니야.");
            }
            return true;
        }

        /// <summary>A walk from <paramref name="from"/> to <paramref name="to"/> takes us nearer it, and within its notice.</summary>
        static bool LeadsUpTo(Vector2 from, Vector2 to, Unit threat)
        {
            float pass = ExplorationRoute.ClosestPass(from, to, threat.Position);
            return pass < Vector2.Distance(from, threat.Position) - 1f && pass < threat.Stats.SightRadius + AvoidMargin;
        }

        /// <summary>A fresh start: no goal, no way taken yet, nothing being kept clear of.</summary>
        void ForgetTheWay()
        {
            _goal = null;
            _goalNest = null;
            _needGoal = true;
            _scoutLegs = 0;
            _heading = Vector2.zero;
            _avoid = null;
            _avoidLegs = 0;
            _backedOffFrom = null;
        }

        /// <summary>Gives up the goal (reached, or no longer worth it); the leader thinks again at the next leg, clear of <paramref name="avoid"/>.</summary>
        void DropGoal(Vector2? avoid = null)
        {
            _goal = null;
            _goalNest = null;
            _needGoal = true;
            _surprises = 0;
            if (avoid == null) return;
            _avoid = avoid;
            _avoidLegs = ReconsiderAfterSurprises;
        }

        /// <summary>
        /// What this leg makes for: the leader's goal, looked at again every leg (P4-121) — the den picked stays the goal only
        /// while the band could still take it as it stands now (hurt, fewer, the den grown back), or the leader thinks
        /// again; while scouting it thinks again every few legs, for a den found on the way. Null while scouting: each
        /// leg is then picked from where the band is. A member out of formation makes for wherever the band is headed.
        /// </summary>
        Vector2? Goal(Adventurer a)
        {
            if (!IsLeader) return _band.HasSpot ? _band.Spot : (Vector2?)null;
            if (_goalNest != null && !(Services.TryGet<GuildIntel>(out var intel)
                                       && WorthGoingTo(intel, _goalNest, a, Services.Get<WorldService>().TownCenterWorld, null, out _)))
                DropGoal();
            if (_needGoal || _goal == null && _scoutLegs >= ScoutLegsPerLook) ChooseGoal(a);
            return _goal;
        }

        /// <summary>
        /// The leader's choice: a known den the band can handle, else a watchtower to climb, else scouting. The den list
        /// comes from <see cref="GuildIntel"/>, so a hunt only heads for what the guild has actually seen — walking into
        /// the fog is scouting, not knowledge.
        /// </summary>
        void ChooseGoal(Adventurer a)
        {
            _needGoal = false;
            _scoutLegs = 0;
            _goal = null;
            _goalNest = null;
            var town = Services.Get<WorldService>().TownCenterWorld;

            // The best known den this band can take — most EXP for the walk (P4-40) — unless it is
            // the one we are standing at (whatever is left there is out of sight) or we go scouting.
            if (!GameRandom.Shared.Chance(FreeHunt.ScoutChance) && Services.TryGet<GuildIntel>(out var intel))
            {
                float bestScore = 0f;
                foreach (var n in intel.KnownNests)
                {
                    if (Vector2.Distance(n.WorldPosition, a.Position) < SpotReached * 2f) continue;
                    if (!WorthGoingTo(intel, n, a, town, _avoid, out float score) || score <= bestScore) continue;
                    bestScore = score;
                    _goalNest = n;
                    _goal = n.WorldPosition;
                }
            }
            _scouting = _goalNest == null;

            // A ruined watchtower the guild knows and nobody has climbed yet (P4-105c): the scouts make for it first — from its
            // top the land round is seen at once.
            if (_scouting && Services.TryGet<Places.PlaceVisits>(out var visits)
                          && visits.TowerToClimb(a.Position, _reach, _avoid, _reach * 0.3f) is { } tower)
                _goal = tower.WorldPosition;
        }

        /// <summary>A known den worth the walk for this band as it stands now, and how much (<see cref="FreeHunt.DenScore"/>).</summary>
        bool WorthGoingTo(GuildIntel intel, Nest n, Adventurer a, Vector2 town, Vector2? avoid, out float score)
        {
            score = 0f;
            int count = ExpectedAt(intel, n);
            if (count <= 0 || Vector2.Distance(n.WorldPosition, town) > _reach) return false;
            if (avoid != null && Vector2.Distance(n.WorldPosition, avoid.Value) <= _reach * 0.3f) return false;
            if (Guild.GuildLaws.KeepToTrails && AreaRisk.KnownDeep(n.WorldPosition)) return false; // the guild's rule keeps hunts out of the deep woods (P4-112)
            if (!CouldTake(intel, n)) return false;
            score = FreeHunt.DenScore(intel.SpeciesOf(n), count, Vector2.Distance(n.WorldPosition, a.Position), n.BirthLevel);
            return score > 0f;
        }
    }
}
