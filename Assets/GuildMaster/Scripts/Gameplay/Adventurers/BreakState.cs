using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Monsters;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// A member that broke (P4-104d) and acts it out where it stands: wanders round the town, squanders its gold at the guild
    /// and lies about after, comes to blows with someone it dislikes (blows, not wounds — <see cref="BreakRules.BrawlFloor"/>), or
    /// goes berserk at anyone near, comrades too (user decision 2026-10-25: it may kill), until it comes to. Walking off its work
    /// is <see cref="TagAlongState"/> or a plain walk home, quitting is the roster's; they are not this state.
    /// </summary>
    public class BreakState : State<Adventurer>
    {
        public BreakKind Kind { get; private set; }
        Adventurer _foe;
        float _until;
        bool _spent;
        float _repath;

        public override string Label => Kind switch
        {
            BreakKind.Wander => "방황 중",
            BreakKind.Spree => _spent ? "뻗어 있음" : "탕진하러 가는 중",
            BreakKind.Brawl => "주먹다짐",
            BreakKind.Berserk => "광폭",
            _ => "무너짐",
        };

        /// <summary>What it is about to act out (and with whom, for a fistfight); set before the state is entered.</summary>
        public void Begin(BreakKind kind, Adventurer foe = null)
        {
            Kind = kind;
            _foe = foe;
        }

        static float Now => AdvCtx.Clock.TotalHours;

        public override void Enter()
        {
            var a = Owner;
            _spent = false;
            _repath = 0f;
            _until = Now + Kind switch
            {
                BreakKind.Wander => BreakRules.WanderHours,
                BreakKind.Brawl => BreakRules.BrawlHours,
                BreakKind.Berserk => BreakRules.BerserkHours,
                _ => BreakRules.WanderHours, // a spree: until it got there and slept it off (reset on arrival)
            };
            a.Engage.Clear();
            if (Kind == BreakKind.Berserk) a.Berserk = true;
            if (Kind == BreakKind.Brawl) a.BrawlFoe = _foe;
            if (Kind == BreakKind.Berserk) a.Mover.SpeedMultiplier = 1.15f;
        }

        public override void Exit()
        {
            var a = Owner;
            a.Berserk = false;
            a.BrawlFoe = null;
            a.Mover.SpeedMultiplier = 1f;
            a.Engage.Clear();
        }

        public override void Tick(float dt)
        {
            switch (Kind)
            {
                case BreakKind.Wander: TickWander(dt); break;
                case BreakKind.Spree: TickSpree(dt); break;
                case BreakKind.Brawl: TickBrawl(dt); break;
                case BreakKind.Berserk: TickBerserk(dt); break;
                default: Owner.ChangeState(Owner.IdleState); break;
            }
        }

        // ---------------- Wandering ----------------

        void TickWander(float dt)
        {
            var a = Owner;
            // Lost in thought, not in a stupor: what attacks it is fought.
            if (a.WasRecentlyAttackedBy(a.LastAttacker, 3f) && a.HandleCombat(dt)) return;
            if (Now >= _until) { a.Say("...이제 좀 낫네."); a.ChangeState(a.IdleState); return; }
            if (a.Mover.IsMoving) return;
            _repath -= dt;
            if (_repath > 0f) return;
            _repath = GameRandom.Shared.Range(2f, 5f);
            a.Mover.SetDestination(AdvCtx.Town.RandomPlazaSpot(GameRandom.Shared, 14), 1f);
        }

        // ---------------- A spree ----------------

        void TickSpree(float dt)
        {
            var a = Owner;
            if (!_spent)
            {
                var door = AdvCtx.GuildDoor;
                if (Vector2.Distance(a.Position, door) > 1.5f)
                {
                    if (!a.Mover.IsMoving) a.Mover.SetDestination(door, 1f);
                    return;
                }
                int cost = BreakRules.SpreeCost(a.Data.gold, GameRandom.Shared.Value);
                a.Data.gold -= cost;
                _spent = true;
                _until = Now + BreakRules.SpreeHangoverHours;
                a.Say(cost > 0 ? $"{cost}G어치 다 마셔버렸어... 딸꾹." : "딸꾹.");
                EventBus.Publish(new MentalBreakEvent { Adventurer = a, Kind = BreakKind.Spree, Gold = cost, Stage = MentalBreakEvent.Phase.Acted });
                return;
            }
            if (Now >= _until) a.ChangeState(a.RestState);
        }

        // ---------------- A fistfight ----------------

        void TickBrawl(float dt)
        {
            var a = Owner;
            var foe = _foe;
            bool over = foe == null || !foe.IsAlive || foe.BrawlFoe != a || Now >= _until
                        || a.HpRatio <= BreakRules.BrawlFloor + 0.01f || foe.HpRatio <= BreakRules.BrawlFloor + 0.01f;
            if (over)
            {
                EndBrawl(a, foe);
                return;
            }
            if (a.Engage.Target != foe) a.Engage.SetTarget(foe);
            a.Engage.Tick(dt);
        }

        /// <summary>Both stop: they think worse of each other, and each rests it off.</summary>
        static void EndBrawl(Adventurer a, Adventurer foe)
        {
            a.BrawlFoe = null;
            if (foe != null && foe.BrawlFoe == a)
            {
                foe.BrawlFoe = null;
                Relationships.Add(a, foe, BreakRules.BrawlRelation, "주먹다짐");
                Relationships.Add(foe, a, BreakRules.BrawlRelation, "주먹다짐");
                EventBus.Publish(new MentalBreakEvent { Adventurer = a, Other = foe, Kind = BreakKind.Brawl, Stage = MentalBreakEvent.Phase.Over });
                if (foe.IsAlive && foe.Fsm.Current == foe.BreakState) foe.ChangeState(foe.RestState);
            }
            if (a.IsAlive) a.ChangeState(a.RestState);
        }

        // ---------------- Berserk ----------------

        void TickBerserk(float dt)
        {
            var a = Owner;
            if (Now >= _until || a.HpRatio < BreakRules.BerserkSpentAt)
            {
                a.Say("...내가, 무슨 짓을...");
                EventBus.Publish(new MentalBreakEvent { Adventurer = a, Kind = BreakKind.Berserk, Stage = MentalBreakEvent.Phase.Over });
                a.ChangeState(a.RestState);
                return;
            }
            var target = a.Engage.Target;
            if (target == null || !target.IsAlive)
            {
                target = Nearest(a);
                if (target != null) a.Engage.SetTarget(target);
            }
            if (target != null)
            {
                a.Engage.Tick(dt);
                return;
            }
            // Nobody in reach: it storms about looking for someone.
            if (a.Mover.IsMoving) return;
            _repath -= dt;
            if (_repath > 0f) return;
            _repath = 1.5f;
            a.Mover.SetDestination(AdvCtx.Town.RandomPlazaSpot(GameRandom.Shared, 10), 1f);
        }

        /// <summary>The nearest it can go for: a comrade or a monster (never the guild master, never a visitor).</summary>
        static Unit Nearest(Adventurer a)
            => UnitRegistry.FindNearest(a.Position, BreakRules.BerserkReach, a,
                (u, me) => u != me && u.IsAlive && (u is Monster || u is Adventurer { IsGuildMember: true, IsVisitor: false }) && Combat.CanReach(me, u));

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class Saved
        {
            public BreakKind kind;
            public string foe;
            public float until;
            public bool spent;
        }

        public override string SaveState() => JsonUtility.ToJson(new Saved
        {
            kind = Kind, foe = _foe != null && _foe.IsAlive ? _foe.UnitId : null, until = _until, spent = _spent,
        });

        public override void ResumeState(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<Saved>(json);
            Kind = s.kind;
            _foe = ctx.Find<Adventurer>(s.foe);
            _until = s.until;
            _spent = s.spent;
            _repath = 0f;
            var a = Owner;
            a.Berserk = Kind == BreakKind.Berserk;
            a.BrawlFoe = Kind == BreakKind.Brawl ? _foe : null;
            // As Enter: a berserker storms about faster (P4-108: lost on load).
            if (Kind == BreakKind.Berserk) a.Mover.SpeedMultiplier = 1.15f;
        }
    }
}
