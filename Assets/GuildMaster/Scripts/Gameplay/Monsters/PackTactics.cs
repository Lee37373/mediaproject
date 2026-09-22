using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Monsters
{
    /// <summary>
    /// Pure rules for skilled+ monster packs (P4-28): encircling a shared target and when to fall
    /// back to regroup. Kept free of Unity objects so they can be tested.
    /// </summary>
    public static class PackTactics
    {
        /// <summary>Below this HP ratio a skilled+ monster that is outnumbered falls back to its pack.</summary>
        public const float RegroupHp = 0.4f;

        /// <summary>
        /// Angle (radians, around the target) of attacker <paramref name="index"/>'s slot. Slots are spread
        /// evenly around the target, centred on the direction the pack comes from, and handed out in
        /// the attackers' current angular order so nobody has to cross over. Bearings are the attackers'
        /// angles as seen from the target.
        /// </summary>
        public static float EncircleAngle(IReadOnlyList<float> bearings, int index)
        {
            int n = bearings.Count;
            if (n <= 1) return bearings.Count == 1 ? bearings[0] : 0f;
            float sx = 0f, sy = 0f;
            foreach (var b in bearings) { sx += Mathf.Cos(b); sy += Mathf.Sin(b); }
            float mean = Mathf.Atan2(sy, sx);
            float mine = Relative(bearings[index], mean);
            int rank = 0;
            for (int i = 0; i < n; i++)
            {
                if (i == index) continue;
                float other = Relative(bearings[i], mean);
                if (other < mine || (Mathf.Approximately(other, mine) && i < index)) rank++;
            }
            float step = Mathf.PI * 2f / n;
            return mean + (rank - (n - 1) * 0.5f) * step;
        }

        /// <summary>Angle difference wrapped to (-π, π].</summary>
        static float Relative(float angle, float reference)
        {
            float d = Mathf.Repeat(angle - reference + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;
            return d <= -Mathf.PI ? d + Mathf.PI * 2f : d;
        }

        /// <summary>Badly hurt and more enemies close by than pack mates fighting beside it.</summary>
        public static bool ShouldRegroup(float hpRatio, int enemiesNear, int alliesNear)
            => hpRatio < RegroupHp && enemiesNear > alliesNear;
    }
}
