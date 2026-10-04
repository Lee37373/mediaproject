using GuildMaster.Core;
using GuildMaster.Units;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>
    /// The travelling merchant NPC (P4-3): walks in from a gate to its stall on the plaza,
    /// trades there while the stall is open, then walks out and disappears. Visitor faction,
    /// so monsters ignore it. Trading itself happens in the UI via <see cref="Economy.MerchantMarket"/>.
    /// </summary>
    public class Merchant : Unit
    {
        public enum Phase { Arriving, Trading, Leaving }

        public Phase Current { get; private set; }
        public string StateLabel => Current switch
        {
            Phase.Arriving => "노점으로 가는 중",
            Phase.Trading => "장사 중",
            _ => "떠나는 중",
        };
        public override string DebugState => StateLabel;

        Vector2 _stall;
        Vector2 _exit;
        float _phaseTime;
        System.Action _onArrived;
        System.Action _onLeft;

        public static Merchant Create(Vector2 gate, Vector2 stall, Transform parent, System.Action onArrived, System.Action onLeft)
        {
            var go = new GameObject("Merchant");
            go.transform.SetParent(parent, false);
            go.transform.position = gate;
            go.AddComponent<PathFollower>();
            var visual = go.AddComponent<CharacterVisual>();
            // Raccoon peddler from the imported art; generated pixel art without the packages.
            var anim = ArtLibrary.Adventurer("raccoon");
            if (anim != null) visual.InitAnimated(anim);
            else visual.Init(PixelArt.Humanoid("merchant", new Color(0.35f, 0.22f, 0.12f), new Color(0.95f, 0.8f, 0.65f),
                new Color(0.55f, 0.35f, 0.6f), new Color(0.9f, 0.75f, 0.3f), new Color(0.3f, 0.25f, 0.2f)));
            var m = go.AddComponent<Merchant>();
            m.Setup(stall, onArrived, onLeft);
            return m;
        }

        protected override void Awake()
        {
            base.Awake();
            Faction = Faction.Visitor;
        }

        void Setup(Vector2 stall, System.Action onArrived, System.Action onLeft)
        {
            UnitId = "MERCHANT";
            DisplayName = "행상인";
            Radius = 0.3f;
            Stats = new CombatStats
            {
                MaxHp = Combat.ScaledHp(60f), Attack = 0f, Defense = 2f, AttackRange = 1f, AttackCooldown = 1f,
                MoveSpeed = 2.2f, SightRadius = 4f,
            };
            ResetVitals();
            _stall = stall;
            _onArrived = onArrived;
            _onLeft = onLeft;
            SetPhase(Phase.Arriving);
            Mover.SetDestination(_stall, 0.1f);
        }

        void SetPhase(Phase p)
        {
            Current = p;
            _phaseTime = 0f;
        }

        /// <summary>Save (P5-4): back at the stall trading, or on the way out, without the arrival callback.</summary>
        public void RestorePhase(Phase p)
        {
            switch (p)
            {
                case Phase.Trading:
                    Mover.Stop();
                    SetPhase(Phase.Trading);
                    break;
                case Phase.Leaving:
                    Leave();
                    break;
            }
        }

        /// <summary>Closes the stall and heads for the nearest gate.</summary>
        public void Leave()
        {
            if (Current == Phase.Leaving) return;
            SetPhase(Phase.Leaving);
            var world = Services.Get<WorldService>();
            float best = float.MaxValue;
            _exit = Position;
            foreach (var g in world.Map.Gates)
            {
                var p = GridMap.CellToWorld(g);
                float d = (p - Position).sqrMagnitude;
                if (d < best) { best = d; _exit = p; }
            }
            Mover.SetDestination(_exit, 0.5f);
        }

        void Update()
        {
            if (!IsAlive) return;
            float dt = Time.deltaTime;
            _phaseTime += dt;
            TickStatus(dt);
            switch (Current)
            {
                case Phase.Arriving:
                    // Blocked paths should not strand the merchant: open wherever it stopped.
                    if (Vector2.Distance(Position, _stall) < 0.4f || (!Mover.IsMoving && _phaseTime > 1f))
                    {
                        Mover.Stop();
                        SetPhase(Phase.Trading);
                        _onArrived?.Invoke();
                    }
                    break;
                case Phase.Trading:
                    if (Mover.IsMoving) Mover.Stop();
                    Visual.FaceTowards(Position + Vector2.left);   // towards the cart
                    break;
                case Phase.Leaving:
                    if (Vector2.Distance(Position, _exit) < 1.2f || (!Mover.IsMoving && _phaseTime > 1f))
                    {
                        var cb = _onLeft;
                        _onLeft = null;
                        cb?.Invoke();
                        Destroy(gameObject);
                    }
                    break;
            }
        }

        public override bool IsHostileTo(Unit other) => false;

        protected override void OnDeath(Unit killer)
        {
            // Not targeted by anything today; if it ever dies the visit simply ends.
            var cb = _onLeft;
            _onLeft = null;
            cb?.Invoke();
            Destroy(gameObject, 0.5f);
        }
    }
}
