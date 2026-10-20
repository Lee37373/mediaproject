using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using GuildMaster.Units;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;
using DragonRules = GuildMaster.Monsters.Dragon;

namespace GuildMaster.Dragons
{
    /// <summary>The guild saw the dragon (P4-93): the first time, or again after it was out of sight a while.</summary>
    public struct DragonSightedEvent
    {
        public Monster Dragon;
        public Vector2 Position;
        public bool Flying;
        public bool First;
    }

    /// <summary>The guild saw the dragon's lair (its hoard) for the first time.</summary>
    public struct DragonLairFoundEvent { public Vector2 Position; public bool DragonAtHome; public int HoardValue; }

    /// <summary>The guild learned that the dragon fell: at once when it saw it, else the next time it saw the lair.</summary>
    public struct DragonSlainEvent
    {
        public MonsterDefinition Def;
        public Vector2 Position;
        /// <summary>Guild members who took part (and now carry its title), by name.</summary>
        public IReadOnlyList<string> Participants;
    }

    /// <summary>The dragon's fire took gold from our treasury for its hoard (the guild sees its own treasury go down).</summary>
    public struct DragonPlunderedEvent { public int Gold; public Vector2 Position; }

    /// <summary>A member took from the hoard (and whether that woke the dragon asleep on it).</summary>
    public struct DragonHoardRaidedEvent { public Adventurer By; public int Value; public bool Woke; public Vector2 Position; }

    /// <summary>The dragon found its hoard robbed and hunts the thief; <see cref="Seen"/>: the guild saw it set off.</summary>
    public struct DragonEnragedEvent { public Adventurer Thief; public Vector2 Position; public bool Seen; }

    /// <summary>
    /// The dragon (P4-93, user decisions 2026-10-20; rules and numbers in <see cref="DragonRules"/>): where its lair is (no
    /// random draws: a seeded world stays the same), the day's waking (the map seed and the day decide), the round it
    /// flies, what the guild has seen of it and of the lair, its fall (titles to those who fought it) — it never comes
    /// back. Its hoard: gold it starts with, more each day, what its fire plunders from towns, what it takes off those it
    /// kills; members raid it (the town's quest, or a brave or greedy free hunter while it is away), and a thief is hunted
    /// down. Its body is an ordinary <see cref="Monster"/> with no nest (saved by the ecosystem like any nest-less monster)
    /// driven by <see cref="DragonSleepState"/>, <see cref="DragonFlightState"/> and <see cref="DragonLandedState"/>.
    /// </summary>
    public class DragonService : MonoBehaviour, ISaveable, ISaveLink
    {
        const float SightCheckSeconds = 0.5f;
        /// <summary>Out of the guild's sight this long, the next sighting is told again.</summary>
        const float ResightHours = 2f;
        const string GoldItem = "dragon_gold";

        WorldService _world;
        GameClock _clock;
        FogOfWar _fog;
        GameDatabase _db;
        int _seed;
        MonsterDefinition _def;
        SpriteRenderer _hoard;
        float _sightTimer, _sneakTimer;
        bool _inSight;
        float _lastSeenHour = -999f, _lastSeenFlyingHour = -999f, _lastSeenAsleepHour = -999f;

        readonly List<Vector2> _route = new();
        int _leg;
        float _flightEndsHour;
        int _plannedDay = -1;
        bool _wakesToday;
        float _wakeHour, _flightHours;
        readonly HashSet<string> _hitters = new();
        readonly List<string> _participants = new();
        string _restoredDragonId;

        readonly List<ItemStack> _hoardItems = new();
        /// <summary>The last one who took from the hoard, and whether the dragon has yet to answer for it.</summary>
        Adventurer _thief;
        bool _robbed;
        Adventurer _quarry;
        float _chaseEndsHour;
        /// <summary>Free hunters' raid plans, one decision per trip (member id → the trip's start hour, go).</summary>
        readonly Dictionary<string, (float trip, bool go)> _raidPlans = new();
        Quest _treasureQuest, _huntQuest;
        string _restoredThief, _restoredQuarry, _restoredTreasureQuest, _restoredHuntQuest;

