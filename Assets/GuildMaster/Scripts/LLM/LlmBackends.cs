using UnityEngine;

namespace GuildMaster.LLM
{
    /// <summary>Chooses the available backend at startup (LLMUnity when the package is installed).</summary>
    public static class LlmBackends
    {
        public static ILlmBackend CreateDefault(Transform parent)
        {
#if LLMUNITY_PRESENT
            return LLMUnityBackend.Create(parent);
#else
            return new NullLlmBackend("LLMUnity 패키지가 설치되지 않았습니다");
#endif
        }

        /// <summary>Call first thing on session start (before the scene's LLM wakes up).</summary>
        public static void DiscardReloadedDuplicates()
        {
#if LLMUNITY_PRESENT
            LLMUnityBackend.DiscardReloadedDuplicates();
#endif
        }
    }
}
