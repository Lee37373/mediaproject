using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Units
{
    /// <summary>
    /// Close-quarters footwork for melee units waiting on their next blow (P4-47): context steering
    /// in the style of "interest minus danger" per direction. Each of <see cref="Directions"/>
    /// headings gets an interest (keep the ideal distance, side-step round the target or walk to the
    /// encircling slot) and a danger measured at a probe point (blocked terrain, hostile telegraphs,
    /// leaving reach, crowding other units, standing next to a second melee enemy), spread half onto
    /// neighbouring headings. The best heading wins; a weak best means "hold still".
    /// Only picks a heading — <see cref="PathFollower.Steer"/> moves the unit. Target choice, retreats
    /// and long-range movement (A*) are untouched.
    /// </summary>
    public static class LocalSteering
    {
        public const int Directions = 16;
        /// <summary>Share of the walking speed used for footwork.</summary>
        public const float SpeedFactor = 0.5f;
        /// <summary>How far past the ideal distance a Veteran+ unit steps back right after a blow.</summary>
        public const float HitAndRunStep = 0.5f;

        const float Probe = 0.6f;
        const float CrowdRadius = 1.0f;
        const float FlankRadius = 1.4f;
        const float StrafeWeight = 0.6f;
        const float HoldThreshold = 0.15f;

        /// <summary>Debug A/B switch (<c>DebugCommands.Steering</c>): off = the old stand-and-strike.</summary>
        public static bool Enabled = true;

        static readonly Vector2[] s_dirs = BuildDirections();
        static readonly float[] s_danger = new float[Directions];
        static readonly List<Unit> s_near = new();

        static Vector2[] BuildDirections()
        {
            var dirs = new Vector2[Directions];
            for (int i = 0; i < Directions; i++)
            {
                float a = i * Mathf.PI * 2f / Directions;
                dirs[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }
            return dirs;
        }

        /// <summary>
        /// Heading for this frame, scaled 0..1 by how much it is wanted (zero = hold still).
        /// <paramref name="side"/> is the unit's circling direction (+1 / -1); <paramref name="slot"/>
        /// replaces circling when a pack assigns an encircling spot. <paramref name="sideBlocked"/> is
        /// true when the circling direction is walled off, so the caller can turn the other way.
        /// </summary>
        public static Vector2 Choose(Unit self, Unit target, Vector2? slot, int side, float backstep, out bool sideBlocked)
        {
            sideBlocked = false;
            var world = Services.Get<WorldService>();
            Services.TryGet<TelegraphService>(out var telegraphs);
            Vector2 pos = self.Position, tpos = target.Position;
            var toTarget = tpos - pos;
            float dist = toTarget.magnitude;
            var radial = dist > 1e-4f ? toTarget / dist : Vector2.right;
            var tangent = new Vector2(-radial.y, radial.x) * side;

            float reach = self.Stats.AttackRange + Combat.ContactPadding + target.Radius;
            float touching = self.Radius + target.Radius;
            float ideal = Mathf.Max(touching, reach * 0.75f) + backstep;
            float outer = Mathf.Max(reach - 0.05f, ideal + 0.2f);
            // >0: close in, <0: back off.
            float radialWant = Mathf.Clamp((dist - ideal) / 0.35f, -1f, 1f);

            Vector2 toSlot = Vector2.zero;
            bool useSlot = slot is Vector2 s && (toSlot = s - pos).sqrMagnitude > 0.3f * 0.3f;
            if (useSlot) toSlot.Normalize();

            UnitRegistry.Collect(pos, CrowdRadius + Probe + 0.5f, u => u != self && u != target && u.IsAlive, s_near);

            for (int i = 0; i < Directions; i++)
            {
                var dir = s_dirs[i];
                var p = pos + dir * Probe;
                float danger = 0f;
                if (!world.Grid.IsWalkable(GridMap.WorldToCell(p), self.PassesGates) || !world.HasClearLine(pos, p)) danger = 2f;
                else
                {
                    float pd = Vector2.Distance(p, tpos);
                    if (pd > outer) danger += Mathf.Clamp01((pd - outer) / 0.3f) * 0.8f;
                    if (pd < touching) danger += 1f;
                    if (telegraphs != null && telegraphs.IsInsideHostileTelegraph(p, self)) danger += 1.5f;
                    foreach (var o in s_near)
                    {
                        var d = o.Position - pos;
                        float dd = d.magnitude;
                        if (dd < 1e-4f) continue;
                        float along = Vector2.Dot(dir, d / dd);
                        if (along > 0f) danger += along * Mathf.Clamp01(1f - (dd - self.Radius - o.Radius) / CrowdRadius) * 0.8f;
                        // A second melee enemy right beside the new spot: we would be fighting two.
                        if (!o.Stats.IsRanged && (o.IsHostileTo(self) || self.IsHostileTo(o)) && Vector2.Distance(p, o.Position) < FlankRadius)
                            danger += 0.4f;
                    }
                }
                s_danger[i] = danger;
            }

            int best = -1;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < Directions; i++)
            {
                var dir = s_dirs[i];
                float interest = radialWant * Vector2.Dot(dir, radial) * Mathf.Abs(radialWant) + (useSlot
                    ? Vector2.Dot(dir, toSlot)
                    : StrafeWeight * Vector2.Dot(dir, tangent));
                interest = Mathf.Max(0f, interest);
                // Danger spreads half onto the neighbouring headings (keeps clear of walls and crowds).
                float danger = Mathf.Max(s_danger[i], 0.5f * Mathf.Max(s_danger[(i + 1) % Directions], s_danger[(i + Directions - 1) % Directions]));
                float score = interest - danger;
                if (score > bestScore) { bestScore = score; best = i; }
            }

            if (!useSlot)
            {
                int ti = Mathf.RoundToInt(Mathf.Atan2(tangent.y, tangent.x) / (Mathf.PI * 2f / Directions));
                sideBlocked = s_danger[(ti % Directions + Directions) % Directions] >= 1f;
            }
            return best < 0 || bestScore < HoldThreshold ? Vector2.zero : s_dirs[best] * Mathf.Clamp01(bestScore);
        }
    }
}