        /// <summary>The living dragon (null once it fell).</summary>
        public Monster Dragon { get; private set; }
        public MonsterDefinition Def => _def;
        public bool HasLair { get; private set; }
        public Vector2Int LairCell { get; private set; }
        public Vector2 Lair => GridMap.CellToWorld(LairCell);
        public bool Slain { get; private set; }
        public float SlainHour { get; private set; }
        /// <summary>The guild knows it fell.</summary>
        public bool SlainKnown { get; private set; }
        public bool LairKnown { get; private set; }
        public bool EverSeen { get; private set; }
        /// <summary>Flying home (the flight is over, or it is badly hurt).</summary>
        public bool Homebound { get; private set; }
        /// <summary>Flying home badly hurt: it burns nothing on the way.</summary>
        public bool Retreating { get; private set; }
        public bool Asleep => Dragon != null && Dragon.IsAlive && Dragon.Fsm.Current == Dragon.DragonSleep;
        public IReadOnlyList<Vector2> Route => _route;
        public int Leg => _leg;
        public float FlightEndsHour => _flightEndsHour;
        public IReadOnlyList<string> Participants => _participants;

        /// <summary>Gold on the hoard (real); the pile's worth with its things (real).</summary>
        public int HoardGold { get; private set; }
        public IReadOnlyList<ItemStack> HoardItems => _hoardItems;
        public int HoardValue => HoardGold + _hoardItems.Sum(s => ValueOf(s));
        /// <summary>What the guild last saw of the hoard's worth (information rule: quests and raids go by this).</summary>
        public int KnownHoardValue { get; private set; }
        /// <summary>The thief it is hunting now (alive, the chase not over), else null.</summary>
        public Unit Quarry => _quarry != null && _quarry.IsAlive && _clock.TotalHours < _chaseEndsHour ? _quarry : null;
        /// <summary>The guild saw it on the wing lately (since it last saw it asleep at home): it takes it to be away.</summary>
        public bool BelievedAway => _clock.TotalHours - _lastSeenFlyingHour <= DragonRules.AwayHours && _lastSeenFlyingHour > _lastSeenAsleepHour;

        public void Init(WorldService world, GameClock clock, FogOfWar fog, GameDatabase db, int seed)
        {
            _world = world;
            _clock = clock;
            _fog = fog;
            _db = db;
            _seed = seed;
            _def = db.monsters.FirstOrDefault(m => m != null && m.isDragon);
            if (_def == null) return;
            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<UnitDamagedEvent>(OnDamaged);
            EventBus.Subscribe<UnitDiedEvent>(OnDied);
            // A loaded game brings its lair, its hoard and its dragon back (P5-4).
            if (Session.GameSettings.RestoresSection(SaveKey)) return;
            var lair = DragonRules.PickLair(world.Map, KeepClear());
            if (lair == null) return;
            HoardGold = DragonRules.HoardStart;
            PlaceLair(lair.Value);
            var m = Services.Get<PoolService>().Spawn<Monster>(Monster.PoolKey, Lair);
            // Set up on a stream of its own (its skill's first cooldown is staggered at random): the shared one stays where
            // it was, so a seeded world comes out the same with the dragon in it (a test member's last stand changed).
            var shared = GameRandom.Shared;
            GameRandom.Shared = DragonRules.DayStream(seed, 0);
            try { m.SetupDragon(_def); }
            finally { GameRandom.Shared = shared; }
            Dragon = m;
            Debug.Log($"[Dragon] lair at {LairCell} ({Vector2.Distance(Lair, world.TownCenterWorld):0} cells out)");
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            EventBus.Unsubscribe<UnitDamagedEvent>(OnDamaged);
            EventBus.Unsubscribe<UnitDiedEvent>(OnDied);
        }

        /// <summary>Dens, villages and ruin entrances the lair keeps clear of.</summary>
        List<Vector2Int> KeepClear()
        {
            var cells = _world.Map.Nests.Select(n => n.Cell).ToList();
            if (Services.TryGet<Villages.VillageService>(out var villages)) cells.AddRange(villages.Villages.Select(v => v.Cell));
            cells.AddRange(_world.Map.Ruins.Select(r => r.Entrance));
            return cells;
        }

