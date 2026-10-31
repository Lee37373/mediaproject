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
    /// so requests are queued by priority (dialogue > party > decisions > banter, a waiting request
    /// rising a level every <see cref="AgingSeconds"/>), deduplicated, bounded per priority, dropped
    /// after waiting too long, and time-limited. Callers always get a result (Ok=false on any
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

        /// <summary>How long a cancelled generation is given to unwind (settable for tests).</summary>
        public float CancelGraceSeconds { get; set; } = 3f;
        /// <summary>How long a new request waits for a cancelled-but-still-running generation to end.
        /// Must cover one full generation (4B model: ~10 s), or requests after any cancel fail as "busy".</summary>
        public float StaleWaitSeconds { get; set; } = 25f;
        /// <summary>A generation the gateway stopped waiting for (timed out or preempted) that is still running natively.</summary>
        Task _stale;
        /// <summary>When that generation was set aside (realtime), so it cannot block the queue forever.</summary>
        float _staleSince;

        // ---------------- Recovery (P4-35) ----------------

        /// <summary>Real failures in a row (a cancel or a preemption is not one) before recovering.</summary>
        public const int FailuresBeforeRecovery = 3;
        /// <summary>A stale generation is given up on after this long, and recovery is tried instead.</summary>
        public const float StaleDeadlineSeconds = 90f;
        /// <summary>The same, per gateway (tests shorten it).</summary>
        public float StaleDeadline { get; set; } = StaleDeadlineSeconds;
        /// <summary>Recovery is not tried more than this many times before the LLM is left alone.</summary>
        public const int MaxRecoveryAttempts = 5;

        int _consecutiveFailures;
        int _recoveryAttempts;
        bool _recovering;
        bool _gaveUp;
        float _nextRecoveryAt;

        /// <summary>Recovery is under way right now.</summary>
        public bool IsRecovering => _recovering;
        /// <summary>Recovery was tried to the limit and did not work; the game stays rule-based.</summary>
        public bool GaveUpOnRecovery => _gaveUp;
        public int RecoveryAttempts => _recoveryAttempts;
        public int ConsecutiveFailures => _consecutiveFailures;

        /// <summary>How long to wait before the next attempt: 30 s, doubling, capped at five minutes.</summary>
        public static float RecoveryDelay(int attempt)
            => Mathf.Min(300f, 30f * Mathf.Pow(2f, Mathf.Max(0, attempt)));

        /// <summary>Enough has gone wrong in a row that the backend is worth restarting.</summary>
        public static bool ShouldRecover(int consecutiveFailures) => consecutiveFailures >= FailuresBeforeRecovery;

        void NoteSuccess()
        {
            _consecutiveFailures = 0;
            _recoveryAttempts = 0;
            _gaveUp = false;
        }

        /// <summary>A real failure (not a cancel or a preemption).</summary>
        void NoteFailure()
        {
            Failed++;
            _consecutiveFailures++;
        }

        /// <summary>Restarts the backend once, from Update's schedule or a debug command.</summary>
        public async void TryRecoverNow()
        {
            if (_recovering) return;
            _recovering = true;
            _recoveryAttempts++;
            Debug.LogWarning($"[LLM] recovering after {_consecutiveFailures} failures (attempt {_recoveryAttempts}/{MaxRecoveryAttempts})");
            bool ok = false;
            try { ok = await _backend.TryRecoverAsync(); }
            catch (Exception e) { Debug.LogWarning($"[LLM] recovery threw: {e.Message}"); }
            _recovering = false;
            _stale = null;
            if (ok)
            {
                NoteSuccess();
                Debug.Log("[LLM] recovered");
            }
            else
            {
                _consecutiveFailures = 0; // start counting again rather than retrying every request
                _nextRecoveryAt = Time.realtimeSinceStartup + RecoveryDelay(_recoveryAttempts);
                if (_recoveryAttempts >= MaxRecoveryAttempts)
                {
                    _gaveUp = true;
                    Debug.LogWarning("[LLM] recovery gave up; staying on the rule-based path");
                }
            }
        }

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
                    LlmBackendState.Loading => _recovering ? "AI: LLM 복구 중…" : "AI: 모델 로딩 중…",
                    LlmBackendState.Failed => _gaveUp ? "AI: LLM 복구 실패 → 규칙 기반" : "AI: LLM 오류 → 규칙 기반",
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
            DropOverdue();
            TickRecovery();
        }

        /// <summary>Session request statistics (P5-3).</summary>
        public LlmStats Stats { get; } = new();

        /// <summary>Queued requests past their <see cref="LlmRequest.MaxWaitSeconds"/> fail now, so their callers fall back (P5-3).</summary>
        void DropOverdue()
        {
            float now = Time.realtimeSinceStartup;
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                var p = _queue[i];
                if (now - p.Exchange.QueuedAt <= p.Request.MaxWaitSeconds) continue;
                _queue.RemoveAt(i);
                Finish(p, LlmResult.Fail("queue timeout"));
            }
        }

        // ---------------- Starvation (P5-3) ----------------

        /// <summary>Every this many seconds of waiting raises a queued request one priority level.</summary>
        public const float AgingSeconds = 20f;

        /// <summary>
        /// Priority used to pick the next request: the request's own, raised a level per
        /// <see cref="AgingSeconds"/> waited. Without it a steady stream of conversations and party
        /// invitations kept decisions and small talk waiting until they were dropped.
        /// </summary>
        public static float EffectivePriority(LlmPriority priority, float waitedSeconds)
            => (int)priority - Mathf.Floor(Mathf.Max(0f, waitedSeconds) / AgingSeconds);

        /// <summary>
        /// Two ways the LLM stops answering without anything throwing: a generation that ignored
        /// cancellation and never ends (every later request fails fast as "busy"), and a backend
        /// that quietly went bad. Both are cleared by restarting the backend, with a backoff so a
        /// model that is simply gone is not restarted over and over.
        /// </summary>
        void TickRecovery()
        {
            if (!Enabled || _recovering || _gaveUp) return;
            if (_stale != null && !_stale.IsCompleted && Time.realtimeSinceStartup - _staleSince > StaleDeadline)
            {
                Debug.LogWarning($"[LLM] a generation has been stuck for {StaleDeadline:0}s; restarting the backend");
                _consecutiveFailures = Mathf.Max(_consecutiveFailures, FailuresBeforeRecovery);
            }
            if (!ShouldRecover(_consecutiveFailures)) return;
            if (Time.realtimeSinceStartup < _nextRecoveryAt) return;
            TryRecoverNow();
        }

        /// <summary>Queues a request. Never throws; returns Ok=false when the LLM is unavailable, busy-dropped, timed out or failed.</summary>
        public Task<LlmResult> RequestAsync(LlmRequest request, CancellationToken ct = default)
        {
            if (!Enabled || !IsReady) return Rejected("offline", request);
            if (ct.IsCancellationRequested) return Rejected("cancelled", request);

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
                return Rejected("queue full", request);

            var pending = new Pending
            {
                Request = request,
                Tcs = new TaskCompletionSource<LlmResult>(),
                Token = ct,
                Order = _order++,
                Exchange = new LlmExchange { Id = ++_exchangeId, QueuedAt = Time.realtimeSinceStartup, Request = request },
            };
            _queue.Add(pending);
            Stats.NoteQueueLength(_queue.Count);
            // Small talk yields the model to anything more important.
            if (_current != null && _current.Request.Preemptible && request.Priority < _current.Request.Priority
                && !_current.Preempt.IsCancellationRequested)
                _current.Preempt.Cancel();
            if (!_busy) _ = PumpAsync();
            return pending.Tcs.Task;
        }

        Task<LlmResult> Rejected(string reason, LlmRequest request)
        {
            Stats.RecordRejected(reason, request?.Tag);
            return Task.FromResult(LlmResult.Fail(reason));
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
            Stats.Record(x);
            if (Debug.isDebugBuild) Debug.Log(FormatSpeedLine(x));
            EventBus.Publish(new LlmExchangeEvent { Exchange = x });
            p.Tcs.TrySetResult(result);
        }

        Pending TakeNext()
        {
            float now = Time.realtimeSinceStartup;
            int best = 0;
            float bestPriority = EffectivePriority(_queue[0].Request.Priority, now - _queue[0].Exchange.QueuedAt);
            for (int i = 1; i < _queue.Count; i++)
            {
                var a = _queue[i];
                float pa = EffectivePriority(a.Request.Priority, now - a.Exchange.QueuedAt);
                if (pa < bestPriority || (pa == bestPriority && a.Order < _queue[best].Order))
                {
                    best = i;
                    bestPriority = pa;
                }
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
                    NoteFailure();
                    Debug.LogWarning($"[LLM] a set-aside generation is still running after {StaleWaitSeconds:0}s; '{p.Request.Tag}' fails as busy.");
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
                    // The native generation is NOT cancelled any more (user report 2026-09-30: "오래 돌리면
                    // LLM 연결이 끊어진다"). Reproduced live with kanana 8B: a preempted small talk was
                    // cancelled mid-generation, the native call never returned ("did not stop after
                    // cancel"), every later request failed as "busy", and the recovery that followed hung
                    // for good in "복구 중" because the new agent waits on the same wedged service.
                    // Cancel only sometimes wedges it (an earlier preemption in the same run was fine),
                    // so the longer the game runs the surer it is to happen. A generation always ends by
                    // itself within its token budget (a few seconds), so we simply stop waiting for this
                    // one: it is set aside as stale and the next request starts once it is done.
                    timeoutCts.Cancel();
                    // A caller cancelling (e.g. a party invite replacing a decision) or a preempted
                    // small talk is not a failure.
                    bool preempted = p.Preempt.IsCancellationRequested;
                    if (!p.Token.IsCancellationRequested && !preempted) NoteFailure();
                    if (!gen.IsCompleted)
                    {
                        _stale = gen;
                        _staleSince = Time.realtimeSinceStartup;
                    }
                    return LlmResult.Fail(p.Token.IsCancellationRequested ? "cancelled" : preempted ? "preempted" : "timeout");
                }
                timeoutCts.Cancel();
                string text = await gen;
                float seconds = (float)sw.Elapsed.TotalSeconds;
                if (string.IsNullOrWhiteSpace(text))
                {
                    NoteFailure();
                    return LlmResult.Fail("empty");
                }
                Completed++;
                NoteSuccess();
                LastSeconds = seconds;
                AverageSeconds = Completed == 1 ? seconds : Mathf.Lerp(AverageSeconds, seconds, 0.2f);
                return new LlmResult { Ok = true, Text = text, Seconds = seconds };
            }
            catch (Exception e)
            {
                NoteFailure();
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
