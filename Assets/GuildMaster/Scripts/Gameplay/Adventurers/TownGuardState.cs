using System.Collections.Generic;
using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Town;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Guild members in town drop what they are doing to fight monsters inside the walls — e.g. one
    /// chased home by a retreating member (user bug report: nobody in town attacked it). Only monsters
    /// the guild can see count (information rule). Raids are left to <see cref="DefendState"/>. One outside the walls the town
    /// cannot take on is not gone out to (P4-108, <see cref="BeyondUs"/>).
    /// When the town is quiet again the member resumes the state it was in (<see cref="Next"/>).
    /// </summary>
    public class TownGuardState : State<Adventurer>
    {
        const float CalmSeconds = 2f;
        const float CheckInterval = 0.5f;
        /// <summary>Hurt members only fight back when hit themselves.</summary>
        const float MinHpToJoin = 0.5f;

        public State<Adventurer> Next { get; internal set; }
        float _calm;
        float _repath;

        public override string Label => "마을 경비";

        /// <summary>
        /// Called from town states every tick (cheap: throttled per adventurer). Switches to guarding
        /// when a visible monster is inside the town; returns true if it did.
        /// </summary>
        public static bool TryStart(Adventurer a, State<Adventurer> resume, ref float timer, float dt)
        {
            timer -= dt;
            if (timer > 0f) return false;
            timer = CheckInterval;
            if (!a.IsAlive || a.IsVisitor || !a.IsGuildMember) return false;
            // By a monster, or by a comrade gone berserk (P4-104d).
            bool hitNow = (a.LastAttacker is Monster || a.LastAttacker is Adventurer { Berserk: true }) && a.WasRecentlyAttackedBy(a.LastAttacker, 3f);
            // A raid the guild has seen is the defence's (DefendState) — but one hit fights back or runs even then (P4-100,
            // user decision 2026-10-21: members resting under half health were left out of the defence and killed lying down).
            if (!hitNow && Services.TryGet<WaveManager>(out var waves) && waves.RaidSighted) return false;
            // Hit, too hurt to fight and nothing to drink: it runs for cover (P4-100).
            if (TakeCoverState.TryStart(a, resume)) return true;
            // Too hurt to fight at all and not hit (it would retreat right away): stay put and let others handle it. Hit with a
            // potion, it turns on its attacker and drinks (CheckRetreat).
            if (!hitNow && (a.HpRatio < a.RetreatThreshold || a.HpRatio < MinHpToJoin)) return false;
            if (!hitNow && FindIntruder(a) == null) return false;
            a.TownGuardState.Next = resume;
            a.ChangeState(a.TownGuardState);
            return true;
        }

        /// <summary>
        /// Nearest living monster the guild currently sees inside the walls, or breaking a gate from
        /// outside (user report 2026-09-22: a stone slime that chased a member home bashed the gate
        /// while everyone inside stood about).
        /// </summary>
        internal static Unit FindIntruder(Adventurer a)
        {
            var town = AdvCtx.Town;
            Services.TryGet<FogOfWar>(out var fog);
            // One it cannot reach (the dragon on the wing, for a sword) it leaves to those who can (P4-93).
            return UnitRegistry.FindNearest(a.Position, 60f,
                u => u is Monster m && m.IsAlive && IsIntruder(town, m) && Combat.CanReach(a, m) && (fog == null || fog.IsVisibleRaw(m.Position))
                     // A comrade gone berserk in town (P4-104d), close enough to see.
                     || u is Adventurer && a.IsBerserkNear(u) && town.IsInTown(u.Position));
        }

        /// <summary>Just outside the wall counts too once it fights us there: it stops bashing the gate then.</summary>
        const float WallReach = 4f;
        /// <summary>The dragon on the wing burns the town from beyond the wall (P4-93): this close, it is an intruder.</summary>
        const float DragonReach = 10f;

        static bool IsIntruder(TownService town, Monster m)
        {
            if (town.IsInTown(m.Position) || m.IsBreakingGate) return true;
            if (m.IsDragon && m.IsAirborne && town.WithinReachOfTown(m.Position, DragonReach)) return true;
            // One the town cannot take on is left outside (P4-108): nobody goes out to it.
            return m.CombatEngagement?.Target is Unit t && t.Faction == Faction.Guild && town.WithinReachOfTown(m.Position, WallReach)
                   && !BeyondUs(m);
        }

        // ---------------- Too strong to go out to (P4-108) ----------------

        /// <summary>Monsters this close to one outside the walls are reckoned to fight beside it.</summary>
        const float WithItRadius = 8f;

        static readonly List<CombatStats> s_band = new();
        static readonly List<float> s_bandHp = new();
        static readonly Dictionary<Monster, bool> s_beyond = new();
        static int s_beyondFrame = -1;

        /// <summary>
        /// A monster outside the walls the members in town could not take on (P4-108, user decision 2026-10-27 "못 이기면 성
        /// 안으로": Lv2–5 members traded blows for hours with Lv13 goblins at the wall, neither side going down). The free hunt's
        /// own judgment (<see cref="FreeHunt.CanTake"/>, with what the guild knows of the kind, P4-107): the fit members in town
        /// against it and those the guild sees beside it. Nobody goes out to it, and one it hits pulls back into the town
        /// (<see cref="TakeCoverState"/>); inside the walls or at the gate it is fought all the same. Worked out once a frame.
        /// </summary>
        internal static bool BeyondUs(Monster m)
        {
            if (m == null || !m.IsAlive || m.Def == null || m.IsDragon) return false;
            var town = AdvCtx.Town;
            if (town.IsInTown(m.Position) || m.IsBreakingGate) return false;
            if (s_beyondFrame != Time.frameCount)
            {
                s_beyondFrame = Time.frameCount;
                s_beyond.Clear();
            }
            if (s_beyond.TryGetValue(m, out bool beyond)) return beyond;

            s_band.Clear();
            s_bandHp.Clear();
            if (Services.TryGet<GuildRoster>(out var roster))
                foreach (var a in roster.Members)
                {
                    if (a == null || !a.IsAlive || a.IsVisitor || !a.IsGuildMember || a.HpRatio < a.RetreatThreshold || !town.IsInTown(a.Position)) continue;
                    var st = a.Stats;
                    st.Attack *= Bestiary.BestiaryService.KnownAttackFactor(m.Def, a.Data.cls);
                    s_band.Add(st);
                    s_bandHp.Add(a.Hp);
                }
            var foe = m.Stats;
            foe.Attack *= Bestiary.BestiaryService.KnownTakenFactor(m.Def);
            Services.TryGet<FogOfWar>(out var fog);
            int seen = UnitRegistry.CountInRadius(m.Position, WithItRadius, fog,
                static (u, f) => u is Monster o && o.IsAlive && !o.IsDragon && (f == null || f.IsVisibleRaw(o.Position)));
            beyond = s_band.Count == 0 || !FreeHunt.CanTake(s_band, s_bandHp, foe, FreeHunt.ExpectedEnemies(seen, m.Def, 0));
            s_beyond[m] = beyond;
            return beyond;
        }

        /// <summary>Hit just now, in town, by one the town cannot take on (<see cref="BeyondUs"/>).</summary>
        internal static bool Outmatched(Adventurer a)
            => TakeCoverState.HitNow(a) && a.LastAttacker is Monster m && AdvCtx.Town.IsInTown(a.Position) && BeyondUs(m);

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class Saved { public string next; }

        public override string SaveState() => JsonUtility.ToJson(new Saved { next = Adventurer.NameOf(Next) });

        public override void ResumeState(string json, SaveContext ctx)
        {
            Next = Owner.StateNamed(JsonUtility.FromJson<Saved>(json).next);
            _calm = CalmSeconds;
            _repath = 0f;
        }

        public override void Enter()
        {
            _calm = CalmSeconds;
            _repath = 0f;
            Owner.Say(FindIntruder(Owner) is Adventurer ? "멈춰! 정신 차려!" : "마을에 몬스터가 들어왔다!");
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            // Badly hurt inside the walls with nothing to drink (P4-100): it runs for cover, not "home" past the intruder.
            if (TakeCoverState.TryStart(a, Next)) return;
            if (a.CheckRetreat(wearinessCounts: false)) return;
            var town = AdvCtx.Town;
            // Not one beyond the town that has stepped back outside the walls, though it hit us (P4-108: not followed out).
            bool Allowed(Unit u) => (u is Monster m && IsIntruder(town, m)) || a.WasRecentlyAttackedBy(u) && !(u is Monster o && BeyondUs(o))
                                    || a.IsBerserkNear(u);
            // Monsters inside the walls are targets even if not aggressive (e.g. a fleeing wolf).
            if (a.HandleCombat(dt, u => u is Monster m && m.IsAlive && IsIntruder(town, m), Allowed))
            {
                _calm = CalmSeconds;
                return;
            }

            var intruder = FindIntruder(a);
            if (intruder != null)
            {
                _calm = CalmSeconds;
                _repath -= dt;
                if (_repath > 0f && a.Mover.IsMoving) return;
                _repath = 0.8f;
                a.Mover.SetDestination(intruder.Position, a.Stats.AttackRange * 0.8f);
                return;
            }

            // Clear what the intruders dropped inside the walls (P4-21).
            if (a.Looting.Tick(dt, AdvCtx.TownLootZone)) { _calm = CalmSeconds; return; }

            _calm -= dt;
            if (_calm > 0f) return;
            a.Engage.Clear();
            a.ChangeState(Next ?? a.IdleState);
        }

        public override void Exit() => Owner.Mover.SpeedMultiplier = 1f;
    }
}