        void PlaceLair(Vector2Int cell)
        {
            HasLair = true;
            LairCell = cell;
            if (_hoard == null)
            {
                var go = new GameObject("DragonHoard");
                go.transform.SetParent(transform, false);
                _hoard = go.AddComponent<SpriteRenderer>();
                _hoard.sprite = DragonArt.Hoard();
            }
            _hoard.transform.position = Lair;
            // A ground decal: under anything standing on it or just north of it.
            _hoard.sortingOrder = CharacterVisual.BaseSortingOrder - Mathf.RoundToInt((Lair.y + 3f) * 8f);
            RefreshHoard();
        }

        /// <summary>Drawn once the guild has seen the lair, as big as the guild last saw the pile.</summary>
        void RefreshHoard()
        {
            if (_hoard == null) return;
            float scale = DragonRules.HoardScale(KnownHoardValue);
            _hoard.enabled = HasLair && LairKnown && scale > 0f;
            _hoard.transform.localScale = new Vector3(scale, scale, 1f);
        }

        int ValueOf(ItemStack s) => _db?.Item(s.itemId) is { } item ? item.baseValue * s.count : 0;

        static void AddTo(List<ItemStack> list, string id, int count)
        {
            if (count <= 0) return;
            for (int i = 0; i < list.Count; i++)
                if (list[i].itemId == id) { list[i] = new ItemStack(id, list[i].count + count); return; }
            list.Add(new ItemStack(id, count));
        }

        // ---------------- Its day ----------------

        void OnHour(HourPassedEvent e)
        {
            if (Dragon != null && Dragon.IsAlive)
            {
                if (Asleep) Dragon.Heal(Dragon.Stats.MaxHp * DragonRules.SleepRegenPerHour);
                if (e.Hour == 0) HoardGold += DragonRules.HoardDaily;
                if (_plannedDay != e.Day)
                {
                    _plannedDay = e.Day;
                    _wakesToday = DragonRules.WakesOn(_seed, e.Day, out _wakeHour, out _flightHours);
                }
                if (_wakesToday && e.Hour == Mathf.FloorToInt(_wakeHour) && Asleep) Wake(e.Day, _flightHours);
            }
            PostQuests();
        }

        /// <summary>Wakes and takes off on the day's round (the day's own stream draws the places) — or after a thief, if robbed.</summary>
        public void Wake(int day, float flightHours)
        {
            if (Dragon == null || !Dragon.IsAlive) return;
            _route.Clear();
            _route.AddRange(DragonRules.Route(_seed, day, _world.Map.Width, _world.Map.SurfaceHeight));
            _leg = 0;
            _flightEndsHour = _clock.TotalHours + flightHours;
            Homebound = Retreating = false;
            Dragon.Fsm.ChangeState(Dragon.DragonFlight);
            // It slept through a theft: it counts its gold on waking.
            if (_robbed && _thief != null && _thief.IsAlive) StartChase(Dragon);
            Debug.Log($"[Dragon] wakes (day {day}) for {flightHours:0.0} h: {string.Join(" → ", _route.Select(p => GridMap.WorldToCell(p)))}");
        }

        /// <summary>Debug and tests: takes off now straight for <paramref name="point"/>, then home.</summary>
        public void FlyTo(Vector2 point, float flightHours)
        {
            if (Dragon == null || !Dragon.IsAlive) return;
            _route.Clear();
            _route.Add(point);
            _leg = 0;
            _flightEndsHour = _clock.TotalHours + flightHours;
            Homebound = Retreating = false;
            if (Dragon.Fsm.Current != Dragon.DragonFlight) Dragon.Fsm.ChangeState(Dragon.DragonFlight);
        }

        /// <summary>
        /// Takes off after fighting on the ground: after a thief if it was robbed, else back to its round, or (woken at home,
        /// or with none) a short one round the lair.
        /// </summary>
        public void TakeOff(Monster m, bool wokenAtHome)
        {
            if (_robbed && _thief != null && _thief.IsAlive)
            {
                StartChase(m);
                return;
            }
            // Brought down on its round: it carries on with it (or home).
            if (!wokenAtHome && (Homebound || _leg < _route.Count)) return;
            _route.Clear();
            var rng = GameRandom.Shared;
            for (int i = 0; i < 2; i++)
            {
                var p = Lair + rng.InsideUnitCircle() * DragonRules.GuardRadius;
                _route.Add(new Vector2(Mathf.Clamp(p.x, DragonRules.EdgeMargin, _world.Map.Width - DragonRules.EdgeMargin),
                    Mathf.Clamp(p.y, DragonRules.EdgeMargin, _world.Map.SurfaceHeight - DragonRules.EdgeMargin)));
            }
            _leg = 0;
            _flightEndsHour = _clock.TotalHours + DragonRules.GuardHours;
            Homebound = Retreating = false;
        }

