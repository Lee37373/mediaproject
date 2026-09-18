using System.Threading;
using System.Threading.Tasks;

namespace GuildMaster.LLM
{
    /// <summary>Lower value = served first.</summary>
    public enum LlmPriority { Dialogue = 0, Party = 1, Decision = 2, Banter = 3 }

    public class LlmRequest
    {
        public string System;
        public string User;
        /// <summary>GBNF grammar constraining the output (usually a JSON object).</summary>
        public string Grammar;
        public int MaxTokens = 160;
        public float Temperature = 0.6f;
        public LlmPriority Priority = LlmPriority.Decision;
        /// <summary>A newer request with the same key supersedes a queued older one.</summary>
        public string DedupeKey;
        public float TimeoutSeconds = 30f;
        public string Tag;
    }

    public struct LlmResult
    {
        public bool Ok;
        public string Text;
        public string Error;
        public float Seconds;

        public static LlmResult Fail(string error) => new() { Ok = false, Error = error };
    }

    public enum LlmBackendState { NotConfigured, Loading, Ready, Failed }

    /// <summary>A text generator (LLMUnity, remote server, or none).</summary>
    public interface ILlmBackend
    {
        LlmBackendState State { get; }
        string Description { get; }
        Task<string> GenerateAsync(LlmRequest request, CancellationToken ct);
        void CancelCurrent();
    }

    /// <summary>Backend used when no model is available: always reports not configured.</summary>
    public class NullLlmBackend : ILlmBackend
    {
        public NullLlmBackend(string reason) { Description = reason; }
        public LlmBackendState State => LlmBackendState.NotConfigured;
        public string Description { get; }
        public Task<string> GenerateAsync(LlmRequest request, CancellationToken ct) => Task.FromResult<string>(null);
        public void CancelCurrent() { }
    }
}
