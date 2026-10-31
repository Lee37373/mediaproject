using System.Collections;
using System.IO;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Disasters;
using GuildMaster.Save;
using GuildMaster.Session;
using GuildMaster.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Save/load (P5-4). The main check is the round trip: save A, boot a fresh session from A,
    /// save B straight away — every section of B must equal A's. A system whose state does not come
    /// back shows up as a differing section. Slots are written to a temporary folder.
    /// </summary>
    public class SaveTests
    {
        const int TestSeed = 20260920;
        static int s_sceneCounter;
        Scene _scene;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveService.FolderOverride = Path.Combine(Application.temporaryCachePath, "save-tests");
            if (Directory.Exists(SaveService.FolderOverride)) Directory.Delete(SaveService.FolderOverride, true);
            _scene = SceneManager.CreateScene($"Save{++s_sceneCounter}");
            SceneManager.SetActiveScene(_scene);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            GameSettings.ShowTitle = true;
            GameSettings.PaintGround = true;
            GameSettings.PendingLoad = null;
            MenuState.Reset();
            SaveService.FolderOverride = null;
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
            yield return Resources.UnloadUnusedAssets();
            System.GC.Collect();
        }

        static void BootNow()
        {
            GameSettings.ShowTitle = false;
            if (GameSettings.PendingLoad == null) GameSettings.PendingSeed = TestSeed;
            GameSettings.PaintGround = false;
            DisasterService.ChanceOverride = 0f;
            DisasterService.SpreadScale = 1f;
            new GameObject("GameBootstrap").AddComponent<GameBootstrap>();
            new GameObject("UI").AddComponent<UIRoot>();
        }

        /// <summary>The save taken in the same frame the restored session booted (before anything ticked).</summary>
        SaveFile _afterBoot;

        /// <summary>Ends the running session (its scene unloads) and boots the saved one in a new scene.</summary>
        IEnumerator BootFrom(SaveFile file)
        {
            var old = _scene;
            _scene = SceneManager.CreateScene($"Save{++s_sceneCounter}");
            SceneManager.SetActiveScene(_scene);
            yield return SceneManager.UnloadSceneAsync(old);
            SaveService.Hand(file);
            BootNow();
            _afterBoot = Saves.Capture();
        }

        static SaveService Saves => Services.Get<SaveService>();

        static void AssertSameSections(SaveFile a, SaveFile b, params string[] except)
        {
            Assert.That(b.seed, Is.EqualTo(a.seed), "seed");
            foreach (var section in a.sections)
            {
                if (System.Array.IndexOf(except, section.key) >= 0) continue;
                var other = b.Find(section.key);
                Assert.That(other, Is.Not.Null, "section " + section.key);
                if (other.json != section.json)
                {
                    // Both sides on disk for a diff (Temp/save-diff-*.json).
                    File.WriteAllText(Path.Combine("Temp", $"save-diff-{section.key}-A.json"), section.json);
                    File.WriteAllText(Path.Combine("Temp", $"save-diff-{section.key}-B.json"), other.json);
                }
                Assert.That(other.json, Is.EqualTo(section.json), $"section '{section.key}' came back the same");
            }
        }

        [System.Serializable]
        class FogBits { public int width; public int height; public string explored; }

        /// <summary>
        /// A moment after a busy load the fog may have lifted a cell or two more than was saved (this failed now and then on
        /// one cell, a different one each time): positions are saved to the hundredth, and an eye half a hundredth off sees
        /// one more cell at the very edge of its circle. Everything explored must still be, and little more is.
        /// </summary>
        static void AssertFogAlmostSame(SaveFile a, SaveFile b, int slack)
        {
            var fa = JsonUtility.FromJson<FogBits>(a.Find("fog").json);
            var fb = JsonUtility.FromJson<FogBits>(b.Find("fog").json);
            Assert.That((fb.width, fb.height), Is.EqualTo((fa.width, fa.height)), "fog size");
            var ba = System.Convert.FromBase64String(fa.explored);
            var bb = System.Convert.FromBase64String(fb.explored);
            int lost = 0, gained = 0;
            for (int i = 0; i < ba.Length && i < bb.Length; i++)
                for (int bit = 0; bit < 8; bit++)
                {
                    bool was = (ba[i] & (1 << bit)) != 0, now = (bb[i] & (1 << bit)) != 0;
                    if (was && !now) lost++;
                    else if (now && !was) gained++;
                }
            Assert.That(lost, Is.Zero, "no explored cell is forgotten");
            Assert.That(gained, Is.LessThanOrEqualTo(slack), "hardly any cell lifts that was not lifted when saved");
        }

        [UnityTest]
        public IEnumerator RoundTrip_EverySectionComesBackTheSame()
        {
            BootNow();
            yield return null;
            var clock = Services.Get<GameClock>();
            // Let the world move a little: members walk out, fog lifts, the hourly EXP ticks.
            clock.SetSpeed(GameClock.SpeedScales.Length - 1);
            float start = clock.TotalHours;
            float end = Time.realtimeSinceStartup + 20f;
            while (clock.TotalHours < start + 3f && Time.realtimeSinceStartup < end) yield return null;

            // Things the saved game must carry: a placed tower, damage, stock.
            var construction = Services.Get<Town.ConstructionService>();
            var treasury = Services.Get<Economy.GuildTreasury>();
            var cost = construction.CostOf(Town.BuildingType.Watchtower);
            treasury.AddGold(cost.goldCost);
            foreach (var c in cost.itemCosts) treasury.AddStock(c.itemId, c.count);
            var world = Services.Get<World.WorldService>();
            Assert.That(construction.TryBuild(Town.BuildingType.Watchtower, world.TownCenterWorld + new Vector2(15f, 0f), out var error), Is.True, error);
            treasury.AddStock("slime_jelly", 7);
            var town = Services.Get<Town.TownService>();
            town.Get(Town.BuildingType.Blacksmith).Damage(40f);
            Services.Get<Town.GateService>().Gates[0].Damage(1e6f); // broken: the walls are open there
            clock.SetSpeed(0); // nothing moves between the save and the comparison
            // Some checks run on real time while paused (fog, nests coming into view): let them settle.
            yield return new WaitForSecondsRealtime(1f);

            var a = Saves.Capture();
            // A freshly started game loads from behind the title, whose own founders were numbered
            // from 1: the save must hand its counter back, or the next visitor gets a member's id.
            Adventurers.AdventurerGenerator.Counter = 0;
            yield return BootFrom(a);
            AssertSameSections(a, _afterBoot);
            var newcomer = Adventurers.AdventurerGenerator.NextId();
            foreach (var m in Services.Get<Adventurers.GuildRoster>().Members)
                Assert.That(m.Data.id, Is.Not.EqualTo(newcomer), "a new adventurer never takes a loaded member's id");
            Adventurers.AdventurerGenerator.Counter--; // that id was only a look

            // And the restored world behaves like it: the broken gate is open, the tower watches.
            var gate = Services.Get<Town.GateService>().Gates[0];
            Assert.That(gate.IsBroken, Is.True);
            Assert.That(Services.Get<World.WorldService>().Grid.IsClosedGate(gate.CenterCell), Is.False, "a broken gate lets the way through");
            var tower = Services.Get<Town.TownService>().Get(Town.BuildingType.Watchtower);
            Assert.That(tower, Is.Not.Null, "the tower is back");
            Assert.That(tower.GetComponent<Fog.VisionSource>().enabled, Is.True, "and watches");

            // A second later (systems that set up in Start, the real-time checks), still the same.
            yield return new WaitForSecondsRealtime(1f);
            AssertSameSections(a, Saves.Capture());
        }

        /// <summary>
        /// P4-86: a town grown two steps comes back as it was — the loaded map is grown again from the fresh one, so
        /// every tile, the gates (where they stand and how battered), the retired den sites and the town's sight match.
        /// </summary>
        [UnityTest]
        public IEnumerator RoundTrip_TheGrownTownComesBack()
        {
            BootNow();
            yield return null;
            var expansion = Services.Get<Town.TownExpansion>();
            var treasury = Services.Get<Economy.GuildTreasury>();
            for (int i = 0; i < 2; i++)
            {
                var price = expansion.NextCost.Value;
                treasury.AddGold(price.Gold);
                foreach (var item in price.Items) treasury.AddStock(item.itemId, item.count);
                Assert.That(expansion.TryExpand(out var error), Is.True, error);
            }
            Services.Get<Town.GateService>().Gates[1].Damage(120f);
            Services.Get<GameClock>().SetSpeed(0);
            yield return new WaitForSecondsRealtime(1f);
            var map = Services.Get<World.WorldService>().Map;
            var tiles = (World.TileType[])map.Tiles.Clone();
            var trees = (bool[])map.Trees.Clone();
            var gates = new System.Collections.Generic.List<Vector2Int>(map.Gates);
            float sight = expansion.TownSight;
            float battered = Services.Get<Town.GateService>().Gates[1].Hp;

            var a = Saves.Capture();
            yield return BootFrom(a);
            AssertSameSections(a, _afterBoot);
            var loaded = Services.Get<World.WorldService>().Map;
            Assert.That(Services.Get<Town.TownExpansion>().Stage, Is.EqualTo(2));
            Assert.That(loaded.TownHalfSize, Is.EqualTo(19));
            Assert.That(loaded.Tiles, Is.EqualTo(tiles), "every tile as it was");
            Assert.That(loaded.Trees, Is.EqualTo(trees));
            Assert.That(loaded.Gates, Is.EqualTo(gates));
            Assert.That(Services.Get<Town.TownExpansion>().TownSight, Is.EqualTo(sight).Within(0.01f));
            var gate = Services.Get<Town.GateService>().Gates[1];
            Assert.That(gate.CenterCell, Is.EqualTo(gates[1]));
            Assert.That(gate.Hp, Is.EqualTo(battered).Within(0.01f), "as battered as it was");
            Assert.That(Services.Get<World.WorldService>().Grid.IsClosedGate(gate.CenterCell), Is.True);
            foreach (var n in Services.Get<Ecosystem.EcosystemManager>().Nests.Where(n => Town.TownExpansionRules.Crowds(n.Cell, loaded.TownCenter, 19)))
                Assert.That(n.Retired && !n.Discovered, Is.True, $"{n.DisplayName} stays the town's ground");
        }

        /// <summary>
        /// The same round trip with everything going on at once: a hunt taken by two, a visitor and the
        /// merchant in town, a disaster burning, loot on the ground, a raid on the way, a camp out in
        /// the wild, auto-crafting set. Saved mid-flow, it all comes back as it was.
        /// </summary>
        [UnityTest]
        public IEnumerator RoundTrip_WithEverythingGoingOn()
        {
            BootNow();
            yield return null;
            var clock = Services.Get<GameClock>();
            var eco = Services.Get<Ecosystem.EcosystemManager>();
            var known = typeof(Ecosystem.GuildIntel).GetField("_known", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            ((System.Collections.Generic.HashSet<Data.MonsterDefinition>)known.GetValue(Services.Get<Ecosystem.GuildIntel>()))
                .Add(System.Linq.Enumerable.First(eco.Species, s => s.id == "slime"));
            string questId = DebugCommands.PostHunt("slime", 3, 30, 10, 2);
            // A region lord down (P4-83): its land sealed, a member with its title.
            DebugCommands.KillLord(0, 0);
            DebugCommands.ForceAccept(questId, 2);
            DebugCommands.SpawnVisitor();
            DebugCommands.MerchantVisit();
            DebugCommands.DropItem("slime_jelly", 2);
            DebugCommands.ForceRaid(0, true);
            // One member far out and hurt makes camp there.
            var camper = Services.Get<Adventurers.GuildRoster>().Members[3];
            var grid = Services.Get<World.WorldService>().Grid;
            Assert.That(grid.TryFindNearestWalkable(Services.Get<World.WorldService>().Map.TownCenter + new Vector2Int(0, -40), 8, out var campCell), Is.True);
            camper.Mover.Stop();
            camper.transform.position = World.GridMap.CellToWorld(campCell);
            // With a comrade (P4-98): past the sitting hour one keeps watch and the other sleeps — and must still be asleep,
            // all but blind, the moment the game is loaded (it used to see as far as an awake eye until its first step).
            var comrade = Services.Get<Adventurers.GuildRoster>().Members.First(m => m != camper && m.IsAlive && m.CurrentQuest == null);
            comrade.Mover.Stop();
            comrade.transform.position = World.GridMap.CellToWorld(campCell) + new Vector2(1f, 0f);
            DebugCommands.Hurt(3, camper.Stats.MaxHp * 0.6f);
            DebugCommands.CampNow(3, false);
            var crafting = Services.Get<Economy.CraftingService>();
            crafting.SetTarget(crafting.Recipes[0], 2);
            // A wildfire (or whatever the ground nearby suits) somewhere out of town.
            var disasters = Services.Get<Disasters.DisasterService>();
            var town = Services.Get<World.WorldService>().Map.TownCenter;
            for (int r = 25; r < 70 && disasters.Active.Count == 0; r += 5)
            for (int k = 0; k < 16 && disasters.Active.Count == 0; k++)
            {
                var cell = town + new Vector2Int(Mathf.RoundToInt(Mathf.Cos(k * 0.39f) * r), Mathf.RoundToInt(Mathf.Sin(k * 0.39f) * r));
                for (int kind = 0; kind < 5 && disasters.Active.Count == 0; kind++) disasters.Begin((Disasters.DisasterKind)kind, cell);
            }
            Assert.That(disasters.Active.Count, Is.GreaterThan(0), "a disaster under way");
            // A pack on its way to a new home (P4-56).
            var migration = Services.Get<Ecosystem.MigrationService>();
            foreach (var nest in eco.Nests)
                if (!Services.Get<Town.WaveManager>().IsRaiding(nest) && migration.TryLaunch(nest)) break;
            Assert.That(migration.Bands.Count, Is.GreaterThan(0), "a band migrating");
            // Other villages (P4-84): one the guild master stands by (seen, its candidates out), one under attack,
            // one fallen with its people on the road to our town, and a new one.
            var villages = Services.Get<Villages.VillageService>();
            DebugCommands.JumpToVillage(0);
            Assert.That(DebugCommands.RaidVillage(1, "goblin", 3, 9f), Does.Contain("raid of"));
            DebugCommands.HurtVillage(2, villages.Villages[2].MaxHp * 0.8f);
            DebugCommands.HurtVillage(2, villages.Villages[2].MaxHp);
            Assert.That(villages.TryFound(out _), Is.True, "a new village");
            // A rare monster (P4-85) whose rumour has come: its hunt on the board, pointing at it.
            var rare = Services.Get<Incidents.IncidentService>().StartRareMonster();
            Assert.That(rare, Is.Not.Null, "a rare monster out there");
            rare.RumourAtHour = clock.TotalHours;
            // And a horde on the march (P4-85b) for a site emptied for it, out of the way of the hunt, the raid and the band.
            var waveManager = Services.Get<Town.WaveManager>();
            var questBoard = Services.Get<Quests.QuestBoard>();
            var hordeSite = System.Linq.Enumerable.First(eco.Nests, n => !n.IsEmpty && !n.IsLair && !n.Sealed && !waveManager.IsRaiding(n)
                && !migration.IsMigrating(n) && !migration.IsTarget(n) && !System.Linq.Enumerable.Any(questBoard.Quests, q => q.TargetNest == n)
                && System.Linq.Enumerable.Any(eco.Species, s => s.canRaid && n.Suits(s)));
            foreach (var m in System.Linq.Enumerable.ToList(hordeSite.Members)) m.Perish();
            var horde = Services.Get<Incidents.IncidentService>().StartHorde(hordeSite, null, 10);
            Assert.That(horde, Is.Not.Null, "a horde on the march");
            horde.RumourAtHour = clock.TotalHours;
            // And a traveller ambushed on the road (P4-85c), holding out.
            var rescue = Services.Get<Incidents.IncidentService>().StartRescue();
            Assert.That(rescue, Is.Not.Null, "an ambush on the road");
            rescue.RumourAtHour = clock.TotalHours;
            // And a ruin whose word has come (P4-85d): its guardians below, its chest shut.
            var ruin = Services.Get<Incidents.IncidentService>().StartRuin();
            Assert.That(ruin, Is.Not.Null, "a ruin talked of");
            ruin.RumourAtHour = clock.TotalHours;
            // A guild facility (P4-87), raised a level.
            Assert.That(DebugCommands.BuildFacility("PoisonLab", 2), Does.StartWith("PoisonLab Lv2"));
            // A nemesis (P4-109): a monster named for a member it killed, a friend's grudge against it, its bounty to come.
            var nemesisDen = System.Linq.Enumerable.First(eco.Nests, n => n.Members.Count > 1 && !n.IsLair && n != hordeSite && n.Species.id != "slime"
                && !waveManager.IsRaiding(n) && !migration.IsMigrating(n));
            var roster = Services.Get<Adventurers.GuildRoster>().Members;
            var named = Services.Get<Nemeses.NemesisService>().Record(nemesisDen.Members[1], roster[roster.Count - 1], Nemeses.DeedKind.Killed,
                new System.Collections.Generic.List<Adventurers.Adventurer> { roster[0] });
            Assert.That(named, Is.Not.Null, "a nemesis");

            clock.SetSpeed(3);
            float start = clock.TotalHours;
            float end = Time.realtimeSinceStartup + 15f;
            while (clock.TotalHours < start + 1.5f && Time.realtimeSinceStartup < end) yield return null;
            clock.SetSpeed(0);
            yield return new WaitForSecondsRealtime(1f);

            var board = Services.Get<Quests.QuestBoard>();
            int quests = board.Quests.Count, takers = board.FindById(questId)?.Assignees.Count ?? 0;
            int visitors = Services.Get<Recruitment.VisitorService>().Visitors.Count;
            bool merchant = Services.Get<Town.MerchantService>().Merchant != null;
            int camps = Services.Get<Adventurers.CampService>().Camps.Count;
            int raidCount = Services.Get<Town.WaveManager>().RaidCount;
            int active = disasters.Active.Count;
            bool villageAttacked = Services.Get<Villages.VillageRaids>().IsUnderAttack(villages.Villages[1]);
            int villageCount = villages.Villages.Count;
            Debug.Log($"[SaveTests] busy save: quests {quests} takers {takers} visitors {visitors} merchant {merchant} camps {camps} raids {raidCount} disasters {active} ground {Services.Get<Items.GroundItemService>().Items.Count} villages {villageCount} village raid {villageAttacked}");
            Assert.That(villageAttacked, Is.True, "a village under attack");
            Assert.That(takers, Is.GreaterThan(0), "the hunt was taken");
            Assert.That(visitors, Is.GreaterThan(0), "a visitor in town");
            Assert.That(merchant, Is.True, "the merchant in town");
            Assert.That(camps, Is.GreaterThan(0), "a camp out in the wild");
            var fire = Services.Get<Adventurers.CampService>().Camps[0];
            Assert.That(fire.Members.Count >= 2 && !fire.Alarmed && !fire.Sitting(clock.TotalHours) && fire.Watcher != null, Is.True,
                $"two or more at the fire (another member nearby may join — it did on the P4-102 map), one on watch ({fire.Members.Count}, alarmed {fire.Alarmed}, watcher {fire.Watcher?.Data.name})");
            var sleeperIds = fire.Members.Where(m => m != fire.Watcher && m.Visual.Lying).Select(m => m.Data.id).ToList();
            Assert.That(sleeperIds, Is.Not.Empty, "someone asleep by the fire");
            Assert.That(rare.Rumoured && rare.Quest != null && !rare.Over, Is.True, "the rare monster's hunt is up");
            string rareQuest = rare.Quest.Id;
            Assert.That(horde.Rumoured && horde.Quest != null && !horde.Over, Is.True, "the horde's interception is up");
            string hordeQuest = horde.Quest.Id;
            int hordeLeft = horde.Members.Count;
            Assert.That(rescue.Rumoured && rescue.Traveller != null && rescue.Traveller.IsAlive && !rescue.Over, Is.True, "the traveller holds out, its rescue is up");
            string rescueQuest = rescue.Quest.Id;
            int ambushersLeft = rescue.Members.Count;
            Assert.That(ruin.Rumoured && ruin.Quest != null && !ruin.ChestOpened, Is.True, "the ruin's exploration is up");
            string ruinQuest = ruin.Quest.Id;
            int guardians = ruin.Members.Count;
            // A monster a member has just marked (P4-87): poison burning, armour broken, chilled — paused, so it all waits.
            var marked = System.Linq.Enumerable.First(eco.Nests, n => n.Members.Count > 0 && !n.IsLair).Members[0];
            var poisoner = Services.Get<Adventurers.GuildRoster>().Members[0];
            marked.Afflictions.Poison(12f, 3f, poisoner);
            marked.Afflictions.BreakArmor(0.2f, 3f);
            marked.Afflictions.Chill(0.3f, 2f);
            var nemeses = Services.Get<Nemeses.NemesisService>().Living.Select(n => (n.UnitId, n.Name, n.Monster.Level, bounty: n.Bounty?.Quest?.Id)).ToList();
            Assert.That(nemeses.Any(n => n.UnitId == named.UnitId), Is.True, "the nemesis lives on");
            Assert.That(named.Bounty?.Quest, Is.Not.Null, "with its bounty up");

            var a = Saves.Capture();
            yield return BootFrom(a);
            AssertSameSections(a, _afterBoot);
            // Every nemesis under its own name, at its level, its bounty on it; the friend's grudge kept (P4-109).
            var loadedNemeses = Services.Get<Nemeses.NemesisService>();
            foreach (var (id, name, level, bountyId) in nemeses)
            {
                var n = loadedNemeses.Find(id);
                Assert.That(n?.Monster, Is.Not.Null, name);
                Assert.That((n.Monster.IsNemesis, n.Monster.DisplayName, n.Monster.Level), Is.EqualTo((true, name, level)), name);
                if (bountyId != null)
                    Assert.That(Services.Get<Quests.QuestBoard>().FindById(bountyId)?.TargetUnit, Is.SameAs(n.Monster), "its bounty is on it");
            }
            Assert.That(Services.Get<Adventurers.GuildRoster>().Members[0].Data.thoughts.Any(t => t.id == Adventurers.ThoughtRules.Grudge.Id && t.about == named.UnitId),
                Is.True, "the grudge");
            // Every one of them (P4-102: a third who sat down short of its seat came back awake).
            foreach (var id in sleeperIds)
            {
                var sleeper = Services.Get<Adventurers.GuildRoster>().Members.First(m => m.Data.id == id);
                Assert.That((sleeper.Visual.Lying, sleeper.GetComponent<Fog.VisionSource>().SightRadius), Is.EqualTo((true, Adventurers.CampRules.SleeperSight)),
                    $"{sleeper.Data.name} asleep by the fire from the first frame");
            }

            board = Services.Get<Quests.QuestBoard>();
            Assert.That(board.Quests.Count, Is.EqualTo(quests), "quests");
            Assert.That(board.FindById(questId)?.Assignees.Count ?? 0, Is.EqualTo(takers), "the hunt's takers");
            Assert.That(Services.Get<Recruitment.VisitorService>().Visitors.Count, Is.EqualTo(visitors), "visitors");
            Assert.That(Services.Get<Town.MerchantService>().Merchant != null, Is.EqualTo(merchant), "the merchant");
            Assert.That(Services.Get<Adventurers.CampService>().Camps.Count, Is.EqualTo(camps), "camps");
            Assert.That(Services.Get<Town.WaveManager>().RaidCount, Is.EqualTo(raidCount), "raids");
            Assert.That(Services.Get<Disasters.DisasterService>().Active.Count, Is.EqualTo(active), "disasters");
            villages = Services.Get<Villages.VillageService>();
            Assert.That(villages.Villages.Count, Is.EqualTo(villageCount), "villages, ruins and the new one");
            Assert.That(Services.Get<Villages.VillageRaids>().IsUnderAttack(villages.Villages[1]), Is.True, "the village raid");
            Assert.That(villages.Villages[2].IsDestroyed && Services.Get<World.WorldService>().Grid.IsWalkable(villages.Villages[2].Cell), Is.True, "the ruins are open ground");
            var loadedRare = board.FindById(rareQuest);
            Assert.That(loadedRare?.Incident, Is.Not.Null, "the rare hunt knows its incident");
            Assert.That(loadedRare.TargetUnit != null && loadedRare.TargetUnit.IsRare && loadedRare.TargetUnit.IsAlive, Is.True, "and its monster, rare as it was");
            Assert.That(Services.Get<Incidents.IncidentService>().Active, Does.Contain(loadedRare.Incident));
            var loadedHorde = board.FindById(hordeQuest)?.Incident;
            Assert.That(loadedHorde, Is.Not.Null, "the horde's quest knows its incident");
            Assert.That(loadedHorde.Members.Count, Is.EqualTo(hordeLeft), "its members");
            Assert.That(loadedHorde.Band != null && System.Linq.Enumerable.Contains(Services.Get<Ecosystem.MigrationService>().Bands, loadedHorde.Band) && loadedHorde.Band.Horde,
                        Is.True, "and its march");
            var loadedRescue = board.FindById(rescueQuest)?.Incident;
            Assert.That(loadedRescue?.Traveller != null && loadedRescue.Traveller.IsAlive, Is.True, "the traveller is back");
            Assert.That(loadedRescue.Traveller.Current, Is.EqualTo(Incidents.Traveller.Phase.Crouching), "still crouching by its cart");
            Assert.That(loadedRescue.Members.Count, Is.EqualTo(ambushersLeft), "with its ambushers");
            Assert.That(System.Linq.Enumerable.Count(loadedRescue.Members, m => m.Engage.Target == loadedRescue.Traveller), Is.GreaterThan(0), "who are still at it");
            var loadedRuin = board.FindById(ruinQuest)?.Incident;
            Assert.That(loadedRuin?.RuinIndex, Is.EqualTo(ruin.RuinIndex), "the ruin's exploration knows its ruin");
            Assert.That(loadedRuin.Members.Count, Is.EqualTo(guardians), "its guardians");
            var loadedMap = Services.Get<World.WorldService>().Map;
            Assert.That(System.Linq.Enumerable.All(loadedRuin.Members, m => loadedMap.RuinAt(World.GridMap.WorldToCell(m.Position)) != null), Is.True, "still down in its halls");

            yield return new WaitForSecondsRealtime(1f);
            var later = Saves.Capture();
            AssertSameSections(a, later, "fog");
            AssertFogAlmostSame(a, later, 8);

            // The loaded raid and migration still hear their members fall (P4-76: the restore did
            // not subscribe to the deaths — a wiped-out raid stayed "on" and kept the defenders
            // at the gate until its time ran out).
            var waves = Services.Get<Town.WaveManager>();
            migration = Services.Get<Ecosystem.MigrationService>();
            var marching = new System.Collections.Generic.List<Monsters.Monster>();
            foreach (var band in migration.Bands) marching.AddRange(band.Members);
            bool raiding = waves.RaidActive;
            var villageRaids = Services.Get<Villages.VillageRaids>();
            foreach (var u in System.Linq.Enumerable.ToList(Units.UnitRegistry.All))
                if (u is Monsters.Monster m && m.IsAlive && (m.DefaultStateOverride == m.RaidState || m.DefaultStateOverride == m.VillageRaidState)) m.Perish();
            foreach (var m in marching) m.Perish();
            yield return null;
            if (raiding) Assert.That(waves.RaidActive, Is.False, "a loaded raid ends when its last raider falls");
            Assert.That(System.Linq.Enumerable.Any(villageRaids.Report()), Is.False, "so does a loaded raid on a village");
            Assert.That(migration.Bands.Count, Is.EqualTo(0), "a loaded band is gone when its members are");
            foreach (var m in marching) Assert.That(migration.TryGetBand(m, out _), Is.False, "the fallen are out of the band");
        }

        [UnityTest]
        public IEnumerator Slots_AreWrittenReadListedAndDeleted_AndBadFilesAreRefused()
        {
            BootNow();
            yield return null;
            Assert.That(SaveService.Peek(0).Exists, Is.False, "empty to begin with");
            Assert.That(Saves.Save(0, out var error), Is.True, error);
            var info = SaveService.Peek(0);
            Assert.That(info.Exists && info.Problem == null, Is.True, info.Problem);
            Assert.That(info.Summary.day, Is.EqualTo(Services.Get<GameClock>().Day));
            Assert.That(info.Summary.gold, Is.EqualTo(Services.Get<Economy.GuildTreasury>().Gold));
            Assert.That(SaveService.TryRead(0, out var file, out error), Is.True, error);
            Assert.That(file.seed, Is.EqualTo(TestSeed));

            Assert.That(Saves.Save(SaveService.SlotCount, out _), Is.False, "no such slot");

            File.WriteAllText(SaveService.PathOf(1), "{ not json");
            Assert.That(SaveService.Peek(1).Problem, Is.Not.Null, "a broken file is reported, not loaded");
            file.version = SaveFile.CurrentVersion + 1;
            File.WriteAllText(SaveService.PathOf(2), JsonUtility.ToJson(file));
            Assert.That(SaveService.Peek(2).Problem, Does.Contain("새 버전"));

            Assert.That(SaveService.Delete(0), Is.True);
            Assert.That(SaveService.Peek(0).Exists, Is.False);
        }
    }
}