        /// <summary>Badly hurt, the chase over, or the flight over: home. Robbed and the thief in sight: after it at once.</summary>
        public void CheckFlight(Monster m)
        {
            if (m.HpRatio < DragonRules.RetreatBelow) { Homebound = Retreating = true; _quarry = null; return; }
            if (_robbed && _thief != null && _thief.IsAlive && (_thief.Position - m.Position).sqrMagnitude <= Sq(DragonRules.AirSight))
            {
                StartChase(m);
                return;
            }
            if (_quarry != null && Quarry == null)
            {
                // The thief is dead or got away: home.
                _quarry = null;
                Homebound = true;
                return;
            }
            if (Quarry == null && _clock.TotalHours >= _flightEndsHour) Homebound = true;
        }

        public Vector2 NextWaypoint => Homebound || _leg >= _route.Count ? Lair : _route[_leg];
        public bool IsHome(Vector2 p) => (p - Lair).sqrMagnitude < 0.01f;
        public void NextLeg() => _leg++;

        /// <summary>
        /// Home: it lies down on its hoard (true) — unless the hoard was robbed while it was away, and then it sets off
        /// after the thief at once (false: it keeps flying).
        /// </summary>
        public bool Settle(Monster m)
        {
            if (_robbed && _thief != null && _thief.IsAlive)
            {
                StartChase(m);
                return false;
            }
            _route.Clear();
            _leg = 0;
            Homebound = Retreating = false;
            m.transform.position = new Vector3(Lair.x, Lair.y, m.transform.position.z);
            return true;
        }

        /// <summary>Robbed (user decision 2026-10-20: it hunts the thief): after the thief, wherever it is, for a while.</summary>
        void StartChase(Monster m)
        {
            _quarry = _thief;
            _robbed = false;
            _chaseEndsHour = _clock.TotalHours + DragonRules.ChaseHours;
            _flightEndsHour = Mathf.Max(_flightEndsHour, _chaseEndsHour);
            Homebound = Retreating = false;
            if (m.Fsm.Current != m.DragonFlight) m.Fsm.ChangeState(m.DragonFlight);
            bool seen = _fog != null && _fog.IsVisibleRaw(m.Position);
            Debug.Log($"[Dragon] robbed: hunts {_thief.DisplayName}");
            EventBus.Publish(new DragonEnragedEvent { Thief = _thief, Position = m.Position, Seen = seen });
        }

        /// <summary>Woken by a blow at home (the guild, a monster); the fight is the landed state's.</summary>
        public void OnWokenByAttack(Unit by) => Debug.Log($"[Dragon] woken at home by {by?.DisplayName ?? "?"}");

        // ---------------- The hoard ----------------

        /// <summary>Its fire caught our buildings or a village: gold for the hoard (our treasury's, or the village's).</summary>
        public void Plunder(bool town, bool village, Vector2 at)
        {
            if (Dragon == null || !Dragon.IsAlive) return;
            if (town && Services.TryGet<Economy.GuildTreasury>(out var treasury))
            {
                int take = Mathf.Min(DragonRules.PlunderTown, treasury.Gold);
                if (take > 0 && treasury.TrySpendGold(take, "드래곤 약탈"))
                {
                    HoardGold += take;
                    EventBus.Publish(new DragonPlunderedEvent { Gold = take, Position = at });
                }
            }
            if (village) HoardGold += DragonRules.PlunderVillage;
        }

