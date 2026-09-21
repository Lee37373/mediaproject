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
        public IEnumerator RecruitNotice_CostsGold_AndBringsAnApplicantWithinHours()
        {
            // P4-38: the guild starts with one full party, and a paid notice brings the next recruit.
            yield return Boot();
            Assert.That(Roster.Members.Select(m => m.Data.cls).Distinct().Count(), Is.EqualTo(AdventurerGenerator.StartingLineup.Length),
                        "the founders are one of each class");

            var visitors = Services.Get<Recruitment.VisitorService>();
            var treasury = Services.Get<Economy.GuildTreasury>();
            int gold = treasury.Gold, before = visitors.Visitors.Count;
            Assert.That(visitors.CannotPostNoticeReason(), Is.Null);
            Assert.That(visitors.PostNotice(), Is.True);
            Assert.That(treasury.Gold, Is.EqualTo(gold - Recruitment.VisitorService.NoticeCost));
            Assert.That(visitors.PostNotice(), Is.False, "one notice at a time");

            float start = Clock.TotalHours;
            Clock.SetSpeed(3);
            yield return WaitFor(() => !visitors.NoticePosted, 30f);
            Assert.That(visitors.NoticePosted, Is.False, "the applicant turned up");
            Assert.That(Clock.TotalHours - start, Is.LessThanOrEqualTo(Recruitment.VisitorService.NoticeMaxHours + 0.5f));
            Assert.That(visitors.Visitors.Count, Is.GreaterThan(before));
        }

        [UnityTest]
        public IEnumerator Gate_ShutsAChaserOut_ItBreaksTheGate_AndRepairCostsGold()
        {
            // P4-39, user request 2026-09-21: the guild comes and goes, monsters must break a gate.
            yield return Boot();
            var gates = Services.Get<Town.GateService>();
            var grid = Services.Get<World.WorldService>().Grid;
            var gate = gates.Gates[0];
            Assert.That(gates.Gates.Count, Is.EqualTo(4));
            Assert.That(grid.TownSealed, Is.True, "every gate stands at the start");

            var member = Roster.Members.First(a => a.IsAlive);
            member.Mover.Stop();
            member.transform.position = gate.InsidePoint - (Vector2)gate.Outward * 2f;
            ClearHomelessOf("goblin");
            var goblin = SpawnMonster("goblin", gate.OutsidePoint + (Vector2)gate.Outward * 2f, meleeOnly: true);
            goblin.ForceTarget(member);

            bool slippedIn = false;
            float until = Time.time + 6f;
            while (Time.time < until && gate.Hp >= gate.MaxHp)
            {
                if (grid.IsInTown(goblin.Position) && !grid.IsClosedGate(World.GridMap.WorldToCell(goblin.Position))) slippedIn = true;
                yield return null;
            }
            Assert.That(slippedIn, Is.False, "the goblin never got past the standing gate");
            Assert.That(gate.Hp, Is.LessThan(gate.MaxHp), "it went for the gate instead");
            Assert.That(goblin.StateLabel, Is.EqualTo("성문 공격"));

            // Near its end: the goblin's next blows break it and the way in opens.
            gate.Damage(gate.Hp - 1f);
            yield return WaitFor(() => gate.IsBroken, 5f);
            Assert.That(gate.IsBroken, Is.True);
            Assert.That(grid.TownSealed, Is.False);
            Assert.That(grid.IsWalkable(gate.CenterCell, passesGates: false), Is.True, "a broken gate lets monsters through");

            // Repairs wait until the attacker is gone, then cost gold.
            Assert.That(gates.CannotRepairReason(gate), Is.Not.Null, "not with a goblin in the gateway");
            goblin.Perish();
            yield return null;
            var treasury = Services.Get<Economy.GuildTreasury>();
            int gold = treasury.Gold, cost = gates.RepairCost(gate);
            Assert.That(cost, Is.GreaterThan(0));
            Assert.That(gates.TryRepair(gate), Is.True, gates.CannotRepairReason(gate));
            Assert.That(treasury.Gold, Is.EqualTo(gold - cost));
            Assert.That(gate.IsBroken, Is.False);
            Assert.That(grid.TownSealed, Is.True);
            Assert.That(grid.IsWalkable(gate.CenterCell, passesGates: false), Is.False);
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

        /// <summary>
        /// P4-19: hiding cuts the chase — the pursuer loses its target and cannot pick the hidden
        /// unit again, which is what leaves it standing in someone else's territory. Striking
        /// someone gives the position away again.
        /// </summary>
        [UnityTest]
        public IEnumerator Stealth_DropsThePursuit_AndAttackingGivesItAway()
        {
            yield return Boot();
            var member = Roster.Members.First(a => a.IsAlive);
            member.Data.skillIds.Clear();
            member.Data.skillIds.Add("stealth");
            SkillFactory.Assign(member, member.Data.skillIds);

            DebugCommands.SpawnNearMaster("goblin", 1, 2f);
            var goblin = UnitRegistry.All.OfType<Monster>().First(m => m.IsAlive && m.Home == null);
            goblin.transform.position = member.Position + new Vector2(1.5f, 0f);
            goblin.ForceTarget(member);
            yield return null;
            Assert.That(goblin.CurrentTarget, Is.EqualTo((Unit)member), "the goblin is chasing");
            Assert.That(member.IsTargetableBy(goblin), Is.True);

            Assert.That(member.TryUseSkill<StealthSkill>(goblin), Is.True, "the skill is ready");
            Assert.That(member.IsHidden, Is.True);
            Assert.That(goblin.CurrentTarget, Is.Not.EqualTo((Unit)member), "it lost the thread");
            Assert.That(member.IsTargetableBy(goblin), Is.False, "and cannot pick it up again");
            Assert.That(member.Visual.Fade, Is.LessThan(1f), "drawn faded while hidden");
            // Allies still see it.
            var ally = Roster.Members.FirstOrDefault(a => a.IsAlive && a != member);
            if (ally != null) Assert.That(member.IsTargetableBy(ally), Is.True, "its own side still sees it");

            // Striking someone shows where it is.
            Combat.ApplyHit(member, goblin);
            Assert.That(member.IsHidden, Is.False, "attacking gives the hiding place away");
            Assert.That(member.IsTargetableBy(goblin), Is.True);
            Assert.That(member.Visual.Fade, Is.EqualTo(1f));
        }

        /// <summary>
        /// P4-33: loot is planned as one round and walked in a single pass, and a monster in sight
        /// ends the round — the adventurer used to set off home and turn back for each drop.
        /// </summary>
        [UnityTest]
        public IEnumerator Looting_PlansOneRound_AndAFightEndsIt()
        {
            yield return Boot();
            var ground = Services.Get<GroundItemService>();
            var grid = Services.Get<World.WorldService>().Grid;
            var member = Roster.Members.First(a => a.IsAlive);
            int before = member.Data.LootCount;

            // Three drops around the member, all on ground it can stand on.
            var spots = new[] { new Vector2(1.5f, 0f), new Vector2(3f, 1f), new Vector2(4.5f, 0f) };
            foreach (var offset in spots)
            {
                var at = member.Position + offset;
                if (grid.TryFindNearestWalkable(World.GridMap.WorldToCell(at), 3, out var cell)) at = World.GridMap.CellToWorld(cell);
                DebugCommands.DropItem("slime_jelly", 1, at.x, at.y);
            }
            Assert.That(ground.Items.Count, Is.EqualTo(3));

            // The round is planned in one go, so more than one stop is queued at once.
            int longestRound = 0;
            float end = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < end && ground.Items.Count > 0)
            {
                longestRound = Mathf.Max(longestRound, member.Looting.RouteLength);
                yield return null;
            }
            Assert.That(longestRound, Is.GreaterThan(1), "several stops were queued as one round");
            Assert.That(member.Data.LootCount, Is.EqualTo(before + 3), "and all of it came back in that pass");

            // With a monster in sight the round is dropped rather than walked.
            var at2 = member.Position + new Vector2(2f, 0f);
            DebugCommands.DropItem("slime_jelly", 1, at2.x, at2.y);
            DebugCommands.SpawnNearMaster("goblin", 1, 2f);
            var goblin = UnitRegistry.All.OfType<Monster>().First(m => m.IsAlive && m.Home == null);
            goblin.transform.position = member.Position + new Vector2(0f, 1.5f);
            yield return WaitFor(() => !member.Looting.IsBusy, 3f);
            Assert.That(member.Looting.IsBusy, Is.False, "fighting comes before tidying up");
        }

        /// <summary>
        /// P4-32: the clear-nest quest is gone. A hunt finishes on its kill count, or as soon as
        /// the nest inside its marked area is wiped out, whichever comes first.
        /// </summary>
        [UnityTest]
        public IEnumerator Hunt_AlsoFinishesWhenTheAreaNestIsWipedOut()
        {
            yield return Boot();
            var board = Services.Get<QuestBoard>();
            var eco = Services.Get<EcosystemManager>();
            var nest = eco.Nests.First(n => n.Population >= 2);
            MakeKnown(nest.Species);
            // Post far more kills than the nest holds, so only clearing it can finish the quest.
            string id = DebugCommands.PostHunt(nest.Species.id, 99, 40, 10, 1, nest.WorldPosition.x, nest.WorldPosition.y);
            var quest = board.FindById(id);
            Assert.That(quest, Is.Not.Null, id);
            Assert.That(quest.Species, Is.EqualTo(nest.Species));

            var taker = Roster.Members.First(a => a.IsAlive && a.CurrentQuest == null);
            Assert.That(board.TryAccept(quest, taker), Is.True);
            taker.CurrentQuest = quest;
            Assert.That(quest.Status, Is.EqualTo(QuestStatus.InProgress));

            foreach (var m in nest.Members.ToList()) if (m.IsAlive) m.Perish();
            // Completed, or already collected by the time we look.
            yield return WaitFor(() => quest.Status is QuestStatus.Completed or QuestStatus.TurnedIn, 5f);
            Assert.That(quest.Status is QuestStatus.Completed or QuestStatus.TurnedIn, Is.True, $"an emptied nest finishes the hunt (was {quest.Status})");
            Assert.That(quest.Progress, Is.LessThan(quest.RequiredCount), "with kills to spare");
        }

        /// <summary>
        /// 2026-09-20 user reports: a quest is worked by one group (a lone adventurer counts as
        /// one), and taking a quest off the board after someone accepted it still pays them.
        /// </summary>
        [UnityTest]
        public IEnumerator Quest_IsClaimedByOneGroup_AndCancellingStillPaysTheTaker()
        {
            yield return Boot();
            var board = Services.Get<QuestBoard>();
            var treasury = Services.Get<Economy.GuildTreasury>();
            MakeKnown(Services.Get<EcosystemManager>().Species.First(s2 => s2.id == "slime"));

            // A one-person quest: nobody can be invited, so the claim rule is what is measured.
            var solo = board.FindById(DebugCommands.PostHunt("slime", 2, 40, 10, party: 1));
            Assert.That(solo, Is.Not.Null);
            var free = Roster.Members.Where(a => a.IsAlive && a.CurrentQuest == null).Take(2).ToList();
            Assert.That(free.Count, Is.EqualTo(2), "two idle members to try with");

            Assert.That(board.AvailableFor(free[1]).Contains(solo), Is.True, "unclaimed: anyone may take it");
            Assert.That(board.TryAccept(solo, free[0]), Is.True, "the first taker claims it");
            free[0].CurrentQuest = solo;
            Assert.That(board.AvailableFor(free[1]).Contains(solo), Is.False, "claimed quests leave the board");
            Assert.That(board.TryAccept(solo, free[1]), Is.False, "and cannot be joined uninvited");
            Assert.That(solo.Assignees.Count, Is.EqualTo(1));

            // Cancelling now must not claw the reward back.
            int goldBefore = treasury.Gold;
            Assert.That(board.Cancel(solo), Is.True);
            Assert.That(solo.Status, Is.EqualTo(QuestStatus.Completed), "paid out, not cancelled");
            Assert.That(treasury.Gold, Is.EqualTo(goldBefore), "the escrow does not come back to the guild");
            Assert.That(solo.PendingPayouts, Is.EqualTo(1), "the taker has a reward waiting");

            // Invitations still fill a party: accepting a three-person quest pulls companions in.
            var group = board.FindById(DebugCommands.PostHunt("slime", 2, 60, 20, party: 3));
            var leader = Roster.Members.First(a => a.IsAlive && a.CurrentQuest == null);
            Assert.That(board.TryAccept(group, leader), Is.True);
            Assert.That(group.Assignees.Count, Is.GreaterThan(1), "the leader brought companions along");
        }

        /// <summary>
        /// P4-13: a watchtower costs gold and materials, lights up ground the town watch cannot
        /// reach on its own, and gives half of that back when it is taken down again.
        /// </summary>
        [UnityTest]
        public IEnumerator Watchtower_IsBuiltForMaterials_SeesFurther_AndRefundsOnDemolish()
        {
            yield return Boot();
            var construction = Services.Get<Town.ConstructionService>();
            var treasury = Services.Get<Economy.GuildTreasury>();
            var world = Services.Get<World.WorldService>();
            var fog = Services.Get<Fog.FogOfWar>();
            var cost = construction.CostOf(Town.BuildingType.Watchtower);

            // Materials gate the build before gold does.
            Assert.That(construction.CannotAffordReason(Town.BuildingType.Watchtower), Is.Not.Null, "no materials yet");
            treasury.AddGold(cost.goldCost);
            foreach (var c in cost.itemCosts) treasury.AddStock(c.itemId, c.count);
            Assert.That(construction.CannotAffordReason(Town.BuildingType.Watchtower), Is.Null, "stocked up");

            var center = world.TownCenterWorld;
            // Two cells of a gate are kept clear so a tower can never plug the way in.
            Assert.That(construction.CannotPlaceAt(Town.BuildingType.Watchtower, center + new Vector2(12f, 0f)), Is.Not.Null, "on the gate");

            var spot = center + new Vector2(15f, 0f);
            var far = center + new Vector2(24f, 0f);
            Assert.That(fog.IsVisibleRaw(far), Is.False, "the town watch alone does not reach out there");

            int goldBefore = treasury.Gold;
            Assert.That(construction.TryBuild(Town.BuildingType.Watchtower, spot, out var error), Is.True, error);
            Assert.That(treasury.Gold, Is.EqualTo(goldBefore - cost.goldCost), "gold paid");
            foreach (var c in cost.itemCosts) Assert.That(treasury.GetStock(c.itemId), Is.EqualTo(0), "materials used up");

            yield return WaitFor(() => fog.IsVisibleRaw(far), 5f);
            Assert.That(fog.IsVisibleRaw(far), Is.True, "the tower watches the road out of town");

            var tower = Services.Get<Town.TownService>().Get(Town.BuildingType.Watchtower);
            Assert.That(tower, Is.Not.Null);
            goldBefore = treasury.Gold;
            Assert.That(construction.TryDemolish(tower, out error), Is.True, error);
            Assert.That(treasury.Gold, Is.EqualTo(goldBefore + Town.ConstructionRules.Refund(cost.goldCost)), "half the gold back");
            foreach (var c in cost.itemCosts)
                Assert.That(treasury.GetStock(c.itemId), Is.EqualTo(Town.ConstructionRules.Refund(c.count)), "half the materials back");

            yield return WaitFor(() => !fog.IsVisibleRaw(far), 5f);
            Assert.That(fog.IsVisibleRaw(far), Is.False, "and it stops watching once it is gone");
            Assert.That(world.Grid.IsWalkable(World.GridMap.WorldToCell(spot)), Is.True, "its ground is free again");
        }

        /// <summary>
        /// P4-23: the fog leaves a frozen grey copy of what the guild last saw, it does not follow
        /// the real monster, and seeing the spot again settles the question and removes it.
        /// </summary>
        [UnityTest]
        public IEnumerator FogGhost_RemembersWhereAMonsterWasLastSeen()
        {
            yield return Boot();
            var avatar = Services.Get<PlayerAvatar>();
            var ghosts = Services.Get<Fog.FogGhostService>();
            var world = Services.Get<World.WorldService>();
            // Well outside town, so only the guild master lights the spot.
            var far = world.TownCenterWorld + new Vector2(30f, 0f);
            avatar.transform.position = far;
            yield return WaitFor(() => Services.Get<Fog.FogOfWar>().IsVisibleRaw(far), 5f);

            DebugCommands.SpawnNearMaster("goblin", 2, 2f);
            var seen = UnitRegistry.All.OfType<Monster>().Where(m => m.IsAlive && m.Home == null).ToList();
            Assert.That(seen, Is.Not.Empty, "monsters spawned next to the guild master");
            // The fog must actually judge them visible first: until then they were never seen, and
            // walking away would (correctly) leave nothing behind.
            yield return WaitFor(() => seen.All(m => m.GetComponent<Fog.FogHideable>() is { IsFogEvaluated: true, IsFogVisible: true }), 5f);
            Assert.That(seen.All(m => m.GetComponent<Fog.FogHideable>().IsFogVisible), Is.True, "the guild sees them before it walks away");
            Assert.That(ghosts.Ghosts.Count, Is.EqualTo(0), "nothing is remembered while it is in plain sight");

            // Walk away: the fog closes over them and keeps the last sighting.
            avatar.transform.position = world.TownCenterWorld;
            yield return WaitFor(() => ghosts.Ghosts.Count >= seen.Count, 5f);
            Assert.That(ghosts.Ghosts.Count, Is.GreaterThanOrEqualTo(seen.Count), "each monster left a ghost");
            var ghost = ghosts.Ghosts[0];
            var frozenAt = ghost.Position;

            // The real monsters keep moving; the memory does not.
            Clock.SetSpeed(3);
            yield return WaitFor(() => seen.Any(m => Vector2.Distance(m.Position, frozenAt) > 2f), 15f);
            Assert.That(ghost.Position, Is.EqualTo(frozenAt), "the ghost stays where it was seen");

            // Looking again removes the memory, whatever is actually there now.
            avatar.transform.position = frozenAt;
            yield return WaitFor(() => !ghosts.Ghosts.Contains(ghost), 5f);
            Assert.That(ghosts.Ghosts.Contains(ghost), Is.False, "seeing the spot again clears the memory");
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
                // Clear of any nest's territory too: residents would join the fight and these tests
                // are about the spawned units alone (P5-1 moved nests closer to town).
                if (!ClearOfTerritories(World.GridMap.CellToWorld(c), 24f)) continue;
                var master = Services.Get<PlayerAvatar>();
                master.Mover.Stop();
                master.transform.position = World.GridMap.CellToWorld(c);
                return master;
            }
            return null;
        }

        /// <summary>
        /// Clears loose monsters of a species that have no nest. Units spawned by these tests are
        /// homeless, and pack membership is "same species, same home" — so any other stray of that
        /// species counts as a pack mate and can be rallied to instead of the ones we placed.
        /// </summary>
        static void ClearHomelessOf(string speciesId)
        {
            foreach (var u in UnitRegistry.All.ToList())
                if (u is Monster m && m.IsAlive && m.Home == null && m.Def.id == speciesId) m.Perish();
        }

        /// <summary>No nest close enough for its residents to wander into the test.</summary>
        static bool ClearOfTerritories(Vector2 at, float margin)
        {
            foreach (var n in Services.Get<EcosystemManager>().Nests)
            {
                if (n.IsEmpty) continue;
                if (Vector2.Distance(n.WorldPosition, at) < n.TerritoryRadius + margin) return false;
            }
            return true;
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
                ClearHomelessOf("goblin_warrior");
                var lone = SpawnMonster("goblin_warrior", master.Position + new Vector2(1.5f, 0f));
                // Beyond the 6-cell "already together" radius, but inside the ground MasterOnOpenGround
                // guarantees walkable (+-8): at 12 the walk back could not be pathed and the warrior
                // dropped straight out of the fall-back, with a 20s cooldown before it would try again.
                var mates = new[]
                {
                    SpawnMonster("goblin_warrior", master.Position + new Vector2(0f, 7f)),
                    SpawnMonster("goblin_warrior", master.Position + new Vector2(1f, 7.5f)),
                };
                typeof(Unit).GetProperty(nameof(Unit.Hp)).SetValue(lone, lone.Stats.MaxHp * 0.3f);
                lone.ForceTarget(master);
                bool regrouped = false;
                yield return WaitFor(() => (regrouped |= lone.StateLabel == "재집결") && mates.Any(m => m.CurrentTarget == master), 15f);
                Assert.That(regrouped, Is.True, "the hurt warrior fell back");
                string diag = $"lone state={lone.StateLabel} hp={lone.HpRatio:P0} " +
                              $"distToMates={mates.Select(m => Vector2.Distance(lone.Position, m.Position)).Min():0.0} | " +
                              string.Join(" | ", mates.Select(m => $"mate state={m.StateLabel} hp={m.HpRatio:P0} target={m.CurrentTarget?.DisplayName ?? "-"} " +
                                                                  $"distToLone={Vector2.Distance(lone.Position, m.Position):0.0}"));
                Assert.That(mates.Any(m => m.CurrentTarget == master), Is.True, "a pack mate came back with it for the enemy. " + diag);
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
            // Stand them on open ground: heals need line of sight, and where members happen to idle
            // in town can put a building between them (this broke once when nests moved, P5-1).
            var master = MasterOnOpenGround();
            Assume.That(master, Is.Not.Null, "open ground on this map");
            patient.Mover.Stop();
            patient.transform.position = (Vector3)master.Position;
            var priest = Roster.Recruit(data, patient.Position + new Vector2(1f, 0f));
            // Out of the mass heal's reach (4) but within a single heal's (6): the priest must choose.
            other.Mover.Stop();
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

        [UnityTest]
        public IEnumerator LuredIntoAnotherPacksTerritory_TheIntruderFightsInsteadOfTurningBack()
        {
            // A monster the player drags into a nest is alone against the whole pack, so the rout
            // rule used to send it home within half a second and luring never started a brawl
            // (user report 2026-09-20).
            yield return Boot();
            var nest = Services.Get<EcosystemManager>().Nests.First(n => n.Population >= 2);
            var resident = nest.Members.First(m => m.IsAlive);
            var intruderSpecies = Services.Get<EcosystemManager>().Species.First(s => s != nest.Species);
            var intruder = SpawnMonster(intruderSpecies.id, nest.WorldPosition + Vector2.up * (nest.TerritoryRadius * 0.6f));
            Assert.That(intruder, Is.Not.Null);
            Assert.That(nest.InTerritory(intruder.Position), Is.True, "the lure ends inside the territory");

            // What a lure looks like on arrival: already swinging at something.
            intruder.ForceTarget(resident);
            float startHp = intruder.Hp;
            yield return WaitFor(() => intruder.DebugState == "도주" || intruder.Hp < startHp || !intruder.IsAlive, 8f);

            Assert.That(intruder.DebugState, Is.Not.EqualTo("도주"), "a fight in progress is not called off by the rout rule");
            Assert.That(!intruder.IsAlive || intruder.Hp < startHp || intruder.CurrentTarget != null,
                Is.True, "the residents and the intruder actually trade blows");
        }

        [UnityTest]
        public IEnumerator AHuntingBandLeavesTogether_AndAHealerNeverGoesAlone()
        {
            // P4-37 (user request): free hunts move as a party, and a priest alone makes no sense.
            yield return Boot();
            var bands = Services.Get<HuntBandService>();
            var first = Roster.Members.First(a => a.IsAlive);
            var second = Roster.Members.First(a => a.IsAlive && a != first);

            first.ChangeState(first.FreeHuntState);
            second.ChangeState(second.FreeHuntState);
            yield return null;

            var band = bands.BandOf(first);
            Assert.That(band, Is.Not.Null);
            Assert.That(bands.BandOf(second), Is.SameAs(band), "the second to decide joins the first, it does not set off alone");
            Assert.That(band.Leader, Is.EqualTo(first), "the first to decide leads");
            Assert.That(band.Mustering, Is.True, "they wait at the gate for company");
            Assert.That(Services.Get<PartyTactics>().LeaderOf(second), Is.EqualTo(first),
                        "so the band gets a quest party's formation and focus fire");

            // A priest on its own turns back rather than hunt without a party.
            second.ChangeState(second.IdleState);
            first.ChangeState(first.IdleState);
            yield return null;
            var priest = Roster.Members.FirstOrDefault(a => a.IsAlive && a.Data.cls == AdventurerClass.Priest);
            if (priest == null)
            {
                priest = first;
                priest.Data.cls = AdventurerClass.Priest;
            }
            priest.ChangeState(priest.FreeHuntState);
            var solo = bands.BandOf(priest);
            Assert.That(solo.Members.Count, Is.EqualTo(1));
            solo.MusterUntilHour = 0f; // its wait is over and nobody came
            yield return WaitFor(() => priest.Fsm.Current != priest.FreeHuntState, 5f);
            Assert.That(priest.Fsm.Current, Is.EqualTo(priest.ReturnState), "the healer turns back instead of hunting alone");
        }

        [UnityTest]
        public IEnumerator HuntingBand_FollowersStayOnTheLeash_AndGoHomeTogether()
        {
            // P4-38, user report 2026-09-21: "after a fight they go off on their own".
            yield return Boot();
            var master = MasterOnOpenGround();
            Assert.That(master, Is.Not.Null, "open ground for the test");
            var p = master.Position + new Vector2(0f, 6f);
            master.transform.position = p + new Vector2(-12f, 0f);
            var bands = Services.Get<HuntBandService>();
            var leader = Roster.Members.First(a => a.Data.cls == AdventurerClass.Warrior);
            var follower = Roster.Members.First(a => a.Data.cls == AdventurerClass.Ranger);
            leader.ChangeState(leader.FreeHuntState);
            follower.ChangeState(follower.FreeHuntState);
            yield return null;
            var band = bands.BandOf(leader);
            Assert.That(bands.BandOf(follower), Is.SameAs(band));
            band.MusterUntilHour = 0f;
            yield return null;
            Assert.That(band.Mustering, Is.False);

            foreach (var (a, at) in new[] { (leader, p), (follower, p + new Vector2(4.5f, 0f)) })
            {
                a.Mover.Stop();
                a.transform.position = at;
            }
            // In the ranger's sight, but well past the leash from the leader.
            var slime = SpawnMonster("slime", p + new Vector2(13f, 0f));
            bool strayed = false;
            float until = Time.time + 3f;
            while (Time.time < until)
            {
                if (follower.Engage.Target == slime && !follower.WasRecentlyAttackedBy(slime)
                    && Vector2.Distance(slime.Position, leader.Position) > PartyTactics.Leash + 0.5f) strayed = true;
                yield return null;
            }
            Assert.That(strayed, Is.False, "a follower does not go after prey far from its leader");

            // One goes home, so does the band.
            follower.ChangeState(follower.ReturnState);
            yield return WaitFor(() => leader.Fsm.Current == leader.ReturnState, 5f);
            Assert.That(leader.Fsm.Current, Is.EqualTo(leader.ReturnState), "the rest follow instead of hunting on alone");
        }

        [UnityTest]
        public IEnumerator WithAnEmptyBoard_MembersHuntOnTheirOwn_AndLeaveWhatTheyCannotTakeAlone()
        {
            // P4-37: an empty quest board used to mean standing in the plaza all day.
            yield return Boot();
            var member = Roster.Members.First(a => a.IsAlive && a.Data.cls != AdventurerClass.Priest);
            member.Data.potions = 2;
            member.ChangeState(member.FreeHuntState);
            yield return null;
            // Skip the muster: this test is about what it hunts, not who it goes with.
            Services.Get<HuntBandService>().BandOf(member).MusterUntilHour = 0f;
            yield return null;
            yield return null;

            var master = MasterOnOpenGround();
            Assert.That(master, Is.Not.Null);
            member.Mover.Stop();
            member.transform.position = master.Position;
            yield return null;

            // Something it can handle walks into view: it goes for it.
            var slime = SpawnMonster("slime", member.Position + Vector2.right * 3f);
            Assert.That(slime, Is.Not.Null);
            yield return WaitFor(() => member.CurrentTarget == slime, 8f);
            Assert.That(member.CurrentTarget, Is.EqualTo(slime), "a Lv1 member hunts a slime on its own");
            Assert.That(member.DebugState, Is.EqualTo("자율 사냥"));

            // Something it cannot: left alone until it strikes first.
            slime.Perish();
            member.Engage.Clear();
            var king = SpawnMonster("slime_king", member.Position + Vector2.left * 4f);
            Assert.That(king, Is.Not.Null);
            yield return WaitFor(() => member.CurrentTarget == king, 4f);
            Assert.That(member.CurrentTarget, Is.Not.EqualTo(king), "a Lv1 member does not pick a fight with a slime king");
            king.Perish();
        }

        [UnityTest]
        public IEnumerator AtTheStart_TheGuildKnowsNoNest()
        {
            // P5-1 handed over the two nearest nests; removed on 2026-09-20 (user decision) because
            // an orange minimap dot on unwalked ground breaks the information rule.
            yield return Boot();
            Assert.That(Services.Get<GuildIntel>().KnownNests.Any(), Is.False, "every nest has to be found by looking at it");
        }
    }
}
