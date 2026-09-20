using GuildMaster.LLM;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// P4-35: when the LLM stops answering the gateway restarts the backend, backing off so a
    /// model that is simply gone is not restarted forever.
    /// </summary>
    public class LlmRecoveryTests
    {
        [Test]
        public void Recovery_StartsOnlyAfterSeveralFailuresInARow()
        {
            for (int i = 0; i < LlmGateway.FailuresBeforeRecovery; i++)
                Assert.That(LlmGateway.ShouldRecover(i), Is.False, $"{i} failures is not enough");
            Assert.That(LlmGateway.ShouldRecover(LlmGateway.FailuresBeforeRecovery), Is.True);
            Assert.That(LlmGateway.ShouldRecover(LlmGateway.FailuresBeforeRecovery + 5), Is.True);
        }

        [Test]
        public void RecoveryDelay_DoublesAndIsCapped()
        {
            Assert.That(LlmGateway.RecoveryDelay(0), Is.EqualTo(30f));
            Assert.That(LlmGateway.RecoveryDelay(1), Is.EqualTo(60f));
            Assert.That(LlmGateway.RecoveryDelay(2), Is.EqualTo(120f));
            Assert.That(LlmGateway.RecoveryDelay(3), Is.EqualTo(240f));
            Assert.That(LlmGateway.RecoveryDelay(4), Is.EqualTo(300f), "capped at five minutes");
            Assert.That(LlmGateway.RecoveryDelay(50), Is.EqualTo(300f));
            Assert.That(LlmGateway.RecoveryDelay(-3), Is.EqualTo(30f), "a negative attempt is the first one");
        }

        [Test]
        public void StuckGeneration_IsGivenUpOnWellBeforeTheSessionEnds()
        {
            // The whole point is that a wedged generation stops blocking the queue on its own.
            Assert.That(LlmGateway.StaleDeadlineSeconds, Is.GreaterThan(0f));
            Assert.That(LlmGateway.StaleDeadlineSeconds, Is.LessThanOrEqualTo(300f));
        }

        [Test]
        public void NoBackend_ReportsThatItCannotRecover()
        {
            var backend = new NullLlmBackend("테스트");
            var task = backend.TryRecoverAsync();
            Assert.That(task.IsCompleted, Is.True);
            Assert.That(task.Result, Is.False, "nothing to bring back");
        }
    }
}
