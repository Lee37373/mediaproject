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
    /// allows and bring the loot home. Anything out of their league is left alone and walked away
    /// from — but whatever hits them first is still fought.
    /// </summary>
    public class FreeHuntState : State<Adventurer>
    {
        /// <summary>Close enough to the roam spot to pick the next one.</summary>
        const float SpotReached = 2.5f;
        const float SearchInterval = 0.6f;
        /// <summary>Seconds between "too strong" remarks, so backing off is not a chatterbox.</summary>
        const float BackOffCooldown = 20f;
        /// <summary>A known den this close counts as "we are standing in their territory".</summary>
        const float NestNearRadius = 12f;

        /// <summary>Surprises on the way to one spot before the band gives up on it and picks another.</summary>
        const int ReconsiderAfterSurprises = 3;

        float _searchTimer;
        float _roamTimer;
        float _startHour;
        float _nextBackOffLine;
        float _formationRepath;
        float _campCheck;
        int _enemies;
        int _nestNearby;
        HuntBand _band;
        /// <summary>Us and the members close enough to fight beside us, as the fight estimate sees them.</summary>
        readonly System.Collections.Generic.List<CombatStats> _bandStats = new();
        readonly System.Collections.Generic.List<float> _bandHp = new();
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
            _surprises = 0;
            LegsWalked = 0;
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
        bool Huntable(Unit u)
            => u is Monster m && m.IsAlive && m.Def != null
               && FreeHunt.CanTake(_bandStats, _bandHp, m.Stats, FreeHunt.ExpectedEnemies(_enemies, m.Def, _nestNearby));

        /// <summary>What the guild expects at a den now: its last count, grown back since (P4-42).</summary>
        static int ExpectedAt(GuildIntel intel, Nest n)
        {
            var rec = intel.Get(n);
            float since = Services.TryGet<GameClock>(out var clock) ? clock.TotalHours - rec.LastSeenHour : 0f;
            // Far sites breed slower (a public rule of the world, not hidden state).
            float growth = Services.TryGet<EcosystemManager>(out var eco) ? eco.GrowthFactorAt(n.WorldPosition) : 1f;
            return FreeHunt.ExpectedDenCount(intel.SpeciesOf(n), rec.LastCount, since, growth);
        }

        /// <summary>What may be fought at all: our own prey, plus anything that has just hit us.</summary>
        bool Allowed(Unit u) => Huntable(u) || Owner.WasRecentlyAttackedBy(u);

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

            // Waiting at the gate for company (user request: hunt as a party).
            if (_band != null && _band.Mustering)
            {
                if (!a.Mover.IsMoving && Vector2.Distance(a.Position, AdvCtx.GuildDoor) > 2f)
                    a.Mover.SetDestination(AdvCtx.GuildDoor, 1.5f);
                return;
            }

            if (a.HandleCombat(dt, _huntable ??= Huntable, _allowed ??= Allowed)) return;
            if (a.Looting.Tick(dt, a.WithinPartyLeash)) return;
            // Night out on a hunt: the band may make camp, then head home in the morning (P4-66).
            if (AdvCtx.TryNightCamp(a, ref _campCheck, dt)) return;

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
            // about 18 cells towards its spot, reads the ground it can see, and aims again — and
            // whenever the terrain it discovers forces the walk to redraw its route, it re-aims at
            // once instead of trudging on along a plan made of guesses.
            var spot = Spot(a);
            if (Reached(a, spot)) { spot = PickSpot(); _surprises = 0; }
            bool surprised = a.Mover.RouteCorrections != _correctionsAtAim;
            if (a.Mover.IsMoving && !surprised) return;
            _roamTimer -= dt;
            if (_roamTimer > 0f) return;
            _roamTimer = GameRandom.Shared.Range(1f, 2.5f);
            if (surprised && ++_surprises >= ReconsiderAfterSurprises)
            {
                // The way there keeps turning out worse than it looked: somewhere else, then.
                spot = PickSpot(spot);
                _surprises = 0;
            }
            AimLeg(a, spot);
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

        /// <summary>
        /// Walks the next leg towards <paramref name="spot"/> (P4-64). A leg that cannot be reached
        /// even by guesswork counts as a surprise, so the band tries another heading and eventually
        /// another spot.
        /// </summary>
        void AimLeg(Adventurer a, Vector2 spot)
        {
            var world = Services.Get<WorldService>();
            float caution = RouteChoice.ForParty(a);
            _leg = ExplorationRoute.NextLeg(world, a.Mover.UnknownToMe, a.Position, spot, caution, _scouting);
            _correctionsAtAim = a.Mover.RouteCorrections;
            // The band walks the leader's route (P4-63): its nerve decides how wide of the woods it goes.
            if (a.Mover.SetDestination(_leg, 1f, caution)) { LegsWalked++; return; }
            // Somewhere else, not the same den again (P4-80: the best den was picked straight back,
            // and the band tried the same unwalkable leg until a scouting roll took it elsewhere).
            if (++_surprises >= ReconsiderAfterSurprises)
            {
                PickSpot(spot);
                _surprises = 0;
            }
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
            if (!Services.TryGet<GuildIntel>(out var intel)) return;
            foreach (var n in intel.KnownNests)
                if (Vector2.Distance(n.WorldPosition, a.Position) <= NestNearRadius)
                    _nestNearby = Mathf.Max(_nestNearby, intel.Get(n).LastCount);
        }

        /// <summary>Ourselves first, then every living member within <see cref="FreeHunt.AllyRadius"/>.</summary>
        void CountBand(Adventurer a)
        {
            _bandStats.Clear();
            _bandHp.Clear();
            _bandStats.Add(a.Stats);
            _bandHp.Add(a.Hp);
            UnitRegistry.Collect(a.Position, FreeHunt.AllyRadius, u => u != a && u is Adventurer m && m.IsGuildMember, AllyBuffer);
            foreach (var u in AllyBuffer)
            {
                _bandStats.Add(u.Stats);
                _bandHp.Add(u.Hp);
            }
            // Judged at full health: how far out the band goes is its strength, not today's scratches
            // (being hurt already sends it home, see FreeHunt.ShouldGoHome).
            _reach = Services.TryGet<EcosystemManager>(out var eco)
                ? FreeHunt.RoamRadius(_bandStats, null, eco.Species, eco.OutermostDistance)
                : FreeHunt.Radius;
        }

        /// <summary>
        /// Leaves before a monster we cannot handle notices us (user decision). One that is already
        /// on us is not avoided — <see cref="Adventurer.HandleCombat"/> fights it and
        /// <see cref="Adventurer.CheckRetreat"/> pulls us out if it goes badly.
        /// </summary>
        bool BackOffFromSomethingTooStrong(Adventurer a)
        {
            if (!IsLeader) return false; // the band turns as one; followers keep formation
            var threat = UnitRegistry.FindNearest(a.Position, a.Stats.SightRadius,
                u => u is Monster m && m.IsAlive && !Huntable(m) && a.CanSee(u));
            if (threat == null || a.WasRecentlyAttackedBy(threat)) return false;
            AimLeg(a, PickSpot(threat.Position));
            if (Time.time >= _nextBackOffLine)
            {
                _nextBackOffLine = Time.time + BackOffCooldown;
                a.Say("저건 우리가 건드릴 상대가 아니야.");
            }
            return true;
        }

        /// <summary>Where the band is headed (the leader's choice), or our own when hunting alone.</summary>
        Vector2 Spot(Adventurer a)
        {
            if (_band != null && _band.HasSpot) return _band.Spot;
            return PickSpot();
        }

        /// <summary>
        /// Next place to walk: a known den the band can handle, else open country. The den list comes
        /// from <see cref="GuildIntel"/>, so a hunt only heads for what the guild has actually seen —
        /// walking into the fog is scouting, not knowledge. Followers never choose; the leader does.
        /// </summary>
        Vector2 PickSpot(Vector2? avoid = null)
        {
            if (!IsLeader && _band.HasSpot) return _band.Spot;

            var a = Owner;
            var world = Services.Get<WorldService>();
            var town = world.TownCenterWorld;
            var rng = GameRandom.Shared;
            var chosen = town;

            // The best known den this band can take — most EXP for the walk (P4-40) — unless it is
            // the one we are standing at (whatever is left there is out of sight) or we go scouting.
            bool found = false;
            if (!rng.Chance(FreeHunt.ScoutChance) && Services.TryGet<GuildIntel>(out var intel))
            {
                float bestScore = 0f;
                foreach (var n in intel.KnownNests)
                {
                    var s = intel.SpeciesOf(n);
                    int count = ExpectedAt(intel, n);
                    if (s == null || count <= 0) continue;
                    // A region lord's lair is gone to on its quest, not on a whim (P4-83): it is far beyond a den's danger.
                    if (intel.KnownLordAt(n) != null) continue;
                    if (Vector2.Distance(n.WorldPosition, town) > _reach) continue;
                    if (Vector2.Distance(n.WorldPosition, a.Position) < SpotReached * 2f) continue;
                    if (avoid != null && Vector2.Distance(n.WorldPosition, avoid.Value) <= _reach * 0.3f) continue;
                    if (!FreeHunt.CanTake(_bandStats, _bandHp, FreeHunt.StatsOf(s, n.BirthLevel), FreeHunt.ExpectedEnemies(1, s, count))) continue;
                    float score = FreeHunt.DenScore(s, count, Vector2.Distance(n.WorldPosition, a.Position), n.BirthLevel);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    chosen = n.WorldPosition;
                    found = true;
                }
            }

            if (!found)
            {
                // Scouting: uniform in distance from town, so the near rings get walked too, and
                // among the tries the one with the least explored ground around it (P4-64) — the
                // point of a scouting leg is to put eyes on country nobody has seen.
                var fog = Services.TryGet<Fog.FogOfWar>(out var f) ? f : null;
                float bestUnseen = -1f;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    float angle = rng.Range(0f, Mathf.PI * 2f);
                    var p = town + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * FreeHunt.ScoutDistance(rng.Value, _reach);
                    // P4-81: a reach into the outer land can be wider than the map is from the town.
                    p = new Vector2(Mathf.Clamp(p.x, 2f, world.Grid.Width - 3f), Mathf.Clamp(p.y, 2f, world.Map.SurfaceHeight - 3f));
                    var cell0 = GridMap.WorldToCell(p);
                    // An unseen cell is a fine place to walk to; only known-blocked ground is moved off.
                    if (!ExplorationRoute.Unseen(fog, cell0))
                    {
                        if (!world.Grid.TryFindNearestWalkable(cell0, 3, out cell0)) continue;
                        p = GridMap.CellToWorld(cell0);
                    }
                    // Not fog the ground seen round it walls in, though (P4-80).
                    if (ExplorationRoute.Unseen(fog, cell0) && !ExplorationRoute.OpensOut(world.Grid, a.Mover.UnknownToMe, cell0)) continue;
                    if (world.Grid.IsInTown(cell0)) continue;
                    if (avoid != null && Vector2.Distance(p, avoid.Value) < _reach * 0.3f) continue;
                    float unseen = fog == null ? 0f : 1f - fog.ExploredFraction(p, 10f);
                    if (unseen <= bestUnseen) continue;
                    bestUnseen = unseen;
                    chosen = p;
                    if (unseen >= 0.99f) break; // nothing there is known: no need to look further
                }
            }
            _scouting = !found;

            if (_band != null)
            {
                _band.Spot = chosen;
                _band.HasSpot = true;
            }
            return chosen;
        }
    }
}
