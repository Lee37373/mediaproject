using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Disasters
{
    /// <summary>The natural disasters (P4-72, user decision 2026-10-03), one for each kind of ground.</summary>
    public enum DisasterKind { Wildfire, PoisonFog, Avalanche, Sandstorm, Quake }

    /// <summary>
    /// Rules of the natural disasters (P4-72, user decisions 2026-10-03): what sets one off, how it
    /// spreads, how long it lasts, how much work it takes to deal with, and what the town pays for
    /// that. Pure numbers and checks, so the EditMode tests can hold them.
    ///
    /// Every disaster follows one pattern: a monster's skill strikes the right ground in a fight and,
    /// by chance, starts one at that spot; it spreads cell by cell up to a limit; left alone it dies
    /// down when its time is up. The guild deals with it through a quest the town posts once the
    /// guild has seen it: workers stand by a cell (or the core) and work it away.
    /// </summary>
    public static class DisasterRules
    {
        // ---- Setting one off ----
        /// <summary>Chance that a hazard skill striking the right ground starts a disaster.</summary>
        public const float TriggerChance = 0.08f;
        /// <summary>At most this many disasters at once on the map.</summary>
        public const int MaxActive = 3;
        /// <summary>No new disaster this close to one already going.</summary>
        public const float MinSpacing = 30f;

        // ---- Effects ----
        /// <summary>Share of max HP lost per game hour standing in fire / poison.</summary>
        public const float FireDamagePerHour = 0.6f;
        public const float PoisonDamagePerHour = 0.25f;
        /// <summary>Share of max HP lost when snow or a crack opens under a unit (it is thrown clear).</summary>
        public const float BuriedDamage = 0.3f;
        /// <summary>Burnt forest grows back after this many game hours.</summary>
        public const float RegrowHours = 72f;
        /// <summary>A den this close to a disaster cell is abandoned by its pack.</summary>
        public const float NestFleeDistance = 2.5f;

        // ---- The town ----
        /// <summary>A disaster cell this close to the town walls reaches the town.</summary>
        public const float TownReach = 2f;
        /// <summary>Gates and buildings this close to a disaster cell take damage while it lasts.</summary>
        public const float TownDamageReach = 4f;
        /// <summary>Share of max durability lost per game hour by what is in reach.</summary>
        public const float TownDamagePerHour = 0.08f;
        /// <summary>The day's tax is cut by this share once a disaster reached the town that day.</summary>
        public const float TaxCut = 0.3f;

        // ---- Dealing with it ----
        /// <summary>A worker this close to a cell (or the core) works it.</summary>
        public const float WorkReach = 1.6f;
        /// <summary>A disaster that ended (dealt with or died down) clears its remaining cells over this many game hours.</summary>
        public const float RecedeHours = 1f;

        /// <summary>The disaster a disaster monster's skill sets off (P4-72), null for any other skill.</summary>
        public static DisasterKind? KindOfSkill(string skillId) => skillId switch
        {
            "ember" => DisasterKind.Wildfire,
            "toxic_spit" => DisasterKind.PoisonFog,
            "frost_roar" => DisasterKind.Avalanche,
            "sand_whirl" => DisasterKind.Sandstorm,
            "quake_stomp" => DisasterKind.Quake,
            _ => null,
        };

        public static HazardKind HazardOf(DisasterKind k) => k switch
        {
            DisasterKind.Wildfire => HazardKind.Fire,
            DisasterKind.PoisonFog => HazardKind.Poison,
            DisasterKind.Avalanche => HazardKind.Snow,
            DisasterKind.Sandstorm => HazardKind.Sandstorm,
            _ => HazardKind.Fissure,
        };

        public static string NameOf(DisasterKind k) => k switch
        {
            DisasterKind.Wildfire => "산불",
            DisasterKind.PoisonFog => "독안개",
            DisasterKind.Avalanche => "눈사태",
            DisasterKind.Sandstorm => "모래폭풍",
            _ => "지진 균열",
        };

        /// <summary>What dealing with it is called (quest titles, speech).</summary>
        public static string WorkName(DisasterKind k) => k switch
        {
            DisasterKind.Wildfire => "진화",
            DisasterKind.PoisonFog => "정화",
            DisasterKind.Avalanche => "제설",
            DisasterKind.Sandstorm => "폭풍의 눈 제거",
            _ => "메우기",
        };

        /// <summary>The ground each kind needs (null: any region — a wildfire needs forest, not a region).</summary>
        public static Region? RegionOf(DisasterKind k) => k switch
        {
            DisasterKind.PoisonFog => Region.Swamp,
            DisasterKind.Avalanche => Region.Snow,
            DisasterKind.Sandstorm => Region.Desert,
            DisasterKind.Quake => Region.Wasteland,
            _ => null,
        };

        /// <summary>
        /// Worked at a core rather than cell by cell: the poison's source pool, the storm's eye. The
        /// cells go once the core is gone.
        /// </summary>
        public static bool HasCore(DisasterKind k) => k is DisasterKind.PoisonFog or DisasterKind.Sandstorm;

        /// <summary>Most cells it ever covers.</summary>
        public static int MaxCells(DisasterKind k) => k switch
        {
            DisasterKind.Wildfire => 140,
            DisasterKind.PoisonFog => 90,
            DisasterKind.Avalanche => 40,
            DisasterKind.Sandstorm => 200,
            _ => 60,
        };

        /// <summary>Game hours until it dies down by itself.</summary>
        public static float LifetimeHours(DisasterKind k) => k switch
        {
            DisasterKind.Wildfire => 20f,
            DisasterKind.PoisonFog => 30f,
            DisasterKind.Avalanche => 48f,
            DisasterKind.Sandstorm => 16f,
            _ => 40f,
        };

        /// <summary>Game hours one worker needs for a cell (or for the whole core).</summary>
        public static float WorkHours(DisasterKind k) => k switch
        {
            DisasterKind.Wildfire => 0.12f,
            DisasterKind.PoisonFog => 1.5f,
            DisasterKind.Avalanche => 0.2f,
            DisasterKind.Sandstorm => 1.2f,
            _ => 0.25f,
        };

        // ---- Spreading ----
        /// <summary>A forest cell on fire burns this long before it is burnt out.</summary>
        public const float BurnHours = 3f;
        /// <summary>Chance per game hour that a burning cell sets each forest neighbour alight.</summary>
        public const float FireSpreadPerHour = 0.2f;
        /// <summary>Poison and cracks grow by about this many cells per game hour; avalanches slide a little more for a while.</summary>
        public const float GrowCellsPerHour = 5f;
        public const float SlideHours = 6f;
        /// <summary>A fresh avalanche buries this many cells at once.</summary>
        public const int AvalancheBurst = 18;
        /// <summary>The sandstorm: radius grows from start to full over its life, the whole storm drifts.</summary>
        public const float StormStartRadius = 3f;
        public const float StormFullRadius = 8f;
        public const float StormDriftPerHour = 3f;

        /// <summary>Sandstorm radius after <paramref name="hours"/> of its life.</summary>
        public static float StormRadius(float hours)
            => Mathf.Lerp(StormStartRadius, StormFullRadius, Mathf.Clamp01(hours / (LifetimeHours(DisasterKind.Sandstorm) * 0.5f)));

        /// <summary>Whether the disaster can take this cell (its ground, nothing on it yet, outside the town).</summary>
        public static bool CanSpreadTo(DisasterKind k, TileType tile, Region region, HazardKind onIt, bool inTown, bool nearMountain)
        {
            if (inTown || !MapData.IsWalkable(tile)) return false;
            switch (k)
            {
                case DisasterKind.Wildfire: return tile == TileType.Forest && onIt == HazardKind.None;
                case DisasterKind.Sandstorm: return onIt is HazardKind.None or HazardKind.Sandstorm; // it passes over everything else
                default:
                    if (onIt != HazardKind.None || RegionOf(k) != region) return false;
                    return k != DisasterKind.Avalanche || nearMountain;
            }
        }

        /// <summary>Whether a hazard strike at a spot with this ground can start this disaster.</summary>
        public static bool GroundFits(DisasterKind k, TileType tile, Region region, bool nearMountain)
            => k switch
            {
                DisasterKind.Wildfire => tile == TileType.Forest,
                DisasterKind.Avalanche => region == Region.Snow && nearMountain,
                _ => RegionOf(k) == region,
            };

        /// <summary>How dangerous the quest reads: fire and poison hurt whoever works them, the rest mostly cost time.</summary>
        public static Data.DangerLevel DangerOf(DisasterKind k) => k switch
        {
            DisasterKind.Wildfire or DisasterKind.PoisonFog or DisasterKind.Sandstorm => Data.DangerLevel.Medium,
            _ => Data.DangerLevel.Low,
        };

        // ---- The town's reward ----
        /// <summary>
        /// What the town pays for dealing with it: more the nearer it is (it is the town's danger
        /// more than the guild's), and more for the slow, dangerous kinds.
        /// </summary>
        public static (int gold, int exp) Reward(DisasterKind k, float distanceFromTown)
        {
            float near = 1f + 2f * Mathf.Clamp01(1f - distanceFromTown / 120f);
            int baseGold = k switch
            {
                DisasterKind.Wildfire => 90,
                DisasterKind.PoisonFog => 110,
                DisasterKind.Avalanche => 70,
                DisasterKind.Sandstorm => 100,
                _ => 80,
            };
            return (Mathf.RoundToInt(baseGold * near), Mathf.RoundToInt(baseGold * 0.6f * near));
        }
    }
}
