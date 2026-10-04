using System.Collections;
using System.IO;
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
            yield return BootFrom(a);
            AssertSameSections(a, _afterBoot);

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
            Debug.Log($"[SaveTests] busy save: quests {quests} takers {takers} visitors {visitors} merchant {merchant} camps {camps} raids {raidCount} disasters {active} ground {Services.Get<Items.GroundItemService>().Items.Count}");
            Assert.That(takers, Is.GreaterThan(0), "the hunt was taken");
            Assert.That(visitors, Is.GreaterThan(0), "a visitor in town");
            Assert.That(merchant, Is.True, "the merchant in town");
            Assert.That(camps, Is.GreaterThan(0), "a camp out in the wild");

            var a = Saves.Capture();
            yield return BootFrom(a);
            AssertSameSections(a, _afterBoot);

            board = Services.Get<Quests.QuestBoard>();
            Assert.That(board.Quests.Count, Is.EqualTo(quests), "quests");
            Assert.That(board.FindById(questId)?.Assignees.Count ?? 0, Is.EqualTo(takers), "the hunt's takers");
            Assert.That(Services.Get<Recruitment.VisitorService>().Visitors.Count, Is.EqualTo(visitors), "visitors");
            Assert.That(Services.Get<Town.MerchantService>().Merchant != null, Is.EqualTo(merchant), "the merchant");
            Assert.That(Services.Get<Adventurers.CampService>().Camps.Count, Is.EqualTo(camps), "camps");
            Assert.That(Services.Get<Town.WaveManager>().RaidCount, Is.EqualTo(raidCount), "raids");
            Assert.That(Services.Get<Disasters.DisasterService>().Active.Count, Is.EqualTo(active), "disasters");

            yield return new WaitForSecondsRealtime(1f);
            AssertSameSections(a, Saves.Capture());
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
