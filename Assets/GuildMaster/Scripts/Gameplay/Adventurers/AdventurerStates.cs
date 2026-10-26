using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Economy;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using GuildMaster.Town;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>Helpers shared by adventurer states.</summary>
    static class AdvCtx
    {
        public static QuestBoard Board => Services.Get<QuestBoard>();
        public static TownService Town => Services.Get<TownService>();
        public static GameClock Clock => Services.Get<GameClock>();

        public static Vector2 GuildDoor => Town.Get(BuildingType.GuildHall).DoorPosition;

        /// <summary>Where members around town pick things up: inside the walls and at the gates (P4-40).</summary>
        public static bool TownLootZone(Vector2 p)
            => Services.TryGet<GateService>(out var gates) ? gates.InTownOrApron(p) : Town.IsInTown(p);

        /// <summary>Checks the current quest; returns false (and switches state) when the adventurer should go back.</summary>
        public static bool QuestStillValid(Adventurer a)
        {
            var q = a.CurrentQuest;
            if (q == null) { a.ChangeState(a.ReturnState); return false; }
            if (q.Status == QuestStatus.Completed) { a.Say("의뢰 완료! 보고하러 가자."); a.ChangeState(a.ReturnState); return false; }
            if (!q.IsActive || !q.IsAssigned(a))
            {
                a.CurrentQuest = null;
                a.ChangeState(a.ReturnState);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Where to go for a quest: the hunting point the player marked (or the disaster's spot), on
        /// ground one can stand on. Adventurers never head for monsters nobody has seen (user bug report).
        /// A point in a pond, on a rock or under snow is moved to the nearest walkable cell, as the path
        /// search does (P4-73: the walker stopped up to six cells short and never "arrived" within three).
        /// </summary>
        public static Vector2 QuestDestination(Adventurer a, Quest q)
        {
            var grid = Services.Get<WorldService>().Grid;
            var cell = GridMap.WorldToCell(q.Point);
            if (Standable(grid, cell)) return q.Point;
            // Also off snow and cracks (P4-72): they are walls one only crosses with no way round.
            for (int r = 1; r <= 6; r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                var c = new Vector2Int(cell.x + dx, cell.y + dy);
                if (Standable(grid, c)) return GridMap.CellToWorld(c);
            }
            return q.Point;
        }

        static bool Standable(GridMap grid, Vector2Int c) => grid.IsWalkable(c) && !Hazards.Blocks(grid.HazardAt(c));

        /// <summary>Seconds between the checks for making camp (P4-66).</summary>
        public const float CampCheckInterval = 0.5f;

        /// <summary>Out on the road at night: the band may stop and make camp (P4-66, the leader decides).</summary>
        public static bool TryNightCamp(Adventurer a, ref float timer, float dt)
        {
            timer -= dt;
            if (timer > 0f) return false;
            timer = CampCheckInterval;
            // Not down in a ruin (P4-85): the night outside does not reach there.
            if (Services.Get<WorldService>().Map.IsUnderground(GridMap.WorldToCell(a.Position))) return false;
            return Services.TryGet<CampService>(out var camps) && camps.TryNightCamp(a);
        }

        public static void Wander(Adventurer a, ref float timer, float dt)
        {
            if (a.Mover.IsMoving) return;
            timer -= dt;
            if (timer > 0f) return;
            timer = GameRandom.Shared.Range(1.5f, 4f);
            a.Mover.SetDestination(Town.RandomPlazaSpot(GameRandom.Shared, 6));
        }
    }

    // ------------------------------------------------------------------ Town

    public class TownIdleState : State<Adventurer>
    {
        const float DecisionIntervalHours = 1.5f;
        float _decisionTimer;
        float _guardTimer;
        float _wanderTimer;

        public override string Label => "마을 대기";

        public override void Enter()
        {
            _decisionTimer = AdvCtx.Clock.HoursToSeconds(DecisionIntervalHours * GameRandom.Shared.Range(0.4f, 1.1f));
            _wanderTimer = GameRandom.Shared.Range(0f, 2f);
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            var q = a.CurrentQuest;
            if (q != null)
            {
                if (q.Status == QuestStatus.Completed) { a.ChangeState(a.VisitState); return; }
                if (q.IsActive && q.IsAssigned(a)) { a.ChangeState(a.TravelState); return; }
                a.CurrentQuest = null;
            }

            if (TownGuardState.TryStart(a, this, ref _guardTimer, dt)) return;
            if (a.Looting.Tick(dt, AdvCtx.TownLootZone)) return;
            AdvCtx.Wander(a, ref _wanderTimer, dt);
            _decisionTimer -= dt;
            if (_decisionTimer > 0f) return;
            // A pending party invitation is this member's decision for now.
            if (Services.TryGet<PartyService>(out var party) && party.IsInvited(a)) { _decisionTimer = 2f; return; }
            // A standing party's members go where their leader takes them (P4-50) — but one its leader does not take out
            // hunting (P4-99: a newcomer, or one that outgrew a newcomer leader) decides on its own.
            // The leader too while the last outing is not settled yet (the others are still coming back),
            // and while one of them is still resting or at the counter (P4-79: they go out together).
            if (Services.TryGet<GuildParties>(out var parties)
                && (parties.FollowsALeader(a) && HuntBandService.SameFooting(a, parties.PartyOf(a).Leader) && !a.IsNewcomer
                    || parties.OutingUnfinished(a) || parties.MembersGettingReady(a))) { _decisionTimer = 2f; return; }
            a.ChangeState(a.DecidingState);
        }
    }

    /// <summary>Waits for an asynchronous (possibly LLM) decision while idling.</summary>
    public class DecidingState : State<Adventurer>
    {
        /// <summary>Real seconds a decision may take before the rules decide anyway (a decision maker that never answers).</summary>
        const float TimeoutRealSeconds = 70f;

        Task<AdventurerDecision> _task;
        CancellationTokenSource _cts;
        float _wanderTimer;
        float _startRealTime;
        float _guardTimer;

        public override string Label => "고민 중";

        public override void Enter()
        {
            var a = Owner;
            var available = AdvCtx.Board.AvailableFor(a).ToList();
            _cts = new CancellationTokenSource();
            _wanderTimer = 1f;
            _startRealTime = Time.unscaledTime;
            if (Services.TryGet<IAdventurerDecisionMaker>(out var maker))
            {
                try { _task = maker.DecideAsync(a, available, _cts.Token); }
                catch (System.Exception e) { Debug.LogException(e); _task = null; }
            }
            _task ??= Task.FromResult(RuleBasedDecisionMaker.Decide(a, available));
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (TownGuardState.TryStart(a, a.IdleState, ref _guardTimer, dt)) return;
            AdvCtx.Wander(a, ref _wanderTimer, dt);

            if (_task.IsCompleted)
            {
                var decision = _task.Status == TaskStatus.RanToCompletion
                    ? _task.Result
                    : RuleBasedDecisionMaker.Decide(a, AdvCtx.Board.AvailableFor(a).ToList());
                Apply(decision);
            }
            else if (Time.unscaledTime - _startRealTime > TimeoutRealSeconds)
            {
                _cts.Cancel();
                Apply(RuleBasedDecisionMaker.Decide(a, AdvCtx.Board.AvailableFor(a).ToList()));
            }
        }

        void Apply(AdventurerDecision d)
        {
            var a = Owner;
            if (!string.IsNullOrEmpty(d.Reason))
            {
                a.LastThought = d.Reason;
            }
            switch (d.Action)
            {
                case AdventurerAction.AcceptQuest:
                    var q = AdvCtx.Board.FindById(d.QuestId);
                    // Still on it after the accept: party work nobody could be asked along for is dropped at once (P4-85).
                    if (q != null && AdvCtx.Board.TryAccept(q, a) && q.IsAssigned(a))
                    {
                        // A big hunt taken counting on company that all said no (P4-100): put down, not walked into alone.
                        if (Services.TryGet<PartyService>(out var asked) && asked.PutDownIfAlone(q, a)) return;
                        a.CurrentQuest = q;
                        a.Say(d.Say);
                        a.ChangeState(a.TravelState);
                        return;
                    }
                    // Not on it after all (taken, or put down at once — P4-100): no "I'll take it!" after the leader's "too few".
                    a.ChangeState(a.IdleState);
                    return;
                case AdventurerAction.Rest:
                    a.Say(d.Say);
                    a.ChangeState(a.RestState);
                    return;
                case AdventurerAction.Shop:
                    a.Say(d.Say);
                    a.ChangeState(a.VisitState);
                    return;
                case AdventurerAction.Hunt:
                    a.Say(d.Say);
                    a.ChangeState(a.FreeHuntState);
                    return;
                case AdventurerAction.Train:
                    a.Say(d.Say);
                    a.ChangeState(a.TrainState);
                    return;
                case AdventurerAction.Visit when d.Place != null:
                    a.Say(d.Say);
                    a.PlaceVisitState.Go(a, d.Place);
                    a.ChangeState(a.PlaceVisitState);
                    return;
            }
            if (!string.IsNullOrEmpty(d.Say)) a.Say(d.Say);
            a.ChangeState(a.IdleState);
        }

        public override void Exit()
        {
            if (_task != null && !_task.IsCompleted) _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }

    // ------------------------------------------------------------------ Quest execution

    public class TravelToQuestState : State<Adventurer>
    {
        Vector2 _destination;

        public override string Label => "이동 중";

        public override void Enter()
        {
            var a = Owner;
            if (a.CurrentQuest == null) { a.ChangeState(a.ReturnState); return; }
            // Wait for party invitations to be answered, then leave together.
            if (Services.TryGet<PartyService>(out var party) && party.IsGathering(a.CurrentQuest)) { a.ChangeState(a.GatherState); return; }
            // Asking closed the gathering, and too few came along (P4-85): the quest went back on the board and the
            // leader is idle again (it threw here on the next line, seen in a 10-day run with 35 members).
            if (a.CurrentQuest == null) return;
            // Escorts walk to (and keep up with) the guild master themselves.
            if (a.CurrentQuest.Type == QuestType.Escort) { a.ChangeState(a.EscortState); return; }
            _destination = AdvCtx.QuestDestination(a, a.CurrentQuest);
            // The party walks its leader's route (P4-63): how far round the woods it goes is the
            // leader's nerve. Running home or answering a raid is a hurry, and takes no detour.
            _caution = RouteChoice.ForParty(a);
            if (!a.Mover.SetDestination(_destination, 1.5f, _caution))
            {
                a.Say("거기엔 갈 수가 없어...");
                AdvCtx.Board.Abandon(a.CurrentQuest, a);
                a.CurrentQuest = null;
                a.ChangeState(a.ReturnState);
            }
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (!AdvCtx.QuestStillValid(a) || a.CheckRetreat()) return;
            if (a.HandleCombat(dt)) return;
            if (a.Looting.Tick(dt, a.WithinPartyLeash)) return;
            if (AdvCtx.TryNightCamp(a, ref _campCheck, dt)) return;

            if (Vector2.Distance(a.Position, _destination) < 3f)
            {
                a.ChangeState(a.HuntState);
                return;
            }
            // Parties travel in formation (P4-27): followers keep their slot, the leader sets the pace.
            if (Services.TryGet<PartyTactics>(out var party))
            {
                if (party.Follow(a, ref _formationRepath, dt, out var leader))
                {
                    if (leader.Fsm.Current == leader.HuntState) a.ChangeState(a.HuntState);
                    return;
                }
                if (party.PaceLeader(a)) return;
            }
            if (!a.Mover.IsMoving) a.Mover.SetDestination(_destination, 1.5f, _caution);
        }

        float _formationRepath;
        float _caution;
        float _campCheck;

        public override void Exit() => Owner.Mover.SpeedMultiplier = 1f;
    }

    /// <summary>
    /// Works the quest area: for hunts the spot the player marked on the map, for nest clearing the
    /// nest. Patrols inside it and fights quest targets that come into view there (information rule:
    /// nothing is known about monsters the guild has not seen).
    /// </summary>
    public class HuntState : State<Adventurer>
    {
        /// <summary>Nest clearing roams this close to the nest.</summary>
        const float NestRoamRadius = 6f;
        /// <summary>Hunt targets a little outside the area (seen from its edge) still count.</summary>
        const float AreaSlack = 3f;

        float _searchTimer;
        float _roamTimer;
        float _formationRepath;
        float _campCheck;

        public override string Label => Owner != null && Owner.CurrentQuest is { Type: QuestType.Treasure } ? "보물 털기"
                                      : Owner != null && Owner.CurrentQuest is { Type: QuestType.Disaster } ? "재해 대응" : "사냥 중";

        public override void Enter()
        {
            _searchTimer = 0f;
            _roamTimer = 0f;
            _workTimer = 0f;
        }

        public override void Exit() => Owner.Mover.SpeedMultiplier = 1f;

        Vector2 Area => Owner.CurrentQuest.Point;
        float AreaRadius => Owner.CurrentQuest.Type == QuestType.Hunt ? Owner.CurrentQuest.Radius : NestRoamRadius;

        /// <summary>
        /// Heroic drive hunt (P4-18), once per quest: after hitting a target that has at least two
        /// friends nearby, run the pack past a known nest of another species and let them brawl there.
        /// </summary>
        bool TryDrive(Adventurer a)
        {
            if (a.Tier < CombatTier.Heroic || a.DroveForQuest == a.CurrentQuest) return false;
            var target = a.Engage.Target;
            if (target == null || !target.WasRecentlyAttackedBy(a, 2f)) return false;
            int pack = UnitRegistry.CountInRadius(target.Position, 5f, IsQuestTarget);
            if (pack < 3) return false;
            a.DroveForQuest = a.CurrentQuest;
            // Keep running away from the pack; the waypoint clips another species' territory on the way.
            var away = a.Position + (a.Position - target.Position).normalized * 8f;
            if (!TerritoryTactics.FindLurePoint(a, target, away, true, out var point, out _)) return false;
            a.LureState.Begin(point, this, "따라와 봐라!");
            a.ChangeState(a.LureState);
            return true;
        }

        bool IsQuestTarget(Unit u)
        {
            var q = Owner.CurrentQuest;
            if (q == null || u is not Monster m) return false;
            // A hunt for particular monsters (a rare variant, a horde, a ruin's guardians, P4-85): those only.
            return q.Fights(m);
        }

        System.Func<Unit, bool> _isAreaTarget; // made once (P5-2)

        /// <summary>A quest target inside the quest area (hunts), or any resident of the nest (clearing).</summary>
        bool IsAreaTarget(Unit u)
        {
            if (!IsQuestTarget(u)) return false;
            var q = Owner.CurrentQuest;
            // Particular monsters are fought wherever they show up (they do not keep to the rumour's circle).
            return q.Type != QuestType.Hunt || q.HuntsParticularMonsters || Vector2.Distance(u.Position, q.Point) <= q.Radius + AreaSlack;
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (!AdvCtx.QuestStillValid(a) || a.CheckRetreat()) return;
            if (TryDrive(a)) return;
            if (a.HandleCombat(dt, _isAreaTarget ??= IsAreaTarget)) return;
            // Loot left by the kills (P4-21) before searching on.
            if (a.Looting.Tick(dt, a.WithinPartyLeash)) return;
            if (AdvCtx.TryNightCamp(a, ref _campCheck, dt)) return;
            if (a.CurrentQuest.Type == QuestType.Disaster)
            {
                WorkTheDisaster(a, dt);
                return;
            }
            // The dragon's hoard (P4-93): to the pile, a hand in it, and home (asleep, it may wake).
            if (a.CurrentQuest.Type == QuestType.Treasure)
            {
                RaidTheHoard(a, dt);
                return;
            }
            // A ruin whose way down the guild has found (P4-85): down and through the halls to the treasure.
            if (a.CurrentQuest.Incident is { Kind: Incidents.IncidentKind.Ruin, Sighted: true } ruin)
            {
                ExploreTheRuin(a, ruin, dt);
                return;
            }

            _searchTimer -= dt;
            bool searched = _searchTimer <= 0f;
            if (searched)
            {
                _searchTimer = 0.6f;
                // Only what this adventurer can actually see.
                // Party members hunt near their leader (P4-38).
                var leash = a.PartyLeashCentre();
                var target = UnitRegistry.FindNearest(a.Position, a.Stats.SightRadius,
                    u => IsAreaTarget(u) && !a.Engage.IsIgnoring(u) && a.CanSee(u) && PartyTactics.InLeash(leash, u.Position));
                if (target != null)
                {
                    a.Engage.SetTarget(target);
                    return;
                }
            }

            // Parties patrol together (P4-27): followers stay in formation around the leader.
            if (Services.TryGet<PartyTactics>(out var party))
            {
                if (party.Follow(a, ref _formationRepath, dt, out _)) return;
                if (party.PaceLeader(a)) return;
            }

            // Drifted out (a chase, a lure): walk back into the area.
            if (searched && Vector2.Distance(a.Position, Area) > AreaRadius + 2f
                && (!a.Mover.IsMoving || Vector2.Distance(a.Mover.Destination, Area) > AreaRadius))
            {
                a.Mover.SetDestination(Area, 1.5f);
                return;
            }

            // Nothing in sight: patrol random spots inside the area.
            if (a.Mover.IsMoving) return;
            _roamTimer -= dt;
            if (_roamTimer > 0f) return;
            _roamTimer = GameRandom.Shared.Range(1f, 3f);
            var world = Services.Get<WorldService>();
            if (world.Grid.TryRandomWalkableNear(GridMap.WorldToCell(Area), Mathf.RoundToInt(AreaRadius), GameRandom.Shared, out var cell))
                a.Mover.SetDestination(GridMap.CellToWorld(cell));
        }

        // ---------------- Ruins (P4-85) ----------------

        float _chestTry;

        /// <summary>
        /// The party goes down the ruin's way (the route steps through it, <see cref="WorldService.FindPath"/>) and walks to
        /// the treasure room, fighting the guardians it meets on the way (above); the leader opens the chest once none is
        /// close to it. Party members follow their leader.
        /// </summary>
        void ExploreTheRuin(Adventurer a, Incidents.Incident ruin, float dt)
        {
            if (Services.TryGet<PartyTactics>(out var party))
            {
                if (party.Follow(a, ref _formationRepath, dt, out _)) return;
                if (party.PaceLeader(a)) return;
            }
            if (!Services.TryGet<Incidents.IncidentService>(out var incidents)) return;
            var chest = incidents.ChestOf(ruin);
            if (Vector2.Distance(a.Position, chest) <= Incidents.IncidentService.ChestReach)
            {
                if (a.Mover.IsMoving) a.Mover.Stop();
                _chestTry -= dt;
                if (_chestTry > 0f) return;
                _chestTry = 0.5f;
                if (incidents.TryOpenChest(ruin, a)) a.Say("보물이다! 가져가자.");
                return;
            }
            if (!a.Mover.IsMoving || Vector2.Distance(a.Mover.Destination, chest) > 0.5f)
                a.Mover.SetDestination(chest, 0.8f);
        }

        // ---------------- The dragon's hoard (P4-93) ----------------

        float _hoardTry;

        /// <summary>The party walks to the lair (followers keep formation); whoever reaches the pile first takes from it.</summary>
        void RaidTheHoard(Adventurer a, float dt)
        {
            if (Services.TryGet<PartyTactics>(out var party))
            {
                if (party.Follow(a, ref _formationRepath, dt, out _)) return;
                if (party.PaceLeader(a)) return;
            }
            if (!Services.TryGet<Dragons.DragonService>(out var dragon) || !dragon.HasLair) return;
            var lair = dragon.Lair;
            if (Vector2.Distance(a.Position, lair) <= Monsters.Dragon.TakeReach)
            {
                if (a.Mover.IsMoving) a.Mover.Stop();
                _hoardTry -= dt;
                if (_hoardTry > 0f) return;
                _hoardTry = 0.5f;
                if (dragon.Take(a)) a.Say("드래곤의 보물이다! 어서 챙겨!");
                return;
            }
            if (!a.Mover.IsMoving || Vector2.Distance(a.Mover.Destination, lair) > 0.8f)
                a.Mover.SetDestination(lair, Monsters.Dragon.TakeReach * 0.6f);
        }

        // ---------------- Natural disasters (P4-72) ----------------

        float _workTimer;
        Vector2Int _workCell;
        Vector2 _workSpot;
        bool _hasWork;

        /// <summary>
        /// Deals with the quest's disaster: walks up to the nearest burning, poisoned, buried or split
        /// cell (or the core) and works it until it is gone, then the next. Whatever attacks meanwhile
        /// is fought first (above). The quest ends when the disaster does.
        /// </summary>
        void WorkTheDisaster(Adventurer a, float dt)
        {
            if (!Services.TryGet<Disasters.DisasterService>(out var disasters)) return;
            var d = a.CurrentQuest.Disaster;
            _workTimer -= dt;
            if (_workTimer <= 0f || !_hasWork)
            {
                _workTimer = 0.5f;
                _hasWork = disasters.NextWork(d, a.Position, out _workCell, out var target) && WorkSpot(a, _workCell, target, out _workSpot);
            }
            if (!_hasWork)
            {
                // Nothing left to do here (it is ending): wait by the spot for the town to call it done.
                if (!a.Mover.IsMoving && Vector2.Distance(a.Position, Area) > 3f) a.Mover.SetDestination(Area, 2f);
                return;
            }
            float reach = Vector2.Distance(a.Position, GridMap.CellToWorld(_workCell));
            if (reach > Disasters.DisasterRules.WorkReach)
            {
                if (!a.Mover.IsMoving || Vector2.Distance(a.Mover.Destination, _workSpot) > 0.5f)
                    if (!a.Mover.SetDestination(_workSpot, 0.2f)) _hasWork = false;
                return;
            }
            if (a.Mover.IsMoving) a.Mover.Stop();
            a.Visual?.FaceTowards(GridMap.CellToWorld(_workCell));
            float hours = dt / Mathf.Max(0.01f, AdvCtx.Clock.RealSecondsPerGameHour);
            if (disasters.Work(d, _workCell, hours, a))
            {
                _hasWork = false;
                if (GameRandom.Shared.Chance(0.25f)) a.Say(WorkLine(d.Kind));
            }
        }

        /// <summary>
        /// Where to stand to work a cell: next to it on ground that is walkable and not burning or
        /// poisoned when there is such a place, else the nearest walkable cell (a core in the poison
        /// has to be worked from inside it).
        /// </summary>
        static bool WorkSpot(Adventurer a, Vector2Int cell, Vector2 target, out Vector2 spot)
        {
            var grid = Services.Get<WorldService>().Grid;
            spot = target;
            float best = float.MaxValue;
            bool found = false;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var c = new Vector2Int(cell.x + dx, cell.y + dy);
                if (!grid.IsWalkable(c) || Hazards.Hurts(grid.HazardAt(c)) || Hazards.Blocks(grid.HazardAt(c))) continue;
                float dist = (GridMap.CellToWorld(c) - a.Position).sqrMagnitude;
                if (dist >= best) continue;
                best = dist;
                spot = GridMap.CellToWorld(c);
                found = true;
            }
            if (found) return true;
            if (!grid.TryFindNearestWalkable(cell, 2, out var near)) return false;
            spot = GridMap.CellToWorld(near);
            return true;
        }

        static string WorkLine(Disasters.DisasterKind k) => k switch
        {
            Disasters.DisasterKind.Wildfire => "불길 하나 잡았다!",
            Disasters.DisasterKind.Avalanche => "여기 눈은 다 치웠어.",
            Disasters.DisasterKind.Quake => "이쪽 틈은 메웠다!",
            _ => "거의 다 됐어!",
        };
    }

    // ------------------------------------------------------------------ Returning & guild services

    /// <summary>
    /// Runs to a lure point past another species' territory (heroic drive hunt, P4-18), ignoring
    /// fights except area-attack dodging, then hands back to <see cref="Next"/> — whose combat logic
    /// usually waits out the brawl (third-party wait) before joining.
    /// </summary>
    public class LureState : State<Adventurer>
    {
        const float Timeout = 10f;
        Vector2 _point;
        string _line;
        public State<Adventurer> Next { get; private set; }

        public override string Label => "몰이 중";

        public void Begin(Vector2 point, State<Adventurer> next, string line)
        {
            _point = point;
            Next = next;
            _line = line;
        }

        public override void Enter()
        {
            var a = Owner;
            a.Engage.Clear();
            a.Mover.SpeedMultiplier = 1.1f;
            a.Mover.SetDestination(_point, 0.5f);
            if (!string.IsNullOrEmpty(_line)) a.Say(_line);
        }

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class Saved
        {
            public Vector2 point;
            public string next;
        }

        public override string SaveState() => JsonUtility.ToJson(new Saved { point = _point, next = Adventurer.NameOf(Next) });

        public override void ResumeState(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<Saved>(json);
            _point = s.point;
            Next = Owner.StateNamed(s.next);
            _line = null;
            Owner.Mover.SpeedMultiplier = 1.1f;
            Owner.Mover.SetDestination(_point, 0.5f);
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (a.CheckRetreat()) return;
            // P4-19: once the pack is right behind at the spot, break the chase with a skill.
            if (TerritoryTactics.TryBreakAway(a, TerritoryTactics.FindPursuer(a), _point, AdvCtx.GuildDoor))
            {
                a.ChangeState(Next ?? a.ReturnState);
                return;
            }
            bool arrived = Vector2.Distance(a.Position, _point) < 0.8f || !a.Mover.IsMoving;
            if (arrived || TimeInState > Timeout) a.ChangeState(Next ?? a.ReturnState);
        }

        public override void Exit() => Owner.Mover.SpeedMultiplier = 1f;
    }

    public class ReturnToTownState : State<Adventurer>
    {
        /// <summary>Running home badly hurt (or exhausted) rather than just walking back.</summary>
        public bool Retreating { get; private set; }
        bool _retreatRequested;

        /// <summary>
        /// A party pulling back to mend (P4-71): what each goes back to once a camp on the way has
        /// patched it up (the quest, the hunt). Null: plain walk home.
        /// </summary>
        public State<Adventurer> ResumeAfter { get; private set; }
        State<Adventurer> _resumeRequested;
        bool _calmRequested;
        float _formationRepath;

        const float LureTimeout = 12f;
        /// <summary>Hurt as they are, members still pick up what lies this close to where the retreat began once no enemy is in sight.</summary>
        const float RetreatLootRadius = 8f;
        Vector2 _retreatFrom;
        bool _luring;
        Vector2 _lurePoint;
        float _campCheck;

        /// <summary>Dragging a pursuer past another den (P4-18): not a walk home anyone can stop.</summary>
        public bool Luring => _luring;

        public override string Label => _luring ? "유인 후퇴" : Retreating ? "후퇴 중" : "귀환 중";

        /// <summary>
        /// Makes the next entry a retreat. A request survives the Exit of a re-entry, so a member
        /// already walking home can still switch to retreating (Exit resets <see cref="Retreating"/>).
        /// </summary>
        public void RequestRetreat(State<Adventurer> resumeAfter = null, bool calm = false)
        {
            _retreatRequested = true;
            _resumeRequested = resumeAfter;
            _calmRequested = calm;
        }

        public override void Enter()
        {
            var a = Owner;
            Retreating = _retreatRequested;
            _retreatRequested = false;
            ResumeAfter = Retreating ? _resumeRequested : null;
            bool calm = _calmRequested;
            _resumeRequested = null;
            _calmRequested = false;
            _formationRepath = 0f;
            _retreatFrom = a.Position;
            a.Mover.SpeedMultiplier = Retreating ? 1.15f : 1f;
            _luring = false;
            // Veterans drag their pursuer past a known hostile nest on the way out (P4-18) — alone:
            // a party pulls back together (P4-71).
            if (Retreating && a.Tier >= CombatTier.Veteran && !InAParty(a))
            {
                var pursuer = TerritoryTactics.FindPursuer(a);
                if (pursuer != null && TerritoryTactics.FindLurePoint(a, pursuer, AdvCtx.GuildDoor, true, out _lurePoint, out _)
                    && a.Mover.SetDestination(_lurePoint, 0.5f))
                {
                    _luring = true;
                    a.Say("저쪽 둥지로 끌고 가자!");
                    return;
                }
            }
            if (Retreating && !calm) a.Say("위험해! 일단 물러나자!");
            a.Mover.SetDestination(AdvCtx.GuildDoor, 0.6f);
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (_luring)
            {
                // P4-19: walking away alone leaves the pursuer on our heels, so it follows us back
                // out again. A skill cuts the thread and leaves it among the residents.
                if (TerritoryTactics.TryBreakAway(a, TerritoryTactics.FindPursuer(a), _lurePoint, AdvCtx.GuildDoor))
                {
                    _luring = false;
                    a.Say("여기까지다!");
                    a.Mover.SetDestination(AdvCtx.GuildDoor, 0.6f);
                    return;
                }
                if (Vector2.Distance(a.Position, _lurePoint) > 0.8f && TimeInState < LureTimeout && a.Mover.IsMoving) return;
                _luring = false;
                a.Mover.SetDestination(AdvCtx.GuildDoor, 0.6f);
            }
            // Walking home still fights whatever attacks on the way: badly hurt, drink a potion or
            // turn it into a retreat instead of fighting to the death.
            if (!Retreating && a.HpRatio < a.RetreatThreshold && a.CheckRetreat()) return;
            if (!Retreating && a.HandleCombat(dt)) return;
            // Pulling back together (P4-96, user decision 2026-10-20): the fit ones turn on whatever catches up with them —
            // within reach only, so the party keeps going — while the hurt run on.
            if (Retreating)
            {
                // Hit inside the walls on the way to the guild (raiders broke in, P4-100): it runs from them, not to the door
                // they may stand at — then walks to the counter once it is quiet.
                if (TakeCoverState.TryStart(a, a.ReturnState)) return;
                if (IsRearGuard(a)) { if (a.HandleCombat(dt, allowed: _rearGuardZone ??= u => CaughtUp(Owner, u))) return; }
                // No longer covering the rear (hurt below the line, or its party gone): it lets go of the foe it held. That foe
                // stayed its target all the way home and into town, and a member "still fighting" is never quiet — its party
                // could neither camp nor mend on the way (P4-98).
                else if (a.Engage.Target != null) a.Engage.Clear();
            }
            if (!Retreating && a.Looting.Tick(dt)) return;
            // The fight is over but they are badly hurt (user report 2026-09-22: the loot was left
            // lying): gather what fell close by — looting stops by itself when an enemy shows.
            if (Retreating && a.Looting.Tick(dt, p => Vector2.Distance(p, _retreatFrom) <= RetreatLootRadius)) return;
            // Far from town and hurt or worn, or out in the dark: make camp rather than walk the
            // whole way (P4-66, user proposal 2026-09-23). Once the fight is well behind.
            _campCheck -= dt;
            if (_campCheck <= 0f)
            {
                _campCheck = AdvCtx.CampCheckInterval;
                if (Services.TryGet<CampService>(out var camps) && camps.TryCampOnTheWay(a)) return;
            }
            if (Vector2.Distance(a.Position, AdvCtx.GuildDoor) < 1.2f)
            {
                a.ChangeState(a.VisitState);
                return;
            }
            // A party walks home together (P4-71, user report: members drifting home one by one):
            // followers keep their slots, the leader waits for stragglers.
            if (Services.TryGet<PartyTactics>(out var party))
            {
                if (party.Follow(a, ref _formationRepath, dt, out _)) return;
                if (party.PaceLeader(a)) return;
            }
            if (!a.Mover.IsMoving) a.Mover.SetDestination(AdvCtx.GuildDoor, 0.6f);
        }

        /// <summary>One of a party out together (a quest, a band, a standing party on the way home).</summary>
        static bool InAParty(Adventurer a) => Services.TryGet<PartyTactics>(out var party) && party.LeaderOf(a) != null;

        System.Func<Unit, bool> _rearGuardZone;

        /// <summary>Fit enough to cover the party's retreat (as fit as one that may rejoin a fight, P4-96), and with a party.</summary>
        static bool IsRearGuard(Adventurer a) => a.HpRatio >= FallBackRules.RejoinAt(a.RetreatThreshold) && InAParty(a);

        static bool CaughtUp(Adventurer a, Unit u) => (u.Position - a.Position).sqrMagnitude <= FallBackRules.RearGuardReach * FallBackRules.RearGuardReach;

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class Saved
        {
            public bool retreating;
            public string resume_after;
            public bool luring;
            public Vector2 lure_point;
            public Vector2 retreat_from;
        }

        public override string SaveState() => JsonUtility.ToJson(new Saved
        {
            retreating = Retreating, resume_after = Adventurer.NameOf(ResumeAfter), luring = _luring,
            lure_point = _lurePoint, retreat_from = _retreatFrom,
        });

        /// <summary>On the way again, without the shout it gave when it set off.</summary>
        public override void ResumeState(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<Saved>(json);
            var a = Owner;
            Retreating = s.retreating;
            ResumeAfter = Retreating ? a.StateNamed(s.resume_after) : null;
            _luring = s.luring;
            _lurePoint = s.lure_point;
            _retreatFrom = s.retreat_from;
            _formationRepath = 0f;
            a.Mover.SpeedMultiplier = Retreating ? 1.15f : 1f;
            if (_luring) a.Mover.SetDestination(_lurePoint, 0.5f);
            else a.Mover.SetDestination(AdvCtx.GuildDoor, 0.6f);
        }

        public override void Exit()
        {
            Owner.Mover.SpeedMultiplier = 1f;
            Retreating = false;
        }
    }

    /// <summary>
    /// At the guild: sell loot, turn in quests, buy supplies. Back from a party outing (P4-50) the
    /// gold goes into the party's pool instead, and the member waits for the leader's split
    /// before shopping with its share.
    /// </summary>
    public class GuildVisitState : State<Adventurer>
    {
        const float VisitSeconds = 1.5f;
        bool _goingToDoor;
        bool _shopped;
        float _guardTimer;
        float _wanderTimer;

        public override string Label => Waiting ? "정산 대기" : "길드 방문";

        bool Waiting => !_shopped && Services.TryGet<GuildParties>(out var parties) && parties.IsWaiting(Owner);

        public override void Enter()
        {
            var a = Owner;
            // Home: whatever it was fighting out there is behind it (P4-98: a foe left over from the way back stayed its target).
            a.Engage.Clear();
            _shopped = false;
            _goingToDoor = Vector2.Distance(a.Position, AdvCtx.GuildDoor) > 1.2f;
            if (_goingToDoor) a.Mover.SetDestination(AdvCtx.GuildDoor, 0.6f);
            else DoBusiness();
        }

        void DoBusiness()
        {
            var a = Owner;
            var trade = Services.Get<TradeService>();
            Services.TryGet<GuildParties>(out var parties);
            // Party loot is the party's (user decision): sold here, the gold goes into the pool.
            bool pooled = parties != null && parties.PartyOf(a) is { } party && party.WentOut(a) && !party.Settling;
            int loot = a.Data.LootCount;
            int earned = trade.BuyLoot(a);
            if (earned > 0 && pooled && parties.Deposit(a, earned))
            {
                a.Data.gold -= earned;
                a.Say($"전리품을 팔아 {earned}G를 파티 몫으로 모았다.");
            }
            else if (earned > 0) a.Say($"전리품을 팔아 {earned}G를 벌었다.");
            else if (loot > 0) a.Say("길드에 돈이 없다니... 전리품은 내가 들고 있지.");
            // A good haul brought home (P4-104e: an everyday good thought).
            if (earned > 0) ThoughtRecorder.Give(a, ThoughtRules.GoodHaul);

            var q = a.CurrentQuest;
            if (q != null)
            {
                if (AdvCtx.Board.TryTurnIn(q, a, out int gold, out int exp))
                {
                    a.Data.questsCompleted++;
                    a.GainExp(exp); // EXP stays personal (user decision)
                    a.CurrentQuest = null;
                    if (pooled && parties.Deposit(a, gold)) a.Say($"보상 {gold}G는 파티 몫으로, {exp}EXP 받았다!");
                    else
                    {
                        a.Data.gold += gold;
                        a.Say($"보상 {gold}G, {exp}EXP 받았다!");
                    }
                }
                else if (!q.IsActive) a.CurrentQuest = null;
            }
            // Meals and lodging owed come off the top, before anything is bought (P4-68).
            if (Services.TryGet<LivingCosts>(out var keep) && keep.Settle(a) is int repaid and > 0)
                a.Say($"밀린 생활비 {repaid}G를 갚았다.");

            if (pooled && parties.Arrive(a)) return; // shop once the share is paid
            Shop();
        }

        void Shop()
        {
            var a = Owner;
            var trade = Services.Get<TradeService>();
            _shopped = true;
            while (a.Data.potions < 2 && trade.SellGoods(a, DefaultContent.Potion)) a.Data.potions++;
            // Then flares (P4-94): cheap, and a foe lost in the trees or the sky is a foe that hits unseen.
            while (a.Data.flares < Flares.FlareRules.Carry && trade.SellGoods(a, Flares.FlareRules.ItemId)) a.Data.flares++;
            // Survival first (potions, flares), then the next gear tier if the gold is left (P4-4).
            GearShopping.TryBuy(a);
        }

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class Saved
        {
            public bool going_to_door;
            public bool shopped;
        }

        public override string SaveState() => JsonUtility.ToJson(new Saved { going_to_door = _goingToDoor, shopped = _shopped });

        /// <summary>Its business at the counter is not done twice: only the walk to the door is taken up again.</summary>
        public override void ResumeState(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<Saved>(json);
            _goingToDoor = s.going_to_door;
            _shopped = s.shopped;
            if (_goingToDoor) Owner.Mover.SetDestination(AdvCtx.GuildDoor, 0.6f);
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (TownGuardState.TryStart(a, this, ref _guardTimer, dt)) return;
            if (_goingToDoor)
            {
                if (a.Mover.IsMoving) return;
                _goingToDoor = false;
                DoBusiness();
                return;
            }
            if (!_shopped)
            {
                // Waiting by the guild for the others and the leader's split.
                if (Waiting) { AdvCtx.Wander(a, ref _wanderTimer, dt); return; }
                Shop();
            }
            if (TimeInState < VisitSeconds) return;

            if (a.HpRatio < 0.9f || a.Data.fatigue > 40f) a.ChangeState(a.RestState);
            else if (a.CurrentQuest != null && a.CurrentQuest.IsActive) a.ChangeState(a.TravelState);
            else a.ChangeState(a.IdleState);
        }
    }

    public class RestState : State<Adventurer>
    {
        public override string Label => "휴식";
        float _guardTimer;

        public override void Enter()
        {
            Owner.Engage.Clear(); // resting, not fighting (P4-98)
            Owner.Mover.SetDestination(AdvCtx.GuildDoor + GameRandom.Shared.InsideUnitCircle() * 1.2f, 0.3f);
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (TownGuardState.TryStart(a, this, ref _guardTimer, dt)) return;
            float hours = dt / AdvCtx.Clock.RealSecondsPerGameHour;
            a.Heal(a.Stats.MaxHp * 0.25f * hours);
            a.Data.fatigue = Mathf.Max(0f, a.Data.fatigue - 30f * hours);
            float relief = AdvCtx.Town.IsInTown(a.Position) ? FacilityService.TownStressRelief : 1f; // dormitory (P4-92)
            a.Data.stress = Mathf.Max(0f, a.Data.stress - 12f * relief * hours);

            if (a.HpRatio >= 0.95f && a.Data.fatigue <= 10f)
            {
                ThoughtRecorder.Give(a, ThoughtRules.WellRested); // P4-104e
                if (a.CurrentQuest != null && a.CurrentQuest.IsActive) a.ChangeState(a.TravelState);
                else a.ChangeState(a.IdleState);
            }
        }
    }

    public class DeadState : State<Adventurer>
    {
        const float FadeSeconds = 2f;

        public override string Label => "사망";

        public override void Enter()
        {
            Owner.Mover.Stop();
            Owner.Engage.Clear();
            Owner.Visual.PlayDeath();
        }

        public override void Tick(float dt)
        {
            Owner.Visual.SetTint(new Color(0.6f, 0.6f, 0.6f, Mathf.Clamp01(1f - TimeInState / FadeSeconds)));
            if (TimeInState >= FadeSeconds) Object.Destroy(Owner.gameObject);
        }
    }
}
