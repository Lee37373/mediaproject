using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Monsters
{
    /// <summary>
    /// When a monster turns for home (the leash). Every rule about it lives here; the states only
    /// ask. Other ways a fight ends — the chase give-up and the third-party wait in
    /// <see cref="Engagement"/>, fleeing predators, regrouping — are listed in Docs/CODEMAP.md §1.2.
    /// </summary>
    public static class MonsterLeash
    {
        /// <summary>Leash multiplier while chasing a live target.</summary>
        public const float PursuitFactor = 2f;
        /// <summary>Far from home, a monster hit this recently keeps fighting whoever hit it.</summary>
        public const float UnderAttackSeconds = 3f;
        /// <summary>A monster-vs-monster fight counts as live while either side landed a blow this recently.</summary>
        public const float MonsterFightGrace = 6f;

        /// <summary>
        /// Should a fighting monster give up and go home? Pure, so the rule can be tested.
        /// <list type="bullet">
        /// <item>Raiders never do (their raid decides).</item>
        /// <item>Within the leash — the species' radius, twice that while chasing a live target (so a
        /// pursuer can be lured into another pack's territory) — it fights on.</item>
        /// <item>Beyond it, it fights on while something hit it in the last <see cref="UnderAttackSeconds"/>,
        /// or while it trades blows with another monster (<see cref="MonsterFightGrace"/>, P4-46:
        /// a lured monster is far from home by definition and used to walk off mid-brawl).
        /// Fights with the guild keep the plain rule, so a retreating member is not hunted down.</item>
        /// </list>
        /// </summary>
        public static bool ShouldReturn(float fromHome, float leashRadius, bool raiding, bool chasingLiveTarget,
                                        bool underAttack, bool monsterFightLive)
        {
            if (raiding) return false;
            float leash = leashRadius * (chasingLiveTarget ? PursuitFactor : 1f);
            return fromHome > leash && !underAttack && !monsterFightLive;
        }

        /// <summary><see cref="ShouldReturn(float,float,bool,bool,bool,bool)"/> for a fighting monster right now.</summary>
        public static bool ShouldReturn(Monster m)
        {
            var target = m.Engage.Target;
            var attacker = m.LastAttacker;
            bool underAttack = attacker != null && attacker.IsAlive && m.WasRecentlyAttackedBy(attacker, UnderAttackSeconds);
            bool monsterFightLive = target is Monster foe && foe.IsAlive
                                    && (foe.WasRecentlyAttackedBy(m, MonsterFightGrace) || m.WasRecentlyAttackedBy(foe, MonsterFightGrace));
            return ShouldReturn(Vector2.Distance(m.Position, m.HomePosition), m.Def.leashRadius,
                                m.DefaultStateOverride != null, target != null && target.IsAlive, underAttack, monsterFightLive);
        }

        /// <summary>Out of a fight (wandering), the plain radius applies.</summary>
        public static bool IsStrayed(Monster m) => Vector2.Distance(m.Position, m.HomePosition) > m.Def.leashRadius;
    }
}
