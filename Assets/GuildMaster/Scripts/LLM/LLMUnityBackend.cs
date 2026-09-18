#if LLMUNITY_PRESENT
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using LLMUnity;
using UnityEngine;

namespace GuildMaster.LLM
{
    /// <summary>
    /// ILlmBackend over LLMUnity (in-process llama.cpp). Looks for an LLM component in the scene
    /// (the user downloads/assigns the GGUF model in its inspector), waits for the server to
    /// start, then attaches a stateless LLMAgent. Every request swaps system prompt / grammar,
    /// which is safe because the gateway serializes requests.
    /// </summary>
    public class LLMUnityBackend : MonoBehaviour, ILlmBackend
    {
        [SerializeField] float startupTimeoutSeconds = 180f;

        LLMUnity.LLM _llm;
        LLMAgent _agent;

        public LlmBackendState State { get; private set; } = LlmBackendState.NotConfigured;
        public string Description { get; private set; } = "LLM 컴포넌트 없음";

        /// <summary>Creates the backend and starts connecting to the scene's LLM (if any).</summary>
        public static LLMUnityBackend Create(Transform parent)
        {
            var go = new GameObject("LLMUnityBackend");
            go.transform.SetParent(parent, false);
            var backend = go.AddComponent<LLMUnityBackend>();
            backend.StartCoroutine(backend.Connect());
            return backend;
        }

        IEnumerator Connect()
        {
            _llm = FindAnyObjectByType<LLMUnity.LLM>();
            if (_llm == null || !_llm.enabled)
            {
                State = LlmBackendState.NotConfigured;
                Description = "씬에 LLM 컴포넌트가 없습니다";
                yield break;
            }
            if (string.IsNullOrEmpty(_llm.model))
            {
                State = LlmBackendState.NotConfigured;
                Description = "LLM 컴포넌트에 모델이 지정되지 않았습니다";
                yield break;
            }

            State = LlmBackendState.Loading;
            Description = $"모델 로딩: {_llm.model}";
            float t = 0f;
            while (!_llm.started && !_llm.failed && t < startupTimeoutSeconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!_llm.started)
            {
                State = LlmBackendState.Failed;
                Description = _llm.failed ? "LLM 서버 시작 실패" : "LLM 로딩 시간 초과";
                yield break;
            }

            // Thinking/reasoning output would waste tokens and break the JSON grammar flow.
            try { _llm.reasoning = false; } catch (System.Exception e) { Debug.LogWarning($"[LLM] reasoning toggle: {e.Message}"); }

            _agent = gameObject.AddComponent<LLMAgent>();
            _agent.llm = _llm;
            _agent.systemPrompt = "You are a game AI. Answer only with the requested JSON.";
            _agent.temperature = 0.6f;
            _agent.numPredict = 160;

            // The agent connects to the LLM in its own Start(); let that run before the first call.
            yield return null;
            yield return null;

            // Warm up (caches the system prompt).
            var warm = _agent.Warmup();
            while (!warm.IsCompleted) yield return null;
            if (warm.IsFaulted)
            {
                State = LlmBackendState.Failed;
                Description = "LLM 에이전트 초기화 실패: " + warm.Exception?.GetBaseException().Message;
                yield break;
            }

            State = LlmBackendState.Ready;
            Description = $"LLMUnity · {System.IO.Path.GetFileNameWithoutExtension(_llm.model)}";
            Debug.Log($"[LLM] Ready: {_llm.model}");
        }

        public async Task<string> GenerateAsync(LlmRequest request, CancellationToken ct)
        {
            if (State != LlmBackendState.Ready || _agent == null) return null;
            _agent.systemPrompt = string.IsNullOrEmpty(request.System) ? "Answer only with JSON." : request.System;
            _agent.grammar = request.Grammar ?? "";
            _agent.numPredict = request.MaxTokens;
            _agent.temperature = request.Temperature;
            return await _agent.Chat(request.User, null, null, addToHistory: false);
        }

        public void CancelCurrent()
        {
            if (_agent != null) _agent.CancelRequests();
        }
    }
}
#endif
