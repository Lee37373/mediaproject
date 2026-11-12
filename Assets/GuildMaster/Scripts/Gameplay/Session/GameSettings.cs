using UnityEngine;

namespace GuildMaster.Session
{
    /// <summary>
    /// Player settings kept between runs (PlayerPrefs, P4-10): LLM on/off, the screen mode, (P4-9) the volumes and (P6-3)
    /// the calmer effects and the moments that pause the game. Also carries what the title screen hands to the next
    /// scene load (a chosen seed, whether to show the title again).
    /// </summary>
    public static class GameSettings
    {
        const string LlmKey = "gm.llmEnabled";
        const string FullscreenKey = "gm.fullscreen";
        const string ResWidthKey = "gm.resWidth";
        const string ResHeightKey = "gm.resHeight";
        const string RewardFxKey = "gm.rewardFx";

        /// <summary>Reward effects (P4-106 H8): coins flying to the gold, building sparkles, the rank banner, level-up pillars.</summary>
        public static bool RewardEffects
        {
            get => PlayerPrefs.GetInt(RewardFxKey, 1) == 1;
            set { PlayerPrefs.SetInt(RewardFxKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        const string NewsKey = "gm.newsEachMorning";

        /// <summary>The town newspaper (P4-130) opens by itself each morning and the game waits while it is open.</summary>
        public static bool NewsEachMorning
        {
            get => PlayerPrefs.GetInt(NewsKey, 1) == 1;
            set { PlayerPrefs.SetInt(NewsKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        // ---------------- Calmer effects and events (P6-3, user decisions 2026-11-10) ----------------
        // Read every frame by many units, so each value is kept in memory and written through to PlayerPrefs.

        const string ShakeKey = "gm.shake";
        const string HitStopKey = "gm.hitStop";
        const string CalmFlashKey = "gm.calmFlashes";
        const string DilemmaOpenKey = "gm.dilemmaAutoOpen";
        const string PauseKeyPrefix = "gm.pause.";

        static float? _shake;
        static bool? _hitStop, _calmFlashes;

        /// <summary>Screen shake strength, 0 (none) .. 1 (as designed).</summary>
        public static float ShakeStrength
        {
            get => _shake ??= Mathf.Clamp01(PlayerPrefs.GetFloat(ShakeKey, 1f));
            set { _shake = Mathf.Clamp01(value); PlayerPrefs.SetFloat(ShakeKey, _shake.Value); PlayerPrefs.Save(); }
        }

        /// <summary>The brief near-freeze on the guild's critical hits (P4-8).</summary>
        public static bool HitStop
        {
            get => _hitStop ??= PlayerPrefs.GetInt(HitStopKey, 1) == 1;
            set { _hitStop = value; PlayerPrefs.SetInt(HitStopKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>
        /// Softer flashes: critical hits look like plain ones (no flare), the red hit tint is faint, a struck village
        /// barely reddens, and the relationship graph's recent lines glow steadily instead of blinking.
        /// </summary>
        public static bool CalmFlashes
        {
            get => _calmFlashes ??= PlayerPrefs.GetInt(CalmFlashKey, 0) == 1;
            set { _calmFlashes = value; PlayerPrefs.SetInt(CalmFlashKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>A new decision card opens its window by itself and the game waits (else only the notification).</summary>
        public static bool DilemmaAutoOpen
        {
            get => PlayerPrefs.GetInt(DilemmaOpenKey, 1) == 1;
            set { PlayerPrefs.SetInt(DilemmaOpenKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Whether the game pauses by itself when this kind of moment happens (off for all by default).</summary>
        public static bool PauseOn(PauseTrigger trigger) => PlayerPrefs.GetInt(PauseKeyPrefix + trigger, 0) == 1;

        public static void SetPauseOn(PauseTrigger trigger, bool on)
        {
            PlayerPrefs.SetInt(PauseKeyPrefix + trigger, on ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>The effects tab's "모두 줄이기": no shake, no hit-stop, soft flashes, no reward effects.</summary>
        public static void CalmAllEffects()
        {
            ShakeStrength = 0f;
            HitStop = false;
            CalmFlashes = true;
            RewardEffects = false;
        }

        /// <summary>The effects tab's "기본값으로".</summary>
        public static void DefaultEffects()
        {
            ShakeStrength = 1f;
            HitStop = true;
            CalmFlashes = false;
            RewardEffects = true;
        }

        public static bool LlmEnabled
        {
            get => PlayerPrefs.GetInt(LlmKey, 1) == 1;
            set { PlayerPrefs.SetInt(LlmKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool Fullscreen
        {
            get => PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) == 1;
            set { PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0); PlayerPrefs.Save(); ApplyScreen(); }
        }

        /// <summary>Saved resolution, or the current one when nothing was chosen yet.</summary>
        public static Vector2Int Resolution
        {
            get => new(PlayerPrefs.GetInt(ResWidthKey, Screen.width), PlayerPrefs.GetInt(ResHeightKey, Screen.height));
            set
            {
                PlayerPrefs.SetInt(ResWidthKey, value.x);
                PlayerPrefs.SetInt(ResHeightKey, value.y);
                PlayerPrefs.Save();
                ApplyScreen();
            }
        }

        // ---------------- Volume (P4-9, 2026-10-31) ----------------

        const string SfxKey = "gm.sfxVolume";
        const string MusicKey = "gm.musicVolume";

        /// <summary>Raised when a volume changes, so the sound service can follow at once.</summary>
        public static event System.Action VolumeChanged;

        /// <summary>Effects volume, 0..1.</summary>
        public static float SfxVolume
        {
            get => PlayerPrefs.GetFloat(SfxKey, 0.8f);
            set { PlayerPrefs.SetFloat(SfxKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); VolumeChanged?.Invoke(); }
        }

        /// <summary>Music volume, 0..1.</summary>
        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(MusicKey, 0.6f);
            set { PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); VolumeChanged?.Invoke(); }
        }

        /// <summary>Applies the saved screen mode (no effect inside the editor's Game view).</summary>
        public static void ApplyScreen()
        {
            if (Application.isEditor) return;
            var r = Resolution;
            Screen.SetResolution(r.x, r.y, Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        }

        /// <summary>Leaves the game (stops play mode inside the editor).</summary>
        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Reloads the game scene: a fresh world (with <see cref="PendingSeed"/> if set).</summary>
        public static void ReloadScene(bool showTitle)
        {
            ShowTitle = showTitle;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEngine.SceneManagement.SceneManager.LoadScene(scene.name);
        }

        // ---------------- Scene hand-over (title → next load) ----------------

        /// <summary>Seed for the next scene load (0 = random / balance asset).</summary>
        public static int PendingSeed { get; set; }

        /// <summary>Show the title screen on the next load (false after "new game" / restart).</summary>
        public static bool ShowTitle { get; set; } = true;

        /// <summary>
        /// Paint the cartoon ground (~9 s on the 256 map). The smoke tests turn it off: they boot the
        /// game forty-odd times and never look at the ground, so it made up most of their run time.
        /// </summary>
        public static bool PaintGround { get; set; } = true;

        /// <summary>
        /// Paint the full-detail ground chunks where the fog lifts (P4-81). The smoke tests turn it off (P4-125): they
        /// keep only the low-detail backdrop (~0.4 s a boot), so a watched run shows ground instead of black.
        /// </summary>
        public static bool GroundDetail { get; set; } = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession()
        {
            PendingSeed = 0;
            ShowTitle = true;
            PaintGround = true;
            GroundDetail = true;
            PendingLoad = null;
            PendingLoadSlot = -1;
            Restoring = null;
        }

        /// <summary>A saved game for the next scene load to put back (P5-4, see <c>Save.SaveService.Load</c>).</summary>
        public static Save.SaveFile PendingLoad { get; set; }
        public static int PendingLoadSlot { get; set; } = -1;

        /// <summary>
        /// The save being put back while the session boots (null otherwise). Systems skip their own
        /// first filling (starting members, the nests' first monsters…) when the save has their section.
        /// </summary>
        public static Save.SaveFile Restoring { get; set; }

        /// <summary>The boot is restoring a save that holds this section: do not fill it fresh.</summary>
        public static bool RestoresSection(string key) => Restoring != null && Restoring.Has(key);
    }
}
