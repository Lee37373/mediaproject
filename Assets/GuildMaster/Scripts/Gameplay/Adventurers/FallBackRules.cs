using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// A badly hurt member of a party in a fight (P4-96, user decision 2026-10-20: "파티 뒤로 빠져 치료"). It no longer runs
    /// home alone, nor does one hurt member pull the whole party out of every fight (P4-71): it falls back behind its party,
    /// by the healer and away from the foe, out of the fight, and comes back once mended. The party pulls back together
    /// only when the fight is lost — half of it out of the fight, or nobody to heal. Pure rules; <see cref="PartyRecovery.WhenHurt"/>
    /// decides and <see cref="Adventurer"/> walks it.
    /// </summary>
    public static class FallBackRules
    {
        /// <summary>How far behind its healer (away from the foe) it stands — well inside the heal's reach (6).</summary>
        public const float BehindHealer = 2f;
        /// <summary>At least this far from the foe it fell back from.</summary>
        public const float KeepClear = 6f;
        /// <summary>No foe within this: nothing to fall back from, the fall-back is over (the party mends if it must, P4-71).</summary>
        public const float ThreatRange = 14f;
        /// <summary>How often it picks its spot again (the fight moves).</summary>
        public const float RepathSeconds = 0.5f;
        /// <summary>Stress of being driven out of the fight (running home alone added 15), once a while.</summary>
        public const float Stress = 8f;
        public const float StressEverySeconds = 30f;
        /// <summary>Pulling back together, a fit member turns on a foe this close to it (the rear guard).</summary>
        public const float RearGuardReach = 4f;

        /// <summary>Back into the fight at this health: a margin over its retreat line, but no higher than a priest heals to.</summary>
        public static float RejoinAt(float retreatThreshold)
            => Mathf.Max(retreatThreshold + 0.1f, Mathf.Min(retreatThreshold + 0.25f, Units.HealSkill.HealsBelow));

        /// <summary>The fight is lost: nobody to heal, or half the party or more out of it (badly hurt or falling back).</summary>
        public static bool PartyPullsOut(int partySize, int outOfTheFight, bool canHeal)
            => !canHeal || outOfTheFight * 2 >= partySize;

        /// <summary>
        /// Where it stands: behind its healer — or behind the party when it is the healer itself — on the side away from the
        /// foe, and no nearer the foe than <see cref="KeepClear"/>.
        /// </summary>
        public static Vector2 Spot(Vector2 self, Vector2? healer, Vector2 partyCentre, Vector2? threat)
        {
            var anchor = healer ?? partyCentre;
            if (threat == null) return anchor;
            var away = anchor - threat.Value;
            if (away.sqrMagnitude < 0.01f) away = self - threat.Value;
            if (away.sqrMagnitude < 0.01f) away = Vector2.down;
            away.Normalize();
            var spot = anchor + away * BehindHealer;
            if ((spot - threat.Value).sqrMagnitude < KeepClear * KeepClear) spot = threat.Value + away * KeepClear;
            return spot;
        }
    }
}