        /// <summary>
        /// A member at the lair reaches into the hoard: whole sacks of its gold and a few things off the pile go into its bag.
        /// While it sleeps on it, that may wake it; away, it will find out when it is back. False when there is nothing to take.
        /// </summary>
        public bool Take(Adventurer a)
        {
            if (a == null || !a.IsAlive || a.Data == null || !HasLair) return false;
            if ((a.Position - Lair).sqrMagnitude > Sq(DragonRules.TakeReach + 0.3f)) return false;
            int sacks = DragonRules.SacksTaken(HoardGold);
            var taken = new List<ItemStack>();
            for (int i = 0; i < DragonRules.TakeItems && _hoardItems.Count > 0; i++)
            {
                var s = _hoardItems[_hoardItems.Count - 1];
                _hoardItems.RemoveAt(_hoardItems.Count - 1);
                taken.Add(s);
            }
            KnownHoardValue = HoardValue - sacks * DragonRules.SackValue;
            if (sacks == 0 && taken.Count == 0)
            {
                a.Say("텅 비었잖아...");
                SettleTreasureQuest(a);
                RefreshHoard();
                return false;
            }
            HoardGold -= sacks * DragonRules.SackValue;
            AddTo(a.CarriedLoot, GoldItem, sacks);
            foreach (var s in taken) AddTo(a.CarriedLoot, s.itemId, s.count);
            int value = sacks * DragonRules.SackValue + taken.Sum(ValueOf);
            bool woke = false;
            if (Dragon != null && Dragon.IsAlive)
            {
                _thief = a;
                if (Asleep && GameRandom.Shared.Chance(DragonRules.TakeWakeChance))
                {
                    woke = true;
                    DragonStates.Wake(Dragon, a);
                }
                _robbed = true;
            }
            Debug.Log($"[Dragon] hoard raided by {a.DisplayName}: {value}G (woke {woke}), {HoardValue}G left");
            EventBus.Publish(new DragonHoardRaidedEvent { By = a, Value = value, Woke = woke, Position = Lair });
            SettleTreasureQuest(a);
            RefreshHoard();
            return true;
        }

        /// <summary>
        /// A free hunter's raid (P4-93, user decision "둘 다"): a brave or greedy leader near enough to a hoard the guild knows
        /// is worth it decides once a trip (by chance) to make for it — while the dragon is believed away, or to creep past it
        /// asleep — and the band follows. True while walking there (the free hunt skips its own walking).
        /// </summary>
        public bool RaidTick(Adventurer a, float trip, bool isLeader)
        {
            if (!HasLair || !LairKnown || a?.Data == null || !isLeader) return false;
            if (!_raidPlans.TryGetValue(a.Data.id, out var plan) || plan.trip != trip)
            {
                // Decided once a trip, and only once the lair is in reach (the band roams on otherwise).
                if (Vector2.Distance(a.Position, Lair) > DragonRules.RaidRange) return false;
                bool go = KnownHoardValue >= DragonRules.RaidMinValue && DragonRules.WouldRaid(a.Data.traitIds)
                          && GameRandom.Shared.Chance(DragonRules.RaidPlanChance);
                plan = (trip, go);
                _raidPlans[a.Data.id] = plan;
                if (go) a.Say(BelievedAway || Dragon == null ? "드래곤이 둥지를 비웠어. 보물을 챙기러 가자!" : "드래곤이 잠든 틈에 보물을 좀 챙겨 볼까...");
            }
            if (!plan.go) return false;
            if (Vector2.Distance(a.Position, Lair) <= DragonRules.TakeReach)
            {
                Take(a);
                _raidPlans[a.Data.id] = (trip, false);
                return false;
            }
            if (!a.Mover.IsMoving || Vector2.Distance(a.Mover.Destination, Lair) > 1f)
                a.Mover.SetDestination(Lair, DragonRules.TakeReach * 0.6f, RouteChoice.ForParty(a));
            return true;
        }

        /// <summary>Members creeping round it asleep may wake it (every few seconds, each one close by).</summary>
        void CheckSneakers()
        {
            if (!Asleep || !Services.TryGet<GuildRoster>(out var roster)) return;
            foreach (var a in roster.Members)
            {
                if (a == null || !a.IsAlive || (a.Position - Dragon.Position).sqrMagnitude > Sq(DragonRules.SneakRadius)) continue;
                if (!GameRandom.Shared.Chance(DragonRules.SneakWakeChance)) continue;
                DragonStates.Wake(Dragon, a);
                return;
            }
        }

        // ---------------- The town's quests ----------------

