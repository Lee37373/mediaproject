using System;
using System.Threading;
using System.Threading.Tasks;
using GuildMaster.LLM;

namespace GuildMaster.Tests
{
    /// <summary>
    /// A scripted LLM backend (P5-3): answers, hangs, throws or reports a missing model as the test
    /// says, so the gateway's handling of each failure can be checked without a real model.
    /// </summary>
    public class FakeLlmBackend : ILlmBackend
    {
        public LlmBackendState State { get; set; } = LlmBackendState.Ready;
        public string Description => "fake";

        /// <summary>What a generation does; by default answers at once.</summary>
        public Func<LlmRequest, CancellationToken, Task<string>> Generate = (_, _) => Task.FromResult("{\"ok\":true}");
        /// <summary>Runs on every recovery attempt (e.g. to unstick a hung generation).</summary>
        public Action OnRecover;
        public bool RecoverSucceeds = true;
        public int Recoveries;
        public int Cancels;

        public Task<string> GenerateAsync(LlmRequest request, CancellationToken ct) => Generate(request, ct);
        public void CancelCurrent() => Cancels++;

        public Task<bool> TryRecoverAsync()
        {
            Recoveries++;
            OnRecover?.Invoke();
            return Task.FromResult(RecoverSucceeds);
        }

        /// <summary>A generation that never ends and ignores cancellation (a wedged native call).</summary>
        public static Func<LlmRequest, CancellationToken, Task<string>> Hang(TaskCompletionSource<string> never) => (_, _) => never.Task;

        public static Func<LlmRequest, CancellationToken, Task<string>> Answer(string text) => (_, _) => Task.FromResult(text);

        /// <summary>Answers after a delay, honouring cancellation.</summary>
        public static Func<LlmRequest, CancellationToken, Task<string>> Slow(int ms, string text) => async (_, ct) =>
        {
            await Task.Delay(ms, ct);
            return text;
        };
    }
}
