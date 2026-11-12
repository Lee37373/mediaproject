using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GuildMaster.LLM
{
    /// <summary>
    /// Works around llama.cpp failing on Windows when user-profile paths contain non-ASCII
    /// characters (e.g. a Korean user name): it resolves its cache directory from
    /// %LOCALAPPDATA% and fails converting it ("No mapping for the Unicode character...").
    /// We point LLAMA_CACHE at an ASCII-only directory before the native library loads.
    /// The native DLLs use the shared UCRT, whose getenv does not see .NET's
    /// Environment.SetEnvironmentVariable, so we also call ucrtbase's _wputenv_s.
    /// </summary>
    public static class LlamaEnvironment
    {
        [DllImport("ucrtbase.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        static extern int _wputenv_s(string name, string value);

        public static string CacheDirectory { get; private set; }

        /// <summary>
        /// A Windows player installed under a path with non-ASCII characters (P5-5: e.g. a Korean desktop folder): the model in
        /// StreamingAssets may not load, so the title and the HUD say so and the game plays on its rules.
        /// </summary>
        public static bool InstallPathNotAscii
            => Application.platform == RuntimePlatform.WindowsPlayer && !IsAscii(Application.streamingAssetsPath);

        /// <summary>What the title screen tells the player when <see cref="InstallPathNotAscii"/>.</summary>
        public const string InstallPathWarning =
            "게임 폴더 경로에 한글 등 영문이 아닌 글자가 있어 AI 대화(로컬 LLM)가 동작하지 않을 수 있습니다.\n" +
            "영문만 있는 경로(예: C:\\Games\\GuildMaster)로 폴더를 옮겨 실행하세요. 그대로 하면 규칙 응답으로 진행됩니다.";

        public static bool IsAscii(string s)
        {
            if (string.IsNullOrEmpty(s)) return true;
            foreach (char c in s) if (c > 127) return false;
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Prepare()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor && Application.platform != RuntimePlatform.WindowsPlayer) return;
            try
            {
                string current = Environment.GetEnvironmentVariable("LLAMA_CACHE");
                if (!string.IsNullOrEmpty(current) && IsAscii(current)) { CacheDirectory = current; return; }

                string dir = PickAsciiDirectory();
                if (dir == null)
                {
                    Debug.LogWarning("[LLM] No ASCII-only directory available for LLAMA_CACHE; local LLM may fail to start.");
                    return;
                }
                Directory.CreateDirectory(dir);
                Environment.SetEnvironmentVariable("LLAMA_CACHE", dir);
                try { _wputenv_s("LLAMA_CACHE", dir); }
                catch (Exception e) { Debug.LogWarning($"[LLM] _wputenv_s unavailable: {e.Message}"); }
                CacheDirectory = dir;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LLM] Failed to prepare llama environment: {e.Message}");
            }
        }

        static string PickAsciiDirectory()
        {
            // Project folder (editor) or game folder (player), then ProgramData, then the drive root.
            string[] candidates =
            {
                Path.GetFullPath(Path.Combine(Application.dataPath, Application.isEditor ? "../Library/LlamaCache" : "LlamaCache")),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GuildMasterTycoon", "llama_cache"),
                Path.Combine(Path.GetPathRoot(Application.dataPath) ?? "C:\\", "GuildMasterTycoon", "llama_cache"),
            };
            foreach (var c in candidates)
                if (IsAscii(c)) return c;
            return null;
        }
    }
}