        /// <summary>
        /// Once the guild knows the lair, the town posts a raid on a hoard worth it (one at a time), and a hunt for the
        /// dragon once it has seen it (one at a time, two parties' worth).
        /// </summary>
        void PostQuests()
        {
            if (!HasLair || !LairKnown || !Services.TryGet<QuestBoard>(out var board)) return;
            if ((_treasureQuest == null || !_treasureQuest.IsActive) && KnownHoardValue >= DragonRules.QuestMinValue)
                _treasureQuest = board.PostTreasureQuest(new QuestDraft
                {
                    Type = QuestType.Treasure, Point = Lair, HasPoint = true, Radius = 6f,
                    RewardGold = 200, RewardExp = 150, MaxParty = 4,
                });
            if ((_huntQuest == null || !_huntQuest.IsActive) && EverSeen && Dragon != null && Dragon.IsAlive)
            {
                if (Services.TryGet<GuildIntel>(out var intel)) intel.MarkKnown(_def);
                board.TryPostRequest(new QuestDraft
                {
                    Type = QuestType.Hunt, Species = _def, Count = 1, Point = Lair, HasPoint = true, Radius = QuestBoard.HuntRadius,
                    RewardGold = 2000, RewardExp = 1200, MaxParty = RegionLords.QuestParty,
                }, QuestClient.Town, out _huntQuest, out _);
            }
        }

        /// <summary>The raid quest is done once someone on it took from the hoard; with the hoard emptied, unclaimed, it comes down.</summary>
        void SettleTreasureQuest(Adventurer taker)
        {
            if (_treasureQuest == null || !_treasureQuest.IsActive || !Services.TryGet<QuestBoard>(out var board)) return;
            if (_treasureQuest.IsAssigned(taker) || KnownHoardValue < DragonRules.SackValue)
                board.SettleTownQuest(_treasureQuest, "드래곤의 보물이 남아 있지 않습니다");
        }

        // ---------------- What the guild sees ----------------

        void Update()
        {
            if (_def == null || _fog == null || Time.deltaTime <= 0f) return;
            _sneakTimer -= Time.deltaTime;
            if (_sneakTimer <= 0f)
            {
                _sneakTimer = DragonRules.SneakCheckSeconds;
                CheckSneakers();
            }
            _sightTimer -= Time.deltaTime;
            if (_sightTimer > 0f) return;
            _sightTimer = SightCheckSeconds;
            float now = _clock.TotalHours;
            if (HasLair && _fog.IsVisibleRaw(LairCell))
            {
                KnownHoardValue = HoardValue;
                if (!LairKnown)
                {
                    LairKnown = true;
                    EventBus.Publish(new DragonLairFoundEvent { Position = Lair, DragonAtHome = Asleep && _fog.IsVisibleRaw(Dragon.Position), HoardValue = KnownHoardValue });
                }
                RefreshHoard();
            }
            if (Dragon != null && Dragon.IsAlive)
            {
                if (_fog.IsVisibleRaw(Dragon.Position))
                {
                    if (!_inSight && now - _lastSeenHour >= ResightHours)
                        EventBus.Publish(new DragonSightedEvent { Dragon = Dragon, Position = Dragon.Position, Flying = Dragon.IsAirborne, First = !EverSeen });
                    EverSeen = true;
                    _inSight = true;
                    _lastSeenHour = now;
                    if (Dragon.IsAirborne) _lastSeenFlyingHour = now;
                    else if (Asleep) _lastSeenAsleepHour = now;
                }
                else _inSight = false;
            }
            if (Slain && !SlainKnown && HasLair && _fog.IsVisibleRaw(LairCell)) AnnounceSlain(Lair);
        }

        // ---------------- Its kills and its fall ----------------

        void OnDamaged(UnitDamagedEvent e)
        {
            if (Dragon != null && e.Target == Dragon && e.Source is Adventurer a && a.Data != null) _hitters.Add(a.Data.id);
        }

        void OnDied(UnitDiedEvent e)
        {
            if (Dragon == null) return;
            if (e.Killer == Dragon && e.Victim != null) CarryOffSpoils(e.Victim.Position);
            if (e.Victim != Dragon) return;
            var at = Dragon.Position;
            Slain = true;
            SlainHour = _clock.TotalHours;
            Dragon = null; // the body goes back to the pool once it fades
            _quarry = null;
            GiveTitles(at);
            bool observed = e.Killer != null && e.Killer.Faction == Faction.Guild || _fog != null && _fog.IsVisibleRaw(at);
            Debug.Log($"[Dragon] fell (by {e.Killer?.DisplayName ?? "?"}, observed {observed})");
            if (observed) AnnounceSlain(at);
            if (_huntQuest != null && Services.TryGet<QuestBoard>(out var board)) board.SettleTownQuest(_huntQuest, "드래곤이 쓰러졌습니다");
        }

