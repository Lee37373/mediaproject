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
            // Keyboard input must reach the game even when the editor window is not focused (tests run
            // from outside): by default the Input System disables the keyboard / routes it to the editor.
            var settings = InputSystem.settings;
            _backgroundBehavior = settings.backgroundBehavior;
            _editorInputBehavior = settings.editorInputBehaviorInPlayMode;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            yield return null;
        }

        InputSettings.BackgroundBehavior _backgroundBehavior;
        InputSettings.EditorInputBehaviorInPlayMode _editorInputBehavior;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            GameSettings.ShowTitle = true;
            MenuState.Reset();
            InputSystem.settings.backgroundBehavior = _backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = _editorInputBehavior;
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
        }

        // ---------------- Helpers ----------------

        /// <summary>Starts a session like Main.unity does (minus the LLM object).</summary>
        /// <summary>Same world every run, so a failure can be reproduced (random seeds made some tests flaky).</summary>
        const int TestSeed = 20260920;

        static IEnumerator Boot(bool showTitle = false)
        {
            GameSettings.ShowTitle = showTitle;
            GameSettings.PendingSeed = TestSeed;
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
            if (!kb.enabled) InputSystem.EnableDevice(kb); // disabled while the editor was unfocused
            // A queued key can be dropped when the editor window changes focus (device resync):
            // queue it again until the input system really sees the key down.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Escape));
                yield return null;
                if (kb.escapeKey.isPressed) break;
            }
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
            string state = $"pauseViews={UnityEngine.Object.FindObjectsByType<PauseMenuView>(FindObjectsSortMode.None).Length} "
                           + $"open={pause.IsOpen} held={Clock.IsHeld} menu={MenuState.IsOpen} "
                           + $"selected={Services.Get<InspectorView>().Selected} esc={Keyboard.current?.escapeKey.isPressed}";
            Assert.That(pause.IsOpen && Clock.IsHeld, Is.True, "ESC with nothing open pauses — " + state);
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
            // Next to the member, but never inside a building wall.
            var grid = Services.Get<World.WorldService>().Grid;
            if (grid.TryFindNearestWalkable(World.GridMap.WorldToCell(at), 3, out var cell)) at = World.GridMap.CellToWorld(cell);
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
        public IEnumerator NoviceArcher_ShootsWithoutKiting()
        {
            yield return Boot();
            Unit.GuildInvulnerable = true; // damage only: the archer must still pick the guild master
            try
            {
                var master = Services.Get<PlayerAvatar>();
                Monster archer = null;
                for (int i = 0; i < 40 && archer == null; i++)
                {
                    DebugCommands.SpawnNearMaster("goblin", 1, 1.5f);
                    archer = UnitRegistry.All.OfType<Monster>().FirstOrDefault(m => m.Home == null && m.IsAlive && m.IsRangedVariant);
                }
                Assert.That(archer, Is.Not.Null, "a goblin archer spawned");
                Assert.That(archer.Tier, Is.EqualTo(CombatTier.Novice));
                archer.ForceTarget(master);
                bool kited = false;
                float end = Time.realtimeSinceStartup + 4f;
                while (Time.realtimeSinceStartup < end)
                {
                    kited |= archer.Engage.IsKiting;
                    yield return null;
                }
                Assert.That(kited, Is.False, "a novice archer next to a melee unit keeps shooting instead of backing off");
            }
            finally { Unit.GuildInvulnerable = false; }
        }

        [UnityTest]
        public IEnumerator Party_TravelsTogether_AndItsSkilledLeaderCallsATarget()
        {
            yield return Boot();
            Unit.GuildInvulnerable = true;
            try
            {
                var members = Roster.Members.Take(3).ToList();
                Assume.That(members.Count, Is.EqualTo(3), "three starting members");
                foreach (var m in members) m.Data.grade = AdventurerGrade.Rare; // skilled: leader calls, members follow
                var goblin = Services.Get<EcosystemManager>().Species.First(s => s.id == "goblin");
                MakeKnown(goblin);
                var center = Services.Get<World.WorldService>().TownCenterWorld;
                string id = DebugCommands.PostHunt("goblin", 30, 60, 20, 3, center.x + 20f, center.y);
                var quest = Services.Get<QuestBoard>().FindById(id);
                Assert.That(quest, Is.Not.Null, "hunt posted: " + id);
                DebugCommands.ForceAccept(id, 3);
                Assert.That(quest.Assignees.Count, Is.EqualTo(3), "a party of three");
                var leader = (Adventurer)quest.Assignees[0];

                Clock.SetSpeed(2);
                yield return WaitFor(() => leader.StateLabel == "사냥 중", 25f);
                Assert.That(leader.StateLabel, Is.EqualTo("사냥 중"), "the party reached the hunting area");
                // Someone may have stopped for loot or a scuffle on the way: the formation gathers them in.
                yield return WaitFor(() => members.All(m => Vector2.Distance(m.Position, leader.Position) < 4f), 6f);
                foreach (var m in members)
                    Assert.That(Vector2.Distance(m.Position, leader.Position), Is.LessThan(4f), $"{m.Data.name} is with the leader");

                Unit called = null;
                Action<PartyTargetCalledEvent> onCall = e => called = e.Target;
                EventBus.Subscribe(onCall);
                var pool = Services.Get<PoolService>();
                var world = Services.Get<World.WorldService>();
                for (int i = 0; i < 3; i++)
                {
                    var p = leader.Position + new Vector2(5f, i - 1f);
                    if (!world.Grid.TryFindNearestWalkable(World.GridMap.WorldToCell(p), 3, out var c)) continue;
                    pool.Spawn<Monster>(Monster.PoolKey, World.GridMap.CellToWorld(c)).Setup(goblin, null);
                }
                yield return WaitFor(() => called != null, 15f);
                EventBus.Unsubscribe(onCall);
                Assert.That(called, Is.InstanceOf<Monster>(), "the skilled leader called a target for focus fire");
            }
            finally { Unit.GuildInvulnerable = false; }
        }

        /// <summary>Moves the guild master to open ground outside town (8 walkable cells every way).</summary>
        static PlayerAvatar MasterOnOpenGround()
        {
            var world = Services.Get<World.WorldService>();
            var grid = world.Grid;
            var c0 = World.GridMap.WorldToCell(world.TownCenterWorld);
            for (int r = 16; r < 50; r++)
            for (int k = 0; k < 16; k++)
            {
                float a = k * Mathf.PI / 8f;
                var c = c0 + new Vector2Int(Mathf.RoundToInt(Mathf.Cos(a) * r), Mathf.RoundToInt(Mathf.Sin(a) * r));
                if (grid.IsInTown(c)) continue;
                bool open = true;
                for (int dy = -8; dy <= 8 && open; dy++)
                for (int dx = -8; dx <= 8 && open; dx++)
                    if (!grid.IsWalkable(c + new Vector2Int(dx, dy))) open = false;
                if (!open) continue;
                var master = Services.Get<PlayerAvatar>();
                master.Mover.Stop();
                master.transform.position = World.GridMap.CellToWorld(c);
                return master;
            }
            return null;
        }

        static Monster SpawnMonster(string speciesId, Vector2 at, bool meleeOnly = false)
        {
            var def = Services.Get<EcosystemManager>().Species.First(s => s.id == speciesId);
            var pool = Services.Get<PoolService>();
            if (Services.Get<World.WorldService>().Grid.TryFindNearestWalkable(World.GridMap.WorldToCell(at), 4, out var cell))
                at = World.GridMap.CellToWorld(cell);
            for (int i = 0; i < 40; i++)
            {
                var m = pool.Spawn<Monster>(Monster.PoolKey, at);
                m.Setup(def, null);
                if (!meleeOnly || !m.IsRangedVariant) return m;
                m.Perish();
            }
            return null;
        }

        /// <summary>Largest angular gap (degrees) between units around a centre: ~90 when four surround it.</summary>
        static float LargestGap(Vector2 centre, System.Collections.Generic.IEnumerable<Unit> units)
        {
            var angles = units.Select(u => Mathf.Atan2(u.Position.y - centre.y, u.Position.x - centre.x) * Mathf.Rad2Deg).OrderBy(a => a).ToList();
            float gap = 0f;
            for (int i = 0; i < angles.Count; i++) gap = Mathf.Max(gap, (i + 1 < angles.Count ? angles[i + 1] : angles[0] + 360f) - angles[i]);
            return gap;
        }

        [UnityTest]
        public IEnumerator SkilledPack_Encircles_NovicePackDoesNot()
        {
            yield return Boot();
            Unit.GuildInvulnerable = true;
            try
            {
                var master = MasterOnOpenGround();
                Assume.That(master, Is.Not.Null, "open ground on this map");
                Clock.SetSpeed(2);
                // Four melee monsters charge the guild master from the same spot to the east.
                System.Collections.Generic.List<Monster> ChargeFromTheEast(string species)
                {
                    var pack = new System.Collections.Generic.List<Monster>();
                    for (int i = 0; i < 4; i++)
                    {
                        var m = SpawnMonster(species, master.Position + new Vector2(6f, (i - 1.5f) * 0.4f), meleeOnly: true);
                        m.ForceTarget(master);
                        pack.Add(m);
                    }
                    return pack;
                }

                var warriors = ChargeFromTheEast("goblin_warrior");
                yield return new WaitForSeconds(4f); // scaled: 8 s at x2
                float skilledGap = LargestGap(master.Position, warriors);
                foreach (var m in warriors) m.Perish();

                var goblins = ChargeFromTheEast("goblin");
                yield return new WaitForSeconds(4f);
                float noviceGap = LargestGap(master.Position, goblins);

                Assert.That(skilledGap, Is.LessThan(200f), "skilled goblin warriors spread around the target");
                Assert.That(noviceGap, Is.GreaterThan(250f), "novice goblins all come at it from one side");
            }
            finally { Unit.GuildInvulnerable = false; }
        }

        [UnityTest]
        public IEnumerator SkilledMonster_Outnumbered_FallsBackAndReturnsWithItsPack()
        {
            yield return Boot();
            Unit.GuildInvulnerable = true;
            try
            {
                var master = MasterOnOpenGround();
                Assume.That(master, Is.Not.Null, "open ground on this map");
                var lone = SpawnMonster("goblin_warrior", master.Position + new Vector2(1.5f, 0f));
                var mates = new[]
                {
                    SpawnMonster("goblin_warrior", master.Position + new Vector2(0f, 12f)),
                    SpawnMonster("goblin_warrior", master.Position + new Vector2(1f, 12.5f)),
                };
                typeof(Unit).GetProperty(nameof(Unit.Hp)).SetValue(lone, lone.Stats.MaxHp * 0.3f);
                lone.ForceTarget(master);
                bool regrouped = false;
                yield return WaitFor(() => (regrouped |= lone.StateLabel == "재집결") && mates.Any(m => m.CurrentTarget == master), 15f);
                Assert.That(regrouped, Is.True, "the hurt warrior fell back");
                Assert.That(mates.Any(m => m.CurrentTarget == master), Is.True, "a pack mate came back with it for the enemy");
            }
            finally { Unit.GuildInvulnerable = false; }
        }

        [UnityTest]
        public IEnumerator Priest_HealsTheMostHurtAlly()
        {
            yield return Boot();
            var data = AdventurerGenerator.Generate(GameRandom.Shared);
            data.cls = AdventurerClass.Priest;
            var patient = Roster.Members.First(a => a.IsAlive);
            var other = Roster.Members.First(a => a.IsAlive && a != patient);
            var priest = Roster.Recruit(data, patient.Position + new Vector2(1f, 0f));
            // Out of the mass heal's reach (4) but within a single heal's (6): the priest must choose.
            other.transform.position = (Vector3)(patient.Position + new Vector2(0f, 5f));
            yield return WaitFor(() => priest.Skills.All(s => s.IsReady), 3f); // skills start staggered
            patient.ReceiveDamage(patient.Hp - patient.Stats.MaxHp * 0.2f, null, false);  // 20%
            other.ReceiveDamage(other.Hp - other.Stats.MaxHp * 0.5f, null, false);        // 50%, farther from death
            float before = patient.Hp, otherBefore = other.Hp;
            Unit healed = null;
            Action<UnitHealedEvent> onHeal = e => healed ??= e.Target;
            EventBus.Subscribe(onHeal);
            bool used = priest.TryUseSupportSkills();
            EventBus.Unsubscribe(onHeal);
            Assert.That(used, Is.True, "the priest used a heal");
            Assert.That(patient.Hp, Is.GreaterThan(before), "the one closest to death got healed");
            Assert.That(healed, Is.EqualTo(patient), "and it was healed first");
            Assert.That(other.Hp >= otherBefore, Is.True);
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
