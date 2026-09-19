using GuildMaster.Adventurers;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Economy;
using GuildMaster.Fog;
using GuildMaster.LLM;
using GuildMaster.Player;
using GuildMaster.Quests;
using GuildMaster.Town;
using GuildMaster.Units;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace GuildMaster.Session
{
    public struct SessionStartedEvent { public int Seed; }

    /// <summary>
    /// Composition root. Creates and wires every system in an explicit order so the
    /// scene only needs this one component. Later phases add their systems here.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class GameBootstrap : MonoBehaviour
    {
        [SerializeField] GameBalance balanceOverride;

        public GameBalance Balance { get; private set; }
        public int Seed { get; private set; }

        Transform _systems;

        void Awake()
        {
            // Restart reloads the scene; keep the already-loaded model instead of loading it twice.
            LlmBackends.DiscardReloadedDuplicates();
            EventBus.Clear();
            Services.Clear();
            Time.timeScale = 1f;
            // Simulation (and async LLM work) keeps running when the window loses focus.
            Application.runInBackground = true;

            Balance = balanceOverride ? balanceOverride : GameBalance.Load();
            Seed = Balance.seed != 0 ? Balance.seed : System.Environment.TickCount & 0x7fffffff;
            GameRandom.Shared = new GameRandom(Seed);
            Services.Register(this);
            Services.Register(Balance);

            _systems = new GameObject("Systems").transform;

            // --- Core systems ---
            var clock = AddSystem<GameClock>();
            clock.Configure(Balance.realSecondsPerGameHour, Balance.startHour);
            Services.Register(clock);
            Services.Register(AddSystem<PoolService>());

            // --- World ---
            var map = MapGenerator.Generate(new MapGenSettings
            {
                Width = Balance.mapWidth,
                Height = Balance.mapHeight,
                Seed = Seed,
                TownHalfSize = Balance.townHalfSize,
                NestCount = Balance.nestCount,
                MinNestDistanceFromTown = Balance.minNestDistanceFromTown,
            });
            var world = new WorldService(map);
            Services.Register(world);

            var mapRenderer = new GameObject("Map").AddComponent<MapRenderer>();
            mapRenderer.Build(map);
            Services.Register(mapRenderer);

            // --- Fog of war ---
            var fog = new GameObject("FogOfWar").AddComponent<FogOfWar>();
            Services.Register(fog);
            var townVision = new GameObject("TownVision").AddComponent<VisionSource>();
            townVision.transform.SetParent(_systems, false);
            townVision.transform.position = world.TownCenterWorld;
            townVision.SightRadius = Balance.townSightRadius;
            townVision.SeesOverWalls = true; // watchtowers on the wall
            fog.Init(map.Width, map.Height, Balance.exploredFogAlpha, Balance.fogTickInterval);

            // --- Content & economy ---
            var db = GameDatabase.Load();
            Services.Register(db);
            var treasury = new GuildTreasury(Balance.startingGold, Balance.startingExp, Balance.expPerHour, Balance.dailyTax);
            treasury.AddStock(DefaultContent.Potion, Balance.startingPotions);
            Services.Register(treasury);
            Services.Register(new TradeService(treasury, db) { LootBuyRate = Balance.lootBuyRate, GoodsSellRate = Balance.goodsSellRate });
            Services.Register(new GameLog(clock, db));

            // --- Town ---
            var town = AddSystem<TownService>();
            town.Init(world, mapRenderer);
            Services.Register(town);

            // --- Units, ecosystem, quests ---
            var pool = Services.Get<PoolService>();
            var projectiles = AddSystem<ProjectileService>();
            projectiles.Init(pool);
            Services.Register(projectiles);
            var fx = AddSystem<FxService>();
            fx.Init(pool);
            Services.Register(fx);
            var telegraphs = AddSystem<TelegraphService>();
            telegraphs.Init(pool);
            Services.Register(telegraphs);
            Services.Register(AddSystem<CrowdSimulation>());

            var eco = AddSystem<EcosystemManager>();
            Services.Register(eco);
            eco.Init(db, map, GameRandom.Shared);
            // What the guild has actually seen; UI and adventurers read this, not the simulation.
            var intel = AddSystem<GuildIntel>();
            intel.Init(eco, fog, clock, world);
            Services.Register(intel);

            var board = AddSystem<QuestBoard>();
            board.Init(treasury, eco, clock);
            Services.Register(board);

            // --- AI: LLM gateway (falls back to rules when no model is available) ---
            var llm = AddSystem<LlmGateway>();
            llm.SetBackend(LlmBackends.CreateDefault(_systems));
            Services.Register(llm);
            // LLM decisions with the rule-based maker as the built-in fallback.
            Services.Register<IAdventurerDecisionMaker>(new LlmDecisionMaker(llm));
            var roster = AddSystem<GuildRoster>();
            roster.Capacity = Balance.rosterCapacity;
            Services.Register(roster);
            // Per-adventurer memories; before recruiting so the starting members remember joining.
            Services.Register(new MemoryRecorder(clock, roster));
            // Multi-person quests: the first taker invites idle members (LLM, rule fallback).
            Services.Register(new PartyService(llm, board, roster));
            var guildDoor = town.Get(BuildingType.GuildHall).DoorPosition;
            for (int i = 0; i < Balance.startingAdventurers; i++)
                roster.Recruit(AdventurerGenerator.Generate(GameRandom.Shared), guildDoor + GameRandom.Shared.InsideUnitCircle());
            // --- Recruitment: visitors who can be talked into joining ---
            var visitors = AddSystem<Recruitment.VisitorService>();
            visitors.Init(clock, roster, world);
            Services.Register(visitors);

            // --- Camera ---
            var cam = SetupCamera(world);
            Services.Register(cam);

            // --- Player ---
            var avatar = PlayerAvatar.Create(world.TownCenterWorld + new Vector2(0f, -2f), Balance.playerMoveSpeed, Balance.playerSightRadius);
            Services.Register(avatar);
            Services.Register(new GameOverService(clock));
            AddSystem<PlayerCommandInput>().Init(avatar, cam, Services.Get<PoolService>());

            // --- Presentation / input ---
            DayNightLighting.Create(_systems, clock);
            EnsureEventSystem();
            AddSystem<GameHotkeys>();

            Debug.Log($"[GuildMaster] Session started. seed={Seed}, nests={map.Nests.Count}");
            EventBus.Publish(new SessionStartedEvent { Seed = Seed });
        }

        T AddSystem<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            go.transform.SetParent(_systems, false);
            return go.AddComponent<T>();
        }

        CameraController SetupCamera(WorldService world)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                camera = go.AddComponent<Camera>();
            }
            camera.orthographic = true;
            camera.orthographicSize = 12f;
            camera.backgroundColor = Color.black;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.transform.position = new Vector3(world.TownCenterWorld.x, world.TownCenterWorld.y, -10f);
            if (!camera.TryGetComponent<CameraController>(out var controller))
                controller = camera.gameObject.AddComponent<CameraController>();
            controller.Configure(world.WorldBounds, Balance.cameraMinZoom, Balance.cameraMaxZoom, Balance.cameraPanSpeed);
            controller.JumpTo(world.TownCenterWorld);
            return controller;
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }
    }
}