        /// <summary>What falls from those it kills goes onto its hoard (not a member's keepsake: that stays where it fell).</summary>
        void CarryOffSpoils(Vector2 at)
        {
            if (!Services.TryGet<Items.GroundItemService>(out var ground)) return;
            foreach (var g in ground.Items.ToList())
            {
                if (g == null || g.Kept || g.Item == null || g.Age > 0.5f || (g.Position - at).sqrMagnitude > 1.5f * 1.5f) continue;
                var s = ground.Take(g, Dragon);
                AddTo(_hoardItems, s.itemId, s.count);
            }
        }

        /// <summary>Members alive who hit it, or stood by when it fell (healers, the ones it was burning).</summary>
        void GiveTitles(Vector2 at)
        {
            if (!Services.TryGet<GuildRoster>(out var roster)) return;
            string title = DragonRules.Title(_def.displayName);
            foreach (var a in roster.Members)
            {
                if (a == null || !a.IsAlive || a.Data == null) continue;
                bool hit = _hitters.Contains(a.Data.id);
                bool near = Vector2.Distance(a.Position, at) <= DragonRules.ParticipantRadius;
                if (!hit && !near) continue;
                if (!a.Data.titles.Contains(title)) a.Data.titles.Add(title);
                AdventurerMemory.Add(a.Data.memories, _clock.TotalHours, 3,
                    $"동료들과 함께 {_def.displayName}{MemoryRecorder.Josa(_def.displayName, "을", "를")} 쓰러뜨렸다", "dragon:" + _def.id);
                _participants.Add(a.Data.name);
            }
            _hitters.Clear();
        }

        void AnnounceSlain(Vector2 at)
        {
            SlainKnown = true;
            EventBus.Publish(new DragonSlainEvent { Def = _def, Position = at, Participants = _participants });
        }

        static float Sq(float x) => x * x;

        // ---------------- Debug ----------------

        /// <summary>The real state (debug: <c>DebugCommands.Dragon()</c>), not what the guild knows.</summary>
        public string Describe()
        {
            if (_def == null) return "드래곤 데이터 없음";
            if (!HasLair) return "둥지 자리를 못 찾음 (드래곤 없음)";
            var s = new System.Text.StringBuilder();
            s.Append($"둥지 {LairCell} (마을에서 {Vector2.Distance(Lair, _world.TownCenterWorld):0}칸), 둥지 발견 {LairKnown}, 본 적 {EverSeen}, ");
            s.Append($"보물 {HoardValue}G (금 {HoardGold}, 물건 {string.Join("/", _hoardItems.Select(i => i.itemId + "x" + i.count))}, 길드가 아는 값 {KnownHoardValue}), ");
            if (_thief != null) s.Append($"도둑 {_thief.DisplayName}(미응징 {_robbed}), ");
            if (Slain) return s.Append($"쓰러짐 {SlainHour:0.0}시 (길드가 앎 {SlainKnown}), 함께한 길드원 {string.Join(", ", _participants)}").ToString();
            var d = Dragon;
            s.Append($"{d.DisplayName} Lv.{d.Level} 체력 {d.Hp:0}/{d.Stats.MaxHp:0} 공격 {d.Stats.Attack:0.0} 방어 {d.Stats.Defense:0.0}, 상태 {d.StateLabel}, 위치 {GridMap.WorldToCell(d.Position)}");
            if (!Asleep) s.Append($", 경로 {_leg}/{_route.Count} 다음 {GridMap.WorldToCell(NextWaypoint)}, 비행 끝 {_flightEndsHour:0.0}시 (지금 {_clock.TotalHours:0.0}), 귀가 {Homebound}·도망 {Retreating}, 공중 피해 {d.AirDamage:0}{(Quarry != null ? $", 추격 {Quarry.DisplayName}" : "")}");
            for (int day = _clock.Day; day < _clock.Day + 7; day++)
                if (DragonRules.WakesOn(_seed, day, out float h, out float len)) { s.Append($", 다음 깨어남 {day}일 {h:0.0}시 ({len:0.0}시간)"); break; }
            return s.ToString();
        }

