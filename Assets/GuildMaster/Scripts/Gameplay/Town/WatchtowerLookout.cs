using GuildMaster.Adventurers;
using GuildMaster.Audio;
using GuildMaster.Core;
using GuildMaster.Flares;
using GuildMaster.Fog;
using GuildMaster.Player;
using GuildMaster.Session;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>The guild master went up a watchtower (<see cref="Tower"/>) or came down (null).</summary>
    public struct LookoutChangedEvent { public Building Tower; }

    /// <summary>The alarm bell was rung from a watchtower: <see cref="Called"/> members were called back to town.</summary>
    public struct AlarmBellRungEvent { public Building Tower; public int Called; }

    /// <summary>
    /// What the guild master can do at a watchtower (user decision 2026-11-02: "감시탑 같은 오브젝트를 플레이어도 작동" —
    /// the lookout and the alarm bell). Pure numbers and rules, tested; <see cref="LookoutService"/> does it.
    /// </summary>
    public static class LookoutRules
    {
        /// <summary>The master's sight up on the tower, over walls (the tower alone sees 14, the master on foot 7).</summary>
        public const float SightRadius = 22f;
        /// <summary>Standing this close to the tower's edge counts as being up on it.</summary>
        public const float PostReach = 2.5f;
        /// <summary>A flare from the tower bursts at most this far from it (cells).</summary>
        public const float FlareRange = 30f;
        /// <summary>Seconds between two flares from the tower (combat seconds: they run with the game speed).</summary>
        public const float FlareCooldownSeconds = 40f;
        /// <summary>Members this far from the town (cells) hear the bell.</summary>
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
        /// fit enough to walk home, outside the walls but within <see cref="BellRadius"/>.
        /// </summary>
        public static bool Recalls(bool member, bool onQuest, bool atOwnBusiness, bool inTown, float distanceFromTown)
            => member && !onQuest && atOwnBusiness && !inTown && distanceFromTown <= BellRadius;

        /// <summary>"3시간", "40초" — what is left of a wait, rounded up.</summary>
        public static string Left(float amount, string unit) => $"{Mathf.CeilToInt(Mathf.Max(0f, amount))}{unit}";
    }

    /// <summary>
    /// The guild master at a watchtower (user decision 2026-11-02). Asked from the tower's window, the master walks to it
    /// and goes up: while there it sees <see cref="LookoutRules.SightRadius"/> cells round, over walls, can send a flare
    /// over any spot within <see cref="LookoutRules.FlareRange"/> (it lights the ground and lifts the fog, as a member's
    /// does) and can ring the alarm bell — members out on their own business come home and stay in town
    /// <see cref="LookoutRules.BellHoldHours"/> hours. Any move order, the tower falling or the master falling brings it
    /// down. Made on first use (<see cref="Ensure"/>); not saved — a load finds the master on the ground.
    /// </summary>
    public class LookoutService : MonoBehaviour
    {
        static LookoutService s_instance;

        public static LookoutService Ensure()
        {
            if (s_instance == null) s_instance = new GameObject("WatchtowerLookout").AddComponent<LookoutService>();
            return s_instance;
        }

        /// <summary>Members called in by the bell are to stay in town now (read by the town's idle members).</summary>
        public static bool HoldsInTown => s_instance != null && s_instance.BellHolding;

        /// <summary>The tower the master is up on, or null.</summary>
        public Building Post { get; private set; }
        /// <summary>The tower the master is walking to, or null.</summary>
        public Building Heading { get; private set; }

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

        // ---------------- Going up ----------------

        /// <summary>Why the master cannot go up this tower now, or null.</summary>
        public string CannotGoUp(Building tower)
        {
            if (tower == null || tower.Type != BuildingType.Watchtower) return "감시탑이 아닙니다.";
            if (tower.IsDestroyed) return "무너진 감시탑에는 올라갈 수 없습니다.";
            if (Master == null || !Master.IsAlive) return "길드 마스터가 움직일 수 없습니다.";
            if (Post == tower) return "이미 이 감시탑에서 망을 보고 있습니다.";
            return null;
        }

        /// <summary>Sends the master up the tower (walking there first). False when it cannot go.</summary>
        public bool GoUp(Building tower, out string error)
        {
            error = CannotGoUp(tower);
            if (error != null) return false;
            var avatar = Master;
            var edge = tower.ClosestPoint(avatar.Position);
            var away = avatar.Position - edge;
            var spot = away.sqrMagnitude < 1e-4f ? edge : edge + away.normalized * 0.8f;
            // MoveTo announces the order, which brings the master down from any other tower first.
            if (Vector2.Distance(avatar.Position, edge) <= LookoutRules.PostReach)
            {
                Man(tower);
                return true;
            }
            if (!avatar.MoveTo(spot, () => Man(tower), 0.3f))
            {
                error = "감시탑까지 갈 길이 없습니다.";
                return false;
            }
            Heading = tower;
            return true;
        }

        void Man(Building tower)
        {
            Heading = null;
            var avatar = Master;
            if (tower == null || tower.IsDestroyed || avatar == null || !avatar.IsAlive) return;
            if (Vector2.Distance(avatar.Position, tower.ClosestPoint(avatar.Position)) > LookoutRules.PostReach + 0.5f) return;
            if (Post != null) StandDown(quiet: true);
            _vision = avatar.GetComponent<VisionSource>();
            if (_vision != null)
            {
                _savedSight = _vision.SightRadius;
                _savedOverWalls = _vision.SeesOverWalls;
                _vision.SightRadius = Mathf.Max(_savedSight, LookoutRules.SightRadius);
                _vision.SeesOverWalls = true;
            }
            Post = tower;
            Log("길드 마스터가 감시탑에 올라 망을 봅니다.");
            EventBus.Publish(new LookoutChangedEvent { Tower = tower });
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
            if (!quiet) Log("길드 마스터가 감시탑에서 내려왔습니다.");
            EventBus.Publish(new LookoutChangedEvent { Tower = null });
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
            var avatar = Master;
            if (Post.IsDestroyed)
            {
                Log("감시탑이 무너져 길드 마스터가 내려왔습니다.");
                StandDown(quiet: true);
                return;
            }
            if (avatar == null || !avatar.IsAlive
                || Vector2.Distance(avatar.Position, Post.ClosestPoint(avatar.Position)) > LookoutRules.PostReach + 1f)
                StandDown();
        }

        // ---------------- The flare ----------------

        public float FlareCooldownLeft => Mathf.Max(0f, _flareReadyAt - Time.time);

        public string CannotFlare()
        {
            if (Post == null) return "감시탑에 올라가 있어야 쏠 수 있습니다.";
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

        /// <summary>Sends a flare from the tower over <paramref name="at"/> (as far as it reaches).</summary>
        public bool FireFlare(Vector2 at, out string error)
        {
            error = CannotFlare();
            if (error != null || !Services.TryGet<FlareService>(out var flares)) return false;
            var burst = LookoutRules.FlareAim(Post.Center, at);
            flares.Fire(Master, burst);
            _flareReadyAt = Time.time + LookoutRules.FlareCooldownSeconds;
            Log("감시탑에서 조명탄을 쏘아 올렸습니다.");
            return true;
        }

        // ---------------- The alarm bell ----------------

        public float BellCooldownHoursLeft => Mathf.Max(0f, _bellReadyAtHour - NowHours);
        public float HoldHoursLeft => Mathf.Max(0f, _holdUntilHour - NowHours);
        public bool BellHolding => HoldHoursLeft > 0f;

        public string CannotRing()
        {
            if (Post == null) return "감시탑에 올라가 있어야 칠 수 있습니다.";
            if (BellCooldownHoursLeft > 0f) return $"종을 다시 치려면 {LookoutRules.Left(BellCooldownHoursLeft, "시간")}";
            return null;
        }

        /// <summary>Rings the bell: members out on their own business nearby come home and stay a while. How many were called.</summary>
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
            EventBus.Publish(new AlarmBellRungEvent { Tower = Post, Called = called });
            return called;
        }

        static void Log(string text)
        {
            if (Services.TryGet<GameLog>(out var log)) log.Add(text, LogCategory.Info);
        }
    }
}
