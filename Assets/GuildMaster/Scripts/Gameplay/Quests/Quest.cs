using System.Collections.Generic;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using UnityEngine;

namespace GuildMaster.Quests
{
    public enum QuestType { Hunt, ClearNest, Escort, Defend }
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
        /// <summary>Hunt: centre of the hunting area the player marked on the map. Escort: the guild master.</summary>
        public Vector2 Point;
        /// <summary>True once the player picked <see cref="Point"/> (hunts require it).</summary>
        public bool HasPoint;
        public float Radius;
        public int Count;
        public int RewardGold;
        public int RewardExp;
        public int MaxParty;
        /// <summary>Escort only: maximum paid hours (reward fields hold hourly rate × hours).</summary>
        public int Hours;
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
        readonly string _nestName;
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

        // ---- Escort (P3-4a) ----
        public int MaxHours { get; }
        /// <summary>Paid escort time so far (game hours outside town with an escort nearby).</summary>
        public float EscortHours { get; internal set; }
        /// <summary>True once the guild master has left town under escort.</summary>
        public bool EscortLeftTown { get; internal set; }

        /// <summary>Amounts actually paid out on completion (escort pays for time used; others pay the full reward).</summary>
        public int PayGold { get; private set; }
        public int PayExp { get; private set; }

        internal void SetPayout(int gold, int exp)
        {
            PayGold = Mathf.Clamp(gold, 0, RewardGold);
            PayExp = Mathf.Clamp(exp, 0, RewardExp);
        }

        public IReadOnlyList<IQuestTaker> Assignees => _assignees;
        public bool IsActive => Status is QuestStatus.Open or QuestStatus.InProgress;
        public bool HasFreeSlot => IsActive && _assignees.Count < MaxParty;
        public int PendingPayouts => _payouts.Count;

        public Quest(string id, QuestDraft d, float postedAtHour)
        {
            Id = id;
            Type = d.Type;
            // A clear-nest quest names the owners as the guild knew them when posting (GuildIntel).
            bool hasIntel = Core.Services.TryGet<GuildIntel>(out var intel);
            Species = d.Type == QuestType.ClearNest && d.Nest != null
                ? (hasIntel ? intel.SpeciesOf(d.Nest) : d.Nest.Species)
                : d.Species;
            Nest = d.Nest;
            _nestName = d.Nest == null ? null : hasIntel ? intel.NameOf(d.Nest) : d.Nest.DisplayName;
            Point = d.Type == QuestType.ClearNest && d.Nest != null ? d.Nest.WorldPosition : d.Point;
            Radius = d.Radius <= 0f ? 8f : d.Radius;
            RequiredCount = Mathf.Max(1, d.Count);
            RewardGold = Mathf.Max(0, d.RewardGold);
            RewardExp = Mathf.Max(0, d.RewardExp);
            MaxParty = Mathf.Clamp(d.MaxParty, 1, 4);
            MaxHours = d.Type == QuestType.Escort ? Mathf.Clamp(d.Hours, 1, 24) : 0;
            PayGold = RewardGold;
            PayExp = RewardExp;
            PostedAtHour = postedAtHour;
            Status = QuestStatus.Open;
        }

        public string Title => Type switch
        {
            QuestType.Hunt => $"{Species?.displayName} 토벌 x{RequiredCount} ({QuestBoard.DescribeArea(Point)})",
            QuestType.ClearNest => $"{_nestName} 소탕",
            QuestType.Escort => $"길드 마스터 호위 (최대 {MaxHours}시간)",
            QuestType.Defend => "마을 방어",
            _ => Id,
        };

        public string ProgressText => Type switch
        {
            QuestType.Hunt => $"{Progress}/{RequiredCount}",
            QuestType.ClearNest => Nest != null && Core.Services.TryGet<GuildIntel>(out var intel) ? intel.DescribeNest(Nest) : "-",
            QuestType.Escort => $"{EscortHours:0.0}/{MaxHours}시간",
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
            int goldLeft = PayGold, expLeft = PayExp;
            for (int i = 0; i < _assignees.Count; i++)
            {
                var a = _assignees[i];
                bool last = i == _assignees.Count - 1;
                int g = last ? goldLeft : Mathf.FloorToInt(PayGold * weights[a.TakerId] / total);
                int e = last ? expLeft : Mathf.FloorToInt(PayExp * weights[a.TakerId] / total);
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
