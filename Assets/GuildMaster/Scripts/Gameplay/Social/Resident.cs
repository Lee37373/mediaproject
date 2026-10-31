using GuildMaster.Core;
using GuildMaster.Town;
using GuildMaster.Units;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Social
{
    /// <summary>
    /// A townsperson (user request 2026-10-07): goes about the day between their workplace and the plaza, sleeps at
    /// night, and talks with anyone in town through <see cref="TownTalkService"/>. Visitor faction like the merchant,
    /// so monsters leave them alone; they do not fight. Generated pixel art (not the adventurer skins), so they read as
    /// townsfolk at a glance.
    /// </summary>
    public class Resident : Unit
    {
        public ResidentProfile Profile { get; private set; }
        /// <summary>Where they work (the door of their building, or a spot on the plaza).</summary>
        public Vector2 Workplace { get; private set; }
        /// <summary>Held by a town talk: stands still and faces the other.</summary>
        public bool InTalk { get; set; }
        public Transform TalkPartner { get; set; }
        /// <summary>Night: at home (their workplace), not out talking.</summary>
        public bool Asleep => Services.TryGet<GameClock>(out var c) && (c.HourOfDay < WakeHour || c.HourOfDay >= SleepHour);

        public const int WakeHour = 6;
        public const int SleepHour = 21;
        const float ReviveSeconds = 30f;

        float _wanderTimer;
        bool _approaching;

        public override string DebugState => InTalk ? "대화 중" : Asleep ? "쉬는 중" : _approaching ? "말 걸러 가는 중" : Profile?.JobLabel ?? "주민";
        public string StateLabel => DebugState;

        public static Resident Create(ResidentProfile profile, Vector2 position, Vector2 workplace, Transform parent)
        {
            var go = new GameObject("Resident_" + profile.Name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.AddComponent<PathFollower>();
            var visual = go.AddComponent<CharacterVisual>();
            var (hair, body, accent, legs) = Colors(profile.Job);
            // CharacterMaker rig when imported (2026-10-30), generated pixel art otherwise.
            if (!visual.InitHuman(Look(profile, hair)))
                visual.Init(PixelArt.Humanoid("resident_" + profile.Id, hair, new Color(0.95f, 0.8f, 0.65f), body, accent, legs),
                            profile.Job == ResidentJob.Child ? 0.8f : 1f);
            var r = go.AddComponent<Resident>();
            r.Setup(profile, workplace);
            return r;
        }

        /// <summary>A resident dressed for the job: the smith with a hammer, the guard armed and helmeted, a small child.</summary>
        static HumanLook Look(ResidentProfile p, Color hair)
        {
            var look = HumanLook.Plain("resident_" + p.Id);
            look.HairColor = hair;
            look.FriendlyEyes = true; // no monster-like eyes on townsfolk (2026-10-31)
            switch (p.Job)
            {
                case ResidentJob.Blacksmith: look.Weapon = "Blunt"; look.WeaponIndex = 0; look.Beard = 3; break;
                case ResidentJob.Guard: look.Weapon = "Spear"; look.WeaponIndex = 1; look.Helmet = 2; look.Shield = 1; break;
                case ResidentJob.Chief: look.Beard = 7; break;
                case ResidentJob.Child: look.Scale = 0.75f; look.Beard = HumanLook.None; break;
            }
            return look;
        }

        static (Color hair, Color body, Color accent, Color legs) Colors(ResidentJob job) => job switch
        {
            ResidentJob.Blacksmith => (new Color(0.2f, 0.15f, 0.1f), new Color(0.45f, 0.3f, 0.2f), new Color(0.3f, 0.3f, 0.32f), new Color(0.25f, 0.2f, 0.18f)),
            ResidentJob.Herbalist => (new Color(0.55f, 0.35f, 0.2f), new Color(0.35f, 0.55f, 0.35f), new Color(0.85f, 0.8f, 0.5f), new Color(0.3f, 0.35f, 0.25f)),
            ResidentJob.Chief => (new Color(0.85f, 0.85f, 0.85f), new Color(0.5f, 0.2f, 0.25f), new Color(0.9f, 0.75f, 0.3f), new Color(0.25f, 0.2f, 0.3f)),
            ResidentJob.Baker => (new Color(0.9f, 0.6f, 0.25f), new Color(0.95f, 0.9f, 0.8f), new Color(0.85f, 0.45f, 0.45f), new Color(0.5f, 0.4f, 0.35f)),
            ResidentJob.Guard => (new Color(0.3f, 0.25f, 0.2f), new Color(0.55f, 0.58f, 0.62f), new Color(0.25f, 0.4f, 0.7f), new Color(0.3f, 0.3f, 0.35f)),
            _ => (new Color(0.75f, 0.55f, 0.2f), new Color(0.35f, 0.6f, 0.85f), new Color(0.95f, 0.85f, 0.3f), new Color(0.35f, 0.3f, 0.5f)),
        };

        protected override void Awake()
        {
            base.Awake();
            Faction = Faction.Visitor;
        }

        void Setup(ResidentProfile profile, Vector2 workplace)
        {
            Profile = profile;
            Workplace = workplace;
            UnitId = profile.Id;
            DisplayName = profile.Name;
            Radius = 0.3f;
            Stats = new CombatStats
            {
                MaxHp = Combat.ScaledHp(50f), Attack = 0f, Defense = 2f, AttackRange = 1f, AttackCooldown = 1f,
                MoveSpeed = profile.Job == ResidentJob.Child ? 2.6f : 2.0f, SightRadius = 4f,
            };
            ResetVitals();
            _wanderTimer = GameRandom.Shared.Range(0.5f, 3f);
        }

        /// <summary>Walks over to someone to talk to them (the talk starts once close, see TownTalkService).</summary>
        public void WalkTo(Vector2 target)
        {
            _approaching = true;
            Mover.SetDestination(target, 1.1f);
        }

        public bool Approaching => _approaching && Mover.IsMoving;

        void Update()
        {
            if (!IsAlive) return;
            float dt = Time.deltaTime;
            TickStatus(dt);
            if (InTalk)
            {
                _approaching = false;
                if (Mover.IsMoving) Mover.Stop();
                if (TalkPartner != null) Visual.FaceTowards(TalkPartner.position);
                return;
            }
            if (_approaching)
            {
                if (!Mover.IsMoving) _approaching = false;
                return;
            }
            if (Mover.IsMoving) return;
            _wanderTimer -= dt;
            if (_wanderTimer > 0f) return;
            _wanderTimer = GameRandom.Shared.Range(3f, 8f);
            var rng = GameRandom.Shared;
            if (Asleep)
            {
                if (Vector2.Distance(Position, Workplace) > 1f) Mover.SetDestination(Workplace, 0.4f);
                return;
            }
            // Children run about the plaza; the others mostly mind their work and drop by the plaza now and then.
            float plaza = Profile.Job == ResidentJob.Child || Profile.Job == ResidentJob.Baker ? 0.8f : 0.45f;
            if (rng.Value < plaza && Services.TryGet<TownService>(out var town)) Mover.SetDestination(town.RandomPlazaSpot(rng, 6), 0.3f);
            else Mover.SetDestination(Workplace + rng.InsideUnitCircle() * 1.5f, 0.3f);
        }

        public override bool IsHostileTo(Unit other) => false;

        protected override void OnDeath(Unit killer)
        {
            // Nothing targets them today; if it ever happens they recover at home after a while.
            Invoke(nameof(Revive), ReviveSeconds);
        }

        void Revive()
        {
            transform.position = Workplace;
            ResetVitals();
        }
    }
}