        // ---------------- Save (P5-4) ----------------

        [Serializable]
        class SaveData
        {
            public bool has_lair;
            public Vector2Int lair;
            public string dragon;
            public bool slain;
            public float slain_hour;
            public bool slain_known;
            public bool lair_known;
            public bool ever_seen;
            public bool in_sight;
            public float last_seen_hour;
            public float last_seen_flying;
            public float last_seen_asleep;
            public List<Vector2> route = new();
            public int leg;
            public float flight_ends;
            public bool homebound;
            public bool retreating;
            public List<string> hitters = new();
            public List<string> participants = new();
            public int hoard_gold;
            public List<ItemStack> hoard_items = new();
            public int known_hoard;
            public string thief;
            public bool robbed;
            public string quarry;
            public float chase_ends;
            public string treasure_quest;
            public string hunt_quest;
        }

        public string SaveKey => "dragon";

        public string Capture()
        {
            var s = new SaveData
            {
                has_lair = HasLair, lair = LairCell, dragon = Dragon != null && Dragon.IsAlive ? Dragon.UnitId : null,
                slain = Slain, slain_hour = SlainHour, slain_known = SlainKnown, lair_known = LairKnown, ever_seen = EverSeen,
                in_sight = _inSight, last_seen_hour = _lastSeenHour, last_seen_flying = _lastSeenFlyingHour, last_seen_asleep = _lastSeenAsleepHour,
                leg = _leg, flight_ends = _flightEndsHour, homebound = Homebound, retreating = Retreating,
                hoard_gold = HoardGold, known_hoard = KnownHoardValue,
                thief = _thief != null && _thief.IsAlive ? _thief.UnitId : null, robbed = _robbed,
                quarry = _quarry != null && _quarry.IsAlive ? _quarry.UnitId : null, chase_ends = _chaseEndsHour,
                treasure_quest = _treasureQuest?.Id, hunt_quest = _huntQuest?.Id,
            };
            s.route.AddRange(_route);
            s.hitters.AddRange(_hitters.OrderBy(h => h, StringComparer.Ordinal));
            s.participants.AddRange(_participants);
            s.hoard_items.AddRange(_hoardItems);
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            Slain = s.slain;
            SlainHour = s.slain_hour;
            SlainKnown = s.slain_known;
            LairKnown = s.lair_known;
            EverSeen = s.ever_seen;
            _inSight = s.in_sight;
            _lastSeenHour = s.last_seen_hour;
            _lastSeenFlyingHour = s.last_seen_flying;
            _lastSeenAsleepHour = s.last_seen_asleep;
            _route.Clear();
            _route.AddRange(s.route);
            _leg = s.leg;
            _flightEndsHour = s.flight_ends;
            Homebound = s.homebound;
            Retreating = s.retreating;
            _hitters.Clear();
            foreach (var h in s.hitters) _hitters.Add(h);
            _participants.Clear();
            _participants.AddRange(s.participants);
            HoardGold = s.hoard_gold;
            _hoardItems.Clear();
            _hoardItems.AddRange(s.hoard_items);
            KnownHoardValue = s.known_hoard;
            _robbed = s.robbed;
            _chaseEndsHour = s.chase_ends;
            _restoredDragonId = s.dragon;
            _restoredThief = s.thief;
            _restoredQuarry = s.quarry;
            _restoredTreasureQuest = s.treasure_quest;
            _restoredHuntQuest = s.hunt_quest;
            if (s.has_lair) PlaceLair(s.lair);
        }

        public void Link(SaveContext ctx)
        {
            Dragon = string.IsNullOrEmpty(_restoredDragonId) ? null : ctx.Find<Monster>(_restoredDragonId);
            if (!string.IsNullOrEmpty(_restoredDragonId) && Dragon == null) ctx.Warnings.Add("dragon: no monster " + _restoredDragonId);
            _thief = ctx.Find<Adventurer>(_restoredThief);
            _quarry = ctx.Find<Adventurer>(_restoredQuarry);
            _treasureQuest = ctx.Find<Quest>(_restoredTreasureQuest);
            _huntQuest = ctx.Find<Quest>(_restoredHuntQuest);
            _restoredDragonId = _restoredThief = _restoredQuarry = _restoredTreasureQuest = _restoredHuntQuest = null;
        }
    }
}
