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
        static readonly System.Random s_seedRng = new();

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

        /// <summary>
        /// The scene's LLM survives scene reloads (dontDestroyOnLoad), so a restart would load a
        /// second copy of the model. Must run before the reloaded LLM's Awake: deactivates and
        /// destroys the new copy when a persistent one is already running.
        /// </summary>
        public static void DiscardReloadedDuplicates()
        {
            var all = FindObjectsByType<LLMUnity.LLM>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            bool persistentExists = false;
            foreach (var l in all) if (l.gameObject.scene.name == "DontDestroyOnLoad") persistentExists = true;
            if (!persistentExists) return;
            foreach (var l in all)
            {
                if (l.gameObject.scene.name == "DontDestroyOnLoad") continue;
                l.enabled = false;
                l.gameObject.SetActive(false);
                Destroy(l.gameObject);
            }
        }

        IEnumerator Connect()
        {
            _llm = null;
            foreach (var l in FindObjectsByType<LLMUnity.LLM>(FindObjectsSortMode.None))
                if (l.enabled && l.gameObject.activeInHierarchy) { _llm = l; break; }
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
            // LLMUnity documents seed 0 as "random" but passes it to llama.cpp, where 0 is a fixed
            // seed: identical prompts then give identical lines forever. Use a fresh seed per call.
            _agent.seed = s_seedRng.Next(1, int.MaxValue);
            // Earlier turns of a conversation go in as real chat turns. The agent itself stays stateless
            // (addToHistory is false), so the history is reset on every call. A stable system prompt plus
            // an unchanged history prefix lets llama.cpp reuse its cache between turns.
            var history = new System.Collections.Generic.List<ChatMessage>();
            if (request.Messages != null)
                foreach (var m in request.Messages)
                    if (!string.IsNullOrWhiteSpace(m.Text)) history.Add(new ChatMessage(m.FromUser ? "user" : "assistant", m.Text));
            _agent.chat = history;
            return await _agent.Chat(request.User, null, null, addToHistory: false);
        }

        public void CancelCurrent()
        {
            if (_agent != null) _agent.CancelRequests();
        }
    }
}
#endif
