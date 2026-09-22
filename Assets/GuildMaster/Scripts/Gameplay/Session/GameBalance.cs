using UnityEngine;

namespace GuildMaster.Session
{
    /// <summary>
    /// Central tuning values. Loaded from Resources/Data/GameBalance, or created with
    /// defaults when the asset does not exist.
    /// </summary>
    [CreateAssetMenu(menuName = "GuildMaster/Game Balance", fileName = "GameBalance")]
    public class GameBalance : ScriptableObject
    {
        [Header("Session")]
        public int seed = 0; // 0 = random each run
        public float realSecondsPerGameHour = 10f;
        public int startHour = 8;

        [Header("Map")]
        public int mapWidth = 128;
        public int mapHeight = 128;
        public int townHalfSize = 11;
        public int nestCount = 16;
        /// <summary>
        /// P5-1: was 24, which left no nest inside the first day's walk. Species are placed by
        /// distance now, so the closest ring is the weakest one and it should be findable early.
        /// P4-41: 20 so the nearest nest stays outside the town's sight (18) and still has to be found.
        /// </summary>
        public int minNestDistanceFromTown = 20;

        [Header("Fog of War")]
        public float fogTickInterval = 0.1f;
        public float playerSightRadius = 7f;
        /// <summary>
        /// P4-13 kept this just inside the walls. P4-41 (user decision 2026-09-22): raiders have to
        /// be seen coming, so the town now watches about 7 cells past the wall (half size 11);
        /// a watchtower still sees further.
        /// </summary>
        public float townSightRadius = 18f;
        [Range(0f, 1f)] public float exploredFogAlpha = 0.55f;

        [Header("Camera")]
        public float cameraMinZoom = 4f;
        public float cameraMaxZoom = 34f;
        public float cameraPanSpeed = 22f;

        [Header("Player Avatar")]
        public float playerMoveSpeed = 4.5f;

        [Header("Economy")]
        /// <summary>
        /// 2026-09-20 (user decision): 300 ran out in two or three days of posting quests. Reward
        /// gold leaves the treasury for the members' purses and only trickles back through potion
        /// and gear sales, so the guild needs a float to work with before that loop spins up.
        /// </summary>
        public int startingGold = 800;
        public int startingExp = 120;
        public float expPerHour = 5f; // P5-1: EXP piled up unused while gold ran dry
        // P5-1: quest rewards leave the treasury and sit in the members' own purses, so the only
        // steady inflow is the tax; at 60 the guild was broke from day 3 on.
        public int dailyTax = 90;
        public int startingPotions = 6;
        [Range(0.3f, 2f)] public float lootBuyRate = 1f;
        [Range(1f, 2f)] public float goodsSellRate = 1.2f;

        [Header("Guild")]
        /// <summary>P4-38 (was 3): one full party from the start, one of each class (see StartingLineup).</summary>
        public int startingAdventurers = 4;
        public int rosterCapacity = 6;

        [Header("Raids (P3-6): swollen nests raid the town")]
        /// <summary>Hourly raid chance per danger level of a nest (level = steps above its threshold).</summary>
        [Range(0f, 0.2f)] public float raidChancePerLevelPerHour = 0.02f; // P5-1: was 0.01, first raid landed on day 7
        /// <summary>
        /// Share of the nest that marches on the town (the rest stays home). P5-1 lowered it from 0.7
        /// to 0.5; back to 0.7 on 2026-09-21 (P4-40, user decision: "not the whole group comes").
        /// Gates (P4-39) and the gate alarm give the defence time the town did not have in P5-1.
        /// </summary>
        [Range(0.1f, 1f)] public float raidSendFraction = 0.7f;
        public int raidMinParty = 3;
        /// <summary>Game hours after which surviving raiders give up and go home.</summary>
        public int raidMaxHours = 10;
        /// <summary>Guild members within this many cells of the town rush to defend.</summary>
        public float raidDefendRadius = 80f;
        /// <summary>
        /// Share of a monster's attack that lands on a building (P5-1). Walls are not people: without
        /// this a dozen raiders flattened the town hall before anyone could walk back.
        /// </summary>
        [Range(0.05f, 1f)] public float monsterBuildingDamageMul = 0.4f;

        [Header("Gates (P4-39)")]
        /// <summary>
        /// Durability of each town gate. Raiders hit it at the building rate above, so five goblins
        /// need about half a minute (three game hours) — time for the guild to come back and defend.
        /// </summary>
        public float gateMaxHp = 300f;
        /// <summary>Gold per point of damage to repair a gate (a broken one: 150G at 300 HP).</summary>
        public float gateRepairGoldPerHp = 0.5f;

        public static GameBalance Load()
        {
            var asset = Resources.Load<GameBalance>("Data/GameBalance");
            return asset ? asset : CreateInstance<GameBalance>();
        }
    }
}
