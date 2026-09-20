using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Economy;
using GuildMaster.Player;
using GuildMaster.Quests;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Session
{
    /// <summary>
    /// Stands in for the player during a balance run (P5-1). It does the two things the loop cannot
    /// start without: it walks the guild master out to scout (nothing can be posted against a nest
    /// the guild has not seen), and it keeps a couple of hunt quests on the board, sized so the
    /// roster reads them as "적당", paying the going rate. It is a measuring instrument, not an AI
    /// player — it never upgrades buildings or builds towers, so a run measures the bare loop.
    /// Created on demand by <see cref="DebugCommands"/>.
    /// </summary>
    public class BalanceAutoPlayer : MonoBehaviour
    {
        public static BalanceAutoPlayer Instance { get; private set; }

        public static BalanceAutoPlayer Ensure()
        {
            if (Instance != null) return Instance;
            Instance = new GameObject("BalanceAutoPlayer").AddComponent<BalanceAutoPlayer>();
            return Instance;
        }

        /// <summary>Gold paid per point of the species' EXP value, per monster asked for.</summary>
        public const float GoldPerPower = 1.5f;
        public const float ExpPerPower = 1f;
        /// <summary>How many hunt quests to keep open at once.</summary>
        public int OpenQuestTarget { get; set; } = 2;
        /// <summary>Never post against a species whose observed population is at or below this.</summary>
        public int SpareBelow { get; set; } = 2;
        public bool Running { get; private set; }
        public int Posted { get; private set; }
        public int Skipped { get; private set; }
        public int ScoutLegs { get; private set; }
        public int ScoutRetreats { get; private set; }
        public int ShieldSaves { get; private set; }

        /// <summary>Scouting ring the master walks, in cells beyond the wall; it grows each lap.</summary>
        public const float FirstRing = 12f;
        public const float RingStep = 9f;
        public const float MaxRing = 46f;
        /// <summary>Head home below this share of health, resume once back above ResumeHp.</summary>
        public const float RetreatHp = 0.65f;
        public const float ResumeHp = 0.85f;
        /// <summary>How often the scout picks its next leg. Health is read every frame.</summary>
        public const float ThinkInterval = 0.25f;

        /// <summary>
        /// Keeps the master alive so a 10-day run is not cut short by a game over. It is a floor, not
        /// immunity: the retreat above still does the work, and every save is reported so the bias is
        /// visible in the numbers. Turn it off to measure how survivable scouting actually is.
        /// </summary>
        public bool ShieldMaster { get; set; } = true;
        public const float ShieldBelow = 0.45f;
        public const float ShieldTo = 0.9f;

        GuildIntel _intel;
        QuestBoard _board;
        GuildTreasury _treasury;
        GuildRoster _roster;
        PlayerAvatar _master;
        WorldService _world;

        int _leg;
        float _ring = FirstRing;
        bool _goingHome;
        float _thinkTimer;

        public void Begin()
        {
            _intel = Services.Get<GuildIntel>();
            _board = Services.Get<QuestBoard>();
            _treasury = Services.Get<GuildTreasury>();
            _roster = Services.Get<GuildRoster>();
            _master = Services.Get<PlayerAvatar>();
            _world = Services.Get<WorldService>();
            Posted = Skipped = ScoutLegs = ScoutRetreats = ShieldSaves = 0;
            _leg = 0;
            _thinkTimer = 0f;
            _ring = FirstRing;
            _goingHome = false;
            Running = true;
            SetCraftTargets();
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        /// <summary>How much to keep on the shelf, as a player would set it in the warehouse.</summary>
        public int PotionTarget { get; set; } = 6;
        public int GearTarget { get; set; } = 3;

        /// <summary>
        /// Without stock targets nobody could buy anything all run: the members fought bare-handed
        /// with 800G in their pockets while the shelves stood empty.
        /// </summary>
        void SetCraftTargets()
        {
            if (!Services.TryGet<CraftingService>(out var crafting)) return;
            foreach (var r in crafting.Recipes)
            {
                if (r.outputItemId == DefaultContent.Potion) crafting.SetTarget(r, PotionTarget);
                else if (r.outputItemId == "gear_t1") crafting.SetTarget(r, GearTarget);
            }
        }

        public void Stop()
        {
            if (!Running) return;
            Running = false;
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
        }

        void OnDestroy()
        {
            if (Running) Stop();
            if (Instance == this) Instance = null;
        }

        void OnHour(HourPassedEvent e)
        {
            int open = _board.Quests.Count(q => q.IsActive);
            if (open >= OpenQuestTarget) return;
            TryPostOne();
        }

        void Update()
        {
            if (!Running) return;
            // The floor is checked every frame: at x4 a quarter second is a game hour of goblins,
            // and the master died between two checks in the first 10-day run.
            HoldTheFloor();
            _thinkTimer -= Time.unscaledDeltaTime;
            if (_thinkTimer > 0f) return;
            _thinkTimer = ThinkInterval;
            Scout();
        }

        /// <summary>Keeps the master above the floor so a run ends on its day count, not a game over.</summary>
        void HoldTheFloor()
        {
            if (!ShieldMaster || _master == null || !_master.IsAlive) return;
            float max = Mathf.Max(1f, _master.Stats.MaxHp);
            if (_master.Hp / max >= ShieldBelow) return;
            _master.Heal(max * ShieldTo - _master.Hp);
            ShieldSaves++;
        }

        /// <summary>
        /// Walks the master around the town in a widening ring, the way a player uncovers the map.
        /// Retreats home when hurt and waits there until healed, so a run is not cut short by a
        /// game over. Nothing here touches fog or intel directly — it only issues move orders.
        /// </summary>
        void Scout()
        {
            if (_master == null || !_master.IsAlive) return;

            float hpRatio = _master.Hp / Mathf.Max(1f, _master.Stats.MaxHp);
            if (!_goingHome && hpRatio < RetreatHp)
            {
                _goingHome = true;
                ScoutRetreats++;
                _master.MoveTo(_world.TownCenterWorld);
                return;
            }
            if (_goingHome)
            {
                if (hpRatio < ResumeHp) return; // healing up in town
                _goingHome = false;
            }
            if (_master.Mover.IsMoving) return;

            var dirs = new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
            var town = _world.Map.TownRect;
            // Try the next few headings; a blocked one (water, mountain) just moves to the one after.
            for (int i = 0; i < dirs.Length; i++)
            {
                var d = dirs[(_leg + i) % dirs.Length];
                float half = (Mathf.Abs(d.x) > 0f ? town.width : town.height) * 0.5f;
                var cell = GridMap.WorldToCell(_world.TownCenterWorld + d * (half + _ring));
                if (!_world.Grid.TryFindNearestWalkable(cell, 8, out cell)) continue;
                if (!_master.MoveTo(GridMap.CellToWorld(cell))) continue;
                _leg += i + 1;
                ScoutLegs++;
                if (_leg % dirs.Length == 0) _ring = Mathf.Min(MaxRing, _ring + RingStep);
                return;
            }
        }

        /// <summary>Strength of the best member, used the same way the board's warning does (P4-11).</summary>
        float LeaderPower()
        {
            float best = 0f;
            foreach (var a in _roster.Members)
                if (a.IsAlive) best = Mathf.Max(best, AdventurerStats.Power(a.Data));
            return Mathf.Max(1f, best);
        }

        /// <summary>
        /// Posts one hunt on the nest the guild knows best, at a count the roster can handle.
        /// Everything it reads is observed intel, so the run obeys the information rule.
        /// </summary>
        public bool TryPostOne()
        {
            var nest = PickNest();
            if (nest == null) { Skipped++; return false; }

            var species = _intel.SpeciesOf(nest);
            int observed = _intel.ObservedPopulation(species);
            int maxParty = Mathf.Min(4, Mathf.Max(1, _roster.Members.Count));
            float power = LeaderPower();

            // Ask for as many as the roster can take on: grow the count while it still reads "적당".
            int count = 1;
            for (int c = 2; c <= Mathf.Min(6, observed - SpareBelow); c++)
            {
                var d = QuestBoard.HuntDanger(species, c, nest.WorldPosition, QuestBoard.HuntRadius);
                if (QuestRisk.Margin(power, maxParty, d) < 1f) break;
                count = c;
            }
            if (count < 1) { Skipped++; return false; }

            float per = Mathf.Max(1, species.expValue);
            int gold = Mathf.RoundToInt(count * per * GoldPerPower);
            int exp = Mathf.RoundToInt(count * per * ExpPerPower);
            if (!_treasury.CanAfford(gold, exp)) { Skipped++; return false; }

            var draft = new QuestDraft
            {
                Type = QuestType.Hunt,
                Species = species,
                Count = count,
                RewardGold = gold,
                RewardExp = exp,
                MaxParty = maxParty,
                Point = nest.WorldPosition,
                HasPoint = true,
                Radius = QuestBoard.HuntRadius,
            };
            if (!_board.TryPost(draft, out _, out _)) { Skipped++; return false; }
            Posted++;
            return true;
        }

        /// <summary>A quest the roster can come back from: below this the auto player leaves it alone.</summary>
        public const float SafeMargin = 1.2f;

        /// <summary>
        /// The nest a sensible player would send this roster to: one it can handle, nearest first.
        /// Picking the fullest nest instead sent Lv1 parties across the map into goblin warriors and
        /// wiped the guild by day 4, which measured the auto player's judgement, not the balance.
        /// </summary>
        Nest PickNest()
        {
            float power = LeaderPower();
            int maxParty = Mathf.Min(4, Mathf.Max(1, _roster.Members.Count));
            var town = _world.TownCenterWorld;

            Nest best = null;
            float bestDistance = float.MaxValue;
            foreach (var n in _intel.KnownNests)
            {
                var species = _intel.SpeciesOf(n);
                if (species == null) continue;
                if (_intel.Get(n).LastCount <= SpareBelow) continue;
                if (_intel.ObservedPopulation(species) <= SpareBelow) continue;
                if (_board.Quests.Any(q => q.IsActive && q.Species == species)) continue;

                // One monster is the smallest quest there is; if even that is unsafe, stay away.
                var danger = QuestBoard.HuntDanger(species, 1, n.WorldPosition, QuestBoard.HuntRadius);
                if (QuestRisk.Margin(power, maxParty, danger) < SafeMargin) continue;

                float distance = Vector2.Distance(n.WorldPosition, town);
                if (distance < bestDistance) { bestDistance = distance; best = n; }
            }
            return best;
        }

        public string Report()
            => $"[autoplayer] running={Running} posted={Posted} skipped={Skipped} scoutLegs={ScoutLegs} retreats={ScoutRetreats} "
             + $"shieldSaves={ShieldSaves}{(ShieldMaster ? "" : " (shield off)")} ring={_ring:0} openTarget={OpenQuestTarget} spareBelow={SpareBelow}";
    }
}
