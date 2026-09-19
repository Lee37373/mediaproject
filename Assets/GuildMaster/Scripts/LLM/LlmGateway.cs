using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GuildMaster.Core;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GuildMaster.LLM
{
    public struct LlmStatusChangedEvent { public LlmBackendState State; public string Description; }

    /// <summary>
    /// Single entry point for LLM work. A local model can only serve one generation at a time,
    /// so requests are queued by priority (dialogue > party > decisions > banter), deduplicated,
    /// bounded per priority, and time-limited. Callers always get a result (Ok=false on any
    /// failure) and are expected to fall back to rule-based logic.
    /// </summary>
    public class LlmGateway : MonoBehaviour
    {
        class Pending
        {
            public LlmRequest Request;
            public TaskCompletionSource<LlmResult> Tcs;
            public CancellationToken Token;
            public long Order;
            public LlmExchange Exchange;
            public bool Started;
            /// <summary>Cancelled when a higher-priority request arrives (Preemptible requests only).</summary>
            public readonly CancellationTokenSource Preempt = new();
        }

        /// <summary>The request currently being generated (null when idle).</summary>
        Pending _current;

        const float CancelGraceSeconds = 3f;
        /// <summary>How long a new request waits for a cancelled-but-still-running generation to end.
        /// Must cover one full generation (4B model: ~10 s), or requests after any cancel fail as "busy".</summary>
        const float StaleWaitSeconds = 25f;
        /// <summary>Generation that timed out and ignored cancellation (still running natively).</summary>
        Task _stale;

        const int HistorySize = 40;
        readonly List<LlmExchange> _history = new();
        int _exchangeId;

        /// <summary>Most recent exchanges, oldest first (debug viewer).</summary>
        public IReadOnlyList<LlmExchange> History => _history;

        static readonly int[] MaxQueuedPerPriority = { 4, 6, 8, 1 };

        readonly List<Pending> _queue = new();
        ILlmBackend _backend = new NullLlmBackend("LLM 미설정");
        bool _busy;
        long _order;
        LlmBackendState _lastState = (LlmBackendState)(-1);

        public ILlmBackend Backend => _backend;
        public bool IsReady => _backend.State == LlmBackendState.Ready;
        public bool IsBusy => _busy;
        public int QueueCount => _queue.Count;
        public int Completed { get; private set; }
        public int Failed { get; private set; }
        public float AverageSeconds { get; private set; }
        public float LastSeconds { get; private set; }

        /// <summary>Set false to force rule-based behaviour (e.g. from settings).</summary>
        public bool Enabled { get; set; } = true;

        public void SetBackend(ILlmBackend backend) => _backend = backend ?? new NullLlmBackend("LLM 미설정");

        public string StatusText
        {
            get
            {
                if (!Enabled) return "AI: 규칙 기반 (LLM 꺼짐)";
                return _backend.State switch
                {
                    LlmBackendState.Ready => _busy
                        ? $"AI: 생각 중… (대기 {_queue.Count})"
                        : $"AI: LLM 준비됨 ({AverageSeconds:0.0}s)",
                    LlmBackendState.Loading => "AI: 모델 로딩 중…",
                    LlmBackendState.Failed => "AI: LLM 오류 → 규칙 기반",
                    _ => "AI: 규칙 기반 (모델 없음)",
                };
            }
        }

        void Update()
        {
            var state = _backend.State;
            if (state != _lastState)
            {
                _lastState = state;
                EventBus.Publish(new LlmStatusChangedEvent { State = state, Description = _backend.Description });
            }
        }

        /// <summary>Queues a request. Never throws; returns Ok=false when the LLM is unavailable, busy-dropped, timed out or failed.</summary>
        public Task<LlmResult> RequestAsync(LlmRequest request, CancellationToken ct = default)
        {
            if (!Enabled || !IsReady) return Task.FromResult(LlmResult.Fail("offline"));
            if (ct.IsCancellationRequested) return Task.FromResult(LlmResult.Fail("cancelled"));

            // Supersede older queued request with the same key.
            if (!string.IsNullOrEmpty(request.DedupeKey))
            {
                for (int i = _queue.Count - 1; i >= 0; i--)
                {
                    if (_queue[i].Request.DedupeKey != request.DedupeKey) continue;
                    Finish(_queue[i], LlmResult.Fail("superseded"));
                    _queue.RemoveAt(i);
                }
            }

            int p = (int)request.Priority;
            int queuedSame = 0;
            foreach (var q in _queue) if ((int)q.Request.Priority == p) queuedSame++;
            if (queuedSame >= MaxQueuedPerPriority[Mathf.Clamp(p, 0, MaxQueuedPerPriority.Length - 1)])
                return Task.FromResult(LlmResult.Fail("queue full"));

            var pending = new Pending
            {
                Request = request,
                Tcs = new TaskCompletionSource<LlmResult>(),
                Token = ct,
                Order = _order++,
                Exchange = new LlmExchange { Id = ++_exchangeId, QueuedAt = Time.realtimeSinceStartup, Request = request },
            };
            _queue.Add(pending);
            // Small talk yields the model to anything more important.
            if (_current != null && _current.Request.Preemptible && request.Priority < _current.Request.Priority
                && !_current.Preempt.IsCancellationRequested)
                _current.Preempt.Cancel();
            if (!_busy) _ = PumpAsync();
            return pending.Tcs.Task;
        }

        async Task PumpAsync()
        {
            _busy = true;
            try
            {
                while (_queue.Count > 0)
                {
                    var next = TakeNext();
                    next.Started = true;
                    next.Exchange.QueueSeconds = Time.realtimeSinceStartup - next.Exchange.QueuedAt;
                    if (next.Token.IsCancellationRequested)
                    {
                        Finish(next, LlmResult.Fail("cancelled"));
                        continue;
                    }
                    _current = next;
                    LlmResult result;
                    try { result = await RunAsync(next); }
                    finally { _current = null; }
                    Finish(next, result);
                    if (this == null) return;
                }
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>One console line per exchange: outcome, queue wait, generation time, output size.</summary>
        public static string FormatSpeedLine(LlmExchange x)
        {
            int chars = x.Response?.Length ?? 0;
            string rate = x.Ok && x.GenerateSeconds > 0.01f ? $" ({chars / x.GenerateSeconds:0.0}자/s)" : "";
            string outcome = x.Ok ? "성공" : x.Error == "cancelled" ? "취소됨(요청 측)" : x.Error == "preempted" ? "양보함(우선 요청 처리)" : "실패(" + x.Error + ")";
            return $"[LLM] #{x.Id} {x.Request?.Tag} {outcome} · 대기 {x.QueueSeconds:0.0}s · 생성 {x.GenerateSeconds:0.0}s · 응답 {chars}자{rate}";
        }

        /// <summary>Average generation seconds of the successful exchanges in the history (0 if none).</summary>
        public float HistoryAverageSeconds
        {
            get
            {
                float sum = 0f;
                int n = 0;
                foreach (var x in _history) if (x.Ok) { sum += x.GenerateSeconds; n++; }
                return n == 0 ? 0f : sum / n;
            }
        }

        /// <summary>Completes a pending request and records it in the history.</summary>
        void Finish(Pending p, LlmResult result)
        {
            var x = p.Exchange;
            x.Ok = result.Ok;
            x.Response = result.Text;
            x.Error = result.Error;
            float elapsed = Time.realtimeSinceStartup - x.QueuedAt;
            if (!p.Started) x.QueueSeconds = elapsed; // dropped while still queued
            else x.GenerateSeconds = result.Ok ? result.Seconds : Mathf.Max(0f, elapsed - x.QueueSeconds);
            result.Exchange = x;
            _history.Add(x);
            if (_history.Count > HistorySize) _history.RemoveAt(0);
            if (Debug.isDebugBuild) Debug.Log(FormatSpeedLine(x));
            EventBus.Publish(new LlmExchangeEvent { Exchange = x });
            p.Tcs.TrySetResult(result);
        }

        Pending TakeNext()
        {
            int best = 0;
            for (int i = 1; i < _queue.Count; i++)
            {
                var a = _queue[i];
                var b = _queue[best];
                if (a.Request.Priority < b.Request.Priority || (a.Request.Priority == b.Request.Priority && a.Order < b.Order))
                    best = i;
            }
            var p = _queue[best];
            _queue.RemoveAt(best);
            return p;
        }

        async Task<LlmResult> RunAsync(Pending p)
        {
            if (!IsReady) return LlmResult.Fail("offline");
            // A previous generation that ignored cancellation may still be running natively.
            // Never start a second one on top of it and never wait on it unbounded.
            if (_stale != null && !_stale.IsCompleted)
            {
                await Task.WhenAny(_stale, Task.Delay(TimeSpan.FromSeconds(StaleWaitSeconds)));
                if (!_stale.IsCompleted)
                {
                    Failed++;
                    return LlmResult.Fail("busy");
                }
            }
            _stale = null;

            var sw = Stopwatch.StartNew();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(p.Token, p.Preempt.Token);
            try
            {
                var gen = _backend.GenerateAsync(p.Request, timeoutCts.Token);
                var timeout = Task.Delay(TimeSpan.FromSeconds(p.Request.TimeoutSeconds), timeoutCts.Token);
                var done = await Task.WhenAny(gen, timeout);
                if (done != gen)
                {
                    _backend.CancelCurrent();
                    timeoutCts.Cancel();
                    // A caller cancelling (e.g. a party invite replacing a decision) or a preempted
                    // small talk is not a failure.
                    bool preempted = p.Preempt.IsCancellationRequested;
                    if (!p.Token.IsCancellationRequested && !preempted) Failed++;
                    // Give the backend a short grace period to unwind; if it does not, remember it
                    // as stale (see above) instead of blocking the queue forever.
                    await Task.WhenAny(gen, Task.Delay(TimeSpan.FromSeconds(CancelGraceSeconds)));
                    if (!gen.IsCompleted)
                    {
                        _stale = gen;
                        Debug.LogWarning($"[LLM] '{p.Request.Tag}' did not stop after cancel; later requests fail fast until it ends.");
                    }
                    return LlmResult.Fail(p.Token.IsCancellationRequested ? "cancelled" : preempted ? "preempted" : "timeout");
                }
                timeoutCts.Cancel();
                string text = await gen;
                float seconds = (float)sw.Elapsed.TotalSeconds;
                if (string.IsNullOrWhiteSpace(text))
                {
                    Failed++;
                    return LlmResult.Fail("empty");
                }
                Completed++;
                LastSeconds = seconds;
                AverageSeconds = Completed == 1 ? seconds : Mathf.Lerp(AverageSeconds, seconds, 0.2f);
                return new LlmResult { Ok = true, Text = text, Seconds = seconds };
            }
            catch (Exception e)
            {
                Failed++;
                Debug.LogWarning($"[LLM] request '{p.Request.Tag}' failed: {e.Message}");
                return LlmResult.Fail(e.Message);
            }
        }

        void OnDestroy()
        {
            foreach (var p in _queue) Finish(p, LlmResult.Fail("shutdown"));
            _queue.Clear();
        }
    }
}
