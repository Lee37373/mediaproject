using GuildMaster.LLM;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// P5-3: a waiting request rises in priority so nothing starves, and the session statistics
    /// count every way a request can end.
    /// </summary>
    public class LlmQueueTests
    {
        [Test]
        public void Waiting_RaisesARequestALevelPerAgingStep()
        {
            Assert.That(LlmGateway.EffectivePriority(LlmPriority.Banter, 0f), Is.EqualTo(3f));
            Assert.That(LlmGateway.EffectivePriority(LlmPriority.Banter, LlmGateway.AgingSeconds - 0.1f), Is.EqualTo(3f));
            Assert.That(LlmGateway.EffectivePriority(LlmPriority.Banter, LlmGateway.AgingSeconds), Is.EqualTo(2f));
            Assert.That(LlmGateway.EffectivePriority(LlmPriority.Decision, -5f), Is.EqualTo(2f), "no negative wait");
        }

        [Test]
        public void ADecisionWaitingLongEnough_GoesBeforeAFreshConversation()
        {
            float fresh = LlmGateway.EffectivePriority(LlmPriority.Dialogue, 0f);
            Assert.That(LlmGateway.EffectivePriority(LlmPriority.Decision, LlmGateway.AgingSeconds), Is.GreaterThan(fresh),
                "one step is not enough to jump the queue");
            // Two steps make it level with a fresh conversation, and level requests go oldest first.
            Assert.That(LlmGateway.EffectivePriority(LlmPriority.Decision, 2f * LlmGateway.AgingSeconds), Is.LessThanOrEqualTo(fresh),
                "a decision is never stuck behind conversations for good");
            Assert.That(2f * LlmGateway.AgingSeconds, Is.LessThan(new LlmRequest().MaxWaitSeconds),
                "and it gets there before the queue would drop it as overdue");
        }

        [Test]
        public void Stats_CountEveryOutcome_AndTimeOnlyWhatWasGenerated()
        {
            var stats = new LlmStats();
            stats.Record(new LlmExchange { Ok = true, QueueSeconds = 2f, GenerateSeconds = 10f, Request = new LlmRequest { Tag = "decision:NPC_001" } });
            stats.Record(new LlmExchange { Ok = true, QueueSeconds = 0f, GenerateSeconds = 6f, Request = new LlmRequest { Tag = "decision:NPC_001" } });
            stats.Record(new LlmExchange { Ok = false, Error = "timeout", QueueSeconds = 4f, GenerateSeconds = 30f, Request = new LlmRequest { Tag = "banter:NPC_001+NPC_002" } });
            stats.RecordRejected("queue full", "banter");
            stats.NoteQueueLength(3);
            stats.NoteQueueLength(1);

            Assert.That(stats.Total, Is.EqualTo(4));
            Assert.That(stats.Succeeded, Is.EqualTo(2));
            Assert.That(stats.Count("ok"), Is.EqualTo(2));
            Assert.That(stats.Count("timeout"), Is.EqualTo(1));
            Assert.That(stats.Count("queue full"), Is.EqualTo(1));
            Assert.That(stats.AverageGenerateSeconds, Is.EqualTo(8f).Within(1e-4), "failed generations do not count towards speed");
            Assert.That(stats.AverageWaitSeconds, Is.EqualTo(2f).Within(1e-4));
            Assert.That(stats.LongestWaitSeconds, Is.EqualTo(4f));
            Assert.That(stats.PeakQueue, Is.EqualTo(3));
            var text = stats.Summary();
            StringAssert.Contains("시간 초과 1", text);
            StringAssert.Contains("대기열 가득 1", text);
            StringAssert.Contains("decision 2/2", text);
            StringAssert.Contains("banter 0/2", text);
        }

        [Test]
        public void Stats_LabelUnknownErrorsAsErrors()
        {
            Assert.That(LlmStats.Label(LlmStats.OutcomeOf(false, null)), Is.EqualTo("오류(error)"));
            Assert.That(LlmStats.Label("Native crash"), Is.EqualTo("오류(Native crash)"));
            Assert.That(LlmStats.Label(LlmStats.OutcomeOf(true, "ignored")), Is.EqualTo("성공"));
        }
    }
}
