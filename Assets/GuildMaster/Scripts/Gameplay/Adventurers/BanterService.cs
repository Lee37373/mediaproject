using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Recruitment;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Idle small talk (P3-5): every few game hours two members idling close together in town exchange a couple of
    /// lines (<see cref="BanterLines"/>). A visitor waiting on the plaza can be paired with a member too (user request
    /// 2026-09-23). The model used to write the lines and nudge feelings with them; since P4-91 (user decision
    /// 2026-10-19) the talk is fixed lines only and changes no feelings.
    /// </summary>
    public class BanterService : MonoBehaviour, ISaveable
    {
        public const int IntervalHours = 1;
        public const float Chance = 0.5f;
        public const float MaxPairDistance = 6f;
        /// <summary>Real seconds between A's line and B's answer.</summary>
        public const float ReplyDelay = 2.5f;

        GuildRoster _roster;
        WorldService _world;
        VisitorService _visitors;
        int _hoursSinceTry;

        readonly List<(Adventurer speaker, string line, float at)> _scheduled = new();

        public int Attempts { get; private set; }
        /// <summary>Talks that were actually held (both still there).</summary>
        public int Talks { get; private set; }

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SaveData { public int hours_since_try, attempts, talks; }

        public string SaveKey => "banter";

        /// <summary>A talk under way (lines not yet said) is not saved.</summary>
        public string Capture() => JsonUtility.ToJson(new SaveData { hours_since_try = _hoursSinceTry, attempts = Attempts, talks = Talks });

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            _hoursSinceTry = s.hours_since_try;
            Attempts = s.attempts;
            Talks = s.talks;
        }

        public void Init(GuildRoster roster, WorldService world)
        {
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
            var pair = PickPair();
            if (pair == null) return false;
            var (a, b) = pair.Value;
            Attempts++;
            return Play(a, b, Pick(BanterLines.Town));
        }

        /// <summary>
        /// A talk somewhere other than town (P4-66: round a campfire). <paramref name="stillThere"/> says whether a
        /// speaker is still at the scene.
        /// </summary>
        public bool TryStartAt(Adventurer a, Adventurer b, System.Func<Adventurer, bool> stillThere)
        {
            if (a == null || b == null || a == b) return false;
            Attempts++;
            return Play(a, b, Pick(BanterLines.Camp), stillThere);
        }

        /// <summary>Shows the two lines; false when either is no longer there.</summary>
        bool Play(Adventurer a, Adventurer b, (string first, string second) lines, System.Func<Adventurer, bool> stillThere = null)
        {
            stillThere ??= StillChatting;
            if (!stillThere(a) || !stillThere(b)) return false;
            Talks++;
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

        /// <summary>A visitor free to chat: wandering the plaza, not already mid-talk with the guild master.</summary>
        bool AvailableVisitor(Adventurer x)
            => x != null && x.IsAlive && x.IsVisitor && !x.InConversation
               && x.Fsm.Current == x.VisitorWanderState && _world.Grid.IsInTown(x.Position);

        /// <summary>A random pair of idle members within talking distance, or a member and a visitor. Never two visitors.</summary>
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

        static (string, string) Pick((string a, string b)[] lines) => lines[GameRandom.Shared.Range(0, lines.Length)];
    }
}
