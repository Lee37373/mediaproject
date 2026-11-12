using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Player;
using GuildMaster.Quests;
using GuildMaster.Session;
using GuildMaster.Town;
using GuildMaster.Units;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Commands
{
    /// <summary>An order the guild master gave (P4-132): where, at what, how many heard it and how many followed.</summary>
    public struct MasterCommandEvent
    {
        public MasterCommand Command;
        /// <summary>Where the master stood (and the flag went).</summary>
        public Vector2 Position;
        /// <summary>The focus order's monster; null for the others.</summary>
        public Unit Target;
        public int Heard, Followed;
        public string Summary;
    }

    /// <summary>
    /// The guild master's orders (P4-132, H6 = F-16; user decision 2026-11-08 — the numbers and words in <see cref="CommandRules"/>).
    /// A command gauge fills with time, quests done and raids seen off; each order costs some of it and has a short cooldown.
    /// Orders are heard by the members within <see cref="CommandRules.Reach"/> of the master, and each decides by its mood and
    /// nature whether to follow (<see cref="CommandRules.ObeyChance"/>) — its words show either way:
    /// <list type="bullet">
    /// <item>응원 (cheer): the followers' attack +25% for 15 s (<see cref="Unit.CheerAttack"/>) and a warm thought.</item>
    /// <item>집중 공격 (focus): the followers go for one monster near the master the guild can see (<see cref="Adventurer.OrderTarget"/>)
    /// — past their party leash and their state's zone; ones idling in town turn out as town guard to do it.</item>
    /// <item>집결 깃발 (rally flag): planted where the master stands, inside the walls or close outside, for a day. While it stands,
    /// each defender of a raid decides once a raid whether to hold the flag instead of its gate (<see cref="FlagFor"/>).</item>
    /// <item>후퇴 나팔 (retreat horn): the followers stop fighting and run home (<see cref="Adventurer.RetreatOnOrder"/>), out of the
    /// town's defence for an hour. Escorts stay with the master.</item>
    /// </list>
    /// Its dice are its own (the shared stream the rest of the game draws from is left alone). The gauge, the cooldowns and the
    /// flag are saved (<c>commands</c>); a cheer and a focus order last seconds and are not.
    /// </summary>
    public class MasterCommands : MonoBehaviour, ISaveable
    {
        /// <summary>Tests: everyone who hears follows.</summary>
        internal static bool Certain;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Certain = false;

        static readonly Color CheerColor = new(1f, 0.82f, 0.3f), FocusColor = new(1f, 0.35f, 0.3f),
            RallyColor = new(0.75f, 0.55f, 1f), RetreatColor = new(0.45f, 0.75f, 1f);

        PlayerAvatar _master;
        GameClock _clock;
        GuildRoster _roster;
        TownService _town;
        GameRandom _rng;
        float _power = CommandRules.StartPower;
        readonly float[] _readyAt = new float[CommandRules.All.Length];
        float _lastHours;
        bool _hasFlag;
        Vector2 _flag;
        float _flagUntil;
        RallyFlag _flagView;
        SpriteRenderer _reach;
        int _reachShown;
        /// <summary>Who heeds the flag in the raid going on (asked once a raid, when it first comes to defend).</summary>
        readonly Dictionary<Adventurer, bool> _heedsFlag = new();
        bool _raidOn;
        readonly List<Adventurer> _heard = new();
        readonly List<string> _refused = new();

        public float Power => _power;
        public bool HasFlag => _hasFlag && Hours < _flagUntil;
        public Vector2 FlagPosition => _flag;
        public float FlagHoursLeft => HasFlag ? _flagUntil - Hours : 0f;
        public Vector2 MasterPosition => _master != null ? _master.Position : Vector2.zero;
        float Hours => _clock != null ? _clock.TotalHours : Time.time / 10f;

        public void Init(PlayerAvatar master, GameClock clock, GuildRoster roster, TownService town, int seed)
        {
            _master = master;
            _clock = clock;
            _roster = roster;
            _town = town;
            _rng = new GameRandom(seed ^ 0x5132);
            _lastHours = Hours;
            for (int i = 0; i < _readyAt.Length; i++) _readyAt[i] = float.NegativeInfinity;
            _flagView = RallyFlag.Create(transform);
            BuildReach();
            EventBus.Subscribe<QuestCompletedEvent>(OnQuestDone);
            EventBus.Subscribe<RaidEndedEvent>(OnRaidEnded);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestDone);
            EventBus.Unsubscribe<RaidEndedEvent>(OnRaidEnded);
        }

        void Update()
        {
            float h = Hours;
            _power = CommandRules.Regen(_power, h - _lastHours);
            _lastHours = h;
            if (_hasFlag && h >= _flagUntil) TakeDownFlag();
            // Each raid asks the defenders anew whether they heed the flag.
            bool raid = Services.TryGet<WaveManager>(out var waves) && waves.RaidSighted;
            if (!raid && _raidOn) _heedsFlag.Clear();
            _raidOn = raid;
            if (_reach != null && _reach.enabled && _master != null) _reach.transform.position = _master.Position;
        }

        void OnQuestDone(QuestCompletedEvent e) => AddPower(CommandRules.QuestDonePower);

        void OnRaidEnded(RaidEndedEvent e)
        {
            if (e.Sighted && e.Killed > 0) AddPower(CommandRules.RaidHeldPower);
        }

        public void AddPower(float amount) => _power = Mathf.Clamp(_power + amount, 0f, CommandRules.MaxPower);

        /// <summary>Debug and tests: the gauge as given.</summary>
        public void SetPower(float power) => _power = Mathf.Clamp(power, 0f, CommandRules.MaxPower);

        /// <summary>Debug and tests: every order ready again.</summary>
        public void ResetCooldowns()
        {
            for (int i = 0; i < _readyAt.Length; i++) _readyAt[i] = float.NegativeInfinity;
        }

        public float HoursToReady(MasterCommand c) => Mathf.Max(0f, _readyAt[(int)c] - Hours);

        /// <summary>What stands in the way of an order before anyone is asked (the master down, the gauge, the cooldown).</summary>
        public CommandRefusal Check(MasterCommand c) => CommandRules.CanAfford(c, _master != null && _master.IsAlive, _power, HoursToReady(c));

        // ---------------- Giving an order ----------------

        /// <summary>
        /// Gives an order; false (and why, in <paramref name="message"/>) when it cannot be given — nothing is spent then.
        /// <paramref name="at"/> is the focus order's clicked point; the other orders go where the master stands.
        /// </summary>
        public bool TryIssue(MasterCommand c, Vector2? at, out string message)
        {
            var refusal = Check(c);
            Unit target = null;
            if (refusal == CommandRefusal.None && c == MasterCommand.Focus) refusal = FindTarget(at, out target);
            if (refusal == CommandRefusal.None && c == MasterCommand.Rally) refusal = FlagSpot(_master.Position);
            if (refusal == CommandRefusal.None) refusal = Gather(c, target);
            if (refusal != CommandRefusal.None)
            {
                message = CommandRules.Why(refusal, c, _power, HoursToReady(c));
                return false;
            }

            _power -= CommandRules.Cost(c);
            _readyAt[(int)c] = Hours + CommandRules.CooldownHours(c);
            var at0 = _master.Position;
            int heard = _heard.Count;
            _refused.Clear();
            if (c == MasterCommand.Rally) Plant(at0);
            else
                foreach (var a in _heard)
                {
                    if (Follows(c, a)) Carry(c, a, target);
                    else
                    {
                        _refused.Add(a.Data.name);
                        a.Say(CommandRules.RefuseLine(c, _rng.Range(0, 100)), false);
                    }
                }
            Show(c, at0, target);

            message = c == MasterCommand.Rally && heard == 0
                ? $"집결 깃발을 꽂았습니다({CommandRules.FlagHours:0}시간). 침공이 오면 따르는 길드원은 깃발 아래에서 막습니다."
                : CommandRules.Summary(c, heard, _refused);
            if (Services.TryGet<GameLog>(out var log)) log.Add(message, LogCategory.Combat);
            EventBus.Publish(new MasterCommandEvent
            {
                Command = c, Position = at0, Target = target, Heard = heard, Followed = heard - _refused.Count, Summary = message,
            });
            return true;
        }

        /// <summary>The monster nearest the clicked point the guild can see, within the master's reach.</summary>
        CommandRefusal FindTarget(Vector2? at, out Unit target)
        {
            target = null;
            if (at == null) return CommandRefusal.NoTarget;
            Services.TryGet<FogOfWar>(out var fog);
            target = UnitRegistry.FindNearest(at.Value, CommandRules.PickRadius, fog,
                static (u, f) => u is Monster m && m.IsAlive && (f == null || f.IsVisibleRaw(m.Position)));
            if (target == null) return CommandRefusal.NoTarget;
            return (target.Position - _master.Position).sqrMagnitude > CommandRules.Reach * CommandRules.Reach
                ? CommandRefusal.TargetFar : CommandRefusal.None;
        }

        /// <summary>
        /// The flag goes up inside the walls or close outside them — out in the open, not in a building (the walk-in guild hall
        /// is in town, and a flag under its roof would be seen by nobody).
        /// </summary>
        CommandRefusal FlagSpot(Vector2 p)
        {
            if (_town == null) return CommandRefusal.None;
            if (!_town.IsInTown(p) && !_town.WithinReachOfTown(p, CommandRules.FlagReach)) return CommandRefusal.FlagTooFar;
            var cell = GridMap.WorldToCell(p);
            foreach (var b in _town.Buildings)
                if (b != null && b.Footprint.Contains(cell)) return CommandRefusal.FlagIndoors;
            return CommandRefusal.None;
        }

        /// <summary>Who hears the order (into <see cref="_heard"/>); <see cref="CommandRefusal.NobodyNear"/> when nobody it is for does.</summary>
        CommandRefusal Gather(MasterCommand c, Unit target)
        {
            _heard.Clear();
            if (_roster == null) return CommandRefusal.NobodyNear;
            var me = _master.Position;
            const float r2 = CommandRules.Reach * CommandRules.Reach;
            bool someoneFighting = false;
            foreach (var a in _roster.Members)
            {
                if (!Member(a)) continue;
                // The flag is for the raid's defenders wherever they stand: they see it go up.
                if (c == MasterCommand.Rally)
                {
                    if (a.Fsm.Current == a.DefendState) _heard.Add(a);
                    continue;
                }
                if ((a.Position - me).sqrMagnitude > r2 || !Hears(c, a, target)) continue;
                _heard.Add(a);
                if (a.InTheThickOfIt) someoneFighting = true;
            }
            if (c == MasterCommand.Rally) return CommandRefusal.None; // planted all the same, for the next raid
            if (_heard.Count == 0 || c == MasterCommand.Cheer && !someoneFighting) return CommandRefusal.NobodyNear;
            return CommandRefusal.None;
        }

        static bool Member(Adventurer a)
            => a != null && a.IsAlive && a.IsGuildMember && !a.IsVisitor && a.Data != null
               && a.Fsm.Current != a.BreakState && a.Fsm.Current != a.DeadState;

        /// <summary>Whether the order is for this member, as it is now.</summary>
        static bool Hears(MasterCommand c, Adventurer a, Unit target)
        {
            var s = a.Fsm.Current;
            bool retreating = s == a.ReturnState && (a.ReturnState.Retreating || a.ReturnState.Luring);
            switch (c)
            {
                case MasterCommand.Cheer:
                    return true;
                case MasterCommand.Focus:
                    if (a.FallingBack || retreating || a.HpRatio < a.RetreatThreshold || target == null || !Combat.CanReach(a, target)) return false;
                    return Fights(a) || a.IsAtLeisureInTown;
                case MasterCommand.Retreat:
                    // Escorts stay by the master; one already running home needs no telling.
                    return !retreating && s != a.EscortState && (Fights(a) || s == a.LureState);
                default:
                    return false;
            }
        }

        /// <summary>A state that fights what comes (its own <c>HandleCombat</c>), out in the field or holding the town.</summary>
        static bool Fights(Adventurer a)
        {
            var s = a.Fsm.Current;
            return s == a.TravelState || s == a.HuntState || s == a.FreeHuntState || s == a.EscortState || s == a.ReturnState
                   || s == a.DefendState || s == a.TownGuardState || s == a.PlaceVisitState;
        }

        bool Follows(MasterCommand c, Adventurer a)
            => Certain || _rng.Chance(CommandRules.ObeyChance(c, ThoughtRecorder.MoodOf(a), a.Data.traitIds, a.Data.values));

        void Carry(MasterCommand c, Adventurer a, Unit target)
        {
            a.Say(CommandRules.ObeyLine(c, _rng.Range(0, 100)), false);
            switch (c)
            {
                case MasterCommand.Cheer:
                    a.Cheer(CommandRules.CheerShare, CommandRules.CheerSeconds);
                    ThoughtRecorder.Give(a, ThoughtRules.Cheered);
                    if (!CasualVfx.PlayIfSeen("Booster_ArrowUp", a.Position, 0.8f) && Services.TryGet<FxService>(out var fx))
                        fx.Spark(a.Position + new Vector2(0f, 0.6f), CheerColor, 0.6f, 0.4f);
                    break;
                case MasterCommand.Focus:
                    // Idling in town: it turns out as town guard to do it, and goes back to what it was doing after.
                    if (a.IsAtLeisureInTown)
                    {
                        a.TownGuardState.Next = a.Fsm.Current;
                        a.ChangeState(a.TownGuardState);
                    }
                    a.OrderTarget(target, CommandRules.FocusSeconds);
                    if (Services.TryGet<FxService>(out var beam)) beam.Beam(a.Position, target.Position, FocusColor, 0.06f, 0.35f);
                    break;
                case MasterCommand.Retreat:
                    a.RetreatOnOrder(Hours + CommandRules.HornHoldHours);
                    break;
            }
        }

        /// <summary>The master's call, seen: a ring as far as it carries, and the order's own mark.</summary>
        void Show(MasterCommand c, Vector2 at, Unit target)
        {
            if (!Services.TryGet<FxService>(out var fx)) return;
            var color = c switch
            {
                MasterCommand.Cheer => CheerColor,
                MasterCommand.Focus => FocusColor,
                MasterCommand.Rally => RallyColor,
                _ => RetreatColor,
            };
            if (c != MasterCommand.Rally) fx.Ring(at, CommandRules.Reach, color, 0.6f);
            if (c == MasterCommand.Focus && target != null && !CasualVfx.PlayIfSeen("Buff1_Red", target.Position, 0.9f))
                fx.Ring(target.Position, 1.1f, FocusColor, 0.5f);
            if (c == MasterCommand.Rally && !CasualVfx.PlayIfSeen("MagicCircle1_Yellow", at, 1f)) fx.Ring(at, 1.5f, RallyColor, 0.6f);
            if (c == MasterCommand.Retreat) CasualVfx.PlayIfSeen("Buff1_Blue", at, 1f);
        }

        // ---------------- The rally flag ----------------

        void Plant(Vector2 at)
        {
            _hasFlag = true;
            _flag = at;
            _flagUntil = Hours + CommandRules.FlagHours;
            _heedsFlag.Clear();
            _flagView.Plant(at);
            // A raid on now: its defenders turn to the flag (or not) at once.
            foreach (var a in _heard)
                if (FlagFor(a) == null) _refused.Add(a.Data.name);
        }

        void TakeDownFlag()
        {
            _hasFlag = false;
            _heedsFlag.Clear();
            _flagView.Take();
        }

        /// <summary>Debug: takes the flag down.</summary>
        public void ClearFlag() => TakeDownFlag();

        /// <summary>
        /// Where a defender holds the town (<see cref="DefendState"/>): the flag, while it stands and this member heeds it — each
        /// defender decides once a raid, the first time it asks, and says so.
        /// </summary>
        public Vector2? FlagFor(Adventurer a)
        {
            if (!HasFlag || a == null || a.Data == null) return null;
            if (!_heedsFlag.TryGetValue(a, out bool heeds))
            {
                heeds = Follows(MasterCommand.Rally, a);
                _heedsFlag[a] = heeds;
                a.Say(heeds ? CommandRules.ObeyLine(MasterCommand.Rally, _rng.Range(0, 100)) : CommandRules.RefuseLine(MasterCommand.Rally, _rng.Range(0, 100)), false);
            }
            return heeds ? _flag : (Vector2?)null;
        }

        // ---------------- The reach, shown while an order is being chosen ----------------

        void BuildReach()
        {
            var go = new GameObject("CommandReach");
            go.transform.SetParent(transform, false);
            _reach = go.AddComponent<SpriteRenderer>();
            int px = Mathf.RoundToInt(CommandRules.Reach * 2f * SpriteFactory.PixelsPerUnit);
            _reach.sprite = SpriteFactory.Circle(px, new Color(1f, 0.9f, 0.5f, 0.05f), new Color(1f, 0.9f, 0.5f, 0.75f));
            _reach.sortingOrder = 40;
            _reach.enabled = false;
        }

        /// <summary>Shows the master's reach while a button is hovered or a target is being picked (counted, so two can overlap).</summary>
        public void ShowReach(bool on)
        {
            _reachShown = Mathf.Max(0, _reachShown + (on ? 1 : -1));
            if (_reach == null) return;
            _reach.enabled = _reachShown > 0 && _master != null;
            if (_reach.enabled) _reach.transform.position = _master.Position;
        }

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SaveData
        {
            public float power;
            /// <summary>Game hours until each order is ready again, in <see cref="CommandRules.All"/>'s order.</summary>
            public List<float> ready_in = new();
            public bool flag;
            public Vector2 flag_at;
            public float flag_hours_left;
        }

        public string SaveKey => "commands";

        public string Capture()
        {
            var s = new SaveData { power = Mathf.Round(_power * 100f) / 100f, flag = HasFlag, flag_at = _flag, flag_hours_left = FlagHoursLeft };
            foreach (var c in CommandRules.All) s.ready_in.Add(HoursToReady(c));
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            float h = Hours;
            _lastHours = h;
            _power = Mathf.Clamp(s.power, 0f, CommandRules.MaxPower);
            for (int i = 0; i < _readyAt.Length; i++)
                _readyAt[i] = i < s.ready_in.Count && s.ready_in[i] > 0f ? h + s.ready_in[i] : float.NegativeInfinity;
            _heedsFlag.Clear();
            if (s.flag && s.flag_hours_left > 0f)
            {
                _hasFlag = true;
                _flag = s.flag_at;
                _flagUntil = h + s.flag_hours_left;
                _flagView.Plant(_flag);
            }
            else TakeDownFlag();
        }
    }
}
