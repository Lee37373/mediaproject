using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Storytelling
{
    /// <summary>The big events the storyteller sets off itself (saved by number: add new ones at the end).</summary>
    public enum StoryBeat { TownRaid, Horde, DragonFlight, Disaster }

    /// <summary>
    /// Where the storyteller is in its round: building up to the next big event, letting the guild breathe after one, or
    /// holding everything off for a guild near its end.
    /// </summary>
    public enum StoryPhase { Calm, Relax, Recovery }

    /// <summary>
    /// The storyteller's rules (P4-111a, G5 — RimWorld's storyteller, user decisions 2026-10-30: one character, no choice;
    /// the big events are its to time and size, what the wilds do of themselves only gets its factor). Pure, tested.
    /// <see cref="StorytellerService"/> keeps the count and sets the events off.
    /// </summary>
    public static class StorytellerRules
    {
        // ---------------- Tension: what the guild has been through lately ----------------

        public const float MaxTension = 100f;
        /// <summary>Game hours for the tension to fall by half.</summary>
        public const float TensionHalfLifeHours = 18f;
        public const float MemberDied = 20f;
        /// <summary>A member driven off near death.</summary>
        public const float NearDeathRetreat = 3f;
        public const float BuildingDestroyed = 12f;
        /// <summary>Damage to a building, per its whole durability.</summary>
        public const float BuildingDamagePerWhole = 8f;
        public const float GateBroken = 8f;
        public const float RaidSighted = 6f;
        public const float DragonSighted = 5f;
        /// <summary>The dragon burnt a building and took gold from the vault.</summary>
        public const float Plundered = 8f;
        public const float DisasterSighted = 4f;

        public static float Decay(float tension, float hours) => tension * Mathf.Pow(0.5f, Mathf.Max(0f, hours) / TensionHalfLifeHours);

        public static float Add(float tension, float amount) => Mathf.Clamp(tension + amount, 0f, MaxTension);

        // ---------------- Pace: building up, the event, the breather ----------------

        /// <summary>After a big event nothing else big comes for this long, and the wilds are quieter.</summary>
        public const float RelaxHours = 24f;
        /// <summary>Game hours of building up after the breather, before escalation.</summary>
        public const float BuildUpMin = 12f, BuildUpMax = 36f;
        /// <summary>The build-up shortens by this share a day, down to <see cref="MinEscalation"/>.</summary>
        public const float EscalationPerDay = 0.025f, MinEscalation = 0.5f;

        public static float Escalation(int day) => Mathf.Max(MinEscalation, 1f - EscalationPerDay * (day - 1));

        /// <summary>Game hours from one big event to the next (<paramref name="roll"/> 0..1).</summary>
        public static float Gap(int day, float roll) => RelaxHours + Mathf.Lerp(BuildUpMin, BuildUpMax, roll) * Escalation(day);

        /// <summary>The first big event comes on this day, between these hours of it (P5-1: the first raid on days 3 to 6).</summary>
        public const int FirstDay = 3;
        public const float FirstHourMin = 6f, FirstHourMax = 30f;

        public static float FirstBeatHour(float roll) => (FirstDay - 1) * 24f + Mathf.Lerp(FirstHourMin, FirstHourMax, roll);

        /// <summary>Due, but the guild is this tense: it waits <see cref="HoldHours"/> more.</summary>
        public const float HoldAtTension = 50f;
        public const float HoldHours = 6f;
        /// <summary>Due, but a raid or a horde is still on: it waits this long.</summary>
        public const float BusyHoldHours = 3f;
        /// <summary>Tension this high from anything gives a breather too, until it falls below <see cref="RelaxEndsBelow"/>.</summary>
        public const float RelaxAtTension = 60f, RelaxEndsBelow = 35f;

        // ---------------- Adapting: a guild near its end ----------------

        public const int FewMembers = 2;
        public const float DeathWindowHours = 48f;
        public const int ManyDeaths = 3;
        /// <summary>Of those it had in the window: three of six lost in two days is near the end.</summary>
        public const float ManyDeathsShare = 0.3f;
        /// <summary>Game hours of recovery after the last thing that called for it.</summary>
        public const float RecoveryHours = 36f;
        /// <summary>The next visitor comes within this many hours when recovery begins (once a recovery).</summary>
        public const float VisitorWithinHours = 2f;
        /// <summary>After a recovery the next big event waits at least this long.</summary>
        public const float AfterRecoveryHours = 12f;

        /// <summary>
        /// The guild is near its end: few left, or many lost lately (<paramref name="recentDeaths"/> in
        /// <see cref="DeathWindowHours"/>) against those it had.
        /// </summary>
        public static bool NearTheEnd(int living, int recentDeaths)
            => living <= FewMembers || recentDeaths >= Mathf.Max(ManyDeaths, Mathf.CeilToInt(ManyDeathsShare * (living + recentDeaths)));

        // ---------------- The wilds' own doings ----------------

        public const float CalmFactor = 1f, RelaxFactor = 0.4f, RecoveryFactor = 0.25f;

        /// <summary>What the wilds' own rolls are multiplied by: nests raiding, newcomers crowding in, disasters from a fight.</summary>
        public static float EcoFactor(StoryPhase phase) => phase switch
        {
            StoryPhase.Relax => RelaxFactor,
            StoryPhase.Recovery => RecoveryFactor,
            _ => CalmFactor,
        };

        /// <summary>Below this a homeless pack waits before it comes at the town.</summary>
        public const float HoldOffBelow = 0.5f;
        public const float HomelessHoldHours = 6f;

        // ---------------- The big events ----------------

        public static int FirstDayOf(StoryBeat b) => b switch
        {
            StoryBeat.Horde => 4,
            StoryBeat.Disaster => 2,
            _ => 3,
        };

        public static float BaseWeight(StoryBeat b) => b switch
        {
            StoryBeat.TownRaid => 3f,
            StoryBeat.Horde => 3f,
            StoryBeat.DragonFlight => 1.5f,
            _ => 2f,
        };

        /// <summary>The same kind twice in a row weighs this much.</summary>
        public const float RepeatFactor = 0.3f;

        /// <summary>A kind's weight in the draw: none before its first day, less if it was the last one.</summary>
        public static float Weight(StoryBeat b, int day, StoryBeat? last, float seasonFactor = 1f)
            => day < FirstDayOf(b) ? 0f : BaseWeight(b) * (last == b ? RepeatFactor : 1f) * Mathf.Max(0f, seasonFactor);

        /// <summary>
        /// A town raid comes from a raiding kind's den this close to the town (cells, past its walls' growth). The nearest ones
        /// lie 70 to 100 cells out at the start (seed 20260920: 73), well past the 45 of a den's own raids; a far den's march is
        /// allowed for (<see cref="Town.WaveManager.Launch(Ecosystem.Nest, int)"/>).
        /// </summary>
        public const float RaidRange = 120f;
        public const int RaidMin = 3, RaidMax = 14;
        public const float RaidPowerPerHead = 8f;

        /// <summary>Raiders wanted against a guild this strong (as many as the den has at home).</summary>
        public static int RaidSize(float guildPower) => Mathf.Clamp(Mathf.RoundToInt(2f + guildPower / RaidPowerPerHead), RaidMin, RaidMax);

        public const int HordeMinSize = 8, HordeMaxSize = 18;
        /// <summary>A horde heads for one of this many empty den sites nearest the town (the nearest alone drew every horde to one place).</summary>
        public const int HordeSiteChoices = 4;
        public const float HordePowerPerHead = 6f;

        public static int HordeSize(float guildPower) => Mathf.Clamp(Mathf.RoundToInt(6f + guildPower / HordePowerPerHead), HordeMinSize, HordeMaxSize);

        /// <summary>A disaster starts by a disaster monster's den this far from the town (cells).</summary>
        public const float DisasterMinDistance = 25f, DisasterMaxDistance = 150f;
        /// <summary>How far from the den it looks for ground that suits it (cells).</summary>
        public const float DisasterSearch = 8f;

        // ---------------- Its own dice ----------------

        /// <summary>
        /// The storyteller's own numbers: the map seed, the game hour and what for, nothing shared — like the dragon's day
        /// (P4-93), so a seeded world stays the same whether or not it rolls, and a loaded game rolls as it would have.
        /// </summary>
        public static GameRandom Stream(int mapSeed, int hour, int salt)
            => new(unchecked((mapSeed ^ 0x5701E11) + hour * 7919 + salt * 104729));

        public static string Name(StoryBeat b) => b switch
        {
            StoryBeat.TownRaid => "마을 침공",
            StoryBeat.Horde => "대규모 무리",
            StoryBeat.DragonFlight => "드래곤 비행",
            _ => "재해",
        };

        public static string Name(StoryPhase p) => p switch
        {
            StoryPhase.Relax => "완화",
            StoryPhase.Recovery => "회복기",
            _ => "평온",
        };
    }
}
