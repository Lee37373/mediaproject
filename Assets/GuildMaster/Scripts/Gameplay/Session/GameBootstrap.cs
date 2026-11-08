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

            // A saved game handed over by Save.SaveService.Load (P5-4): the world is rebuilt from its
            // seed below, then every system gets its section back at the end.
            var load = GameSettings.PendingLoad;
            int loadSlot = GameSettings.PendingLoadSlot;
            GameSettings.PendingLoad = null;
            GameSettings.PendingLoadSlot = -1;
            GameSettings.Restoring = load;

            Balance = balanceOverride ? balanceOverride : GameBalance.Load();
            // A seed typed on the title screen wins (P4-10), then the balance asset, then random.
            Seed = GameSettings.PendingSeed != 0 ? GameSettings.PendingSeed
                 : Balance.seed != 0 ? Balance.seed : System.Environment.TickCount & 0x7fffffff;
            GameSettings.PendingSeed = 0;
            MenuState.Reset();
            GameRandom.Shared = new GameRandom(Seed);
            Services.Register(this);
            Services.Register(Balance);
            // Save/load (P5-4): systems register below in this file's order, which is also the restore order.
            var save = new Save.SaveService(Seed);
            Services.Register(save);

            _systems = new GameObject("Systems").transform;

            // --- Core systems ---
            var clock = AddSystem<GameClock>();
            clock.Configure(Balance.realSecondsPerGameHour, Balance.startHour);
            Services.Register(clock);
            save.Register(clock);
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
                InnerHalfSize = Balance.innerHalfSize,
                InnerNestCount = Balance.innerNestCount,
                Landforms = Balance.landforms,
            });
            // The ruins' dungeons (P4-85): an underground band above the surface, from its own stream of the seed.
            map = Underground.Extend(map, Seed);
            // Places worth going to (P4-105), clear of the ruins' ways down: their own stream of the seed, the ground untouched.
            if (Balance.landforms) PlacePlacement.Lay(map, Seed);
            var world = new WorldService(map);
            Services.Register(world);

            var mapRenderer = new GameObject("Map").AddComponent<MapRenderer>();
            mapRenderer.Build(map);
            Services.Register(mapRenderer);

            // --- Fog of war ---
            var fog = new GameObject("FogOfWar").AddComponent<FogOfWar>();
            Services.Register(fog);
            save.Register(fog);
            var townVision = new GameObject("TownVision").AddComponent<VisionSource>();
            townVision.transform.SetParent(_systems, false);
            townVision.transform.position = world.TownCenterWorld;
            townVision.SightRadius = Balance.townSightRadius;
            townVision.SeesOverWalls = true; // watchtowers on the wall
            // P4-81: the ground is painted where the fog lifts (the town first, on the first frame).
            EventBus.Subscribe<FogRevealedEvent>(e => { if (mapRenderer != null) mapRenderer.RequestGround(e.Cells); });
            fog.Init(map.Width, map.Height, Balance.exploredFogAlpha, Balance.fogTickInterval);
            mapRenderer.PaintRequestedNow();
            // P4-102: the zones the guild knows by name (their hearts seen).
            var zones = AddSystem<ZoneDiscovery>();
            zones.Init(map, fog);
            Services.Register(zones);
            // P4-105: places worth going to (hot springs, shrines, ore and herbs, old camps, watchtowers) — shown, learnt, kept.
            var places = AddSystem<Places.PlaceService>();
            places.Init(map, fog, clock);
            Services.Register(places);
            save.Register(places);

            // --- Content & economy ---
            var db = GameDatabase.Load();
            Services.Register(db);
            // The daily tax starts at the town hall's level 1; upgrades raise it (UpgradeService).
            var townHall = db.Building(BuildingType.TownHall)?.Level(1);
            var treasury = new GuildTreasury(Balance.startingGold, townHall?.dailyTax ?? 0);
            treasury.AddStock(DefaultContent.Potion, Balance.startingPotions);
            Services.Register(treasury);
            save.Register(treasury);
            var trade = new TradeService(treasury, db) { LootBuyRate = Balance.lootBuyRate, GoodsSellRate = Balance.goodsSellRate };
            Services.Register(trade);
            save.Register(trade); // the loot buy rate the guild master set (it used to go back to the default on loading)
            var gameLog = new GameLog(clock, db);
            Services.Register(gameLog);
            save.Register(gameLog);

            // --- Town ---
            var town = AddSystem<TownService>();
            town.Init(world, mapRenderer, db); // P5-1: level-1 durability comes from the building data
            Services.Register(town);
            save.Register(town);
            // Gates (P4-39): open for the guild, shut to monsters until broken.
            var gates = AddSystem<GateService>();
            gates.Init(world, treasury, Balance.gateMaxHp, Balance.gateRepairGoldPerHp);
            Services.Register(gates);
            save.Register(gates);

            // Player-placed buildings (P4-13): the watchtower and what comes after it.
            var construction = AddSystem<ConstructionService>();
            construction.Init(town, world, mapRenderer, treasury, db);
            Services.Register(construction);
            // The walls pushed out a step at a time (P4-86). Saved after the gates: rebuilding the walls moves them as loaded.
            var expansion = AddSystem<TownExpansion>();
            expansion.Init(world, town, gates, mapRenderer, townVision, treasury, db);
            Services.Register(expansion);
            save.Register(expansion);
            // The guild's traps (P4-113; its arrow towers are placed buildings): saved after the walls, which are rebuilt first.
            var defences = AddSystem<DefenceService>();
            defences.Init(world, treasury, fog);
            Services.Register(defences);
            save.Register(defences);
            // The town's lamp posts, bushes, flower beds and the like (P4-114): pictures only, set out from the map.
            var decor = AddSystem<TownDecor>();
            decor.Init(world, town);
            Services.Register(decor);
            // The walls standing up (P4-115): a picture on each wall cell, put up again when the walls move out.
            var walls = AddSystem<TownWallView>();
            walls.Init(world);
            Services.Register(walls);

            // --- Units, ecosystem, quests ---
            var pool = Services.Get<PoolService>();
            var projectiles = AddSystem<ProjectileService>();
            projectiles.Init(pool);
            Services.Register(projectiles);
            var fx = AddSystem<FxService>();
            fx.Init(pool);
            Services.Register(fx);
            // Combat juice (P4-8): particle bursts, hit-stop, death/level-up effects, shake.
            var particles = AddSystem<HitParticles>();
            particles.Init(pool);
            Services.Register(particles);
            AddSystem<CombatFeedback>().Init(particles, fx);
            // Effects from the Cartoon Casual VFX Pack when imported (2026-10-30): hits, skills, heals, level-ups.
            var casualVfx = AddSystem<CasualVfx>();
            casualVfx.Init();
            Services.Register(casualVfx);
            // Sound effects and town music (P4-9, 2026-10-31); silent when Resources/Audio is empty.
            var sound = AddSystem<GuildMaster.Audio.SoundService>();
            sound.Init();
            Services.Register(sound);
            // Reward effects in the world (P4-106 H8): buildings finished or upgraded, level-ups.
            AddSystem<RewardFx>().Init(fx, clock);
            // Flares (P4-94): members light up a foe that slipped out of sight.
            var flares = AddSystem<Flares.FlareService>();
            flares.Init(pool);
            Services.Register(flares);
            // Items on the ground (P4-21): drops of the dead, picked up by monsters and adventurers.
            var groundItems = AddSystem<Items.GroundItemService>();
            groundItems.Init(pool, clock, db);
            Services.Register(groundItems);
            save.Register(groundItems);
            var telegraphs = AddSystem<TelegraphService>();
            telegraphs.Init(pool);
            Services.Register(telegraphs);
            Services.Register(AddSystem<CrowdSimulation>());

            var eco = AddSystem<EcosystemManager>();
            Services.Register(eco);
            save.Register(eco);
            eco.Init(db, map, GameRandom.Shared);
            // Territorial clashes: lopsided fights rout the weaker side (P4-18).
            var territory = AddSystem<TerritoryService>();
            territory.Init(eco);
            Services.Register(territory);
            save.Register(territory);
            // Crowded nests move a band a step towards the town (P4-56); WaveManager rolls when.
            var migration = AddSystem<MigrationService>();
            migration.Init(eco, world, clock);
            Services.Register(migration);
            save.Register(migration);
            // Packs driven out with nowhere to go walk their habitat as one (P4-58).
            var roaming = AddSystem<RoamingPackService>();
            roaming.Init(eco, world, clock);
            Services.Register(roaming);
            save.Register(roaming);
            // What the guild has actually seen; UI and adventurers read this, not the simulation.
            var intel = AddSystem<GuildIntel>();
            intel.Init(eco, fog, clock, world);
            Services.Register(intel);
            save.Register(intel);
            // The bestiary (P4-107): research of the kinds the guild has seen, their weakness and strength, mastery.
            var bestiary = AddSystem<Bestiary.BestiaryService>();
            bestiary.Init(intel, eco, fog, clock);
            Services.Register(bestiary);
            save.Register(bestiary);
            // The four region lords (P4-83): who fell, the cleansed land, titles, the victory.
            var lords = new RegionLordService(eco, world, clock);
            Services.Register(lords);
            save.Register(lords);
            // The other villages (P4-84): placed with the map (their own random stream), found by sight, raided, recruited at.
            var villages = AddSystem<Villages.VillageService>();
            villages.Init(world, eco, clock, Seed);
            Services.Register(villages);
            save.Register(villages);
            // Fog memory (P4-23): grey last-seen copies of what the fog swallowed.
            var ghosts = AddSystem<Fog.FogGhostService>();
            ghosts.Init(pool, fog, clock, eco);
            Services.Register(ghosts);
            save.Register(ghosts);

            var board = AddSystem<QuestBoard>();
            board.Init(treasury, eco, clock, Seed);
            Services.Register(board);
            save.Register(board);
            AddSystem<QuestAreaMarkers>().Init();
            // The town posts a paid hunt on every den the guild has seen occupied (P4-82); the guild keeps a fee.
            var townQuests = new TownQuestPoster(board, intel, eco, clock, world);
            Services.Register(townQuests);
            save.Register(townQuests);
            // The other villages' errands (P4-133): goods to carry, ore or herbs to gather, a den's parts — paid in gold and an item.
            var errands = new ErrandPoster(board, villages, clock, Seed);
            Services.Register(errands);
            save.Register(errands);
            AddSystem<DeliveryParcels>().Init();

            // Natural disasters (P4-72): set off by monsters' skills, dealt with through town quests.
            var disasters = AddSystem<Disasters.DisasterService>();
            disasters.Init(clock, world, mapRenderer, board, treasury);
            Services.Register(disasters);
            save.Register(disasters);
            AddSystem<Disasters.HazardOverlay>().Init(world, fog);
            // P4-103: the deep woods' miasma and traps (after the overlay, which draws the miasma as it is laid).
            var woods = AddSystem<DeepWoods>();
            woods.Init(world, fog, clock, Seed);
            Services.Register(woods);
            // Sudden incidents (P4-85): heard of by rumour, dealt with through the quests their clients post.
            var incidents = AddSystem<Incidents.IncidentService>();
            incidents.Init(clock, world, eco, board, fog);
            Services.Register(incidents);
            save.Register(incidents);
            // The dragon (P4-93): asleep at its lair far out (placed without random draws), some days on the wing; its fire.
            var dragonFire = AddSystem<DragonFireFx>();
            dragonFire.Init(pool);
            Services.Register(dragonFire);
            var dragon = AddSystem<Dragons.DragonService>();
            dragon.Init(world, clock, fog, db, Seed);
            Services.Register(dragon);
            save.Register(dragon);

            // --- AI: LLM gateway, for talk only (recruitment, persuasion, proposal cards; rules when no model) ---
            var llm = AddSystem<LlmGateway>();
            llm.SetBackend(LlmBackends.CreateDefault(_systems));
            llm.Enabled = GameSettings.LlmEnabled; // settings menu (P4-10)
            Services.Register(llm);
            // Every NPC decision is the rules' (P4-91, user decision 2026-10-19: no model in behaviour).
            Services.Register<IAdventurerDecisionMaker>(new RuleBasedDecisionMaker());
            var roster = AddSystem<GuildRoster>();
            Services.Register(roster);
            save.Register(roster);
            // The guild's facilities (P4-87): before the founders, whose stats it scales; derived from the town, not saved.
            Services.Register(new FacilityService(town, roster));
            // Per-adventurer memories; before recruiting so the starting members remember joining.
            Services.Register(new MemoryRecorder(clock, roster));
            // Thoughts and mood (P4-104c): what happens to members and round them, fading in time.
            Services.Register(new ThoughtRecorder(clock, roster));
            // Nemeses (P4-109): a monster seen killing or driving off a member is named for it; grudges, the town's bounty.
            var nemeses = AddSystem<Nemeses.NemesisService>();
            nemeses.Init(clock, fog, roster, intel, incidents);
            Services.Register(nemeses);
            save.Register(nemeses);
            // Multi-person quests: the first taker invites idle members (each answers by the rules);
            // party work is gathered for over hours as members come home (P4-97), so it is saved.
            var partyInvites = new PartyService(board, roster, clock);
            Services.Register(partyInvites);
            save.Register(partyInvites);
            // Quests nobody takes for 6 game hours can be talked into by the guild master (P4-24).
            Services.Register(new Persuasion.PersuasionService(board, roster, clock));
            // Notifications (F-1): the list behind the toasts and the HUD badge, fed by gameplay events.
            var notifications = new Notifications.NotificationCenter(() => clock.TotalHours, () => $"{clock.Day}일 {clock.Hour:00}:{clock.Minute:00}");
            Services.Register(notifications);
            save.Register(notifications);
            Services.Register(new Notifications.NotificationSources(notifications, clock, board));
            // Bands that go out hunting without a quest (P4-37); PartyTactics formats them like a party.
            var bands = AddSystem<HuntBandService>();
            bands.Init(clock, Balance.huntBandSize);
            Services.Register(bands);
            save.Register(bands);
            // Standing parties (P4-50): formed in town, pooled pay split by the leader back home.
            var parties = AddSystem<GuildParties>();
            parties.Init(clock, new PartySettlement());
            Services.Register(parties);
            save.Register(parties);
            // Party roles, formation and focus fire on the leader's call (P4-27).
            var partyTactics = AddSystem<PartyTactics>();
            partyTactics.Init(pool);
            Services.Register(partyTactics);
            // Learning on level-up (P4-34): the next skill of the class, by the rules.
            var skillChoice = AddSystem<SkillChoiceService>();
            skillChoice.Init();
            Services.Register(skillChoice);
            // Idle small talk in town (fixed lines).
            var banter = AddSystem<BanterService>();
            banter.Init(roster, world);
            Services.Register(new RelationshipRecorder());
            Services.Register(banter);
            save.Register(banter);
            // Feelings in a fight (P4-131, F-12): friends beside, foes in the way, rivals' races, mentors near a kill.
            var bonds = AddSystem<BondService>();
            bonds.Init(roster, clock, Seed);
            Services.Register(bonds);
            // Camps out in the wild (P4-66): campfires, the watch, raiders drawn by the light, talk at the fire.
            var camps = AddSystem<CampService>();
            camps.Init(clock, world, pool, roster);
            Services.Register(camps);
            save.Register(camps);
            var guildDoor = town.Get(BuildingType.GuildHall).DoorPosition;
            // A loaded game brings its own members back (P5-4).
            for (int i = 0; i < (GameSettings.RestoresSection("roster") ? 0 : Balance.startingAdventurers); i++)
            {
                // Starting lineup covers a fixed set of roles instead of whatever the RNG happens to roll.
                var founder = AdventurerGenerator.Generate(GameRandom.Shared);
                founder.cls = AdventurerGenerator.StartingLineup[i % AdventurerGenerator.StartingLineup.Length];
                roster.Recruit(founder, guildDoor + GameRandom.Shared.InsideUnitCircle());
            }
            // Daily accounts (F-3): income, spending and events per game day. Created after the starting members
            // joined, so they do not count as the first day's recruits.
            var ledger = new Reports.DailyLedger(treasury, clock.Day, clock.HourOfDay);
            Services.Register(ledger);
            save.Register(ledger);
            // The town newspaper (P4-130, F-9): the day's stories, composed when the ledger closes the day, out the next morning.
            var news = new Reports.NewsDesk(clock, roster);
            Services.Register(news);
            save.Register(news);
            // Stress and its consequences (F-2): demands, walking away from quests, quitting; interview, leave, bonus.
            var welfare = new Welfare.GuildWelfare(clock, roster, treasury, board, notifications);
            Services.Register(welfare);
            save.Register(welfare);
            // Mental breaks and inspirations (P4-104d): from mood, apart from F-2's stress.
            Services.Register(new MentalBreaks(clock, roster));
            // Members at the places worth going to (P4-105b): a soak, a prayer, a gathering; the end of a blessing.
            var placeVisits = AddSystem<Places.PlaceVisits>();
            placeVisits.Init(places, clock, roster, world);
            Services.Register(placeVisits);
            // Members' keep (P4-68): meals at midnight, lodging while resting in town, unpaid goes on the slate.
            var living = new LivingCosts(roster, treasury, world, Balance.dailyMealGold, Balance.lodgingGoldPerHour);
            Services.Register(living);
            save.Register(living);
            // Economy round 2 (user decision 2026-10-29): clients pay for finished quests, and the guild buys food.
            Services.Register(new CommissionService(treasury));
            var food = new FoodStore(roster, treasury);
            Services.Register(food);
            save.Register(food); // P4-119: the KMS systems' state is saved too
            // Tribute talks (상납금, 2026-10-29): who was asked when, and grudges.
            var tribute = new TributeService();
            Services.Register(tribute);
            save.Register(tribute);
            // Residents' odd jobs for small income (일손 돕기, 2026-10-29).
            var chores = new ChoreService(treasury);
            Services.Register(chores);
            save.Register(chores);
            // What death leaves behind (F-6): the wall of the fallen, relics to fetch, last stands and the wounds after them.
            var legacy = new Legacy.GuildLegacy(clock, roster, treasury, notifications);
            Services.Register(legacy);
            save.Register(legacy);
            // Quest proposals (F-8): the LLM (or the rules) offers a few quests to choose from instead of the posting form.
            var proposals = new Proposals.QuestProposalService(board, intel, roster, treasury, clock, notifications);
            Services.Register(proposals);
            save.Register(proposals);
            // --- Recruitment: visitors who can be talked into joining ---
            var visitors = AddSystem<Recruitment.VisitorService>();
            visitors.Init(clock, world);
            Services.Register(visitors);
            save.Register(visitors);
            // Lets ambient town small talk pair a visitor with a member too (user request 2026-09-23);
            // wired here (after VisitorService is created) rather than in BanterService.Init above.
            banter.SetVisitorService(visitors);
            // Building upgrades (P4-1).
            Services.Register(new UpgradeService(town, treasury, db, roster));
            // Crafting at the apothecary / blacksmith (P4-2).
            var crafting = AddSystem<CraftingService>();
            crafting.Init(treasury, db, clock, town);
            Services.Register(crafting);
            save.Register(crafting);
            // Travelling merchant buying the guild's stock (P4-3).
            var market = new MerchantMarket(treasury, db, eco.LootAbundance);
            Services.Register(market);
            var merchant = AddSystem<MerchantService>();
            merchant.Init(clock, world, market);
            Services.Register(merchant);
            save.Register(merchant);
            // The guild's fame and rank (P4-106, H1): after everything whose events it counts; S is the victory.
            var rank = AddSystem<Guild.GuildRankService>();
            rank.Init(clock, roster, treasury, loading: load != null);
            Services.Register(rank);
            save.Register(rank);
            // The guild's rules (P4-112, H2): after the rank that opens them; the systems read their numbers from it.
            var laws = AddSystem<Guild.GuildLaws>();
            laws.Init(clock, roster);
            Services.Register(laws);
            save.Register(laws);
            // The seasons (P4-111b, H11): from the day, nothing saved; the others ask it for their factor.
            var seasons = AddSystem<Seasons.SeasonService>();
            seasons.Init(clock, roster, world);
            Services.Register(seasons);
            // Town life between everyone in town — members, visitors, the merchant (user request 2026-10-01):
            // comfort, quarrels and making up, rumors. Next to BanterService, which it leaves as it is.
            var townTalk = AddSystem<Social.TownTalkService>();
            townTalk.Init(llm, roster, world, visitors, merchant, clock);
            Services.Register(townTalk);
            save.Register(townTalk);
            // Townsfolk (user request 2026-10-07): six residents who go about their day and talk with everyone in town.
            var residents = AddSystem<Social.ResidentService>();
            residents.Init(town, world);
            Services.Register(residents);
            save.Register(residents);
            townTalk.SetResidents(residents);
            // The residents' requests and the town's word on the guild (P4-134, F-14): asked of the guild master directly,
            // thanked once and with a standing favour of their trade; new visitors hear what the town thinks.
            var residentRequests = new Social.ResidentRequests(board, residents, roster, treasury, clock, Seed);
            Services.Register(residentRequests);
            save.Register(residentRequests);
            // Feelings over heads and the relationship graph (F-10, user request 2026-10-11).
            var emotions = new Social.EmotionService(clock, roster, residents);
            Services.Register(emotions);
            save.Register(emotions);
            // KMS's event cards (F-13) are part of the decision cards now (P4-118, DilemmaService below).
            // "다음 할 일" checklist and hints (F-19).
            var guide = new Guide.GuideTracker();
            Services.Register(guide);
            save.Register(guide);

            // --- Camera ---
            var cam = SetupCamera(world);
            Services.Register(cam);
            // The spectator director (P4-129, H7): the moments worth watching, and the camera mode that goes to them.
            var highlights = AddSystem<Director.HighlightService>();
            highlights.Init(clock, roster, world);
            Services.Register(highlights);
            var spectator = AddSystem<Director.SpectatorCamera>();
            spectator.Init(highlights);
            Services.Register(spectator);

            // --- Player ---
            var avatar = PlayerAvatar.Create(world.TownCenterWorld + new Vector2(0f, -2f), Balance.playerMoveSpeed, Balance.playerSightRadius);
            Services.Register(avatar);
            save.Register(avatar);
            var gameOver = new GameOverService(clock);
            Services.Register(gameOver);
            save.Register(gameOver);

            // --- Raids (P3-6) ---
            Monsters.RaidState.BuildingDamageMul = Balance.monsterBuildingDamageMul; // P5-1
            var waves = AddSystem<WaveManager>();
            waves.Init(new RaidSettings
            {
                ChancePerLevelPerHour = Balance.raidChancePerLevelPerHour, SendFraction = Balance.raidSendFraction, MinParty = Balance.raidMinParty,
                MaxHours = Balance.raidMaxHours,
            }, clock, eco, world);
            Services.Register(waves);
            save.Register(waves);
            // Raids on the other villages (P4-84): dens by them and packs passing; the village posts its defence.
            var villageRaids = AddSystem<Villages.VillageRaids>();
            villageRaids.Init(new RaidSettings
            {
                ChancePerLevelPerHour = Balance.raidChancePerLevelPerHour, SendFraction = Balance.raidSendFraction, MinParty = Balance.raidMinParty,
                MaxHours = Balance.raidMaxHours,
            }, clock, eco, villages);
            Services.Register(villageRaids);
            save.Register(villageRaids);
            AddSystem<DefenseCoordinator>().Init(waves, roster, world, Balance.raidDefendRadius);
            // The storyteller (P4-111, G5): after everything it sets off or quiets — raids, hordes, the dragon, disasters.
            var storyteller = AddSystem<Storytelling.StorytellerService>();
            storyteller.Init(clock, world, eco, roster, Seed);
            Services.Register(storyteller);
            save.Register(storyteller);
            // Decision cards (P4-111c, H3): dealt by the storyteller's mood, after everything a choice can touch.
            var dilemmas = AddSystem<Dilemmas.DilemmaService>();
            dilemmas.Init(clock, roster, treasury, world, Seed);
            Services.Register(dilemmas);
            save.Register(dilemmas);
            // The guild master's orders (P4-132, H6): a cheer, a focus, a rally flag, a retreat horn — one gauge for all.
            var orders = AddSystem<Commands.MasterCommands>();
            orders.Init(avatar, clock, roster, town, Seed);
            Services.Register(orders);
            save.Register(orders);
            AddSystem<PlayerCommandInput>().Init(avatar, cam, Services.Get<PoolService>());

            // --- Presentation / input ---
            DayNightLighting.Create(_systems, clock);
            EnsureEventSystem();
            AddSystem<GameHotkeys>();

            if (load != null)
            {
                var warnings = save.Restore(load, loadSlot);
                Debug.Log($"[GuildMaster] Save restored (slot {loadSlot}, day {load.summary.day}, {warnings.Count} warnings).");
            }
            GameSettings.Restoring = null;

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
            // Looking into a ruin's dungeon (P4-85), the camera keeps to it.
            controller.AreaBounds = world.UndergroundBoundsAt;
            controller.JumpTo(world.TownCenterWorld, byPlayer: false);
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
