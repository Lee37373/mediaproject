using GuildMaster.Adventurers;
using GuildMaster.Audio;
using GuildMaster.Core;
using GuildMaster.Flares;
using GuildMaster.Fog;
using GuildMaster.Places;
using GuildMaster.Player;
using GuildMaster.Session;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>The guild master went up a ruined watchtower (<see cref="Ruin"/>) or came down (null).</summary>
    public struct LookoutChangedEvent { public PlaceSite Ruin; }

    /// <summary>The alarm bell was rung from a ruined watchtower: <see cref="Called"/> members were called back to town.</summary>
    public struct AlarmBellRungEvent { public PlaceSite Ruin; public int Called; }

    /// <summary>
    /// What the guild master can do up a ruined watchtower (user decisions 2026-11-02: "상호작용이 필요한 감시탑은 폐허
    /// 감시탑" — the lookout, a flare and the alarm bell; the watchtower the guild builds only widens the town's sight, as
    /// before). Pure numbers and rules, tested; <see cref="LookoutService"/> does it.
    /// </summary>
    public static class LookoutRules
    {
        /// <summary>The master's sight up the ruin, over walls and trees: what a member sees from it once (the master on foot: 7).</summary>
        public const float SightRadius = PlaceRules.TowerSightRadius;
        /// <summary>Standing this close to the ruin counts as being up on it.</summary>
        public const float PostReach = 2.5f;
        /// <summary>A flare from the ruin bursts at most this far from it (cells).</summary>
        public const float FlareRange = 30f;
        /// <summary>Seconds between two flares (combat seconds: they run with the game speed).</summary>
        public const float FlareCooldownSeconds = 40f;
        /// <summary>Members this far from the town (cells) answer the bell.</summary>
        public const float BellRadius = 70f;
        /// <summary>Game hours those called in stay in town before going out again.</summary>
        public const float BellHoldHours = 3f;
        /// <summary>Game hours before the bell can be rung again.</summary>
        public const float BellCooldownHours = 8f;

        /// <summary>Where a flare aimed at <paramref name="picked"/> bursts: there, or as far as it reaches that way.</summary>
        public static Vector2 FlareAim(Vector2 tower, Vector2 picked)
        {
            var d = picked - tower;
            return d.sqrMagnitude <= FlareRange * FlareRange ? picked : tower + d.normalized * FlareRange;
        }

        /// <summary>
        /// Whether the bell calls this member back: a guild member at its own business (no quest — a contract is kept),
        /// outside the walls but within <see cref="BellRadius"/> of the town.
        /// </summary>
        public static bool Recalls(bool member, bool onQuest, bool atOwnBusiness, bool inTown, float distanceFromTown)
            => member && !onQuest && atOwnBusiness && !inTown && distanceFromTown <= BellRadius;

        /// <summary>"3시간", "40초" — what is left of a wait, rounded up.</summary>
        public static string Left(float amount, string unit) => $"{Mathf.CeilToInt(Mathf.Max(0f, amount))}{unit}";
    }

    /// <summary>
    /// The guild master up a ruined watchtower (user decisions 2026-11-02). Asked from the ruin's window, the master walks there
    /// and climbs it: the first climb lifts the fog <see cref="PlaceRules.TowerSightRadius"/> round, as a scout's does (and
    /// the scouts need not go any more); while up, the master sees that far, can send a flare over any spot within
    /// <see cref="LookoutRules.FlareRange"/>, and can ring the alarm bell — members out on their own business near the town come
    /// home and stay in <see cref="LookoutRules.BellHoldHours"/> hours. Any move order or the master falling brings it down.
    /// Made on first use (<see cref="Ensure"/>); not saved — a load finds the master on the ground.
    /// </summary>
    public class LookoutService : MonoBehaviour
    {
        static LookoutService s_instance;

        public static LookoutService Ensure()
        {
            if (s_instance == null) s_instance = new GameObject("RuinLookout").AddComponent<LookoutService>();
            return s_instance;
        }

        /// <summary>Members called in by the bell are to stay in town now (read by the town's idle members).</summary>
        public static bool HoldsInTown => s_instance != null && s_instance.BellHolding;

        /// <summary>The ruin the master is up on, or null.</summary>
        public PlaceSite Post { get; private set; }
        /// <summary>The ruin the master is walking to, or null.</summary>
        public PlaceSite Heading { get; private set; }

        PlayerAvatar _avatar;
        VisionSource _vision;
        float _savedSight;
        bool _savedOverWalls;
        float _flareReadyAt;
        float _bellReadyAtHour = float.MinValue;
        float _holdUntilHour = float.MinValue;

        void Awake()
        {
            EventBus.Subscribe<PlayerMoveOrderedEvent>(OnMoveOrdered);
            EventBus.Subscribe<PlayerDownedEvent>(OnDowned);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<PlayerMoveOrderedEvent>(OnMoveOrdered);
            EventBus.Unsubscribe<PlayerDownedEvent>(OnDowned);
            if (s_instance == this) s_instance = null;
        }

        static float NowHours => Services.TryGet<GameClock>(out var c) ? c.TotalHours : 0f;

        PlayerAvatar Master => _avatar != null ? _avatar : _avatar = Services.TryGet<PlayerAvatar>(out var a) ? a : null;

        public static bool IsRuin(PlaceSite p) => p != null && p.Kind == PlaceKind.WatchRuin;

        // ---------------- Going up ----------------

        /// <summary>Why the master cannot go up this ruin now, or null.</summary>
        public string CannotGoUp(PlaceSite ruin)
        {
            if (!IsRuin(ruin)) return "폐허 감시탑이 아닙니다.";
            if (Master == null || !Master.IsAlive) return "길드 마스터가 움직일 수 없습니다.";
            if (Post == ruin) return "이미 이 감시탑에서 망을 보고 있습니다.";
            return null;
        }

        /// <summary>Sends the master up the ruin (walking there first). False when it cannot go.</summary>
        public bool GoUp(PlaceSite ruin, out string error)
        {
            error = CannotGoUp(ruin);
            if (error != null) return false;
            var master = Master;
            if (Vector2.Distance(master.Position, ruin.WorldPosition) <= LookoutRules.PostReach)
            {
                Man(ruin);
                return true;
            }
            // MoveTo announces the order, which brings the master down from any other ruin first.
            if (!master.MoveTo(ruin.WorldPosition, () => Man(ruin), 0.8f))
            {
                error = "폐허 감시탑까지 갈 길이 없습니다.";
                return false;
            }
            Heading = ruin;
            return true;
        }

        void Man(PlaceSite ruin)
        {
            Heading = null;
            var master = Master;
            if (!IsRuin(ruin) || master == null || !master.IsAlive) return;
            if (Vector2.Distance(master.Position, ruin.WorldPosition) > LookoutRules.PostReach + 0.5f) return;
            if (Post != null) StandDown(quiet: true);
            // The first climb shows the land round at once, as a scout's does — and no scout need go any more.
            bool first = Services.TryGet<PlaceService>(out var places) && !places.IsClimbed(ruin);
            if (first)
            {
                places.MarkClimbed(ruin);
                if (Services.TryGet<FogOfWar>(out var fog)) fog.Reveal(ruin.WorldPosition, PlaceRules.TowerSightRadius);
            }
            _vision = master.GetComponent<VisionSource>();
            if (_vision != null)
            {
                _savedSight = _vision.SightRadius;
                _savedOverWalls = _vision.SeesOverWalls;
                _vision.SightRadius = Mathf.Max(_savedSight, LookoutRules.SightRadius);
                _vision.SeesOverWalls = true;
            }
            Post = ruin;
            Log(first ? "길드 마스터가 폐허 감시탑에 올라 둘레를 처음으로 살폈습니다." : "길드 마스터가 폐허 감시탑에 올라 망을 봅니다.");
            EventBus.Publish(new LookoutChangedEvent { Ruin = ruin });
        }

        /// <summary>Brings the master down (its sight back to its own).</summary>
        public void StandDown(bool quiet = false)
        {
            Heading = null;
            if (Post == null) return;
            if (_vision != null)
            {
                _vision.SightRadius = _savedSight;
                _vision.SeesOverWalls = _savedOverWalls;
            }
            _vision = null;
            Post = null;
            if (!quiet) Log("길드 마스터가 폐허 감시탑에서 내려왔습니다.");
            EventBus.Publish(new LookoutChangedEvent { Ruin = null });
        }

        void OnMoveOrdered(PlayerMoveOrderedEvent e)
        {
            Heading = null;
            if (Post != null) StandDown();
        }

        void OnDowned(PlayerDownedEvent e) => StandDown(quiet: true);

        void Update()
        {
            if (Post == null) return;
            var master = Master;
            if (master == null || !master.IsAlive || Vector2.Distance(master.Position, Post.WorldPosition) > LookoutRules.PostReach + 1f)
                StandDown();
        }

        // ---------------- The flare ----------------

        public float FlareCooldownLeft => Mathf.Max(0f, _flareReadyAt - Time.time);

        public string CannotFlare()
        {
            if (Post == null) return "폐허 감시탑에 올라가 있어야 쏠 수 있습니다.";
            if (!Services.TryGet<FlareService>(out _)) return "조명탄을 쏠 수 없습니다.";
            if (FlareCooldownLeft > 0f) return $"다음 조명탄까지 {LookoutRules.Left(FlareCooldownLeft, "초")}";
            return null;
        }

        /// <summary>Asks for a spot on the map, then sends a flare over it.</summary>
        public bool AimFlare(out string error)
        {
            error = CannotFlare();
            if (error != null) return false;
            if (!Services.TryGet<MapPicker>(out var picker))
            {
                error = "지도를 고를 수 없습니다.";
                return false;
            }
            picker.Begin($"조명탄을 쏠 곳을 클릭하세요 (감시탑에서 {LookoutRules.FlareRange:0}칸 안 · 우클릭/Esc 취소)", p => FireFlare(p, out _));
            return true;
        }

        /// <summary>Sends a flare from the ruin over <paramref name="at"/> (as far as it reaches).</summary>
        public bool FireFlare(Vector2 at, out string error)
        {
            error = CannotFlare();
            if (error != null || !Services.TryGet<FlareService>(out var flares)) return false;
            var burst = LookoutRules.FlareAim(Post.WorldPosition, at);
            flares.Fire(Master, burst);
            _flareReadyAt = Time.time + LookoutRules.FlareCooldownSeconds;
            Log("폐허 감시탑에서 조명탄을 쏘아 올렸습니다.");
            return true;
        }

        // ---------------- The alarm bell ----------------

        public float BellCooldownHoursLeft => Mathf.Max(0f, _bellReadyAtHour - NowHours);
        public float HoldHoursLeft => Mathf.Max(0f, _holdUntilHour - NowHours);
        public bool BellHolding => HoldHoursLeft > 0f;

        public string CannotRing()
        {
            if (Post == null) return "폐허 감시탑에 올라가 있어야 칠 수 있습니다.";
            if (BellCooldownHoursLeft > 0f) return $"종을 다시 치려면 {LookoutRules.Left(BellCooldownHoursLeft, "시간")}";
            return null;
        }

        /// <summary>Rings the bell: members out on their own business near the town come home and stay a while. How many were called.</summary>
        public int RingBell(out string error)
        {
            error = CannotRing();
            if (error != null) return 0;
            int called = 0;
            if (Services.TryGet<GuildRoster>(out var roster) && Services.TryGet<WorldService>(out var world))
            {
                var centre = world.TownCenterWorld;
                foreach (var a in roster.Members)
                {
                    if (a == null || !a.IsAlive || a.Fsm == null) continue;
                    var s = a.Fsm.Current;
                    bool ownBusiness = s == a.FreeHuntState || s == a.PlaceVisitState || s == a.IdleState || s == a.DecidingState;
                    float d = Vector2.Distance(world.OnTheSurface(a.Position), centre);
                    if (!LookoutRules.Recalls(a.IsGuildMember && !a.IsVisitor, a.CurrentQuest != null, ownBusiness,
                                              world.Grid.IsInTown(a.Position), d)) continue;
                    a.ChangeState(a.ReturnState);
                    a.Say("종소리다! 마을로 돌아가자.");
                    called++;
                }
            }
            float now = NowHours;
            _bellReadyAtHour = now + LookoutRules.BellCooldownHours;
            _holdUntilHour = now + LookoutRules.BellHoldHours;
            Sound.Play(SoundKeys.DayStart);
            Log(called > 0
                ? $"경종을 울렸습니다! 밖에 있던 길드원 {called}명이 마을로 돌아옵니다. {LookoutRules.BellHoldHours:0}시간 동안 마을을 지킵니다."
                : $"경종을 울렸습니다. 불러들일 길드원은 없었고, {LookoutRules.BellHoldHours:0}시간 동안 마을의 길드원이 밖으로 나가지 않습니다.");
            EventBus.Publish(new AlarmBellRungEvent { Ruin = Post, Called = called });
            return called;
        }

        static void Log(string text)
        {
            if (Services.TryGet<GameLog>(out var log)) log.Add(text, LogCategory.Info);
        }
    }
}
