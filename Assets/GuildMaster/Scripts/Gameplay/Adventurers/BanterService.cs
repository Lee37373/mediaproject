using System.Collections.Generic;
using System.Threading.Tasks;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.LLM;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Idle small talk (P3-5): every few game hours two members idling close together in town
    /// exchange a couple of lines (LLM, or a fixed pair). Only asks the LLM when the gateway is
    /// completely idle, so it never delays decisions, invitations or recruitment talks.
    /// </summary>
    public class BanterService : MonoBehaviour
    {
        public const int IntervalHours = 2;
        public const float Chance = 0.6f;
        public const float MaxPairDistance = 4f;
        /// <summary>Real seconds between A's line and B's answer.</summary>
        public const float ReplyDelay = 2.5f;

        LlmGateway _gateway;
        GuildRoster _roster;
        WorldService _world;
        int _hoursSinceTry;
        bool _inFlight;

        readonly List<(Adventurer speaker, string line, float at)> _scheduled = new();

        public int Attempts { get; private set; }
        public int LlmBanters { get; private set; }
        public int FixedBanters { get; private set; }

        public void Init(LlmGateway gateway, GuildRoster roster, WorldService world)
        {
            _gateway = gateway;
            _roster = roster;
            _world = world;
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        void OnDestroy() => EventBus.Unsubscribe<HourPassedEvent>(OnHour);

        void OnHour(HourPassedEvent e)
        {
            if (++_hoursSinceTry < IntervalHours) return;
            _hoursSinceTry = 0;
            if (GameRandom.Shared.Value < Chance) TryStart();
        }

        /// <summary>Starts a banter now if a suitable pair exists. Returns false if nothing started.</summary>
        public bool TryStart()
        {
            if (_inFlight) return false;
            var pair = PickPair();
            if (pair == null) return false;
            var (a, b) = pair.Value;
            bool llmReady = _gateway != null && _gateway.Enabled && _gateway.IsReady;
            if (llmReady && (_gateway.IsBusy || _gateway.QueueCount > 0)) return false; // never queue behind real work
            Attempts++;
            if (!llmReady) return Play(a, b, FixedPair(), false);
            _ = RunAsync(a, b);
            return true;
        }

        async Task RunAsync(Adventurer a, Adventurer b)
        {
            _inFlight = true;
            try
            {
                var result = await _gateway.RequestAsync(BanterPrompt.BuildRequest(a, b));
                if (this == null) return;
                if (result.Ok && LlmJson.TryParse<BanterPrompt.ResponseDto>(result.Text, out var dto)
                    && BanterPrompt.TryInterpret(dto, out var lineA, out var lineB, a.Data.name, b.Data.name))
                {
                    if (result.Exchange != null) result.Exchange.Note = "채택";
                    Debug.Log($"[LLM-BANTER] {a.Data.name}: \"{lineA}\" / {b.Data.name}: \"{lineB}\" ({dto.a_to_b:+0;-0;0}/{dto.b_to_a:+0;-0;0}, {result.Seconds:0.0}s)");
                    if (Play(a, b, (lineA, lineB), true))
                    {
                        Relationships.Add(a, b, BanterPrompt.ClampDelta(dto.a_to_b), "잡담");
                        Relationships.Add(b, a, BanterPrompt.ClampDelta(dto.b_to_a), "잡담");
                    }
                }
                else if (result.Error == "preempted")
                {
                    // Yielded to real work; stay quiet rather than filling in a canned line.
                    if (result.Exchange != null) result.Exchange.Note = "양보: 더 중요한 요청 처리";
                }
                else
                {
                    if (result.Exchange != null) result.Exchange.Note = "고정 대사로 대체: " + (result.Ok ? "parse" : result.Error);
                    Play(a, b, FixedPair(), false);
                }
            }
            finally
            {
                _inFlight = false;
            }
        }

        /// <summary>Shows the two lines; false when either left before the model finished.</summary>
        bool Play(Adventurer a, Adventurer b, (string first, string second) lines, bool fromLlm)
        {
            // They may have left for a quest while the model was writing.
            if (!StillChatting(a) || !StillChatting(b)) return false;
            if (fromLlm) LlmBanters++; else FixedBanters++;
            a.Say(lines.first);
            _scheduled.Add((b, lines.second, Time.unscaledTime + ReplyDelay));
            return true;
        }

        void Update()
        {
            for (int i = _scheduled.Count - 1; i >= 0; i--)
            {
                var s = _scheduled[i];
                if (Time.unscaledTime < s.at) continue;
                _scheduled.RemoveAt(i);
                if (s.speaker != null && s.speaker.IsAlive) s.speaker.Say(s.line);
            }
        }

        bool StillChatting(Adventurer x)
            => x != null && x.IsAlive && x.CurrentQuest == null && _world.Grid.IsInTown(x.Position);

        bool Available(Adventurer x)
            => x != null && x.IsAlive && !x.IsVisitor && x.CurrentQuest == null
               && x.Fsm.Current == x.IdleState && _world.Grid.IsInTown(x.Position)
               && !(Services.TryGet<PartyService>(out var party) && party.IsInvited(x));

        /// <summary>A random pair of idle members within talking distance.</summary>
        (Adventurer, Adventurer)? PickPair()
        {
            var idle = new List<Adventurer>();
            foreach (var m in _roster.Members) if (Available(m)) idle.Add(m);
            var pairs = new List<(Adventurer, Adventurer)>();
            for (int i = 0; i < idle.Count; i++)
                for (int j = i + 1; j < idle.Count; j++)
                    if (Vector2.Distance(idle[i].Position, idle[j].Position) <= MaxPairDistance) pairs.Add((idle[i], idle[j]));
            if (pairs.Count == 0) return null;
            var p = pairs[GameRandom.Shared.Range(0, pairs.Count)];
            // Random speaking order.
            return GameRandom.Shared.Value < 0.5f ? p : (p.Item2, p.Item1);
        }

        static (string, string) FixedPair() => BanterPrompt.FixedPairs[GameRandom.Shared.Range(0, BanterPrompt.FixedPairs.Length)];
    }
}
