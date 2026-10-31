using System.Collections.Generic;
using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Town;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// A newcomer at the training ground (P4-99, user decisions 2026-10-21): weaker than the monsters round the town
    /// (<see cref="Adventurer.IsNewcomer"/>), it stays in town while the ground teaches — walks into the yard and drills at
    /// the dummies, now and then taking another place — and thinks again after <see cref="NewcomerRules.TrainHours"/>
    /// (a quest may have come up), or as soon as it has outgrown it or the ground stops teaching. The EXP itself is the
    /// training ground's hourly pay to everyone idle in town (<see cref="UpgradeService"/>). It turns out for an intruder
    /// like anyone in town.
    /// </summary>
    public class TrainState : State<Adventurer>
    {
        /// <summary>Seconds between strokes at the dummy, and between changes of place in the yard.</summary>
        const float StrokeMin = 0.8f, StrokeMax = 1.6f, MoveEvery = 20f;

        float _guardTimer, _strokeTimer, _moveTimer;
        Vector2 _dummy, _spot;
        bool _arrived, _waiting;

        /// <summary>The places taken in the yard now, so two trainees do not share a dummy's side (P4-115).</summary>
        static readonly List<TrainState> s_training = new();
        static readonly List<(Vector2 spot, Vector2 dummy)> s_free = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_training.Clear();

        static readonly string[] Lines = { "하나, 둘!", "자세부터 다시.", "조금씩 손에 익는다.", "언젠가 저 숲 너머까지 가 볼 거야." };

        public override string Label => "훈련 중";

        public override void Enter()
        {
            var a = Owner;
            a.Engage.Clear();
            _arrived = false;
            _strokeTimer = StrokeMax;
            _moveTimer = MoveEvery;
            if (!s_training.Contains(this)) s_training.Add(this);
            if (!TakePlace(a)) a.ChangeState(a.IdleState);
        }

        public override void Exit() => s_training.Remove(this);

        /// <summary>
        /// A free place beside one of the yard's dummies or targets (<see cref="TrainingYardLayout"/>, P4-115), facing it; when
        /// all are taken, somewhere in the yard to wait a turn. False with no yard.
        /// </summary>
        bool TakePlace(Adventurer a)
        {
            var ground = Services.TryGet<TownService>(out var town) ? town.Get(BuildingType.TrainingGround) : null;
            if (ground == null || ground.IsDestroyed || !Services.TryGet<WorldService>(out var world)) return false;
            var yard = ground.Footprint;
            _spot = new Vector2(float.MaxValue, float.MaxValue);   // out of the others' way while choosing
            s_free.Clear();
            foreach (var s in TrainingYardLayout.Of(ground.Level))
            {
                if (!s.Drills) continue;
                var at = TrainingYardLayout.WorldOf(yard, s);
                for (int side = -1; side <= 1; side += 2)
                {
                    var spot = at + new Vector2(side * TrainingYardLayout.Beside, 0.05f);
                    if (spot.x < yard.xMin + 0.55f || spot.x > yard.xMax - 0.55f) continue;   // on the fence
                    if (!world.Grid.IsWalkable(GridMap.WorldToCell(spot)) || Taken(spot)) continue;
                    s_free.Add((spot, at));
                }
            }
            _waiting = s_free.Count == 0;
            if (!_waiting)
            {
                (_spot, _dummy) = s_free[GameRandom.Shared.Range(0, s_free.Count)];
                return a.Mover.SetDestination(_spot, 0.1f);
            }
            // Every place taken: wait in the yard, watching the middle.
            _dummy = ground.Center;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var cell = new Vector2Int(GameRandom.Shared.Range(yard.xMin + 1, yard.xMax - 1), GameRandom.Shared.Range(yard.yMin + 1, yard.yMax - 1));
                if (!world.Grid.IsWalkable(cell)) continue;
                _spot = GridMap.CellToWorld(cell);
                if (Taken(_spot)) continue;
                return a.Mover.SetDestination(_spot, 0.2f);
            }
            return a.Mover.SetDestination(ground.DoorPosition, 0.4f);
        }

        bool Taken(Vector2 spot)
        {
            for (int i = s_training.Count - 1; i >= 0; i--)
            {
                var t = s_training[i];
                // One left without an Exit (its owner died or went away in the state): no longer here.
                if (t.Owner == null || !t.Owner.IsAlive || t.Machine.Current != t) { s_training.RemoveAt(i); continue; }
                if (t != this && (t._spot - spot).sqrMagnitude < 0.6f * 0.6f) return true;
            }
            return false;
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (TownGuardState.TryStart(a, this, ref _guardTimer, dt)) return;
            // Taken on a quest meanwhile (persuaded, or its party's), or done here: back to deciding.
            bool teaches = Services.TryGet<UpgradeService>(out var upgrades) && upgrades.Trains;
            if (a.CurrentQuest != null || !teaches || !a.IsNewcomer || TimeInState >= AdvCtx.Clock.HoursToSeconds(NewcomerRules.TrainHours))
            {
                a.ChangeState(a.IdleState);
                return;
            }
            if (a.Looting.Tick(dt, AdvCtx.TownLootZone)) return;
            if (a.Mover.IsMoving) return;
            if (!_arrived)
            {
                _arrived = true;
                if (GameRandom.Shared.Chance(0.3f)) a.Say(Lines[GameRandom.Shared.Range(0, Lines.Length)], toFeed: false);
            }
            _moveTimer -= dt;
            if (_moveTimer <= 0f)
            {
                _moveTimer = MoveEvery;
                _arrived = false;
                TakePlace(a);
                return;
            }
            // A stroke at the dummy: it turns to it and a little spark flies off its straw (waiting a turn: just watching).
            a.Visual?.FaceTowards(_dummy);
            if (_waiting) return;
            _strokeTimer -= dt;
            if (_strokeTimer > 0f) return;
            _strokeTimer = GameRandom.Shared.Range(StrokeMin, StrokeMax);
            if (Services.TryGet<FxService>(out var fx))
                fx.Spark(_dummy + new Vector2(Mathf.Sign(a.Position.x - _dummy.x) * 0.3f, 0.75f), new Color(1f, 0.95f, 0.8f), 0.45f, 0.12f);
        }
    }
}
