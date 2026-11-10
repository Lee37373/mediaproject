using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Director;
using GuildMaster.Disasters;
using GuildMaster.Dragons;
using GuildMaster.Town;
using UnityEngine;

namespace GuildMaster.Session
{
    /// <summary>The kinds of moment that may pause the game by themselves (P6-3, settings' events tab).</summary>
    public enum PauseTrigger { Raid, Dragon, StrongFoe, MemberDanger, MemberDeath, MentalBreak, Disaster }

    /// <summary>The game paused itself for a moment (P6-3).</summary>
    public struct EventPausedEvent { public PauseTrigger Trigger; public string Reason; }

    /// <summary>
    /// Pauses the game when a moment the player chose happens (P6-3, user decisions 2026-11-10: "그대로 / 일시정지" per
    /// moment, all "그대로" at first; a slow-down was dropped because the release only runs at x1). Only what the guild
    /// knows: the moments come from the spectator director's highlights (seen fights and crises) and from the same events
    /// the notifications announce. The player resumes as after any pause (Space, 1, the speed buttons).
    /// </summary>
    public class EventPause : MonoBehaviour
    {
        /// <summary>Off in the smoke tests (a player's saved choice would stop a test's clock).</summary>
        public static bool Enabled = true;

        /// <summary>After pausing for one kind of moment, the same kind waits this many game hours of play before it pauses again.</summary>
        public const float RepeatHours = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Enabled = true;

        GameClock _clock;
        readonly float[] _lastPause = new float[System.Enum.GetValues(typeof(PauseTrigger)).Length];

        /// <summary>Why the game stands still, while it is this pause (cleared once the game runs again), else null.</summary>
        public string Reason { get; private set; }
        public PauseTrigger Trigger { get; private set; }

        public void Init(GameClock clock)
        {
            _clock = clock;
            for (int i = 0; i < _lastPause.Length; i++) _lastPause[i] = float.NegativeInfinity;
            EventBus.Subscribe<HighlightStartedEvent>(OnHighlight);
            EventBus.Subscribe<RaidSightedEvent>(OnRaid);
            EventBus.Subscribe<DragonSightedEvent>(OnDragon);
            EventBus.Subscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Subscribe<MentalBreakEvent>(OnBreak);
            EventBus.Subscribe<DisasterSightedEvent>(OnDisaster);
            EventBus.Subscribe<GameSpeedChangedEvent>(OnSpeed);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<HighlightStartedEvent>(OnHighlight);
            EventBus.Unsubscribe<RaidSightedEvent>(OnRaid);
            EventBus.Unsubscribe<DragonSightedEvent>(OnDragon);
            EventBus.Unsubscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Unsubscribe<MentalBreakEvent>(OnBreak);
            EventBus.Unsubscribe<DisasterSightedEvent>(OnDisaster);
            EventBus.Unsubscribe<GameSpeedChangedEvent>(OnSpeed);
        }

        // ---------------- Words ----------------

        /// <summary>The events tab's row name.</summary>
        public static string Label(PauseTrigger t) => t switch
        {
            PauseTrigger.Raid => "침공",
            PauseTrigger.Dragon => "드래곤",
            PauseTrigger.StrongFoe => "강적과의 싸움",
            PauseTrigger.MemberDanger => "길드원 위기",
            PauseTrigger.MemberDeath => "길드원 사망",
            PauseTrigger.MentalBreak => "정신적 붕괴",
            _ => "재해",
        };

        /// <summary>Two letters beside the HUD clock while paused for it.</summary>
        public static string Short(PauseTrigger t) => t switch
        {
            PauseTrigger.Raid => "침공",
            PauseTrigger.Dragon => "드래곤",
            PauseTrigger.StrongFoe => "강적",
            PauseTrigger.MemberDanger => "위기",
            PauseTrigger.MemberDeath => "사망",
            PauseTrigger.MentalBreak => "붕괴",
            _ => "재해",
        };

