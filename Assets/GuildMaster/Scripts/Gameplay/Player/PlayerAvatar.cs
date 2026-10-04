using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Units;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Player
{
    public struct PlayerMoveOrderedEvent { public Vector2 Target; }
    /// <summary>The guild master fell (HP 0). Ends the game (see GameOverService).</summary>
    public struct PlayerDownedEvent { public Unit Killer; }

    /// <summary>
    /// The guild master: click-to-move, provides vision, walks up to NPCs to interact.
    /// A guild-faction unit so monsters attack it, but it never fights back — adventurers
    /// escort and protect it. Reaching 0 HP ends the game.
    /// </summary>
    [RequireComponent(typeof(PathFollower))]
    public class PlayerAvatar : Unit, ISaveable, ISaveLink, ISaveResume
    {
        const float TownRegenPerHour = 0.10f;

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SaveData
        {
            public UnitSave unit;
            public bool moving;
            public Vector2 destination;
        }

        SaveData _restoring;

        public string SaveKey => "master";

        /// <summary>Where the master stands and walks to; what it meant to do on arrival (talk to someone) is dropped.</summary>
        public string Capture() => JsonUtility.ToJson(new SaveData
        {
            unit = CaptureUnit(), moving = Mover.IsMoving, destination = Mover.IsMoving ? Mover.Destination : Vector2.zero,
        });

        public void Restore(string json, SaveContext ctx)
        {
            _restoring = JsonUtility.FromJson<SaveData>(json);
            Mover.Stop();
            RestoreUnit(_restoring.unit);
            ctx.Register(UnitId, this);
        }

        public void Link(SaveContext ctx) => LinkUnit(_restoring.unit, ctx);

        public void Resume(SaveContext ctx)
        {
            if (_restoring.moving) Mover.SetDestination(_restoring.destination);
            _restoring = null;
        }

        System.Action _onArrive;
        GameClock _clock;

        public static PlayerAvatar Create(Vector2 position, float speed, float sight)
        {
            var go = new GameObject("GuildMaster(Player)");
            go.transform.position = position;
            go.AddComponent<PathFollower>();
            var visual = go.AddComponent<CharacterVisual>();
            var anim = ArtLibrary.Adventurer("penguin");
            if (anim != null) visual.InitAnimated(anim);
            else visual.Init(PixelArt.GuildMaster());
            var vision = go.AddComponent<VisionSource>();
            vision.SightRadius = sight;
            go.AddComponent<HealthBar>();
            var avatar = go.AddComponent<PlayerAvatar>();
            avatar.Setup(speed, sight);
            return avatar;
        }

        protected override void Awake()
        {
            base.Awake();
            Faction = Faction.Guild;
            Mover.Arrived += HandleArrived;
            Mover.PathFailed += () => _onArrive = null;
        }

        void Setup(float speed, float sight)
        {
            UnitId = "PLAYER";
            DisplayName = "길드 마스터";
            Radius = 0.3f;
            Stats = new CombatStats
            {
                MaxHp = Combat.ScaledHp(100f), Attack = 0f, Defense = 3f, AttackRange = 1f, AttackCooldown = 1f,
                MoveSpeed = speed, SightRadius = sight,
            };
            ResetVitals();
            GetComponent<HealthBar>().Init(this, 0.75f, new Color(1f, 0.85f, 0.35f));
            _clock = Services.TryGet<GameClock>(out var c) ? c : null;
        }

        public bool MoveTo(Vector2 target, System.Action onArrive = null, float stopDistance = 0.05f)
        {
            if (!IsAlive) return false;
            _onArrive = onArrive;
            bool ok = Mover.SetDestination(target, stopDistance);
            if (ok) EventBus.Publish(new PlayerMoveOrderedEvent { Target = target });
            return ok;
        }

        void HandleArrived()
        {
            var cb = _onArrive;
            _onArrive = null;
            cb?.Invoke();
        }

        void Update()
        {
            if (!IsAlive) return;
            float dt = Time.deltaTime;
            TickStatus(dt);
            if (_clock != null && Services.TryGet<WorldService>(out var world) && world.Grid.IsInTown(Position))
                Heal(Stats.MaxHp * TownRegenPerHour * dt / _clock.RealSecondsPerGameHour);
        }

        /// <summary>The guild master never attacks.</summary>
        public override bool IsHostileTo(Unit other) => false;

        protected override void OnDeath(Unit killer)
        {
            Visual.SetTint(new Color(0.5f, 0.5f, 0.5f, 0.8f));
            EventBus.Publish(new PlayerDownedEvent { Killer = killer });
        }
    }
}
