using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Monsters;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Units
{
    /// <summary>
    /// Who decides how a group walks (P4-63, user decision 2026-09-23). The route is one decision
    /// for the whole group, and the same rule serves the guild and the monsters: a caution weight on
    /// the risk of the ground (<see cref="RouteRisk"/>), handed to the path at
    /// <c>PathFollower.SetDestination</c>.
    ///
    /// - A guild party walks by its leader's nerve: the brave cut through the woods, the fearful go
    ///   round them. Followers keep the formation, so the party walks the leader's route.
    /// - In a hurry nobody detours (<see cref="Hurried"/>): running home or hurt, and answering a
    ///   raid on the town.
    /// - A pack that hunts from cover (a species that lives in the woods) walks the woods by choice:
    ///   a negative weight, which makes the forest the shortcut instead of the long way round.
    ///
    /// Only travelling states pass a weight; fighting, looting and the short walks around town use
    /// the plain shortest path as before.
    /// </summary>
    public static class RouteChoice
    {
        /// <summary>Ordinary nerve: a forest cell costs about 1.6 times its walking cost.</summary>
        public const float Normal = 0.9f;
        /// <summary>Floor and ceiling of the personality range (brave / coward).</summary>
        public const float Bold = 0.15f;
        public const float Careful = 2.5f;
        /// <summary>No detour at all: the shortest walk, risk ignored.</summary>
        public const float Hurried = 0f;
        /// <summary>
        /// How hard a trait's danger aversion pulls (brave -0.5, coward +1). Measured on the 256 map
        /// over 108 walks out of town: a hurried one crosses 1.15 forest cells, a brave one about
        /// 0.9, an ordinary one 0.65 and a coward 0.53, for about 1% more walking.
        /// </summary>
        public const float AversionWeight = 1.3f;
        /// <summary>A species that ambushes from the trees: the forest is cheap for it.</summary>
        public const float FromCover = -0.5f;

        /// <summary>Caution of a walker with this danger aversion (<see cref="Traits.TraitSet"/>).</summary>
        public static float FromNerve(float dangerAversion)
            => Mathf.Clamp(Normal + dangerAversion * AversionWeight, Bold, Careful);

        /// <summary>
        /// The caution of the party this adventurer walks with: its leader's, so every member takes
        /// the same view of the woods. On its own it follows its own nerve.
        /// </summary>
        public static float ForParty(Adventurer a)
        {
            if (a == null) return Normal;
            var leader = Leader(a) ?? a;
            return FromNerve(leader.Traits?.DangerAversion ?? 0f);
        }

        static Adventurer Leader(Adventurer a)
        {
            if (Services.TryGet<PartyTactics>(out var tactics) && tactics.LeaderOf(a) is { } outLeader) return outLeader;
            if (Services.TryGet<GuildParties>(out var parties) && parties.PartyOf(a) is { Leader: { } standing }) return standing;
            return null;
        }

        /// <summary>
        /// The caution of a monster band on the march (a raid, a migration, a roaming pack). The
        /// species is the same for the whole band, so the band walks as one without being asked.
        /// </summary>
        public static float ForPack(Monster m)
            => m != null && m.Def != null && m.Def.habitatBiomes.Contains(Biome.Forest) ? FromCover : Hurried;
    }
}
