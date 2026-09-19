using UnityEngine;

namespace GuildMaster.Core
{
    /// <summary>
    /// Full-screen menus (title, pause, settings — P4-10) register here while open so game hotkeys
    /// and window shortcuts stay quiet underneath them.
    /// </summary>
    public static class MenuState
    {
        static int s_open;

        public static bool IsOpen => s_open > 0;

        public static void Push() => s_open++;
        public static void Pop() => s_open = Mathf.Max(0, s_open - 1);

        /// <summary>A scene (re)load destroys every menu without popping: start from zero.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset() => s_open = 0;
    }
}
