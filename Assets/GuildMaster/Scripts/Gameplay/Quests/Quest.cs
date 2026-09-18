using System.Collections.Generic;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using UnityEngine;

namespace GuildMaster.Quests
{
    public enum QuestType { Hunt, ClearNest, Explore, Defend }
    public enum QuestStatus { Open, InProgress, Completed, TurnedIn, Cancelled, Failed }

    /// <summary>Anyone who can take quests (adventurers).</summary>
    public interface IQuestTaker
    {
        string TakerId { get; }
        string TakerName { get; }
        bool IsAvailableForQuest { get; }
    }

    /// <summary>Player input for posting a quest.</summary>
    public struct QuestDraft
    {
        public QuestType Type;
        public MonsterDefinition Species;
        public Nest Nest;
        public Vector2 Point;
        public float Radius;
        public int Count;
        public int RewardGold;
        public int RewardExp;
        public int MaxParty;
    }

    public class Quest
    {
        readonly List<IQuestTaker> _assignees = new();
        readonly Dictionary<string, int> _contribution = new();
        readonly Dictionary<string, (int gold, int exp)> _payouts = new();

        public string Id { get; }
        public QuestType Type { get; }
        public MonsterDefinition Species { get; }
        public Nest Nest { get; }
        public Vector2 Point { get; }
        public float Radius { get; }
        public int RequiredCount { get; }
        public int Progress { get; internal set; }
        public int RewardGold { get; }
        public int RewardExp { get; }
        public int MaxParty { get; }
        public QuestStatus Status { get; internal set; }
        public float PostedAtHour { get; }
        public float CompletedAtHour { get; internal set; }
        public DangerLevel Danger { get; internal set; }

        public IReadOnlyList<IQuestTaker> Assignees => _assignees;
        public bool IsActive => Status is QuestStatus.Open or QuestStatus.InProgress;
        public bool HasFreeSlot => IsActive && _assignees.Count < MaxParty;
        public int PendingPayouts => _payouts.Count;

        public Quest(string id, QuestDraft d, float postedAtHour)
        {
            Id = id;
            Type = d.Type;
            Species = d.Type == QuestType.ClearNest && d.Nest != null ? d.Nest.Species : d.Species;
            Nest = d.Nest;
            Point = d.Type == QuestType.ClearNest && d.Nest != null ? d.Nest.WorldPosition : d.Point;
            Radius = d.Radius <= 0f ? 8f : d.Radius;
            RequiredCount = Mathf.Max(1, d.Count);
            RewardGold = Mathf.Max(0, d.RewardGold);
            RewardExp = Mathf.Max(0, d.RewardExp);
            MaxParty = Mathf.Clamp(d.MaxParty, 1, 4);
            PostedAtHour = postedAtHour;
            Status = QuestStatus.Open;
        }

        public string Title => Type switch
        {
            QuestType.Hunt => $"{Species?.displayName} 토벌 x{RequiredCount}",
            QuestType.ClearNest => $"{Nest?.DisplayName} 소탕",
            QuestType.Explore => $"탐사 ({Mathf.RoundToInt(Point.x)}, {Mathf.RoundToInt(Point.y)})",
            QuestType.Defend => "마을 방어",
            _ => Id,
        };

        public string ProgressText => Type switch
        {
            QuestType.Hunt => $"{Progress}/{RequiredCount}",
            QuestType.ClearNest => Nest != null ? $"남은 개체 {Nest.Population}" : "-",
            QuestType.Explore => $"{Progress}%",
            _ => "",
        };

        public static string DangerLabel(DangerLevel d) => d switch
        {
            DangerLevel.Low => "낮음",
            DangerLevel.Medium => "보통",
            _ => "높음",
        };

        public bool IsAssigned(IQuestTaker t) => _assignees.Contains(t);

        internal void AddAssignee(IQuestTaker t) { if (!_assignees.Contains(t)) _assignees.Add(t); }
        internal bool RemoveAssignee(IQuestTaker t) => _assignees.Remove(t);

        internal void AddContribution(IQuestTaker t, int amount)
        {
            _contribution.TryGetValue(t.TakerId, out var c);
            _contribution[t.TakerId] = c + amount;
        }

        /// <summary>Splits the escrowed reward among assignees by contribution (every assignee gets a base share).</summary>
        internal void ComputePayouts()
        {
            _payouts.Clear();
            if (_assignees.Count == 0) return;
            float baseWeight = 1f;
            float total = 0f;
            var weights = new Dictionary<string, float>();
            foreach (var a in _assignees)
            {
                _contribution.TryGetValue(a.TakerId, out var c);
                float w = baseWeight + c;
                weights[a.TakerId] = w;
                total += w;
            }
            int goldLeft = RewardGold, expLeft = RewardExp;
            for (int i = 0; i < _assignees.Count; i++)
            {
                var a = _assignees[i];
                bool last = i == _assignees.Count - 1;
                int g = last ? goldLeft : Mathf.FloorToInt(RewardGold * weights[a.TakerId] / total);
                int e = last ? expLeft : Mathf.FloorToInt(RewardExp * weights[a.TakerId] / total);
                goldLeft -= g;
                expLeft -= e;
                _payouts[a.TakerId] = (g, e);
            }
        }

        internal bool TryTakePayout(IQuestTaker t, out int gold, out int exp)
        {
            if (_payouts.TryGetValue(t.TakerId, out var p))
            {
                _payouts.Remove(t.TakerId);
                (gold, exp) = p;
                return true;
            }
            gold = exp = 0;
            return false;
        }

        internal (int gold, int exp) DrainRemainingPayouts()
        {
            int g = 0, e = 0;
            foreach (var p in _payouts.Values) { g += p.gold; e += p.exp; }
            _payouts.Clear();
            return (g, e);
        }
    }
}
