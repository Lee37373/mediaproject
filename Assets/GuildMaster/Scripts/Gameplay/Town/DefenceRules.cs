using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>
    /// The town's outer defences (P4-113, H10 — user decisions 2026-10-31: arrow towers and traps, up to 15 cells beyond the
    /// walls, the towers apart from the watchtower, and nobody goes out to guard them). Pure: where they may go, what a tower
    /// of each level does, what a trap does and costs, and how often a monster sees one in time. <see cref="ArrowTower"/>
    /// shoots, <see cref="DefenceService"/> keeps the traps.
    /// </summary>
    public static class DefenceRules
    {
        /// <summary>How far beyond the walls a defence may stand, in cells (user decision).</summary>
        public const int Reach = 15;

        /// <summary>The town's buildings that are its outer defences (only the arrow tower: the traps are not buildings).</summary>
        public static bool IsDefence(BuildingType type) => type == BuildingType.ArrowTower;

        /// <summary>Cells from this cell to the town's walls: 0 inside or on them, 1 right outside (Chebyshev).</summary>
        public static int CellsBeyondWalls(Vector2Int c, RectInt town)
            => Mathf.Max(0, Mathf.Max(Mathf.Max(town.xMin - c.x, c.x - (town.xMax - 1)), Mathf.Max(town.yMin - c.y, c.y - (town.yMax - 1))));

        /// <summary>Why a defence cannot stand on this ground (inside the walls, or too far out), or null.</summary>
        public static string OutOfReach(RectInt foot, RectInt town)
        {
            for (int y = foot.yMin; y < foot.yMax; y++)
            for (int x = foot.xMin; x < foot.xMax; x++)
            {
                int d = CellsBeyondWalls(new Vector2Int(x, y), town);
                if (d <= 0) return "방어 시설은 성벽 밖에만 지을 수 있습니다.";
                if (d > Reach) return $"방어 시설은 성벽에서 {Reach}칸 안에만 지을 수 있습니다.";
            }
            return null;
        }

        // ---------------- The arrow tower ----------------

        public const int TowerLevels = 3;

        /// <summary>Raw damage of one arrow (before the target's defence), by level.</summary>
        public static float TowerDamage(int level) => level switch { >= 3 => 26f, 2 => 16f, _ => 10f };
        /// <summary>Seconds between arrows, by level.</summary>
        public static float TowerCooldown(int level) => level switch { >= 3 => 1.0f, 2 => 1.2f, _ => 1.4f };
        /// <summary>Range in cells, by level; it sees one cell further, over walls (it stands high).</summary>
        public static float TowerRange(int level) => level switch { >= 3 => 9f, 2 => 8f, _ => 7f };
        public static float TowerSight(int level) => TowerRange(level) + 1f;
        /// <summary>Its arrows fly over the trees more often than an archer's (it shoots from above).</summary>
        public const float TowerTreeBlock = 0.2f;

        public static string TowerEffect(int level)
            => $"사거리 {TowerRange(level):0}칸 · {TowerCooldown(level):0.#}초마다 화살(피해 {TowerDamage(level):0})";

        // ---------------- Traps ----------------

        /// <summary>A trap's price: gold and iron ore.</summary>
        public const int TrapGold = 60, TrapIron = 2;
        public const string TrapIronItem = "iron_ore";
        /// <summary>Times a trap springs before it must be set again, and the gold to set a spent one.</summary>
        public const int TrapCharges = 3, TrapRearmGold = 30;
        /// <summary>A sprung trap takes this share of the monster's health and holds it this long.</summary>
        public const float TrapDamage = 0.15f, TrapHoldSeconds = 2f;
        /// <summary>Seconds between looks at who stands on a trap (the deep woods' traps look as often).</summary>
        public const float CheckSeconds = 0.2f;

        /// <summary>
        /// The chance a monster sees a trap in time and steps round it (user decision: the skilled and better only), by its
        /// combat tier: instinct and novice never, skilled a quarter, veteran 40%, heroic and above 55%.
        /// </summary>
        public static float DodgeChance(Units.CombatTier tier) => tier switch
        {
            Units.CombatTier.Instinct or Units.CombatTier.Novice => 0f,
            Units.CombatTier.Skilled => 0.25f,
            Units.CombatTier.Veteran => 0.4f,
            _ => 0.55f,
        };

        /// <summary>What a trap does to this much health (at least one).</summary>
        public static float TrapHit(float maxHp) => Mathf.Max(1f, maxHp * TrapDamage);
    }
}
