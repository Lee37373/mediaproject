using System;
using UnityEngine;

namespace GuildMaster.Units
{
    /// <summary>
    /// Tier-dependent target scoring shared by adventurers and monsters (higher score = better).
    /// Every tier: nearer is better, keep the current target unless something is clearly better,
    /// strike back at whoever hits us, avoid piling onto one victim. Higher tiers add factors
    /// (see <see cref="CombatTier"/>).
    /// </summary>
    public static class TargetSelector
    {
        const float Stickiness = 1.5f;
        const float CrowdRadius = 3f;

        /// <summary>Best candidate within radius passing the filter (and not ignored by the engagement), or null.</summary>
        public static Unit Pick(Unit self, float radius, Func<Unit, bool> filter, Engagement engage = null)
        {
            Unit best = null;
            float bestScore = float.NegativeInfinity;
            float r2 = radius * radius;
            foreach (var u in UnitRegistry.All)
            {
                if (u == null || u == self || !u.IsAlive) continue;
                if ((u.Position - self.Position).sqrMagnitude > r2) continue;
                if (filter != null && !filter(u)) continue;
                if (engage != null && engage.IsIgnoring(u)) continue;
                if (!u.IsTargetableBy(self)) continue;
                float s = Score(self, u, self.Tier, engage?.Target);
                if (s > bestScore) { bestScore = s; best = u; }
            }
            return best;
        }

        public static float Score(Unit self, Unit u, CombatTier tier, Unit current)
        {
            float score = -Vector2.Distance(self.Position, u.Position);
            if (u == current) score += Stickiness;
            if (self.WasRecentlyAttackedBy(u, 4f)) score += tier == CombatTier.Novice ? 1.5f : 4f;

            // Allies already fighting this candidate (same side as us, targeting it).
            int crowd = UnitRegistry.CountInRadius(u.Position, CrowdRadius, a => IsAlly(self, a) && a.CurrentTarget == u);
            bool finishable = u.Hp <= self.Stats.Attack * 2.5f;
            if (tier >= CombatTier.Heroic && finishable) score += 3f + crowd * 1f;   // focus fire on a kill
            else
            {
                // Spread out: a mild cost per attacker, and a heavy one past the free "slots" around a
                // victim, so a pack splits up instead of everyone queueing behind the same back.
                int slots = tier >= CombatTier.Veteran ? 1 : 2;
                score -= crowd * (tier >= CombatTier.Veteran ? 1.5f : 0.8f) + Mathf.Max(0, crowd - slots) * 3f;
            }

            if (tier >= CombatTier.Skilled) score += (1f - u.HpRatio) * 3f;           // finish the wounded

            var victim = u.CurrentTarget;
            if (tier >= CombatTier.Veteran && victim != null && IsAlly(self, victim))
            {
                // Guard the back line: enemies going after our shooters or badly hurt allies.
                if (victim.Stats.IsRanged || victim.HpRatio < 0.5f) score += 3f;
                if (tier >= CombatTier.Heroic && victim.HpRatio < 0.35f) score += 3f;  // cover a retreating ally
            }
            return score;
        }

        /// <summary>Same side as <paramref name="self"/> (not self, not hostile either way, same faction).</summary>
        public static bool IsAlly(Unit self, Unit other)
            => other != null && other != self && other.IsAlive && other.Faction == self.Faction
               && !self.IsHostileTo(other) && !other.IsHostileTo(self);
    }
}
