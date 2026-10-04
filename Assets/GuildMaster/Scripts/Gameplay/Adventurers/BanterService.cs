using System.Collections.Generic;
using System.Threading.Tasks;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.LLM;
using GuildMaster.Recruitment;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Idle small talk (P3-5): every few game hours two members idling close together in town
    /// exchange a couple of lines (LLM, or a fixed pair). Only asks the LLM when the gateway is
    /// completely idle, so it never delays decisions, invitations or recruitment talks.
    /// A visitor waiting on the plaza can be paired with a member too (user request 2026-09-23):
    /// the town itself can win someone over, not just the guild master's formal dialogue. See
    /// <see cref="ApplyVisitorAffinity"/>.
    /// </summary>
    public class BanterService : MonoBehaviour, ISaveable
    {
        // Rebalanced (user report 2026-09-23: "너무 대화나 상호작용이 적은 느낌"), then rebalanced
        // again the same day ("애들이 마을 밖에 탐험 안나감 + 대화도 잘 안함"). The local LLM backend
        // serves one generation at a time (llama.cpp, single slot) and often cannot truly interrupt
        // a generation already in progress (see LlmGateway's stale-generation handling) — so every
        // banter attempt that gets preempted by a real quest decision can stall the WHOLE queue for
        // several seconds, which both delays adventurers leaving on quests and makes the banter that
        // triggered it fail anyway ("preempted"). The first pass (Chance 0.6→0.85) tripled how often
        // that collision could happen. Landing between the two: still checked hourly and with a much
        // wider pairing radius (fixes the original "nobody nearby" problem, which is not a source of
        // LLM contention by itself), but at a chance that roughly halves the collision risk versus
        // the first pass while still beating the original rate.
        public const int IntervalHours = 1;
        public const float Chance = 0.5f;
        public const float MaxPairDistance = 6f;
        /// <summary>Real seconds between A's line and B's answer.</summary>
        public const float ReplyDelay = 2.5f;

        LlmGateway _gateway;
        GuildRoster _roster;
        WorldService _world;
        VisitorService _visitors;
        int _hoursSinceTry;
        bool _inFlight;

        readonly List<(Adventurer speaker, string line, float at)> _scheduled = new();

        public int Attempts { get; private set; }
        public int LlmBanters { get; private set; }
        public int FixedBanters { get; private set; }

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SaveData { public int hours_since_try, attempts, llm, fixed_lines; }

        public string SaveKey => "banter";

        /// <summary>A talk under way (lines not yet said) is not saved.</summary>
        public string Capture() => JsonUtility.ToJson(new SaveData
        {
            hours_since_try = _hoursSinceTry, attempts = Attempts, llm = LlmBanters, fixed_lines = FixedBanters,
        });

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            _hoursSinceTry = s.hours_since_try;
            Attempts = s.attempts;
            LlmBanters = s.llm;
            FixedBanters = s.fixed_lines;
        }

        public void Init(LlmGateway gateway, GuildRoster roster, WorldService world)
        {
            _gateway = gateway;
            _roster = roster;
            _world = world;
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        /// <summary>
        /// Wired in after this component (GameBootstrap creates VisitorService later). Optional: without
        /// it banter simply stays guild-member-only, as before.
        /// </summary>
        public void SetVisitorService(VisitorService visitors) => _visitors = visitors;

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

        /// <summary>
        /// A talk somewhere other than town (P4-66: round a campfire). It happens once per camp, so a
        /// busy model does not skip it: the fixed lines stand in. <paramref name="stillThere"/> says
        /// whether a speaker is still at the scene when the lines come back.
        /// </summary>
        public bool TryStartAt(Adventurer a, Adventurer b, string scene, System.Func<Adventurer, bool> stillThere)
        {
            if (_inFlight || a == null || b == null || a == b) return false;
            bool llmReady = _gateway != null && _gateway.Enabled && _gateway.IsReady;
            Attempts++;
            if (!llmReady || _gateway.IsBusy || _gateway.QueueCount > 0) return Play(a, b, FixedCampPair(), false, stillThere);
            _ = RunAsync(a, b, scene, stillThere);
            return true;
        }

        async Task RunAsync(Adventurer a, Adventurer b, string scene = null, System.Func<Adventurer, bool> stillThere = null)
        {
            _inFlight = true;
            try
            {
                var result = await _gateway.RequestAsync(BanterPrompt.BuildRequest(a, b, scene));
                if (this == null) return;
                if (result.Ok && LlmJson.TryParse<BanterPrompt.ResponseDto>(result.Text, out var dto)
                    && BanterPrompt.TryInterpret(dto, out var lineA, out var lineB, a.Data.name, b.Data.name))
                {
                    if (result.Exchange != null) result.Exchange.Note = "채택";
                    Debug.Log($"[LLM-BANTER] {a.Data.name}: \"{lineA}\" / {b.Data.name}: \"{lineB}\" ({dto.a_to_b:+0;-0;0}/{dto.b_to_a:+0;-0;0}, {result.Seconds:0.0}s)");
                    if (Play(a, b, (lineA, lineB), true, stillThere))
                    {
                        Relationships.Add(a, b, BanterPrompt.ClampDelta(dto.a_to_b), "잡담");
                        Relationships.Add(b, a, BanterPrompt.ClampDelta(dto.b_to_a), "잡담");
                        ApplyVisitorAffinity(a, b, dto.a_to_b, dto.b_to_a);
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
                    Play(a, b, scene != null ? FixedCampPair() : FixedPair(), false, stillThere);
                }
            }
            finally
            {
                _inFlight = false;
            }
        }

        /// <summary>
        /// When one side of the chat is a visitor, their reaction to the guild member also nudges the
        /// same persistent affinity the formal recruit dialogue reads and writes
        /// (<see cref="Adventurers.AdventurerData.recruitAffinity"/> via <see cref="RecruitmentSession"/>),
        /// so a visitor can warm to the guild through the town itself. First contact seeds it exactly as
        /// opening a formal talk would; reaching <see cref="RecruitmentSession.AutoJoinAffinity"/> joins
        /// them on the spot, with no proposal needed.
        /// </summary>
        void ApplyVisitorAffinity(Adventurer a, Adventurer b, int aToB, int bToA)
        {
            var visitor = a.IsVisitor ? a : b.IsVisitor ? b : null;
            if (visitor == null || !visitor.IsVisitor) return; // may have joined/left meanwhile
            int delta = BanterPrompt.ClampDelta(visitor == a ? aToB : bToA);
            var data = visitor.Data;
            data.recruitAffinity = RecruitmentSession.NextRecruitAffinity(data.recruitAffinity, visitor.Traits, delta);
            if (data.recruitAffinity < RecruitmentSession.AutoJoinAffinity) return;
            if (!Services.TryGet<GuildRoster>(out var roster) || !roster.AddExisting(visitor)) return; // roster full
            EventBus.Publish(new RecruitmentEndedEvent { Visitor = visitor, Outcome = RecruitOutcome.Joined, Turns = data.recruitTurns });
        }

        /// <summary>Shows the two lines; false when either left before the model finished.</summary>
        bool Play(Adventurer a, Adventurer b, (string first, string second) lines, bool fromLlm, System.Func<Adventurer, bool> stillThere = null)
        {
            // They may have left for a quest while the model was writing.
            stillThere ??= StillChatting;
            if (!stillThere(a) || !stillThere(b)) return false;
            if (fromLlm) LlmBanters++; else FixedBanters++;
            float hour = Services.TryGet<GameClock>(out var clock) ? clock.TotalHours : 0f;
            DialogueLog.AddBanter(a.Data, b.Data, hour, lines.first, lines.second);
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

        bool AvailableMember(Adventurer x)
            => x != null && x.IsAlive && !x.IsVisitor && x.CurrentQuest == null
               && x.Fsm.Current == x.IdleState && _world.Grid.IsInTown(x.Position)
               && !(Services.TryGet<PartyService>(out var party) && party.IsInvited(x));

        /// <summary>A visitor free to chat: wandering the plaza, not already mid-talk with the guild master
        /// (that talk owns the same affinity field; the two never run at once).</summary>
        bool AvailableVisitor(Adventurer x)
            => x != null && x.IsAlive && x.IsVisitor && !x.InConversation
               && x.Fsm.Current == x.VisitorWanderState && _world.Grid.IsInTown(x.Position);

        /// <summary>A random pair of idle members within talking distance, or a member and a visitor.
        /// Never two visitors: there is no guild feeling to nudge there.</summary>
        (Adventurer, Adventurer)? PickPair()
        {
            var idle = new List<Adventurer>();
            foreach (var m in _roster.Members) if (AvailableMember(m)) idle.Add(m);
            var pairs = new List<(Adventurer, Adventurer)>();
            for (int i = 0; i < idle.Count; i++)
                for (int j = i + 1; j < idle.Count; j++)
                    if (Vector2.Distance(idle[i].Position, idle[j].Position) <= MaxPairDistance) pairs.Add((idle[i], idle[j]));

            if (_visitors != null)
                foreach (var v in _visitors.Visitors)
                {
                    if (!AvailableVisitor(v)) continue;
                    foreach (var m in idle)
                        if (Vector2.Distance(v.Position, m.Position) <= MaxPairDistance) pairs.Add((v, m));
                }

            if (pairs.Count == 0) return null;
            var p = pairs[GameRandom.Shared.Range(0, pairs.Count)];
            // Random speaking order.
            return GameRandom.Shared.Value < 0.5f ? p : (p.Item2, p.Item1);
        }

        static (string, string) FixedPair() => BanterPrompt.FixedPairs[GameRandom.Shared.Range(0, BanterPrompt.FixedPairs.Length)];
        static (string, string) FixedCampPair() => BanterPrompt.FixedCampPairs[GameRandom.Shared.Range(0, BanterPrompt.FixedCampPairs.Length)];
    }
}
