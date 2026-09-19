using UnityEngine;

namespace GuildMaster.Core
{
    public struct HourPassedEvent { public int Day; public int Hour; }
    public struct DayPassedEvent { public int Day; }
    public struct GameSpeedChangedEvent { public int SpeedIndex; public float Scale; }

    /// <summary>
    /// In-game calendar. Game time advances with scaled time, so pausing/speeding
    /// the game is done through Time.timeScale (see SetSpeed).
    /// </summary>
    public class GameClock : MonoBehaviour
    {
        public static readonly float[] SpeedScales = { 0f, 1f, 2f, 4f };

        [SerializeField] float realSecondsPerGameHour = 10f;
        [SerializeField] int startHour = 8;

        float _totalHours;
        int _lastWholeHour;

        public int Day => Mathf.FloorToInt(_totalHours / 24f) + 1;
        public float HourOfDay => _totalHours % 24f;
        public int Hour => Mathf.FloorToInt(HourOfDay);
        public int Minute => Mathf.FloorToInt((HourOfDay - Hour) * 60f);
        public float TotalHours => _totalHours;
        public float RealSecondsPerGameHour => realSecondsPerGameHour;
        public int SpeedIndex { get; private set; } = 1;
        public bool IsNight => HourOfDay < 6f || HourOfDay >= 20f;

        /// <summary>0 at midnight, 1 at noon.</summary>
        public float Daylight => 0.5f - 0.5f * Mathf.Cos(HourOfDay / 24f * Mathf.PI * 2f);

        public void Configure(float secondsPerHour, int hourAtStart)
        {
            realSecondsPerGameHour = Mathf.Max(0.1f, secondsPerHour);
            startHour = hourAtStart;
            _totalHours = startHour;
            _lastWholeHour = Mathf.FloorToInt(_totalHours);
        }

        void Awake()
        {
            _totalHours = startHour;
            _lastWholeHour = startHour;
        }

        void Update()
        {
            _totalHours += Time.deltaTime / realSecondsPerGameHour;
            int whole = Mathf.FloorToInt(_totalHours);
            while (_lastWholeHour < whole)
            {
                _lastWholeHour++;
                int day = _lastWholeHour / 24 + 1;
                int hour = _lastWholeHour % 24;
                EventBus.Publish(new HourPassedEvent { Day = day, Hour = hour });
                if (hour == 0) EventBus.Publish(new DayPassedEvent { Day = day });
            }
        }

        /// <summary>Converts game hours to real (scaled) seconds.</summary>
        public float HoursToSeconds(float hours) => hours * realSecondsPerGameHour;

        /// <summary>When set (game over) the clock stays paused and speed changes are ignored.</summary>
        public bool Frozen { get; private set; }

        public void Freeze()
        {
            SetSpeed(0);
            Frozen = true;
        }

        public void SetSpeed(int index)
        {
            if (Frozen) return;
            SpeedIndex = Mathf.Clamp(index, 0, SpeedScales.Length - 1);
            ApplyScale();
        }

        int _holds;

        /// <summary>True while something (e.g. a dialogue window) holds the game paused.</summary>
        public bool IsHeld => _holds > 0;

        /// <summary>
        /// Pauses the game until the matching <see cref="Release"/>, keeping the chosen speed
        /// (speed buttons still change it; it applies once released).
        /// </summary>
        public void Hold()
        {
            _holds++;
            ApplyScale();
        }

        public void Release()
        {
            if (_holds == 0) return;
            _holds--;
            ApplyScale();
        }

        void ApplyScale()
        {
            Time.timeScale = _holds > 0 ? 0f : SpeedScales[SpeedIndex];
            EventBus.Publish(new GameSpeedChangedEvent { SpeedIndex = SpeedIndex, Scale = Time.timeScale });
        }

        public void TogglePause() => SetSpeed(SpeedIndex == 0 ? 1 : 0);

        public string FormatTime() => $"{Day}일차 {Hour:00}:{Minute:00}";

        void OnDestroy() => Time.timeScale = 1f;
    }
}
