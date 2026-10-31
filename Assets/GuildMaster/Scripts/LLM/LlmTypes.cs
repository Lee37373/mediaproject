using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GuildMaster.LLM
{
    /// <summary>Lower value = served first.</summary>
    public enum LlmPriority { Dialogue = 0, Party = 1, Decision = 2, Banter = 3 }

    /// <summary>One chat turn: what the user (FromUser) or the model (assistant) said.</summary>
    public struct LlmMessage
    {
        public bool FromUser;
        public string Text;

        public LlmMessage(bool fromUser, string text) { FromUser = fromUser; Text = text; }
    }

    public class LlmRequest
    {
        public string System;
        /// <summary>The newest user message (the one being answered).</summary>
        public string User;
        /// <summary>
        /// Earlier turns of a conversation, oldest first, alternating user/assistant and starting with a
        /// user turn (null/empty = single-shot request). Backends pass them as real chat turns.
        /// </summary>
        public List<LlmMessage> Messages;
        /// <summary>GBNF grammar constraining the output (usually a JSON object).</summary>
        public string Grammar;
        public int MaxTokens = 160;
        public float Temperature = 0.6f;
        public LlmPriority Priority = LlmPriority.Decision;
        /// <summary>A newer request with the same key supersedes a queued older one.</summary>
        public string DedupeKey;
        public float TimeoutSeconds = 30f;
        /// <summary>
        /// Longest time it may wait in the queue before it is dropped as "queue timeout" (P5-3):
        /// the caller falls back to its rules instead of an NPC standing about for minutes.
        /// </summary>
        public float MaxWaitSeconds = 45f;
        /// <summary>May be cancelled mid-generation when a higher-priority request arrives (small talk).</summary>
        public bool Preemptible;
        public string Tag;
    }

    public struct LlmResult
    {
        public bool Ok;
        public string Text;
        public string Error;
        public float Seconds;
        /// <summary>Debug record of this request (null when it never reached the queue).</summary>
        public LlmExchange Exchange;

        public static LlmResult Fail(string error) => new() { Ok = false, Error = error };
    }

    /// <summary>One request/response pair kept for the debug viewer.</summary>
    public class LlmExchange
    {
        public int Id;
        /// <summary>Realtime (seconds since startup) when the request was queued.</summary>
        public float QueuedAt;
        public float QueueSeconds;
        public float GenerateSeconds;
        public LlmRequest Request;
        public bool Ok;
        public string Response;
        public string Error;
        /// <summary>Caller's verdict after validation (e.g. "채택", "대체: quest_id").</summary>
        public string Note;
    }

    /// <summary>Published by the gateway whenever an exchange finishes (success or failure).</summary>
    public struct LlmExchangeEvent { public LlmExchange Exchange; }

    public enum LlmBackendState { NotConfigured, Loading, Ready, Failed }

    /// <summary>A text generator (LLMUnity, remote server, or none).</summary>
    public interface ILlmBackend
    {
        LlmBackendState State { get; }
        string Description { get; }
        Task<string> GenerateAsync(LlmRequest request, CancellationToken ct);
        void CancelCurrent();
        /// <summary>
        /// Tries to bring a backend that stopped answering back to Ready (P4-35). False when it
        /// cannot — a native generation that is truly wedged cannot be rescued from here, and the
        /// game simply stays on its rule-based path.
        /// </summary>
        Task<bool> TryRecoverAsync();
    }

    /// <summary>Backend used when no model is available: always reports not configured.</summary>
    public class NullLlmBackend : ILlmBackend
    {
        public NullLlmBackend(string reason) { Description = reason; }
        public LlmBackendState State => LlmBackendState.NotConfigured;
        public string Description { get; }
        public Task<string> GenerateAsync(LlmRequest request, CancellationToken ct) => Task.FromResult<string>(null);
        public void CancelCurrent() { }
        public Task<bool> TryRecoverAsync() => Task.FromResult(false);
    }
}
