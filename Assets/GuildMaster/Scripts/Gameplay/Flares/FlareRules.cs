using UnityEngine;

namespace GuildMaster.Flares
{
    /// <summary>
    /// Flares (P4-94, user decisions 2026-10-20): made at the apothecary and sold at the counter, carried by members,
    /// shot without being told the moment a foe they are fighting slips out of sight (the dragon on the wing, an archer in
    /// a forest). Where one bursts the fog lifts round it for a while and the guild sees whatever stands in the light.
    /// The numbers are here; <see cref="FlareService"/> keeps the burning ones.
    /// </summary>
    public static class FlareRules
    {
        public const string ItemId = "flare";

        /// <summary>How far round its burst a flare lights (cells): wider than anyone's own sight (7–9).</summary>
        public const float Radius = 12f;
        /// <summary>How long one burns (combat seconds, with the game speed like every fight timer).</summary>
        public const float BurnSeconds = 30f;
        /// <summary>The shot's flight from the shooter up to where it bursts.</summary>
        public const float FlightSeconds = 0.8f;
        /// <summary>A member shoots one at most this far from itself (so it stands in the light too).</summary>
        public const float MaxThrow = 10f;
        /// <summary>A guild unit this close to something in a flare's light sees it, whatever lies between.</summary>
        public const float SeeLitWithin = 20f;
        /// <summary>A lost foe whose last-seen spot is this near a burning flare's centre is in light already: no second shot.</summary>
        public const float CoveredWithin = Radius * 0.5f;
        /// <summary>One member's wait between two shots.</summary>
        public const float CooldownSeconds = 8f;
        /// <summary>How many a member buys up to at the counter (as potions).</summary>
        public const int Carry = 2;
        /// <summary>A foe that is not hostile by nature is worth a flare when it hit this member this recently.</summary>
        public const float ThreatMemorySeconds = 10f;

        /// <summary>Where a member at <paramref name="from"/> sends a flare after a foe last seen at <paramref name="lastSeen"/>.</summary>
        public static Vector2 AimPoint(Vector2 from, Vector2 lastSeen)
        {
            var d = lastSeen - from;
            return d.sqrMagnitude <= MaxThrow * MaxThrow ? lastSeen : from + d.normalized * MaxThrow;
        }

        /// <summary>A flare burning at <paramref name="centre"/> lights <paramref name="point"/>.</summary>
        public static bool Lights(Vector2 centre, Vector2 point) => (point - centre).sqrMagnitude <= Radius * Radius;

        /// <summary>
        /// A member shoots one when it carries one, its last shot is <see cref="CooldownSeconds"/> behind it, the foe's
        /// last-seen spot is not lit already, and the foe is a threat (hostile, or hit it lately).
        /// </summary>
        public static bool WouldFire(int carried, float now, float readyAt, bool alreadyLit, bool threat)
            => carried > 0 && now >= readyAt && !alreadyLit && threat;
    }
}
