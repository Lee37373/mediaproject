using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Items;
using GuildMaster.Monsters;
using GuildMaster.Player;
using GuildMaster.Quests;
using GuildMaster.Session;
using GuildMaster.UI;
using GuildMaster.Units;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Play mode smoke tests: boot the real game (GameBootstrap + UIRoot) in an empty scene — no LLM
    /// object, so decisions run on the rule-based fallback — and check the main flows end to end.
    /// Any error log or exception fails the test (Unity's LogAssert default). Each test gets its own
    /// scene and world.
    /// </summary>
    public class SmokeTests
    {
        static int s_sceneCounter;
        Scene _scene;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _scene = SceneManager.CreateScene($"Smoke{++s_sceneCounter}");
            SceneManager.SetActiveScene(_scene);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            GameSettings.ShowTitle = true;
            MenuState.Reset();
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
        }

        // ---------------- Helpers ----------------

        /// <summary>Starts a session like Main.unity does (minus the LLM object).</summary>
        static IEnumerator Boot(bool showTitle = false)
        {
            GameSettings.ShowTitle = showTitle;
            new GameObject("GameBootstrap").AddComponent<GameBootstrap>();
            new GameObject("UI").AddComponent<UIRoot>();
            yield return null;
            yield return null;
        }

        /// <summary>Waits (real time) until the condition holds or the timeout passes.</summary>
        static IEnumerator WaitFor(Func<bool> condition, float realSeconds)
        {
            float end = Time.realtimeSinceStartup + realSeconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }

        static IEnumerator PressEscape()
        {
            var kb = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Escape));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
        }

        /// <summary>Marks a species as seen by the guild (quest posting only allows known species).</summary>
        static void MakeKnown(MonsterDefinition species)
        {
            var field = typeof(GuildIntel).GetField("_known", BindingFlags.NonPublic | BindingFlags.Instance);
            ((System.Collections.Generic.HashSet<MonsterDefinition>)field.GetValue(Services.Get<GuildIntel>())).Add(species);
        }

        static GuildRoster Roster => Services.Get<GuildRoster>();
        static GameClock Clock => Services.Get<GameClock>();

        // ---------------- Tests ----------------

        [UnityTest]
        public IEnumerator Boot_RunsFourGameHoursWithoutErrors()
        {
            yield return Boot();
            Assert.That(Roster.Members.Count, Is.GreaterThan(0), "starting members");
            Assert.That(UnitRegistry.All.OfType<Monster>().Count(m => m.IsAlive), Is.GreaterThan(0), "monsters spawned");
            float start = Clock.TotalHours;
            Clock.SetSpeed(3);
            yield return WaitFor(() => Clock.TotalHours >= start + 4f, 30f);
            Assert.That(Clock.TotalHours, Is.GreaterThanOrEqualTo(start + 4f), "the game kept running");
        }

        [UnityTest]
        public IEnumerator Title_HoldsTheClock_UntilNewGame()
        {
            yield return Boot(showTitle: true);
            var title = UnityEngine.Object.FindFirstObjectByType<TitleView>();
            Assert.That(title.IsOpen && Clock.IsHeld && MenuState.IsOpen, Is.True, "title shown, game paused");
            Assert.That(Time.timeScale, Is.EqualTo(0f));

            typeof(TitleView).GetMethod("NewGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(title, null);
            yield return null;
            Assert.That(title.IsOpen || Clock.IsHeld || MenuState.IsOpen, Is.False, "new game resumes");
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator Escape_OpensThePauseMenu_AndClosesIt()
        {
            yield return Boot();
            var pause = UnityEngine.Object.FindFirstObjectByType<PauseMenuView>();
            yield return PressEscape();
            Assert.That(pause.IsOpen && Clock.IsHeld, Is.True, "ESC with nothing open pauses");
            yield return PressEscape();
            Assert.That(pause.IsOpen || Clock.IsHeld, Is.False, "ESC again resumes");
        }

        [UnityTest]
        public IEnumerator Monster_WearsGearDroppedNearItsNest()
        {
            yield return Boot();
            var nest = Services.Get<EcosystemManager>().Nests
                .First(n => n.Species.pickupMode == MonsterPickupMode.Equip && n.Population >= 2);
            var at = nest.WorldPosition + new Vector2(1f, 0f);
            DebugCommands.DropItem("gear_t2", 1, at.x, at.y);
            Clock.SetSpeed(3);
            yield return WaitFor(() => nest.Members.Any(m => m.Equipment.Worn.Any(i => i.id == "gear_t2")), 20f);
            var wearer = nest.Members.FirstOrDefault(m => m.Equipment.Worn.Any(i => i.id == "gear_t2"));
            Assert.That(wearer, Is.Not.Null, $"a resident of {nest.DisplayName} wears the dropped gear");
            Assert.That(wearer.Stats.Attack, Is.GreaterThan(wearer.Def.attack), "gear raises attack");
        }

        [UnityTest]
        public IEnumerator Adventurer_PicksUpAnItemDroppedInTown()
        {
            yield return Boot();
            Unit taker = null;
            Action<GroundItemTakenEvent> onTaken = e => taker = e.Taker;
            EventBus.Subscribe(onTaken);
            var member = Roster.Members.First(a => a.IsAlive);
            var at = member.Position + new Vector2(1.5f, 0f);
            DebugCommands.DropItem("slime_jelly", 1, at.x, at.y);
            yield return WaitFor(() => taker != null, 15f);
            EventBus.Unsubscribe(onTaken);
            Assert.That(taker, Is.InstanceOf<Adventurer>(), "a guild member picked it up");
            Assert.That(Services.Get<GroundItemService>().Items.Count, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator HuntQuest_SendsTheAdventurerToTheMarkedArea()
        {
            yield return Boot();
            var eco = Services.Get<EcosystemManager>();
            var species = eco.Species.First(s => s.id == "slime");
            MakeKnown(species);
            string id = DebugCommands.PostHunt("slime", 2, 30, 10);
            var quest = Services.Get<QuestBoard>().FindById(id);
            Assert.That(quest, Is.Not.Null, "hunt posted: " + id);
            DebugCommands.ForceAccept(id, 1);
            var hunter = Roster.Members.First(a => a.CurrentQuest == quest);
            float before = Vector2.Distance(hunter.Position, quest.Point);
            Clock.SetSpeed(2);
            yield return WaitFor(() => hunter == null || !hunter.IsAlive || Vector2.Distance(hunter.Position, quest.Point) < before - 3f
                                       || quest.Status != QuestStatus.InProgress, 20f);
            Assert.That(quest.Status != QuestStatus.InProgress || Vector2.Distance(hunter.Position, quest.Point) < before - 3f, Is.True,
                "the hunter heads for the marked point (or already finished)");
        }

        [UnityTest]
        public IEnumerator TownGuard_AttacksAMonsterInsideTheWalls()
        {
            yield return Boot();
            Unit.GuildInvulnerable = true; // keep the guild master alive; guards must still fight
            try
            {
                DebugCommands.SpawnNearMaster("goblin", 1, 3f);
                var goblin = UnitRegistry.All.OfType<Monster>().First(m => m.Home == null && m.IsAlive);
                yield return WaitFor(() => !goblin.IsAlive || Roster.Members.Any(a => a.StateLabel == "마을 경비"), 15f);
                Assert.That(!goblin.IsAlive || Roster.Members.Any(a => a.StateLabel == "마을 경비"), Is.True,
                    "members in town react to the intruder");
            }
            finally { Unit.GuildInvulnerable = false; }
        }

        // ---------------- Regressions ----------------

        [UnityTest]
        public IEnumerator PackHowl_NeedsAPackMate_AndHastesOnlyWhenItCalls()
        {
            yield return Boot();
            Unit.GuildInvulnerable = true;
            try
            {
                var master = Services.Get<PlayerAvatar>();
                DebugCommands.SpawnNearMaster("skull", 1, 2f, forceTarget: false);
                var first = UnitRegistry.All.OfType<Monster>().First(m => m.Home == null && m.IsAlive);
                Assert.That(new PackHowlSkill().TryUse(first, master), Is.False, "a lone skeleton has nobody to call");
                yield return null;
                Assert.That(first.Mover.BuffMultiplier, Is.EqualTo(1f), "and no free haste");

                DebugCommands.SpawnNearMaster("skull", 1, 2f, forceTarget: false);
                Assert.That(new PackHowlSkill().TryUse(first, master), Is.True, "a pack mate answers");
                yield return null;
                Assert.That(first.Mover.BuffMultiplier, Is.GreaterThan(1f));
            }
            finally { Unit.GuildInvulnerable = false; }
        }

        [UnityTest]
        public IEnumerator FallenMember_StaysDead_AfterAPosthumousLevelUp()
        {
            yield return Boot();
            var member = Roster.Members.First(a => a.IsAlive);
            member.ReceiveDamage(member.Hp + 999f, null, false);
            Assert.That(member.IsAlive, Is.False);
            // A projectile fired before death can still land the killing blow and its EXP.
            member.GainExp(member.Data.ExpToNextLevel * 3);
            member.RecalculateStats();
            Assert.That(member.IsAlive, Is.False, "never revived");
            Assert.That(member.Hp, Is.EqualTo(0f));
        }

        [UnityTest]
        public IEnumerator WalkingHome_BadlyHurt_TurnsIntoARetreat()
        {
            yield return Boot();
            var member = Roster.Members.First(a => a.IsAlive);
            bool retreated = false;
            Action<AdventurerRetreatedEvent> onRetreat = e => { if (e.Adventurer == member) retreated = true; };
            EventBus.Subscribe(onRetreat);
            member.Data.potions = 0;
            member.ChangeState(member.ReturnState);
            Assert.That(member.ReturnState.Retreating, Is.False, "a normal walk home");
            member.ReceiveDamage(member.Hp - member.Stats.MaxHp * 0.1f, null, false);
            yield return null;
            yield return null;
            EventBus.Unsubscribe(onRetreat);
            Assert.That(retreated, Is.True, "fighting on the way home, it retreats instead of fighting to the death");
        }
    }
}
