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
        public int nestCount = 14;
        public int minNestDistanceFromTown = 24;

        [Header("Fog of War")]
        public float fogTickInterval = 0.1f;
        public float playerSightRadius = 7f;
        /// <summary>Just inside the walls (P4-13); seeing further out needs a watchtower.</summary>
        public float townSightRadius = 8f;
        [Range(0f, 1f)] public float exploredFogAlpha = 0.55f;

        [Header("Camera")]
        public float cameraMinZoom = 4f;
        public float cameraMaxZoom = 34f;
        public float cameraPanSpeed = 22f;

        [Header("Player Avatar")]
        public float playerMoveSpeed = 4.5f;

        [Header("Economy")]
        public int startingGold = 300;
        public int startingExp = 120;
        public float expPerHour = 8f;
        public int dailyTax = 60;
        public int startingPotions = 6;
        [Range(0.3f, 2f)] public float lootBuyRate = 1f;
        [Range(1f, 2f)] public float goodsSellRate = 1.2f;

        [Header("Guild")]
        public int startingAdventurers = 3;
        public int rosterCapacity = 6;

        [Header("Raids (P3-6): swollen nests raid the town")]
        /// <summary>Hourly raid chance per danger level of a nest (level = steps above its threshold).</summary>
        [Range(0f, 0.2f)] public float raidChancePerLevelPerHour = 0.01f;
        /// <summary>Share of the nest that marches on the town (the rest stays home).</summary>
        [Range(0.1f, 1f)] public float raidSendFraction = 0.7f;
        public int raidMinParty = 3;
        /// <summary>Game hours after which surviving raiders give up and go home.</summary>
        public int raidMaxHours = 10;
        /// <summary>Guild members within this many cells of the town rush to defend.</summary>
        public float raidDefendRadius = 45f;

        public static GameBalance Load()
        {
            var asset = Resources.Load<GameBalance>("Data/GameBalance");
            return asset ? asset : CreateInstance<GameBalance>();
        }
    }
}
