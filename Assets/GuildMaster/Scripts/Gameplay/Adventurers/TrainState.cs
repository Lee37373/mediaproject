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
        Vector2 _dummy;
        bool _arrived;

        static readonly string[] Lines = { "하나, 둘!", "자세부터 다시.", "조금씩 손에 익는다.", "언젠가 저 숲 너머까지 가 볼 거야." };

        public override string Label => "훈련 중";

        public override void Enter()
        {
            var a = Owner;
            a.Engage.Clear();
            _arrived = false;
            _strokeTimer = StrokeMax;
            _moveTimer = MoveEvery;
            if (!TakePlace(a)) a.ChangeState(a.IdleState);
        }

        /// <summary>A walkable cell in the training yard to drill at, facing its middle (the dummies); false with no yard.</summary>
        bool TakePlace(Adventurer a)
        {
            var ground = Services.TryGet<TownService>(out var town) ? town.Get(BuildingType.TrainingGround) : null;
            if (ground == null || ground.IsDestroyed || !Services.TryGet<WorldService>(out var world)) return false;
            var yard = ground.Footprint;
            _dummy = ground.Center;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var cell = new Vector2Int(GameRandom.Shared.Range(yard.xMin, yard.xMax), GameRandom.Shared.Range(yard.yMin, yard.yMax));
                if (!world.Grid.IsWalkable(cell)) continue;
                var spot = GridMap.CellToWorld(cell);
                if ((spot - _dummy).sqrMagnitude < 1f) continue; // not on the dummies themselves
                return a.Mover.SetDestination(spot, 0.2f);
            }
            return a.Mover.SetDestination(ground.DoorPosition, 0.4f);
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
            // A stroke at the dummy: it turns to it and a little spark flies.
            a.Visual?.FaceTowards(_dummy);
            _strokeTimer -= dt;
            if (_strokeTimer > 0f) return;
            _strokeTimer = GameRandom.Shared.Range(StrokeMin, StrokeMax);
            if (Services.TryGet<FxService>(out var fx))
                fx.Spark(Vector2.MoveTowards(a.Position, _dummy, 0.45f) + new Vector2(0f, 0.25f), new Color(1f, 0.95f, 0.8f), 0.45f, 0.12f);
        }
    }
}
