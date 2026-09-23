using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>Why a camp was pitched: to patch up, or to sit out the dark.</summary>
    public enum CampReason { Recover, Night }

    /// <summary>
    /// Rules of camping out in the wild (P4-66, user decisions 2026-09-23): when a band far from
    /// town stops to make a fire, how fast the fire mends them, how far its light shows, and when
    /// the fire is put out. Pure numbers, so the EditMode tests can hold them.
    /// </summary>
    public static class CampRules
    {
        // ---- When ----
        /// <summary>Further from town than this, a band would rather make camp than walk home hurt.</summary>
        public const float FarFromTown = 30f;
        /// <summary>One on its own is always in danger at a fire: it only camps much further out.</summary>
        public const float FarFromTownAlone = 50f;
        public const float CampBelowHp = 0.6f;
        public const float CampAtFatigue = 70f;
        public const float CampBelowHpAlone = 0.5f;
        public const float CampAtFatigueAlone = 85f;
        /// <summary>No enemy in sight for this long (seconds) before anyone sits down.</summary>
        public const float QuietSeconds = 5f;
        /// <summary>Comrades this close join the fire.</summary>
        public const float JoinRadius = 12f;

        // ---- The fire ----
        /// <summary>How far the firelight shows (cells). Under the 14 at which a monster gives up a chase.</summary>
        public const float LightRadiusNight = 13f;
        public const float LightRadiusDay = 7f;
        /// <summary>What the fire itself lights up around it (fog of war).</summary>
        public const float FireSight = 4f;
        /// <summary>A sleeper sees next to nothing.</summary>
        public const float SleeperSight = 1.5f;
        /// <summary>Sitting round the fire before the first ones turn in (game hours): the time for talk.</summary>
        public const float SitHours = 1f;

        // ---- Mending (per game hour; the town's RestState is 25% HP, -30 fatigue) ----
        public const float SleepHealPerHour = 0.12f;
        public const float SleepFatiguePerHour = 25f;
        public const float WatchHealPerHour = 0.05f;
        public const float WatchFatiguePerHour = 8f;
        public const float SitHealPerHour = 0.06f;
        public const float SitFatiguePerHour = 10f;
        /// <summary>Game hours one member keeps watch before waking the next.</summary>
        public const float WatchHours = 2f;

        // ---- The end ----
        public const float DoneHp = 0.85f;
        public const float DoneFatigue = 25f;
        public const float MaxHours = 12f;
        /// <summary>A night camp is broken at dawn (the clock's night ends at 6).</summary>
        public const float DawnHour = 6f;

        /// <summary>Far enough from town that making camp beats the walk home.</summary>
        public static bool FarOut(float distanceToTown, bool alone)
            => distanceToTown >= (alone ? FarFromTownAlone : FarFromTown);

        /// <summary>Hurt or worn out enough to stop and mend (the weakest of the band decides).</summary>
        public static bool NeedsRest(float hpRatio, float fatigue, bool alone)
            => alone ? hpRatio < CampBelowHpAlone || fatigue >= CampAtFatigueAlone
                     : hpRatio < CampBelowHp || fatigue >= CampAtFatigue;

        /// <summary>
        /// The chance a band out at night stops for it, from its leader's caution
        /// (<see cref="Units.RouteChoice.ForParty"/>: 0.15 brave … 0.9 normal … 2.5 coward).
        /// Alone, half as likely: the dark is dangerous, but so is a fire nobody watches.
        /// </summary>
        public static float NightCampChance(float caution, bool alone)
        {
            float p = Mathf.Clamp(0.25f + 0.3f * caution, 0.3f, 0.95f);
            return alone ? p * 0.5f : p;
        }

        /// <summary>How far away a monster can see this fire.</summary>
        public static float LightRadius(bool night) => night ? LightRadiusNight : LightRadiusDay;

        /// <summary>Everyone mended (and, for a night camp, the night over), or it has gone on long enough.</summary>
        public static bool Done(CampReason reason, float lowestHp, float highestFatigue, bool night, float hoursCamped)
        {
            if (hoursCamped >= MaxHours) return true;
            if (lowestHp < DoneHp || highestFatigue > DoneFatigue) return false;
            return reason != CampReason.Night || !night;
        }

        /// <summary>Which member keeps watch <paramref name="hoursOfWatch"/> into the night watches (round robin).</summary>
        public static int WatchIndex(float hoursOfWatch, int members)
            => members <= 0 ? -1 : Mathf.FloorToInt(Mathf.Max(0f, hoursOfWatch) / WatchHours) % members;
    }
}
