using System.Collections.Generic;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Monsters
{
    /// <summary>
    /// The four region lords (P4-83, user decisions 2026-10-17): one final boss of its own kind deep in each outer
    /// region (desert, snowfield, swamp, wasteland), far stronger than a pack boss (<see cref="MonsterBoss"/>) —
    /// level 35, fifteen times the health — guarded by a pack of the region's kind that breeds back while the lord
    /// lives. It never leaves its lair. Killing it cleanses the region's outer land (beyond the inner edge): nothing
    /// new is born, settles or moves in there any more, so the region empties as the rest are killed or leave. All
    /// four down is the victory (play goes on). Pure numbers and rules, tested.
    /// </summary>
    public static class RegionLords
    {
        public const int Level = 35;
        public const float HpMultiplier = 15f;
        /// <summary>
        /// Tuned in play (2026-10-18): at ×2 four level-24 members took one apart unhurt, and at ×3 still lost nobody. P4-88
        /// (user decision "much stronger"): ×4, eight guards, the strike every 5 s — four level-24s should lose someone or fall
        /// back, eight should win.
        /// </summary>
        public const float AttackMultiplier = 4f;
        public const float DefenseBonus = 4f;
        public const int ExpMultiplier = 10;
        /// <summary>Guards the lair starts with (the lair's capacity is its guards' nest size).</summary>
        public const int InitialEscorts = 8;
        /// <summary>Seconds between its strikes (<see cref="Units.LordStrikeSkill"/>), 7 before P4-88.</summary>
        public const float StrikeCooldown = 5f;
        /// <summary>Guards the lord calls out once, when its health first drops below <see cref="CallBelowHp"/>.</summary>
        public const int CalledEscorts = 3;
        public const float CallBelowHp = 0.5f;
        /// <summary>Gives up a chase this far from the lair (its own leash).</summary>
        public const float LeashRadius = 12f;
        /// <summary>
        /// The strength a group needs to read a lord's quest as "적당" (<c>Adventurers.QuestRisk</c>): eight members
        /// around level 18, or four around level 33. A lord's level is beyond the three danger steps, so its quest is
        /// weighed by this.
        /// </summary>
        public const float RiskRequirement = 90f;
        /// <summary>
        /// Most members a lord's quest takes (user decision 2026-10-17: "several parties have to gather"): two parties'
        /// worth, still one group (the first taker invites the rest).
        /// </summary>
        public const int QuestParty = 8;
        /// <summary>Guild members this close when the lord falls took part in the fight, besides those who hit it.</summary>
        public const float ParticipantRadius = 15f;

        public static readonly Region[] OuterRegions = { Region.Desert, Region.Snow, Region.Swamp, Region.Wasteland };

        /// <summary>
        /// The lair of each outer region: the nest site in it farthest from the town, beyond <paramref name="innerEdge"/>
        /// (deep in the new outer land, P4-81). A region with no site out there gets no lord.
        /// </summary>
        public static Dictionary<Region, int> PickLairs(IReadOnlyList<NestSite> sites, Vector2 town, float innerEdge)
        {
            var lairs = new Dictionary<Region, int>();
            var best = new Dictionary<Region, float>();
            for (int i = 0; i < sites.Count; i++)
            {
                var region = sites[i].Region;
                if (region == Region.Grassland) continue;
                float d = Vector2.Distance(GridMap.CellToWorld(sites[i].Cell), town);
                if (d <= innerEdge || best.TryGetValue(region, out float far) && far >= d) continue;
                best[region] = d;
                lairs[region] = i;
            }
            return lairs;
        }

        /// <summary>Cleansed land (no new monsters): a cleansed region's ground beyond <paramref name="innerEdge"/> from the town.</summary>
        public static bool IsSealed(Region region, float distanceFromTown, ICollection<Region> cleansed, float innerEdge)
            => region != Region.Grassland && cleansed != null && cleansed.Contains(region) && distanceFromTown > innerEdge;

        public static string RegionName(Region r) => r switch
        {
            Region.Desert => "사막",
            Region.Snow => "설원",
            Region.Swamp => "늪지",
            Region.Wasteland => "황무지",
            _ => "초원",
        };

        /// <summary>The title a member who took part in the kill carries ("모래 폭군 카르샤를 쓰러뜨린 자").</summary>
        public static string Title(string lordName) => $"{lordName}{Adventurers.MemoryRecorder.Josa(lordName, "을", "를")} 쓰러뜨린 자";
    }
}
