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
        /// Where to go for a quest: the hunting point the player marked, or the nest to clear.
        /// Adventurers never head for monsters nobody has seen (user bug report).
        /// </summary>
        public static Vector2 QuestDestination(Adventurer a, Quest q) => q.Point;

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
            if (a.Looting.Tick(dt, AdvCtx.Town.IsInTown)) return;
            AdvCtx.Wander(a, ref _wanderTimer, dt);
            _decisionTimer -= dt;
            if (_decisionTimer > 0f) return;
            // A pending party invitation is this member's decision for now.
            if (Services.TryGet<PartyService>(out var party) && party.IsInvited(a)) { _decisionTimer = 2f; return; }
            a.ChangeState(a.DecidingState);
        }
    }

    /// <summary>Waits for an asynchronous (possibly LLM) decision while idling.</summary>
    public class DecidingState : State<Adventurer>
    {
        /// <summary>Real seconds (LLM latency does not scale with game speed); covers queue wait + generation.</summary>
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
                a.LastThoughtFromLlm = d.FromLLM;
            }
            switch (d.Action)
            {
                case AdventurerAction.AcceptQuest:
                    var q = AdvCtx.Board.FindById(d.QuestId);
                    if (q != null && AdvCtx.Board.TryAccept(q, a))
                    {
                        a.CurrentQuest = q;
                        a.Say(d.Say);
                        a.ChangeState(a.TravelState);
                        return;
                    }
                    break;
                case AdventurerAction.Rest:
                    a.Say(d.Say);
                    a.ChangeState(a.RestState);
                    return;
                case AdventurerAction.Shop:
                    a.Say(d.Say);
                    a.ChangeState(a.VisitState);
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
            // Escorts walk to (and keep up with) the guild master themselves.
            if (a.CurrentQuest.Type == QuestType.Escort) { a.ChangeState(a.EscortState); return; }
            _destination = AdvCtx.QuestDestination(a, a.CurrentQuest);
            if (!a.Mover.SetDestination(_destination, 1.5f))
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
            if (a.Looting.Tick(dt)) return;

            if (Vector2.Distance(a.Position, _destination) < 3f)
            {
                a.ChangeState(a.HuntState);
                return;
            }
            if (!a.Mover.IsMoving) a.Mover.SetDestination(_destination, 1.5f);
        }
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

        public override string Label => "사냥 중";

        public override void Enter()
        {
            _searchTimer = 0f;
            _roamTimer = 0f;
        }

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
            return q.Type == QuestType.ClearNest ? m.Home == q.Nest : m.Def == q.Species;
        }

        /// <summary>A quest target inside the quest area (hunts), or any resident of the nest (clearing).</summary>
        bool IsAreaTarget(Unit u)
        {
            if (!IsQuestTarget(u)) return false;
            var q = Owner.CurrentQuest;
            return q.Type != QuestType.Hunt || Vector2.Distance(u.Position, q.Point) <= q.Radius + AreaSlack;
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (!AdvCtx.QuestStillValid(a) || a.CheckRetreat()) return;
            if (TryDrive(a)) return;
            if (a.HandleCombat(dt, IsAreaTarget)) return;
            // Loot left by the kills (P4-21) before searching on.
            if (a.Looting.Tick(dt)) return;

            _searchTimer -= dt;
            if (_searchTimer <= 0f)
            {
                _searchTimer = 0.6f;
                // Only what this adventurer can actually see.
                var target = UnitRegistry.FindNearest(a.Position, a.Stats.SightRadius, u => IsAreaTarget(u) && !a.Engage.IsIgnoring(u) && a.CanSee(u));
                if (target != null)
                {
                    a.Engage.SetTarget(target);
                    return;
                }
                // Drifted out (a chase, a lure): walk back into the area.
                if (Vector2.Distance(a.Position, Area) > AreaRadius + 2f && (!a.Mover.IsMoving || Vector2.Distance(a.Mover.Destination, Area) > AreaRadius))
                {
                    a.Mover.SetDestination(Area, 1.5f);
                    return;
                }
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

        public override void Tick(float dt)
        {
            var a = Owner;
            if (a.CheckRetreat()) return;
            bool arrived = Vector2.Distance(a.Position, _point) < 0.8f || !a.Mover.IsMoving;
            if (arrived || TimeInState > Timeout) a.ChangeState(Next ?? a.ReturnState);
        }

        public override void Exit() => Owner.Mover.SpeedMultiplier = 1f;
    }

    public class ReturnToTownState : State<Adventurer>
    {
        public bool Retreating;

        const float LureTimeout = 12f;
        bool _luring;
        Vector2 _lurePoint;

        public override string Label => _luring ? "유인 후퇴" : Retreating ? "후퇴 중" : "귀환 중";

        public override void Enter()
        {
            var a = Owner;
            a.Mover.SpeedMultiplier = Retreating ? 1.15f : 1f;
            _luring = false;
            // Veterans drag their pursuer past a known hostile nest on the way out (P4-18).
            if (Retreating && a.Tier >= CombatTier.Veteran)
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
            if (Retreating) a.Say("위험해! 일단 물러나자!");
            a.Mover.SetDestination(AdvCtx.GuildDoor, 0.6f);
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (_luring)
            {
                if (Vector2.Distance(a.Position, _lurePoint) > 0.8f && TimeInState < LureTimeout && a.Mover.IsMoving) return;
                _luring = false;
                a.Mover.SetDestination(AdvCtx.GuildDoor, 0.6f);
            }
            if (!Retreating && a.HandleCombat(dt)) return;
            if (!Retreating && a.Looting.Tick(dt)) return;
            if (Vector2.Distance(a.Position, AdvCtx.GuildDoor) < 1.2f)
            {
                a.ChangeState(a.VisitState);
                return;
            }
            if (!a.Mover.IsMoving) a.Mover.SetDestination(AdvCtx.GuildDoor, 0.6f);
        }

        public override void Exit()
        {
            Owner.Mover.SpeedMultiplier = 1f;
            Retreating = false;
        }
    }

    /// <summary>At the guild: sell loot, turn in quests, buy supplies.</summary>
    public class GuildVisitState : State<Adventurer>
    {
        const float VisitSeconds = 1.5f;
        bool _goingToDoor;
        float _guardTimer;

        public override string Label => "길드 방문";

        public override void Enter()
        {
            var a = Owner;
            _goingToDoor = Vector2.Distance(a.Position, AdvCtx.GuildDoor) > 1.2f;
            if (_goingToDoor) a.Mover.SetDestination(AdvCtx.GuildDoor, 0.6f);
            else DoBusiness();
        }

        void DoBusiness()
        {
            var a = Owner;
            var trade = Services.Get<TradeService>();
            int loot = a.Data.LootCount;
            int earned = trade.BuyLoot(a);
            if (earned > 0) a.Say($"전리품을 팔아 {earned}G를 벌었다.");
            else if (loot > 0) a.Say("길드에 돈이 없다니... 전리품은 내가 들고 있지.");

            var q = a.CurrentQuest;
            if (q != null)
            {
                if (AdvCtx.Board.TryTurnIn(q, a, out int gold, out int exp))
                {
                    a.Data.gold += gold;
                    a.Data.questsCompleted++;
                    a.GainExp(exp);
                    a.CurrentQuest = null;
                    a.Say($"보상 {gold}G, {exp}EXP 받았다!");
                }
                else if (!q.IsActive) a.CurrentQuest = null;
            }

            var db = Services.Get<GameDatabase>();
            while (a.Data.potions < 2 && trade.SellGoods(a, DefaultContent.Potion)) a.Data.potions++;
            // Survival first (potions), then the next gear tier if the gold is left (P4-4).
            GearShopping.TryBuy(a);
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
            Owner.Mover.SetDestination(AdvCtx.GuildDoor + GameRandom.Shared.InsideUnitCircle() * 1.2f, 0.3f);
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (TownGuardState.TryStart(a, this, ref _guardTimer, dt)) return;
            float hours = dt / AdvCtx.Clock.RealSecondsPerGameHour;
            a.Heal(a.Stats.MaxHp * 0.25f * hours);
            a.Data.fatigue = Mathf.Max(0f, a.Data.fatigue - 30f * hours);
            a.Data.stress = Mathf.Max(0f, a.Data.stress - 12f * hours);

            if (a.HpRatio >= 0.95f && a.Data.fatigue <= 10f)
            {
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
            var c = Owner.Visual.Body.color;
            Owner.Visual.SetTint(new Color(0.6f, 0.6f, 0.6f, Mathf.Clamp01(1f - TimeInState / FadeSeconds)));
            if (TimeInState >= FadeSeconds) Object.Destroy(Owner.gameObject);
        }
    }
}
