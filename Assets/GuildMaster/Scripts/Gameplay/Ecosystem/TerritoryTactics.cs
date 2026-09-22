using GuildMaster.Core;
using GuildMaster.Monsters;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Ecosystem
{
    /// <summary>
    /// Luring with territories (P4-18). A pursuer follows right behind us, so we cannot make it cross a
    /// territory we avoid ourselves. Instead we clip the edge of another species' territory on the way
    /// to where we are going: residents strike whoever is inside — the pursuer included — and its
    /// retaliation scoring (TargetSelector) turns it on them while we keep running.
    /// Adventurers only use nests the guild knows (assuming the species' largest territory);
    /// monsters know their surroundings.
    /// </summary>
    public static class TerritoryTactics
    {
        const float MaxSearch = 16f;
        /// <summary>Waypoint depth inside the territory, as a fraction of its radius from the centre.</summary>
        const float EdgeDepth = 0.6f;
        const int Samples = 16;
        /// <summary>Longest acceptable extra walk for the lure.</summary>
        public const float MaxDetour = 12f;

        /// <summary>
        /// Waypoint for <paramref name="self"/>, fleeing <paramref name="pursuer"/> towards
        /// <paramref name="destination"/>, inside the edge of a territory whose residents would attack the
        /// pursuer (not our own or the pursuer's pack). False if none is worth the detour.
        /// </summary>
        public static bool FindLurePoint(Unit self, Unit pursuer, Vector2 destination, bool knownNestsOnly, out Vector2 point, out Nest via)
        {
            point = default;
            via = null;
            if (self == null || pursuer == null || !Services.TryGet<EcosystemManager>(out var eco)) return false;
            var world = Services.Get<WorldService>();
            var pursuerSpecies = (pursuer as Monster)?.Def;
            // Residents attack guild units and other species; a nest-less non-monster pursuer is ignored.
            if (pursuer.Faction != Faction.Guild && pursuerSpecies == null) return false;
            float best = float.MaxValue;
            Services.TryGet<GuildIntel>(out var intel);
            foreach (var n in eco.Nests)
            {
                // Adventurers go by what the guild saw: last seen owners, skip nests last seen empty.
                bool byIntel = knownNestsOnly && intel != null;
                if (knownNestsOnly && !n.Discovered) continue;
                var species = byIntel ? intel.SpeciesOf(n) : n.Species;
                bool empty = byIntel ? intel.Get(n) is { Observed: true, LastCount: 0 } : n.IsEmpty;
                if (empty) continue;
                // Residents attack every other pack, same species included. Monsters know the packs;
                // adventurers cannot tell which nest a pursuer came from, so they avoid its species.
                if (byIntel ? species == pursuerSpecies
                            : n == (self as Monster)?.Home || n == (pursuer as Monster)?.Home) continue;
                float radius = knownNestsOnly ? species.territoryRadius * 1.4f : n.TerritoryRadius;
                if (radius <= 0f || Vector2.Distance(self.Position, n.WorldPosition) > MaxSearch) continue;
                for (int k = 0; k < Samples; k++)
                {
                    float ang = k * Mathf.PI * 2f / Samples;
                    var candidate = n.WorldPosition + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (radius * EdgeDepth);
                    float detour = Detour(self.Position, candidate, destination);
                    if (!IsGoodLure(self.Position, pursuer.Position, candidate, detour)) continue;
                    if (!world.Grid.TryFindNearestWalkable(GridMap.WorldToCell(candidate), 1, out var cell)) continue;
                    if (detour < best) { best = detour; point = GridMap.CellToWorld(cell); via = n; }
                }
            }
            return via != null;
        }

        /// <summary>Extra distance for going via <paramref name="waypoint"/> instead of straight (pure).</summary>
        public static float Detour(Vector2 from, Vector2 waypoint, Vector2 to)
            => Vector2.Distance(from, waypoint) + Vector2.Distance(waypoint, to) - Vector2.Distance(from, to);

        /// <summary>We reach the waypoint before the pursuer, and it is not too far out of the way (pure, tested).</summary>
        public static bool IsGoodLure(Vector2 self, Vector2 pursuer, Vector2 waypoint, float detour)
            => detour <= MaxDetour && Vector2.Distance(self, waypoint) < Vector2.Distance(pursuer, waypoint);

        // ---------------- Finishing the lure with a skill (P4-19) ----------------

        /// <summary>How close behind the pursuer has to be for a break-away to be worth the skill.</summary>
        public const float CloseBehind = 3f;
        /// <summary>How near the lure point we must be before breaking away.</summary>
        public const float ArrivedDistance = 2f;

        /// <summary>
        /// We have led the pursuer to the spot and it is right behind us (pure, tested). Judged by
        /// the lure point rather than the territory, so nothing the guild has not seen is read.
        /// </summary>
        public static bool ShouldBreakAway(Vector2 self, Vector2 lurePoint, Vector2 pursuer)
            => Vector2.Distance(self, lurePoint) <= ArrivedDistance && Vector2.Distance(self, pursuer) <= CloseBehind;

        /// <summary>
        /// Walking away is not enough on its own — the pursuer is on our heels and follows us right
        /// back out (P4-18's known limit). A skill breaks the thread: slip out of sight so it is
        /// left standing among the residents, or hop clear so it has to cross the ground on foot.
        /// </summary>
        public static bool TryBreakAway(Unit self, Unit pursuer, Vector2 lurePoint, Vector2 onward)
        {
            if (self == null || pursuer == null || !pursuer.IsAlive) return false;
            if (!ShouldBreakAway(self.Position, lurePoint, pursuer.Position)) return false;
            if (self.TryUseSkill<StealthSkill>(pursuer)) return true;
            var warp = self.FindReadySkill<WarpSkill>();
            return warp != null && warp.WarpToPoint(self, onward);
        }

        /// <summary>Closest live unit chasing <paramref name="self"/>: its last attacker, or anything targeting it.</summary>
        public static Unit FindPursuer(Unit self, float radius = 10f)
        {
            if (self.LastAttacker != null && self.LastAttacker.IsAlive && Time.time - self.LastAttackedTime < 5f
                && Vector2.Distance(self.Position, self.LastAttacker.Position) <= radius)
                return self.LastAttacker;
            return UnitRegistry.FindNearest(self.Position, radius, u => u != self && u.CurrentTarget == self);
        }
    }
}
