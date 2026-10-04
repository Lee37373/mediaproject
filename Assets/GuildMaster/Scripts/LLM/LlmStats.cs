using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GuildMaster.LLM
{
    /// <summary>
    /// Request statistics for the whole session (P5-3): how requests ended, how long they waited
    /// and generated, and per caller tag. Shown in the F2 window and <c>DebugCommands.LlmStatus</c>.
    /// Requests turned away before queueing (LLM off, queue full) are counted too.
    /// </summary>
    public class LlmStats
    {
        readonly Dictionary<string, int> _outcomes = new();
        readonly Dictionary<string, (int ok, int fail)> _byTag = new();
        double _waitSum, _generateSum;
        int _waited, _generated;

        public int Total { get; private set; }
        public int Succeeded { get; private set; }
        public float LongestWaitSeconds { get; private set; }
        public int PeakQueue { get; private set; }

        public int Count(string outcome) => _outcomes.TryGetValue(outcome, out var n) ? n : 0;
        public float AverageWaitSeconds => _waited == 0 ? 0f : (float)(_waitSum / _waited);
        public float AverageGenerateSeconds => _generated == 0 ? 0f : (float)(_generateSum / _generated);

        /// <summary>Outcome key of an exchange: "ok" or its error ("timeout", "queue timeout", …).</summary>
        public static string OutcomeOf(bool ok, string error) => ok ? "ok" : string.IsNullOrEmpty(error) ? "error" : error;

        /// <summary>A request that went through the queue (finished, failed or dropped there).</summary>
        public void Record(LlmExchange x)
        {
            Add(OutcomeOf(x.Ok, x.Error), x.Request?.Tag, x.Ok);
            _waitSum += x.QueueSeconds;
            _waited++;
            if (x.QueueSeconds > LongestWaitSeconds) LongestWaitSeconds = x.QueueSeconds;
            if (x.Ok)
            {
                _generateSum += x.GenerateSeconds;
                _generated++;
            }
        }

        /// <summary>A request turned away before it was queued.</summary>
        public void RecordRejected(string reason, string tag) => Add(reason, tag, false);

        public void NoteQueueLength(int n)
        {
            if (n > PeakQueue) PeakQueue = n;
        }

        void Add(string outcome, string tag, bool ok)
        {
            Total++;
            if (ok) Succeeded++;
            _outcomes[outcome] = Count(outcome) + 1;
            tag = KindOf(tag);
            var t = _byTag.TryGetValue(tag, out var v) ? v : default;
            _byTag[tag] = ok ? (t.ok + 1, t.fail) : (t.ok, t.fail + 1);
        }

        /// <summary>The caller kind of a tag: "decision:NPC_003" counts as "decision".</summary>
        public static string KindOf(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "?";
            int colon = tag.IndexOf(':');
            return colon > 0 ? tag.Substring(0, colon) : tag;
        }

        /// <summary>Korean label for an outcome key.</summary>
        public static string Label(string outcome) => outcome switch
        {
            "ok" => "성공",
            "timeout" => "시간 초과",
            "queue timeout" => "대기 초과",
            "queue full" => "대기열 가득",
            "superseded" => "새 요청으로 대체",
            "cancelled" => "요청 측 취소",
            "preempted" => "양보",
            "offline" => "LLM 꺼짐",
            "busy" => "이전 생성 멈춤",
            "empty" => "빈 응답",
            "shutdown" => "종료",
            _ => "오류(" + outcome + ")",
        };

        /// <summary>One line of outcomes, one of timings, then the busiest caller tags.</summary>
        public string Summary(int tags = 6)
        {
            var sb = new StringBuilder();
            sb.Append($"요청 {Total} · ");
            sb.Append(string.Join(" · ", _outcomes.OrderByDescending(kv => kv.Value).Select(kv => $"{Label(kv.Key)} {kv.Value}")));
            sb.Append($"\n대기 평균 {AverageWaitSeconds:0.0}s · 최장 {LongestWaitSeconds:0.0}s · 생성 평균 {AverageGenerateSeconds:0.0}s · 최대 대기열 {PeakQueue}");
            if (_byTag.Count > 0)
                sb.Append("\n").Append(string.Join(" · ", _byTag.OrderByDescending(kv => kv.Value.ok + kv.Value.fail).Take(tags)
                    .Select(kv => $"{kv.Key} {kv.Value.ok}/{kv.Value.ok + kv.Value.fail}")));
            return sb.ToString();
        }
    }
}
