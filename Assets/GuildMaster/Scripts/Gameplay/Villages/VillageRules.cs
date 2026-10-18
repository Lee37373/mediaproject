using UnityEngine;

namespace GuildMaster.Villages
{
    /// <summary>
    /// The numbers of the other villages (P4-84, user decisions 2026-10-18) in one place, with the small pure
    /// rules built on them (covered by EditMode tests).
    /// </summary>
    public static class VillageRules
    {
        // ---- Where (VillagePlacement) ----
        /// <summary>Villages per outer region at the start: one on the inner land, one beyond it.</summary>
        public const int PerRegion = 2;
        /// <summary>The inner village lies this far from the town centre at least (out of the grassland round the town)...</summary>
        public const float InnerMin = 60f;
        /// <summary>...and within the inner land (<see cref="Ecosystem.EcosystemManager.InnerEdge"/>).</summary>
        public const float InnerMax = 180f;
        /// <summary>The outer village lies at least this far out, clear of the inner edge.</summary>
        public const float OuterMin = 190f;
        /// <summary>Villages keep this far apart (cells).</summary>
        public const float Spacing = 60f;
        /// <summary>And this far from every den site, lairs included (cells).</summary>
        public const float NestClearance = 15f;
        /// <summary>And this far from the map edge, where newcomer packs walk in.</summary>
        public const int EdgeMargin = 12;
        /// <summary>Size of the icon on the map (cells, square): a building's size, not enterable.</summary>
        public const int Size = 3;

        // ---- Durability ----
        public const float MaxHp = 600f;
        /// <summary>
        /// Share of a raider's attack the village takes. Palisades and a militia hold better than one of our
        /// buildings (<see cref="Monsters.RaidState.BuildingDamageMul"/> 0.4): at 0.4 one raid of seven goblins
        /// flattened a village in under two game hours, long before anyone could hear of it.
        /// </summary>
        public const float DamageMul = 0.15f;
        /// <summary>Game hours without a hit before the village starts mending.</summary>
        public const float RegenDelayHours = 6f;
        /// <summary>Share of its durability it mends per game hour after that.</summary>
        public const float RegenPerHour = 0.05f;
        /// <summary>Below this share of durability its candidates flee to our town.</summary>
        public const float RefugeeBelow = 0.25f;

        // ---- Raids (VillageRaids) ----
        /// <summary>Dens this close to a village raid it (as <see cref="Town.WaveManager.RaidRadius"/> for our town).</summary>
        public const float RaidRadius = 45f;
        /// <summary>
        /// Share of a den's raid rolls that come down on its village (P4-88, user decision 2026-10-18: half as often as our
        /// town's raids). A den of a dozen goblins took a village in two days, three raids in a row.
        /// </summary>
        public const float DenRaidShare = 0.5f;
        /// <summary>A migrating or roaming pack passing this close attacks it.</summary>
        public const float PassingRadius = 8f;
        /// <summary>After a passing pack's attack, the next passing pack leaves the village alone this long.</summary>
        public const float PassingCooldownHours = 12f;
        /// <summary>A raid on a village gives up after this many game hours.</summary>
        public const float RaidMaxHours = 6f;
        /// <summary>The guild "sees the village attacked" when it sees a raider this close to it.</summary>
        public const float AttackSeenRadius = 12f;
        /// <summary>A defence quest's area round the village.</summary>
        public const float DefenceRadius = 10f;
        /// <summary>A defence quest pays this much more than a hunt of the same danger and size: it is urgent.</summary>
        public const float DefenceRewardFactor = 1.5f;

        // ---- Candidates ----
        public const int CandidateCount = 2;
        /// <summary>Every this many game hours the oldest candidate moves on and newcomers fill the place.</summary>
        public const float RotateHours = 24f;
        /// <summary>The guild master meets the candidates within this distance of the village (cells)...</summary>
        public const float MeetRadius = 8f;
        /// <summary>...and they go back in once the guild master is this far away.</summary>
        public const float PartRadius = 14f;

        // ---- New villages ----
        /// <summary>Each midnight, for every village lost, a new one is founded with this chance.</summary>
        public const float FoundChancePerDay = 0.2f;
        /// <summary>A cleansed region (its lord fell, P4-83) draws new villages this many times as often.</summary>
        public const float CleansedWeight = 3f;

        /// <summary>Durability after <paramref name="hours"/> of mending at most (none until <see cref="RegenDelayHours"/> after the last hit).</summary>
        public static float Mend(float hp, float maxHp, float hoursSinceHit, float hours)
        {
            if (hp <= 0f || hoursSinceHit < RegenDelayHours) return hp;
            return Mathf.Min(maxHp, hp + maxHp * RegenPerHour * hours);
        }

        /// <summary>Damage a raider's blow does to a village.</summary>
        public static float Blow(float attack) => Mathf.Max(0.5f, attack * DamageMul);

        /// <summary>A candidate's level: about what the monsters there are born at (P4-84), never below the town's visitors.</summary>
        public static int CandidateLevel(int birthLevel, int townVisitorLevel, int roll)
            => Mathf.Clamp(Mathf.Max(townVisitorLevel, birthLevel + roll), 1, Monsters.MonsterLevel.MaxLevel);

        /// <summary>Whether a candidate who came at <paramref name="arrivedHour"/> moves on at the village's rotation <paramref name="now"/>.</summary>
        public static bool MovesOn(float arrivedHour, float now) => now - arrivedHour >= RotateHours - 0.01f;
    }
}
