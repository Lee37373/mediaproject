using System;
using UnityEngine;

namespace GuildMaster.Units
{
    /// <summary>
    /// Tier-dependent target scoring shared by adventurers and monsters (higher score = better).
    /// Every tier: nearer is better, keep the current target unless something is clearly better,
    /// strike back at whoever hits us. Novice and up also avoid piling onto one victim
    /// (<see cref="CombatTiers.CrowdPenalty"/>); higher tiers add factors (see <see cref="CombatTier"/>).
    /// </summary>
    public static class TargetSelector
    {
        const float Stickiness = 1.5f;
        const float CrowdRadius = 3f;
        /// <summary>Score bonus for the target a leader called (P4-27); outweighs a few units of distance.</summary>
        public const float CalledTargetBonus = 4f;
        /// <summary>Penalty for a target that cannot fight back: worth this many cells of distance.</summary>
        public const float BaitPenalty = 8f;

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
            if (self.WasRecentlyAttackedBy(u, 4f)) score += tier <= CombatTier.Novice ? 1.5f : 4f;
            // Someone who cannot fight back (the guild master) is bait: anything that can fight comes
            // first (user report 2026-09-22: a lured monster and the den it was led into both went
            // for the master and never for each other, then walked home once it left).
            if (u.Stats.Attack <= 0f) score -= BaitPenalty;
            // Instinct: the nearest (or whoever hits it) — no counting of allies at all.
            if (tier == CombatTier.Instinct) return score;

            // Allies already fighting this candidate (same side as us, targeting it).
            int crowd = UnitRegistry.CountInRadius(u.Position, CrowdRadius, a => IsAlly(self, a) && a.CurrentTarget == u);
            bool finishable = u.Hp <= self.Stats.Attack * 2.5f;
            if (u == self.CalledTarget) score += CalledTargetBonus;                   // the leader's call: pile on
            else if (tier >= CombatTier.Heroic && finishable) score += 3f + crowd * 1f; // focus fire on a kill
            else score -= CombatTiers.CrowdPenalty(tier, crowd);                     // spread out

            if (tier >= CombatTier.Skilled) score += (1f - u.HpRatio) * 3f;           // finish the wounded

            var victim = u.CurrentTarget;
            if (victim != null && IsAlly(self, victim))
            {
                // Guard the back line: enemies going after our shooters (front-liners from Skilled,
                // everyone from Veteran) or badly hurt allies (Veteran).
                if (victim.IsBackLine && (tier >= CombatTier.Veteran || self.GuardsBackLine)) score += 3f;
                else if (tier >= CombatTier.Veteran && victim.HpRatio < 0.5f) score += 3f;
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
