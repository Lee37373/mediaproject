using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Quests;

namespace GuildMaster.Persuasion
{
    /// <summary>
    /// Persuasion of adventurers to take a quest nobody wants (P4-24). A quest that stays untaken for
    /// <see cref="PersuasionRules.OpenAfterHours"/> game hours opens persuasion: the guild master may talk to
    /// the free adventurers in town one by one (<see cref="GetSession"/>). Talks are kept per
    /// (quest, adventurer) so a closed window resumes where it stopped. There are no standing NPC parties in
    /// the game, so the talk is with individuals; once one accepts, the normal party invitation
    /// (<see cref="PartyService"/>) gathers the rest.
    /// </summary>
    public class PersuasionService : IDisposable
    {
        readonly QuestBoard _board;
        readonly GuildRoster _roster;
        readonly GameClock _clock;
        readonly Dictionary<string, PersuasionSession> _sessions = new();

        public PersuasionService(QuestBoard board, GuildRoster roster, GameClock clock)
        {
            _board = board;
            _roster = roster;
            _clock = clock;
            EventBus.Subscribe<QuestClosedEvent>(OnQuestClosed);
            EventBus.Subscribe<AdventurerDiedEvent>(OnAdventurerGone);
            EventBus.Subscribe<AdventurerLeftEvent>(OnAdventurerLeft);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<QuestClosedEvent>(OnQuestClosed);
            EventBus.Unsubscribe<AdventurerDiedEvent>(OnAdventurerGone);
            EventBus.Unsubscribe<AdventurerLeftEvent>(OnAdventurerLeft);
            _sessions.Clear();
        }

        /// <summary>True when this quest has stayed untaken long enough to persuade someone.</summary>
        public bool IsOpen(Quest q)
            => q != null && PersuasionRules.IsOpen(q.Status, q.Assignees.Count, q.PostedAtHour, _clock.TotalHours);

        /// <summary>Game hours until persuasion opens for an untaken quest (0 when open or no longer waiting).</summary>
        public float HoursUntilOpen(Quest q)
            => q == null ? 0f : PersuasionRules.HoursUntilOpen(q.PostedAtHour, _clock.TotalHours);

        /// <summary>
        /// Free guild members in town who could take the quest, best fit first (the ones who would find it
        /// easiest, then by name). Empty while the quest is not persuadable.
        /// </summary>
        public List<Adventurer> Candidates(Quest q)
        {
            var list = new List<Adventurer>();
            if (q == null || !q.HasFreeSlot) return list;
            Services.TryGet<PartyService>(out var party);
            foreach (var a in _roster.Members)
            {
                if (a == null || !a.IsAlive || a.IsVisitor || !a.IsAvailableForQuest || !a.IsAtLeisureInTown) continue;
                if (party != null && party.IsInvited(a)) continue;
                list.Add(a);
            }
            return list
                .OrderByDescending(a => QuestRisk.Margin(AdventurerStats.Power(a.Data), 1, q))
                .ThenBy(a => a.Data.name, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>The talk with this adventurer about this quest (created on first use, resumed afterwards).</summary>
        public PersuasionSession GetSession(Quest q, Adventurer a)
        {
            if (q == null || a == null) return null;
            string key = Key(q, a);
            if (!_sessions.TryGetValue(key, out var session))
                _sessions[key] = session = new PersuasionSession(a, q, _board, _clock);
            return session;
        }

        /// <summary>The existing talk, if any (does not create one).</summary>
        public PersuasionSession FindSession(Quest q, Adventurer a)
            => q != null && a != null && _sessions.TryGetValue(Key(q, a), out var s) ? s : null;

        static string Key(Quest q, Adventurer a) => q.Id + "|" + a.Data.id;

        void OnQuestClosed(QuestClosedEvent e)
        {
            if (e.Quest == null) return;
            string prefix = e.Quest.Id + "|";
            foreach (var key in _sessions.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                _sessions.Remove(key);
        }

        void OnAdventurerGone(AdventurerDiedEvent e) => Forget(e.Adventurer);
        void OnAdventurerLeft(AdventurerLeftEvent e) => Forget(e.Adventurer);

        void Forget(Adventurer a)
        {
            if (a == null || a.Data == null) return;
            string suffix = "|" + a.Data.id;
            foreach (var key in _sessions.Keys.Where(k => k.EndsWith(suffix, StringComparison.Ordinal)).ToList())
                _sessions.Remove(key);
        }
    }
}
