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
        /// <summary>
        /// P4-53 (user decision 2026-09-22): 256×256, was 128. P4-81 (user decision 2026-10-16): 512×512 for
        /// the other towns and the region bosses. The old 256 map stays in the middle as it was tuned
        /// (grassland, levels 1→15 out to <see cref="Ecosystem.EcosystemManager.InnerEdge"/>); the land
        /// beyond is new late-game ground.
        /// </summary>
        public int mapWidth = 512;
        public int mapHeight = 512;
        public int townHalfSize = 11;
        /// <summary>P4-81 (user decision): the whole map keeps 64 nests — fewer near the town than on the 256 map.</summary>
        public int nestCount = 64;
        /// <summary>
        /// P4-81: nests placed within this many cells of the town along x and y (the old 256 map's
        /// square) before the rest go further out — spread evenly, 64 nests would leave a quarter of
        /// them near the town.
        /// </summary>
        public int innerHalfSize = 128;
        public int innerNestCount = 32;
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
        // The daily tax is the town hall's level 1 (building data, P4-69): kept here too it drifted apart and
        // a town hall upgrade paid nothing. P5-1's 90G lives there now. There is no guild EXP (P4-82).
        public int startingPotions = 6;
        [Range(0.3f, 2f)] public float lootBuyRate = 1f;
        [Range(1f, 2f)] public float goodsSellRate = 1.2f;
        /// <summary>
        /// P4-68 (user decision 2026-09-27): members pay their keep to the guild. In the 5-day run the
        /// guild fell from 800 to 36G while each member sat on 350–411G (about 70G earned a day).
        /// Meals are charged per member at midnight wherever they are, lodging per game hour of rest in
        /// town; a camp is free. What they cannot pay becomes debt. Measured over 5 days: about 10–12G
        /// a day each, nearly all meals (members rest in town only an hour or so a day).
        /// </summary>
        public int dailyMealGold = 10;
        public int lodgingGoldPerHour = 2;

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
        public float raidDefendRadius = 362f; // the whole map (half its diagonal): P4-53 180 for 256 (was 80 for 128), P4-81 362 for 512
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
        /// <summary>Gold per point of damage to repair a building (P4-74, as for gates; a fallen 500-HP hall: 250G).</summary>
        public float buildingRepairGoldPerHp = 0.5f;

        public static GameBalance Load()
        {
            var asset = Resources.Load<GameBalance>("Data/GameBalance");
            return asset ? asset : CreateInstance<GameBalance>();
        }
    }
}
