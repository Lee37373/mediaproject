using System.Collections.Generic;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using UnityEngine;

namespace GuildMaster.Quests
{
    /// <summary><see cref="Disaster"/>: dealing with a natural disaster, posted by the town (P4-72).</summary>
    public enum QuestType { Hunt, Escort, Defend, Disaster }
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
        public Vector2 Point { get; }
        public float Radius { get; }
        public int RequiredCount { get; }
        public int Progress { get; internal set; }
        public int RewardGold { get; private set; }
        /// <summary>Gold added to the posted reward through negotiation (P4-25); already part of <see cref="RewardGold"/>.</summary>
        public int BonusGold { get; private set; }
        public int RewardExp { get; }
        public int MaxParty { get; }
        public QuestStatus Status { get; internal set; }
        public float PostedAtHour { get; }
        public float CompletedAtHour { get; internal set; }
        public DangerLevel Danger { get; internal set; }
        /// <summary>
        /// Hunt: the den in the area the guild had seen occupied when this was posted, or null (a hunt
        /// for strays). Once the guild sees it standing empty or held by others, the hunt is settled.
        /// </summary>
        public Nest TargetNest { get; internal set; }

        // ---- Natural disasters (P4-72) ----
        /// <summary>
        /// Posted and paid for by the town: no guild gold was escrowed, so nothing of the reward goes
        /// back to the treasury (only gold the guild itself added in a negotiation).
        /// </summary>
        public bool TownFunded { get; internal set; }
        /// <summary>The disaster to deal with.</summary>
        public Disasters.Disaster Disaster { get; internal set; }
        /// <summary>Cells (or cores) the workers have dealt with so far.</summary>
        public int Worked { get; internal set; }

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

        /// <summary>Raises the escrowed gold reward. The board takes the gold from the treasury first (<see cref="QuestBoard.TryRaiseReward"/>).</summary>
        internal void RaiseReward(int gold)
        {
            if (gold <= 0) return;
            RewardGold += gold;
            BonusGold += gold;
            PayGold += gold;
        }

        public IReadOnlyList<IQuestTaker> Assignees => _assignees;
        public bool IsActive => Status is QuestStatus.Open or QuestStatus.InProgress;
        public bool HasFreeSlot => IsActive && _assignees.Count < MaxParty;
        public int PendingPayouts => _payouts.Count;

        public Quest(string id, QuestDraft d, float postedAtHour)
        {
            Id = id;
            Type = d.Type;
            Species = d.Species;
            Point = d.Point;
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
            QuestType.Escort => $"길드 마스터 호위 (최대 {MaxHours}시간)",
            QuestType.Defend => "마을 방어",
            QuestType.Disaster when Disaster != null
                => $"{Disaster.Name} {Disasters.DisasterRules.WorkName(Disaster.Kind)} ({QuestBoard.DescribeArea(Point)}, 마을 의뢰)",
            _ => Id,
        };

        public string ProgressText => Type switch
        {
            QuestType.Hunt => HuntProgressText,
            QuestType.Escort => $"{EscortHours:0.0}/{MaxHours}시간",
            // Only the guild's own work: how much is left out there is not known (P4-72 information rule).
            QuestType.Disaster => Worked > 0 ? $"{Worked}곳 처리" : "작업 전",
            _ => "",
        };

        /// <summary>
        /// "2/5" plus the nest in the hunting area when the guild knows one (P4-32): clearing that
        /// nest finishes the quest even with kills to spare, so the player can see it.
        /// </summary>
        string HuntProgressText
        {
            get
            {
                string text = $"{Progress}/{RequiredCount}";
                var nest = QuestBoard.NestInArea(this);
                if (nest != null && Core.Services.TryGet<GuildIntel>(out var intel))
                    text += $" · {intel.NameOf(nest)} {intel.DescribeNest(nest)}";
                return text;
            }
        }

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

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        public class QuestSave
        {
            public string id;
            public QuestType type;
            public string species;
            public Vector2 point;
            public float radius;
            public int required;
            public int progress;
            public int reward_gold;
            public int bonus_gold;
            public int reward_exp;
            public int max_party;
            public QuestStatus status;
            public float posted;
            public float completed;
            public DangerLevel danger;
            public int target_nest = -1;
            public bool town_funded;
            public int disaster = -1;
            public int worked;
            public int max_hours;
            public float escort_hours;
            public bool escort_left;
            public int pay_gold;
            public int pay_exp;
            public List<string> assignees = new();
            public List<string> contribution_ids = new();
            public List<int> contribution = new();
            public List<string> payout_ids = new();
            public List<int> payout_gold = new();
            public List<int> payout_exp = new();
        }

