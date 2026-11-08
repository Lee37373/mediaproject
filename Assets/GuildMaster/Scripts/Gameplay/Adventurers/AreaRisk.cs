using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Quests;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// What the ground round a spot adds to a fight there (P4-104b, G3): the danger steps rate a hunt by its own den and its
    /// young's level, and members walked into packed zones and deep woods as if those were any other ground. P4-102: in a zone
    /// one kind holds, the dens next door pile in and parties died to the lot; P4-103: in the deep woods they died blind.
    /// Read from what the guild knows only (<see cref="GuildIntel"/>, the explored map), so the quest board's warning, the
    /// members' choices, invitations and persuasion all weigh the same thing.
    /// </summary>
    public static class AreaRisk
    {
        /// <summary>Dens of the same kind this close to a spot are reckoned to join a fight there (a pack of dens lies within 24 cells).</summary>
        public const float CrowdRadius = 24f;
        /// <summary>Share of those dens' last-seen residents expected to come (their own den's share is <see cref="FreeHunt.NestJoinFraction"/>).</summary>
        public const float NeighbourJoinFraction = 0.3f;
        /// <summary>A den this close to the spot is the target itself, not a neighbour.</summary>
        const float SameSpot = 3f;
        /// <summary>The crowd at most doubles what a hunt asks.</summary>
        public const float MaxCrowdFactor = 2f;

        /// <summary>Only kinds that gang up or come at people pile in (as <see cref="FreeHunt.ExpectedEnemies"/>: slimes come one at a time).</summary>
        public static bool GangsUp(MonsterDefinition s) => s != null && (s.packHunter || s.aggressiveToHumans);

        /// <summary>Foes expected from the dens next door, seen holding <paramref name="neighboursSeen"/> between them (pure).</summary>
        public static int ExtraFoes(MonsterDefinition s, int neighboursSeen)
            => GangsUp(s) && neighboursSeen > 0 ? Mathf.CeilToInt(neighboursSeen * NeighbourJoinFraction) : 0;

        /// <summary>What a hunt for <paramref name="count"/> asks with that many more expected (pure): in proportion, at most ×2.</summary>
        public static float CrowdFactor(int extra, int count)
            => extra <= 0 ? 1f : Mathf.Min(MaxCrowdFactor, 1f + extra / (float)Mathf.Max(FreeHunt.AssumedPackSize, count));

        /// <summary>Residents last seen at the known dens of this kind round the spot, the target den (or one at the spot) left out.</summary>
        public static int NeighboursSeen(MonsterDefinition s, Vector2 point, Nest except)
        {
            if (s == null || !Services.TryGet<GuildIntel>(out var intel)) return 0;
            int seen = 0;
            foreach (var n in intel.KnownNests)
            {
                if (n == except || intel.SpeciesOf(n) != s) continue;
                float d = Vector2.Distance(n.WorldPosition, point);
                if (d > CrowdRadius || d <= SameSpot) continue;
                seen += Mathf.Max(0, intel.Get(n).LastCount);
            }
            return seen;
        }

        /// <summary>Foes expected from the known dens next door to this spot.</summary>
        public static int ExtraFoesAround(MonsterDefinition s, Vector2 point, Nest except)
            => GangsUp(s) ? ExtraFoes(s, NeighboursSeen(s, point, except)) : 0;

        /// <summary>Deep woods the guild has seen here (explored and still standing).</summary>
        public static bool KnownDeep(Vector2 point)
        {
            if (!Services.TryGet<WorldService>(out var world)) return false;
            var cell = GridMap.WorldToCell(point);
            if (!world.Grid.IsDeep(cell)) return false;
            return !Services.TryGet<Fog.FogOfWar>(out var fog) || fog.IsExploredRaw(cell);
        }

        /// <summary>
        /// How much more an ordinary hunt asks for where it is (1 = nothing): the crowd round it and known deep woods
        /// (<see cref="DeepWoodRules.QuestFactor"/>). Other work is left as it rates itself — an incident, a lord or the dragon,
        /// and the urgent kinds (defence, a disaster, an escort) that are not weighed by their ground.
        /// </summary>
        public static float QuestFactor(Quest q)
        {
            if (q == null || !ErrandRules.IsHuntLike(q.Type) || q.Species == null || q.Incident != null || QuestRisk.IsBossWork(q)) return 1f;
            float f = CrowdFactor(ExtraFoesAround(q.Species, q.Point, q.TargetNest), q.RequiredCount);
            if (KnownDeep(q.Point)) f *= DeepWoodRules.QuestFactor;
            return f;
        }

        /// <summary>Why <see cref="QuestFactor"/> is above one, in words for the board (null = nothing to say).</summary>
        public static string Warning(Quest q)
        {
            if (q == null || !ErrandRules.IsHuntLike(q.Type) || q.Species == null || q.Incident != null || QuestRisk.IsBossWork(q)) return null;
            bool crowd = ExtraFoesAround(q.Species, q.Point, q.TargetNest) > 0, deep = KnownDeep(q.Point);
            if (!crowd && !deep) return null;
            string near = $"주변 {q.Species.displayName} 둥지 합세";
            return crowd && deep ? "깊은 숲·" + near : crowd ? near : "깊은 숲";
        }
    }
}
