using UnityEngine;

namespace GuildMaster.Session
{
    /// <summary>
    /// Player settings kept between runs (PlayerPrefs, P4-10): LLM on/off and the screen mode.
    /// Volume joins with the sound work (P4-9). Also carries what the title screen hands to the next
    /// scene load (a chosen seed, whether to show the title again).
    /// </summary>
    public static class GameSettings
    {
        const string LlmKey = "gm.llmEnabled";
        const string FullscreenKey = "gm.fullscreen";
        const string ResWidthKey = "gm.resWidth";
        const string ResHeightKey = "gm.resHeight";

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession()
        {
            PendingSeed = 0;
            ShowTitle = true;
            PaintGround = true;
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
