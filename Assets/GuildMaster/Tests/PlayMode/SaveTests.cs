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

            // A few frames later (systems that set up in Start), still the same. The fog is left out
            // until the units come back too (P5-4b/c): until then they see from where they booted.
            yield return null;
            yield return null;
            AssertSameSections(a, Saves.Capture(), "fog");
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
