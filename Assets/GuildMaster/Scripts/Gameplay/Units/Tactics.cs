using GuildMaster.Core;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Units
{
    /// <summary>
    /// Position scoring for ranged combat: firing spots with line of sight, kiting away from
    /// melee threats, and cover from enemy shooters (trees / hard cover between us and them).
    /// </summary>
    public static class Tactics
    {
        const int RingSamples = 12;

        static WorldService World => Services.Get<WorldService>();

        /// <summary>Nearest living hostile melee unit within radius.</summary>
        public static Unit NearestMeleeThreat(Unit self, float radius)
            => UnitRegistry.FindNearest(self.Position, radius, u => u != self && !u.Stats.IsRanged && (u.IsHostileTo(self) || self.IsHostileTo(u)));

        /// <summary>Enemy shooter currently targeting us (or that hit us recently), if any.</summary>
        public static Unit EnemyShooter(Unit self)
        {
            if (self.LastAttacker != null && self.LastAttacker.IsAlive && self.LastAttacker.Stats.IsRanged && Time.time - self.LastAttackedTime < 4f)
                return self.LastAttacker;
            return UnitRegistry.FindNearest(self.Position, 9f, u => u.Stats.IsRanged && u.CurrentTarget == self);
        }

        /// <summary>
        /// Best spot around the target at ~80% range with line of sight, avoiding melee enemies,
        /// preferring cover from enemy shooters and short travel.
        /// </summary>
        public static bool FindFiringPosition(Unit self, Unit target, out Vector2 best)
        {
            best = self.Position;
            var world = World;
            var shooter = EnemyShooter(self);
            Services.TryGet<TelegraphService>(out var telegraphs);
            float bestScore = float.NegativeInfinity;
            float baseAngle = Mathf.Atan2(self.Position.y - target.Position.y, self.Position.x - target.Position.x);

            for (int ring = 0; ring < 2; ring++)
            {
                float dist = self.Stats.AttackRange * (ring == 0 ? 0.8f : 0.55f);
                for (int i = 0; i < RingSamples; i++)
                {
                    float a = baseAngle + (i % 2 == 0 ? 1 : -1) * (i + 1) / 2 * (Mathf.PI * 2f / RingSamples);
                    var cand = target.Position + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * dist;
                    if (!world.Grid.IsWalkable(GridMap.WorldToCell(cand))) continue;
                    if (!world.HasLineOfSight(cand, target.Position, 1)) continue;

                    float score = -Vector2.Distance(self.Position, cand) * 0.6f;
                    score -= 4f * UnitRegistry.CountInRadius(cand, 2f, u => !u.Stats.IsRanged && u.IsHostileTo(self));
                    if (shooter != null && shooter != target && !world.HasLineOfSight(shooter.Position, cand, 0)) score += 2.5f;
                    if (telegraphs != null && telegraphs.IsInsideHostileTelegraph(cand, self)) score -= 20f;
                    if (score > bestScore) { bestScore = score; best = cand; }
                }
            }
            return bestScore > float.NegativeInfinity;
        }

        /// <summary>
        /// Kite step: move away from a melee threat while staying close to firing range of the target.
        /// </summary>
        public static bool FindKitePosition(Unit self, Unit threat, Unit target, float step, out Vector2 best)
        {
            best = self.Position;
            var world = World;
            var away = (self.Position - threat.Position).normalized;
            if (away == Vector2.zero) away = Vector2.right;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < 10; i++)
            {
                float ang = (i % 2 == 0 ? 1 : -1) * (i + 1) / 2 * 30f;
                var dir = (Vector2)(Quaternion.Euler(0, 0, ang) * away);
                var cand = self.Position + dir * step;
                if (!world.Grid.IsWalkable(GridMap.WorldToCell(cand)) || !world.HasClearLine(self.Position, cand)) continue;

                float score = Vector2.Distance(cand, threat.Position) * 2f;
                if (target != null)
                {
                    float d = Vector2.Distance(cand, target.Position);
                    if (d > self.Stats.AttackRange) score -= (d - self.Stats.AttackRange) * 3f;
                    if (world.HasLineOfSight(cand, target.Position, 1)) score += 1.5f;
                }
                score -= 3f * UnitRegistry.CountInRadius(cand, 1.8f, u => u != threat && !u.Stats.IsRanged && u.IsHostileTo(self));
                if (score > bestScore) { bestScore = score; best = cand; }
            }
            return bestScore > float.NegativeInfinity;
        }
    }
}
