using System;
using System.Collections;
using System.Threading.Tasks;
using GuildMaster.LLM;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GuildMaster.Tests
{
    /// <summary>
    /// P5-3: whatever the model does — hang, crash, answer nothing, be missing — every caller gets
    /// an answer in bounded time, the queue keeps moving, and a backend that went bad is restarted.
    /// The gateway runs on its own (no game boot) with a <see cref="FakeLlmBackend"/>.
    /// </summary>
    public class LlmFaultTests
    {
        GameObject _go;
        LlmGateway _gateway;
        FakeLlmBackend _fake;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("LlmGatewayTest");
            _gateway = _go.AddComponent<LlmGateway>();
            _fake = new FakeLlmBackend();
            _gateway.SetBackend(_fake);
            // Seconds instead of the real tens of seconds, so the tests stay short.
            _gateway.CancelGraceSeconds = 0.1f;
            _gateway.StaleWaitSeconds = 0.2f;
            _gateway.StaleDeadline = 1f;
        }

        [TearDown]
        public void TearDown() => Object.Destroy(_go);

        static LlmRequest Request(float timeout = 2f, string tag = "test") => new() { User = "?", TimeoutSeconds = timeout, Tag = tag };

        static IEnumerator Await(Task task, float realSeconds = 5f)
        {
            float end = Time.realtimeSinceStartup + realSeconds;
            while (!task.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(task.IsCompleted, Is.True, "the caller got an answer in time");
        }

        [UnityTest]
        public IEnumerator AHungGeneration_TimesOut_LaterRequestsFailFast_AndTheBackendIsRestarted()
        {
            var never = new TaskCompletionSource<string>();
            _fake.Generate = FakeLlmBackend.Hang(never);

            var first = _gateway.RequestAsync(Request(0.3f));
            yield return Await(first);
            Assert.That(first.Result.Ok, Is.False);
            Assert.That(first.Result.Error, Is.EqualTo("timeout"));
            // Not cancelled natively (KMS 2026-09-30: a cancel mid-generation wedged the service); it is set aside instead.
            Assert.That(_fake.Cancels, Is.EqualTo(0), "the generation is left to end by itself");

            // Still running natively: the next request does not pile a second generation on top.
            var second = _gateway.RequestAsync(Request(0.3f));
            yield return Await(second);
            Assert.That(second.Result.Error, Is.EqualTo("busy"));

            // Stuck past the deadline: the gateway restarts the backend, which ends the hang.
            _fake.OnRecover = () =>
            {
                never.TrySetResult(null);
                _fake.Generate = FakeLlmBackend.Answer("{\"a\":1}");
            };
            float end = Time.realtimeSinceStartup + 5f;
            while (_fake.Recoveries == 0 && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(_fake.Recoveries, Is.EqualTo(1), "restarted once");

            var third = _gateway.RequestAsync(Request());
            yield return Await(third);
            Assert.That(third.Result.Ok, Is.True, "working again after the restart: " + third.Result.Error);
            Assert.That(_gateway.ConsecutiveFailures, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Crashes_AndEmptyAnswers_AreFailuresNotExceptions_AndThreeInARowRestartTheBackend()
        {
            _fake.Generate = (_, _) => throw new InvalidOperationException("native crash");
            var thrown = _gateway.RequestAsync(Request());
            yield return Await(thrown);
            Assert.That(thrown.Result.Ok, Is.False);
            Assert.That(thrown.Result.Error, Is.EqualTo("native crash"));

            _fake.Generate = (_, _) => Task.FromException<string>(new Exception("faulted"));
            var faulted = _gateway.RequestAsync(Request());
            yield return Await(faulted);
            Assert.That(faulted.Result.Error, Is.EqualTo("faulted"));

            _fake.Generate = FakeLlmBackend.Answer("  ");
            _fake.OnRecover = () => _fake.Generate = FakeLlmBackend.Answer("{\"a\":1}");
            var empty = _gateway.RequestAsync(Request());
            yield return Await(empty);
            Assert.That(empty.Result.Error, Is.EqualTo("empty"));

            float end = Time.realtimeSinceStartup + 3f;
            while (_fake.Recoveries == 0 && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(_fake.Recoveries, Is.EqualTo(1), $"{LlmGateway.FailuresBeforeRecovery} failures in a row restart the backend");

            var ok = _gateway.RequestAsync(Request());
            yield return Await(ok);
            Assert.That(ok.Result.Ok, Is.True);
            var stats = _gateway.Stats;
            Assert.That(stats.Count("ok"), Is.EqualTo(1));
            Assert.That(stats.Count("empty"), Is.EqualTo(1));
            Assert.That(stats.Count("native crash") + stats.Count("faulted"), Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator AMissingModel_AnswersOfflineAtOnce_AndTheHudSaysRuleBased()
        {
            foreach (var state in new[] { LlmBackendState.Failed, LlmBackendState.NotConfigured, LlmBackendState.Loading })
            {
                _fake.State = state;
                var t = _gateway.RequestAsync(Request());
                Assert.That(t.IsCompleted, Is.True, $"{state}: no waiting");
                Assert.That(t.Result.Error, Is.EqualTo("offline"));
            }
            _fake.State = LlmBackendState.Failed;
            StringAssert.Contains("규칙 기반", _gateway.StatusText);
            _gateway.Enabled = false;
            _fake.State = LlmBackendState.Ready;
            Assert.That(_gateway.RequestAsync(Request()).Result.Error, Is.EqualTo("offline"), "switched off in the settings");
            Assert.That(_gateway.Stats.Count("offline"), Is.EqualTo(4));
            yield break;
        }

        [UnityTest]
        public IEnumerator ARequestWaitingTooLong_IsDropped_SoItsCallerFallsBack()
        {
            _fake.Generate = FakeLlmBackend.Slow(1000, "{\"a\":1}");
            var running = _gateway.RequestAsync(Request(5f, "running"));
            var waiting = Request(5f, "waiting");
            waiting.MaxWaitSeconds = 0.2f;
            var dropped = _gateway.RequestAsync(waiting);
            yield return Await(dropped, 2f);
            Assert.That(dropped.Result.Error, Is.EqualTo("queue timeout"));
            Assert.That(running.IsCompleted, Is.False, "dropped while the other was still generating");
            yield return Await(running);
            Assert.That(running.Result.Ok, Is.True);
            Assert.That(_gateway.Stats.Count("queue timeout"), Is.EqualTo(1));
        }
    }
}
