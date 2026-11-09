using System.IO;
using System.Linq;
using LLMUnity;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GuildMaster.EditorTools
{
    /// <summary>
    /// One-click Windows player build (P5-5). Only the model the Main scene's LLM uses is shipped
    /// (LLMUnity copies every model marked "include in build" into StreamingAssets, and stale
    /// entries would add gigabytes), the player is named "Guild Master Tycoon" (the project keeps its own name),
    /// and it goes to Builds/Windows (git-ignored). About 5.3GB: the model 4.6GB, the Windows llama libraries
    /// without CUDA 0.5GB. Afterwards the project is as it was: LLMUnity's moved libraries and model copy, the
    /// product name, the preloaded assets.
    /// </summary>
    public static class WindowsBuild
    {
        public const string ProductName = "Guild Master Tycoon";
        public const string ExeName = "GuildMasterTycoon.exe";
        const string MainScene = "Assets/GuildMaster/Scenes/Main.unity";

        public static string OutputDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Windows"));

        [MenuItem("GuildMaster/Windows 빌드")]
        static void BuildFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Build] 플레이를 멈춘 뒤에 실행하세요.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        /// <summary>Builds the player and returns a one-line summary (also used from eval).</summary>
        public static string Build()
        {
            string model = SceneModel();
            if (model == null) return Fail("Main 씬에서 LLM 모델을 찾지 못했습니다.");
            foreach (var entry in LLMManager.modelEntries.ToList())
                LLMManager.SetIncludeInBuild(entry, entry.filename == model);

            // The player's name only for the build: left in the project it would move the editor's saves and settings
            // (persistentDataPath and PlayerPrefs go by the product name). The preloaded assets are put back too — the
            // Input System adds its actions asset for the build and, built from eval, never took it out again (P4-135 had).
            string projectName = PlayerSettings.productName;
            var preloaded = PlayerSettings.GetPreloadedAssets();
            PlayerSettings.productName = ProductName;
            PlayerSettings.runInBackground = true;

            string exe = Path.Combine(OutputDir, ExeName);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { MainScene },
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report;
            try { report = BuildPipeline.BuildPlayer(options); }
            finally
            {
                // LLMUnity moves the other platforms' libraries out and copies the model into StreamingAssets for the build,
                // and puts them back only on a later editor tick (delayCall) — which a build run from eval never reached: the
                // project was left with win-x64 alone and a 4.9GB model copy. Put them back now (the later call finds nothing).
                LLMBuilder.Reset();
                PlayerSettings.productName = projectName;
                PlayerSettings.SetPreloadedAssets(preloaded);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            var s = report.summary;
            if (s.result != BuildResult.Succeeded)
                return Fail($"빌드 실패: {s.result}, 오류 {s.totalErrors}개 (콘솔 참고)");

            long bytes = Directory.EnumerateFiles(OutputDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            string msg = $"[Build] 완료: {exe} · 모델 {model} · 전체 {bytes / (1024f * 1024f * 1024f):0.00}GB · {s.totalTime.TotalSeconds:0}초";
            Debug.Log(msg);
            return msg;
        }

        /// <summary>The model file name assigned to the LLM component in the Main scene.</summary>
        static string SceneModel()
        {
            var open = EditorSceneManager.GetSceneByPath(MainScene);
            bool opened = !open.isLoaded;
            var scene = opened ? EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Additive) : open;
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    var llm = root.GetComponentInChildren<LLMUnity.LLM>(true);
                    if (llm != null && !string.IsNullOrEmpty(llm.model)) return llm.model;
                }
                return null;
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        static string Fail(string message)
        {
            Debug.LogError("[Build] " + message);
            return "[Build] " + message;
        }
    }
}
