using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Units;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Incidents
{
    /// <summary>
    /// A traveller or merchant on the road between villages (P4-85 rescue, user decision 2026-10-18: its own NPC, the
    /// merchant who visits the town stays as it is). It does not fight: ambushed, it crouches by its cart and holds out
    /// as long as it can (<see cref="HoldOut"/>); once nobody attacks it any more it goes on to where it was going and
    /// is gone. Monsters that go for people go for it (<see cref="ExposedToMonsters"/>).
    /// </summary>
    public class Traveller : Unit
    {
        public enum Phase { Crouching, Travelling }

        public Phase Current { get; private set; }
        public Vector2 Destination { get; private set; }
        /// <summary>Called once when it reaches where it was going (it is destroyed right after).</summary>
        public System.Action<Traveller> Arrived;

        SpriteRenderer _cart;
        float _stuckTime;

        public string StateLabel => Current == Phase.Crouching ? "습격받는 중" : "길을 가는 중";
        public override string DebugState => StateLabel;
        public override bool ExposedToMonsters => IsAlive;

        public static Traveller Create(string id, string name, Vector2 at, Vector2 destination, Transform parent)
        {
            var go = new GameObject("Traveller");
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            go.AddComponent<PathFollower>();
            var visual = go.AddComponent<CharacterVisual>();
            var anim = ArtLibrary.Adventurer("raccoon");
            if (anim != null) visual.InitAnimated(anim);
            else visual.Init(PixelArt.Humanoid("traveller", new Color(0.4f, 0.3f, 0.2f), new Color(0.95f, 0.8f, 0.65f),
                new Color(0.45f, 0.55f, 0.35f), new Color(0.85f, 0.7f, 0.35f), new Color(0.3f, 0.25f, 0.2f)));
            visual.SetTint(new Color(0.95f, 0.9f, 0.8f));
            var t = go.AddComponent<Traveller>();
            go.AddComponent<HealthBar>().Init(t, 0.75f, new Color(0.95f, 0.75f, 0.3f));
            go.AddComponent<FogHideable>();
            t.Setup(id, name, destination);
            return t;
        }

        protected override void Awake()
        {
            base.Awake();
            Faction = Faction.Visitor;
        }

        void Setup(string id, string name, Vector2 destination)
        {
            UnitId = id;
            DisplayName = name;
            Radius = 0.3f;
            Stats = new CombatStats
            {
                MaxHp = Combat.ScaledHp(60f), Attack = 0f, Defense = 0f, AttackRange = 1f, AttackCooldown = 1f, MoveSpeed = 2.2f, SightRadius = 4f,
            };
            ResetVitals();
            Destination = destination;
            Current = Phase.Crouching;
            _cart = new GameObject("Cart").AddComponent<SpriteRenderer>();
            _cart.transform.SetParent(transform, false);
            _cart.transform.localPosition = new Vector2(-0.95f, -0.2f);
            _cart.sprite = CartoonArt.MerchantCart();
            GetComponent<FogHideable>().Refresh();
        }

        /// <summary>
        /// Ambushed: crouches where it is with <paramref name="hp"/> to hold out on (P4-85: the time limit of the rescue is how
        /// long that lasts, <see cref="IncidentRules.HoldOutHp"/>).
        /// </summary>
        public void HoldOut(float hp)
        {
            Stats.MaxHp = Mathf.Max(Combat.ScaledHp(60f), hp);
            ResetVitals();
            Crouch();
        }

        void Crouch()
        {
            Current = Phase.Crouching;
            Mover.Stop();
        }

        /// <summary>Nobody is on it any more: on its way again.</summary>
        public void GoOn()
        {
            if (!IsAlive || Current == Phase.Travelling) return;
            Current = Phase.Travelling;
            _stuckTime = 0f;
            Mover.SetDestination(Destination, 1f);
        }

        /// <summary>Save (P5-4): back as it was, without the hold-out's fresh life.</summary>
        public void RestoreState(Phase phase, float maxHp, float hp)
        {
            Stats.MaxHp = maxHp;
            ResetVitals();
            SetHp(Mathf.Clamp(hp, 1f, maxHp));
            if (phase == Phase.Travelling) GoOn();
            else Crouch();
        }

        void Update()
        {
            if (!IsAlive || Time.deltaTime <= 0f) return;
            TickStatus(Time.deltaTime);
            if (_cart != null) _cart.sortingOrder = CharacterVisual.BaseSortingOrder - Mathf.RoundToInt(_cart.transform.position.y * 8f);
            if (Current != Phase.Travelling) return;
            if (Vector2.Distance(Position, Destination) < 1.5f) { Arrive(); return; }
            // A path that will not take it there (blocked, or it was shoved off): it gets there by other roads.
            if (!Mover.IsMoving)
            {
                _stuckTime += Time.deltaTime;
                if (_stuckTime > 1f && !Mover.SetDestination(Destination, 1f)) Arrive();
            }
            else _stuckTime = 0f;
        }

        void Arrive()
        {
            var cb = Arrived;
            Arrived = null;
            cb?.Invoke(this);
            Destroy(gameObject);
        }

        public override bool IsHostileTo(Unit other) => false;

        protected override void OnDeath(Unit killer) => Destroy(gameObject, 1.2f);
    }
}
