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
        }

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
                    _queue[i].Tcs.TrySetResult(LlmResult.Fail("superseded"));
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
            };
            _queue.Add(pending);
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
                    if (next.Token.IsCancellationRequested)
                    {
                        next.Tcs.TrySetResult(LlmResult.Fail("cancelled"));
                        continue;
                    }
                    var result = await RunAsync(next);
                    next.Tcs.TrySetResult(result);
                    if (this == null) return;
                }
            }
            finally
            {
                _busy = false;
            }
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
            var sw = Stopwatch.StartNew();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(p.Token);
            try
            {
                var gen = _backend.GenerateAsync(p.Request, timeoutCts.Token);
                var timeout = Task.Delay(TimeSpan.FromSeconds(p.Request.TimeoutSeconds), timeoutCts.Token);
                var done = await Task.WhenAny(gen, timeout);
                if (done != gen)
                {
                    _backend.CancelCurrent();
                    timeoutCts.Cancel();
                    Failed++;
                    // Let the backend unwind before starting the next request.
                    try { await gen; } catch { /* ignored */ }
                    return LlmResult.Fail(p.Token.IsCancellationRequested ? "cancelled" : "timeout");
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
            foreach (var p in _queue) p.Tcs.TrySetResult(LlmResult.Fail("shutdown"));
            _queue.Clear();
        }
    }
}