        internal QuestSave CaptureSave(IReadOnlyList<Nest> nests)
        {
            var s = new QuestSave
            {
                id = Id, type = Type, species = Species != null ? Species.id : null, point = Point, radius = Radius, required = RequiredCount,
                progress = Progress, reward_gold = RewardGold, bonus_gold = BonusGold, reward_exp = RewardExp, max_party = MaxParty,
                status = Status, posted = PostedAtHour, completed = CompletedAtHour, danger = Danger,
                target_nest = TargetNest != null ? IndexOf(nests, TargetNest) : -1, town_funded = TownFunded,
                disaster = Disaster != null ? Disaster.Id : -1, worked = Worked, max_hours = MaxHours, escort_hours = EscortHours,
                escort_left = EscortLeftTown, pay_gold = PayGold, pay_exp = PayExp,
            };
            foreach (var a in _assignees) s.assignees.Add(a.TakerId);
            var ids = new List<string>(_contribution.Keys);
            ids.Sort(System.StringComparer.Ordinal);
            foreach (var id in ids) { s.contribution_ids.Add(id); s.contribution.Add(_contribution[id]); }
            ids = new List<string>(_payouts.Keys);
            ids.Sort(System.StringComparer.Ordinal);
            foreach (var id in ids) { s.payout_ids.Add(id); s.payout_gold.Add(_payouts[id].gold); s.payout_exp.Add(_payouts[id].exp); }
            return s;
        }

        static int IndexOf(IReadOnlyList<Nest> nests, Nest n)
        {
            for (int i = 0; i < nests.Count; i++) if (nests[i] == n) return i;
            return -1;
        }

        /// <summary>A saved quest as it stood (its takers and disaster come in <see cref="LinkSave"/>).</summary>
        internal Quest(QuestSave s, GameDatabase db, IReadOnlyList<Nest> nests)
        {
            Id = s.id;
            Type = s.type;
            Species = db.Monster(s.species);
            Point = s.point;
            Radius = s.radius;
            RequiredCount = s.required;
            Progress = s.progress;
            RewardGold = s.reward_gold;
            BonusGold = s.bonus_gold;
            RewardExp = s.reward_exp;
            MaxParty = s.max_party;
            Status = s.status;
            PostedAtHour = s.posted;
            CompletedAtHour = s.completed;
            Danger = s.danger;
            TargetNest = s.target_nest >= 0 && s.target_nest < nests.Count ? nests[s.target_nest] : null;
            TownFunded = s.town_funded;
            Worked = s.worked;
            MaxHours = s.max_hours;
            EscortHours = s.escort_hours;
            EscortLeftTown = s.escort_left;
            PayGold = s.pay_gold;
            PayExp = s.pay_exp;
            for (int i = 0; i < s.contribution_ids.Count && i < s.contribution.Count; i++) _contribution[s.contribution_ids[i]] = s.contribution[i];
            for (int i = 0; i < s.payout_ids.Count && i < s.payout_gold.Count && i < s.payout_exp.Count; i++)
                _payouts[s.payout_ids[i]] = (s.payout_gold[i], s.payout_exp[i]);
        }

        internal void LinkSave(QuestSave s, Core.SaveContext ctx)
        {
            foreach (var id in s.assignees) if (ctx.Find<IQuestTaker>(id) is { } t) _assignees.Add(t);
            if (s.disaster >= 0) Disaster = ctx.Find<Disasters.Disaster>(Disasters.Disaster.SaveKeyOf(s.disaster));
        }
    }
}