        /// <summary>What counts, for the row's tooltip.</summary>
        public static string Describe(PauseTrigger t) => t switch
        {
            PauseTrigger.Raid => "마을로 오는 침공을 발견했을 때, 성문이 뚫렸을 때, 대규모 무리가 마을에 왔을 때",
            PauseTrigger.Dragon => "드래곤이 보일 때, 길드원이 드래곤과 싸우기 시작할 때, 드래곤이 마을 위에 왔을 때",
            PauseTrigger.StrongFoe => "길드원이 지대 군주·원수·희귀 몬스터·우두머리와 싸우기 시작할 때",
            PauseTrigger.MemberDanger => "싸우는 길드원의 체력이 바닥났을 때, 여럿이 함께 쓰러질 위기일 때",
            PauseTrigger.MemberDeath => "길드원이 죽었을 때",
            PauseTrigger.MentalBreak => "길드원이 정신적으로 무너졌을 때",
            _ => "자연재해가 번지는 것을 보았을 때",
        };

        /// <summary>Which moment of the spectator director counts as which (none for a last stand or a retreat: they are over).</summary>
        public static PauseTrigger? Of(HighlightKind k) => k switch
        {
            HighlightKind.Dragon or HighlightKind.DragonOverTown => PauseTrigger.Dragon,
            HighlightKind.Lord or HighlightKind.Nemesis or HighlightKind.Rare or HighlightKind.Boss => PauseTrigger.StrongFoe,
            HighlightKind.LowHp or HighlightKind.Wipe => PauseTrigger.MemberDanger,
            // The fight at the gate is the raid already paused for when it was sighted.
            HighlightKind.Horde or HighlightKind.GateBroken => PauseTrigger.Raid,
            _ => null,
        };

        // ---------------- Events ----------------

        void OnHighlight(HighlightStartedEvent e)
        {
            if (e.Highlight != null && Of(e.Highlight.Kind) is { } t) Pause(t, e.Highlight.Title);
        }

        void OnRaid(RaidSightedEvent e) => Pause(PauseTrigger.Raid, $"침공 — {e.Direction}쪽에서 적 무리");
        void OnDragon(DragonSightedEvent e) => Pause(PauseTrigger.Dragon, e.First ? "드래곤 발견" : "드래곤 출현");

        void OnDied(AdventurerDiedEvent e)
        {
            if (e.Adventurer?.Data != null) Pause(PauseTrigger.MemberDeath, $"{e.Adventurer.Data.name} 사망");
        }

        // Like the notification (NotificationSources.OnBreak): the start of a break, not a member quitting.
        void OnBreak(MentalBreakEvent e)
        {
            if (e.Stage == MentalBreakEvent.Phase.Began && e.Adventurer?.Data != null && e.Kind != BreakKind.Quit)
                Pause(PauseTrigger.MentalBreak, $"{e.Adventurer.Data.name} 정신적 붕괴");
        }

        void OnDisaster(DisasterSightedEvent e)
        {
            if (e.Disaster != null) Pause(PauseTrigger.Disaster, $"{e.Disaster.Name} 발생");
        }

        void OnSpeed(GameSpeedChangedEvent e)
        {
            if (e.SpeedIndex != 0) Reason = null;
        }

        /// <summary>Pauses for a moment when the player chose so (public for tests).</summary>
        public bool Pause(PauseTrigger t, string reason)
        {
            if (!Enabled || _clock == null || _clock.Frozen || !GameSettings.PauseOn(t)) return false;
            // The player's own pause stays theirs.
            if (_clock.SpeedIndex == 0 && Reason == null) return false;
            float now = _clock.TotalHours;
            if (now - _lastPause[(int)t] < RepeatHours) return false;
            _lastPause[(int)t] = now;
            _clock.SetSpeed(0);
            Trigger = t;
            Reason = reason;
            EventBus.Publish(new EventPausedEvent { Trigger = t, Reason = reason });
            return true;
        }
    }
}
