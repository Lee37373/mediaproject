using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using LLMUnity;

namespace GuildMaster.EditorTools
{
    /// <summary>
    /// One-click swap of the scene's LLM model through LLMUnity's own downloader (edit mode only).
    /// Qwen3-14B (9 GB) does not fit an 8 GB laptop GPU and is far too slow for dialogue; the menu items
    /// download a smaller model, assign it to the LLM component of the open scene and save the scene.
    /// </summary>
    public static class LlmModelSwap
    {
        const string Gemma3_4B = "https://huggingface.co/lmstudio-community/gemma-3-4b-it-GGUF/resolve/main/gemma-3-4b-it-Q4_K_M.gguf";
        const string Qwen35_9B = "https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/main/Qwen3.5-9B-Q4_K_M.gguf";
        const string Qwen3_4B_Instruct = "https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF/resolve/main/Qwen3-4B-Instruct-2507-Q4_K_M.gguf";

        static bool s_running;
        static string s_label = "";

        [MenuItem("GuildMaster/LLM/모델 교체: Gemma 3 4B (약 2.5GB, 한국어 우수)")]
        static void UseGemma() => DownloadAndApply(Gemma3_4B, "Gemma 3 4B");

        [MenuItem("GuildMaster/LLM/모델 교체: Qwen 3.5 9B (약 5.7GB, 가장 정확)")]
        static void UseQwen9B() => DownloadAndApply(Qwen35_9B, "Qwen 3.5 9B");

        [MenuItem("GuildMaster/LLM/모델 교체: Qwen3-4B Instruct 2507 (약 2.5GB)")]
        static void UseQwen4B() => DownloadAndApply(Qwen3_4B_Instruct, "Qwen3 4B Instruct 2507");

        [MenuItem("GuildMaster/LLM/현재 씬 LLM 모델 확인")]
        static void LogCurrent()
        {
            var llm = Object.FindFirstObjectByType<LLMUnity.LLM>(FindObjectsInactive.Include);
            Debug.Log(llm == null ? "[LLM] 씬에 LLM 컴포넌트가 없습니다." : $"[LLM] 현재 모델: {llm.model} (컨텍스트 {llm.contextSize}, GPU 레이어 {llm.numGPULayers})");
        }

        static async void DownloadAndApply(string url, string label)
        {
            if (s_running) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[LLM] 플레이를 멈춘 뒤에 실행하세요.");
                return;
            }
            s_running = true;
            s_label = label;
            EditorApplication.update += ShowProgress;
            try
            {
                Debug.Log($"[LLM] 모델 다운로드 시작: {label}. 진행률 표시줄이 사라질 때까지 기다리세요.");
                string file = await LLMManager.DownloadModel(url, true, label);
                if (string.IsNullOrEmpty(file)) Debug.LogError("[LLM] 모델 다운로드 실패 (콘솔의 이전 오류 참고).");
                else Apply(file);
            }
            finally
            {
                EditorApplication.update -= ShowProgress;
                EditorUtility.ClearProgressBar();
                s_running = false;
            }
        }

        static void Apply(string file)
        {
            var llm = Object.FindFirstObjectByType<LLMUnity.LLM>(FindObjectsInactive.Include);
            if (llm == null)
            {
                Debug.LogError($"[LLM] 다운로드는 끝났지만 열려 있는 씬에 LLM 컴포넌트가 없습니다. Main 씬을 연 뒤 LLM 오브젝트의 Model에서 {file}을 고르세요.");
                return;
            }
            llm.model = file;
            EditorUtility.SetDirty(llm);
            var scene = llm.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[LLM] 모델을 {file}(으)로 교체하고 씬을 저장했습니다. 플레이하면 새 모델이 로드됩니다.");
        }

        static void ShowProgress()
        {
            float p = Mathf.Clamp01(LLMManager.modelProgress);
            EditorUtility.DisplayProgressBar("LLM 모델 다운로드", $"{s_label} · {p * 100f:0}%", p);
        }
    }
}
