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

        float _searchTimer;
        float _roamTimer;
        float _startHour;
        float _nextBackOffLine;
        float _formationRepath;
        int _enemies;
        int _nestNearby;
        HuntBand _band;
        /// <summary>Species the guild's own count says to leave alone; refreshed on the search tick.</summary>
        readonly System.Collections.Generic.HashSet<MonsterDefinition> _spared = new();
        /// <summary>Us and the members close enough to fight beside us, as the fight estimate sees them.</summary>
        readonly System.Collections.Generic.List<CombatStats> _bandStats = new();
        readonly System.Collections.Generic.List<float> _bandHp = new();
        static readonly System.Collections.Generic.List<Unit> AllyBuffer = new();
        /// <summary>How far out this band roams (P4-38): grows with what it could take on.</summary>
        float _reach = FreeHunt.Radius;

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
            CountBand(Owner);
            _band = Services.TryGet<HuntBandService>(out var bands) ? bands.Join(Owner) : null;
            if (_band != null && _band.Leader == Owner && _band.Members.Count == 1) Owner.Say("사냥 나갈 사람?");
        }

        public override void Exit()
        {
            Owner.Mover.SpeedMultiplier = 1f;
            if (_band != null && !_band.Mustering) _band.GoingHome = true;
            if (Services.TryGet<HuntBandService>(out var bands)) bands.Leave(Owner);
            _band = null;
        }

        bool IsLeader => _band == null || _band.Leader == Owner;

        /// <summary>Worth attacking: one we can handle, of a species the guild is not running short of.</summary>
        bool Huntable(Unit u)
            => u is Monster m && m.IsAlive && m.Def != null && !_spared.Contains(m.Def)
               && FreeHunt.CanTake(_bandStats, _bandHp, m.Stats, FreeHunt.ExpectedEnemies(_enemies, m.Def, _nestNearby));

        /// <summary>What may be fought at all: our own prey, plus anything that has just hit us.</summary>
        bool Allowed(Unit u) => Huntable(u) || Owner.WasRecentlyAttackedBy(u);

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

            if (a.HandleCombat(dt, Huntable, Allowed)) return;
            if (a.Looting.Tick(dt, a.WithinPartyLeash)) return;

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

            var spot = Spot(a);
            if (Vector2.Distance(a.Position, spot) < SpotReached) spot = PickSpot();
            if (a.Mover.IsMoving) return;
            _roamTimer -= dt;
            if (_roamTimer > 0f) return;
            _roamTimer = GameRandom.Shared.Range(1f, 2.5f);
            if (!a.Mover.SetDestination(spot, 1f)) PickSpot();
        }

        /// <summary>
        /// The band walks home together: whatever sends one member back sends everyone back, so
        /// nobody trails home alone and nobody is left hunting on its own (P4-38).
        /// </summary>
        bool ShouldGoHome(Adventurer a)
        {
            if (_band != null && _band.GoingHome) return true;
            bool quest = AdvCtx.Board.AvailableFor(a).Any();
            if (FreeHunt.ShouldGoHome(AdvCtx.Clock.TotalHours - _startHour, a.Data.LootCount, quest, a.HpRatio)) return true;
            var leader = _band?.Leader;
            return leader != null && leader != a && leader.Fsm.Current == leader.ReturnState;
        }

        /// <summary>
        /// What the guild knows about here, refreshed once per search: how many of us, how many of
        /// them, whose den we are standing in, and which species to leave alone. The den counts come
        /// from <see cref="GuildIntel"/>, so they are what the whole guild has seen between them —
        /// a member walks in already knowing what a colleague found here (user request).
        /// </summary>
        void Survey(Adventurer a)
        {
            CountBand(a);
            _enemies = UnitRegistry.CountInRadius(a.Position, a.Stats.SightRadius, u => u is Monster m && m.IsAlive);
            _nestNearby = 0;
            _spared.Clear();
            if (!Services.TryGet<GuildIntel>(out var intel) || !Services.TryGet<EcosystemManager>(out var eco)) return;
            foreach (var s in eco.Species)
                if (FreeHunt.Spare(EcologyWarning.Status(s, intel.ObservedPopulation(s), intel.KnownNestCount(s))))
                    _spared.Add(s);
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
                ? FreeHunt.RoamRadius(_bandStats, null, eco.Species)
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
            a.Mover.SetDestination(PickSpot(threat.Position), 1f);
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

            bool found = false;
            if (rng.Chance(0.6f) && Services.TryGet<GuildIntel>(out var intel))
            {
                var dens = intel.KnownNests
                    .Where(n => Vector2.Distance(n.WorldPosition, town) <= _reach
                                && intel.SpeciesOf(n) != null
                                && !_spared.Contains(intel.SpeciesOf(n))
                                && intel.Get(n).LastCount > 0
                                && FreeHunt.CanTake(_bandStats, _bandHp, FreeHunt.StatsOf(intel.SpeciesOf(n)),
                                       FreeHunt.ExpectedEnemies(1, intel.SpeciesOf(n), intel.Get(n).LastCount))
                                && (avoid == null || Vector2.Distance(n.WorldPosition, avoid.Value) > _reach * 0.3f))
                    .ToList();
                if (dens.Count > 0)
                {
                    chosen = dens[rng.Range(0, dens.Count)].WorldPosition;
                    found = true;
                }
            }

            if (!found)
            {
                var centre = GridMap.WorldToCell(town);
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    if (!world.Grid.TryRandomWalkableNear(centre, Mathf.RoundToInt(_reach), rng, out var cell)) continue;
                    if (world.Grid.IsInTown(cell)) continue;
                    var p = GridMap.CellToWorld(cell);
                    if (avoid != null && Vector2.Distance(p, avoid.Value) < _reach * 0.3f) continue;
                    chosen = p;
                    break;
                }
            }

            if (_band != null)
            {
                _band.Spot = chosen;
                _band.HasSpot = true;
            }
            return chosen;
        }
    }
}
