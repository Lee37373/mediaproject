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

        public static Vector2 QuestDestination(Adventurer a, Quest q)
        {
            if (q.Type == QuestType.Hunt && Services.TryGet<EcosystemManager>(out var eco))
            {
                var nest = eco.FindNearestNest(a.Position, q.Species, requireMembers: true);
                if (nest != null) return nest.WorldPosition;
            }
            return q.Point;
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

            AdvCtx.Wander(a, ref _wanderTimer, dt);
            _decisionTimer -= dt;
            if (_decisionTimer <= 0f) a.ChangeState(a.DecidingState);
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

            if (Vector2.Distance(a.Position, _destination) < 3f)
            {
                a.ChangeState(a.CurrentQuest.Type == QuestType.Explore ? a.ExploreState : a.HuntState);
                return;
            }
            if (!a.Mover.IsMoving) a.Mover.SetDestination(_destination, 1.5f);
        }
    }

    public class HuntState : State<Adventurer>
    {
        float _searchTimer;
        float _roamTimer;
        Vector2 _area;

        public override string Label => "사냥 중";

        public override void Enter()
        {
            _area = AdvCtx.QuestDestination(Owner, Owner.CurrentQuest);
            _searchTimer = 0f;
            _roamTimer = 0f;
        }

        bool IsQuestTarget(Unit u)
        {
            var q = Owner.CurrentQuest;
            if (q == null || u is not Monster m) return false;
            return q.Type == QuestType.ClearNest ? m.Home == q.Nest : m.Def == q.Species;
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (!AdvCtx.QuestStillValid(a) || a.CheckRetreat()) return;
            if (a.HandleCombat(dt, IsQuestTarget)) return;

            _searchTimer -= dt;
            if (_searchTimer <= 0f)
            {
                _searchTimer = 0.6f;
                var target = UnitRegistry.FindNearest(a.Position, a.Stats.SightRadius * 2.5f, IsQuestTarget);
                if (target != null)
                {
                    a.Engage.SetTarget(target);
                    return;
                }
                // Current hunting ground depleted: move on to another nest of the species.
                var q = a.CurrentQuest;
                if (q.Type == QuestType.Hunt && Services.TryGet<EcosystemManager>(out var eco))
                {
                    var nest = eco.FindNearestNest(a.Position, q.Species, requireMembers: true);
                    if (nest != null && Vector2.Distance(nest.WorldPosition, _area) > 2f)
                    {
                        _area = nest.WorldPosition;
                        a.Mover.SetDestination(_area, 1.5f);
                    }
                }
            }

            if (a.Mover.IsMoving) return;
            _roamTimer -= dt;
            if (_roamTimer > 0f) return;
            _roamTimer = GameRandom.Shared.Range(1f, 3f);
            var world = Services.Get<WorldService>();
            if (world.Grid.TryRandomWalkableNear(GridMap.WorldToCell(_area), 6, GameRandom.Shared, out var cell))
                a.Mover.SetDestination(GridMap.CellToWorld(cell));
        }
    }

    public class ExploreState : State<Adventurer>
    {
        public override string Label => "탐사 중";

        public override void Tick(float dt)
        {
            var a = Owner;
            if (!AdvCtx.QuestStillValid(a) || a.CheckRetreat()) return;
            if (a.HandleCombat(dt)) return;
            if (a.Mover.IsMoving) return;

            var q = a.CurrentQuest;
            var world = Services.Get<WorldService>();
            Services.TryGet<FogOfWar>(out var fog);
            Vector2Int best = GridMap.WorldToCell(q.Point);
            for (int i = 0; i < 12; i++)
            {
                var p = q.Point + GameRandom.Shared.InsideUnitCircle() * q.Radius;
                var c = GridMap.WorldToCell(p);
                if (!world.Grid.IsWalkable(c)) continue;
                best = c;
                if (fog == null || !fog.IsExplored(c)) break;
            }
            a.Mover.SetDestination(GridMap.CellToWorld(best));
        }
    }

    // ------------------------------------------------------------------ Returning & guild services

    public class ReturnToTownState : State<Adventurer>
    {
        public bool Retreating;

        public override string Label => Retreating ? "후퇴 중" : "귀환 중";

        public override void Enter()
        {
            var a = Owner;
            a.Mover.SpeedMultiplier = Retreating ? 1.15f : 1f;
            if (Retreating) a.Say("위험해! 일단 물러나자!");
            a.Mover.SetDestination(AdvCtx.GuildDoor, 0.6f);
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (!Retreating && a.HandleCombat(dt)) return;
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
        }

        public override void Tick(float dt)
        {
            var a = Owner;
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

        public override void Enter()
        {
            Owner.Mover.SetDestination(AdvCtx.GuildDoor + GameRandom.Shared.InsideUnitCircle() * 1.2f, 0.3f);
        }

        public override void Tick(float dt)
        {
            var a = Owner;
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
        }

        public override void Tick(float dt)
        {
            var c = Owner.Visual.Body.color;
            Owner.Visual.SetTint(new Color(0.6f, 0.6f, 0.6f, Mathf.Clamp01(1f - TimeInState / FadeSeconds)));
            if (TimeInState >= FadeSeconds) Object.Destroy(Owner.gameObject);
        }
    }
}
