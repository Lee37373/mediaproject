using System.Collections.Generic;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using UnityEngine;

namespace GuildMaster.Quests
{
    /// <summary>
    /// <see cref="Disaster"/>: dealing with a natural disaster, posted by the town (P4-72). Saved by number: new kinds go at
    /// the end (Treasure: the dragon's hoard, P4-93; the errands P4-133: <see cref="Gather"/> ore or herbs from the known
    /// spots, <see cref="Collect"/> a monster's part, <see cref="Deliver"/> goods to another village — <see cref="ErrandRules"/>).
    /// </summary>
    public enum QuestType { Hunt, Escort, Defend, Disaster, Treasure, Gather, Collect, Deliver }
    public enum QuestStatus { Open, InProgress, Completed, TurnedIn, Cancelled, Failed }

    /// <summary>
    /// Who posts the quest and pays for it (P4-82). <see cref="Guild"/>: the guild itself, which escrows the gold
    /// (escorts only). <see cref="Town"/>: our town (its hunts on the dens the guild has seen, and disasters).
    /// <see cref="Patron"/>: another client whose request the guild master took from the proposal cards.
    /// Every client but the guild pays the whole reward and the guild keeps a fee (<see cref="QuestPricing"/>).
    /// <see cref="Village"/>: another village (P4-84) — hunts on the dens by it, and its defence when the guild sees it attacked.
    /// <see cref="Incident"/>: whoever a sudden incident troubles (P4-85, <see cref="Quest.Incident"/> names them).
    /// <see cref="Resident"/>: one of our townsfolk, whose request the guild master gave one member (P4-134,
    /// <see cref="Quest.ResidentId"/>): no fee — the resident thanks the guild itself — and nobody else takes it up.
    /// New clients go at the end: saves keep the number.
    /// </summary>
    public enum QuestClient { Guild, Town, Patron, Village, Incident, Resident }

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
        /// <summary>Gather/Collect (P4-133): the item to bring in; <see cref="Count"/> is how many.</summary>
        public string ItemId;
        /// <summary>Deliver (P4-133): what is carried (words only).</summary>
        public string GoodsName;
        /// <summary>An item the client pays besides the gold (P4-133), or none (the board picks one for a village or a patron).</summary>
        public ItemStack RewardItem;
    }

    public class Quest
    {
        readonly List<IQuestTaker> _assignees = new();
        readonly Dictionary<string, int> _contribution = new();
        readonly Dictionary<string, (int gold, int exp)> _payouts = new();

        public string Id { get; }
        public QuestType Type { get; }
        public MonsterDefinition Species { get; }
        /// <summary>Centre of the area. Fixed, but for an incident's rumoured spot that the guild later sees (P4-85).</summary>
        public Vector2 Point { get; internal set; }
        public float Radius { get; internal set; }
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

        /// <summary>
        /// Taken off the board by the guild after someone took it (QuestBoard.Cancel): the takers are paid, but the job
        /// was not done, so the client pays nothing (economy round 2, 2026-10-29).
        /// </summary>
        public bool CalledOff { get; internal set; }
        // ---- Client and fee (P4-82) ----
        public QuestClient Client { get; internal set; }
        /// <summary>
        /// Paid for by its client, not the guild: no guild gold was escrowed, so nothing of the reward goes
        /// back to the treasury (only gold the guild itself added in a negotiation).
        /// </summary>
        public bool FundedByClient => Client != QuestClient.Guild;
        /// <summary>The guild's cut of the client's gold, paid to the treasury when the work is done. <see cref="RewardGold"/> is what is left for the members.</summary>
        public int FeeGold { get; internal set; }

        public string ClientLabel => Client == QuestClient.Village && Village != null ? Village.DisplayName
                                   : Client == QuestClient.Incident && Incident != null ? Incidents.IncidentRules.ClientName(Incident.Kind)
                                   : Client == QuestClient.Resident && ResidentProfile != null ? Social.ResidentRequestRules.Who(ResidentProfile)
                                   : ClientName(Client);

        public static string ClientName(QuestClient c) => c switch
        {
            QuestClient.Guild => "길드",
            QuestClient.Town => "우리 마을",
            QuestClient.Village => "다른 마을",
            QuestClient.Resident => "주민",
            _ => "의뢰인",
        };

        // ---- A resident's request (P4-134) ----
        /// <summary>The resident who asked it (<see cref="QuestClient.Resident"/>), else null.</summary>
        public string ResidentId { get; internal set; }

        Social.ResidentProfile ResidentProfile
            => ResidentId != null && Core.Services.TryGet<Social.ResidentService>(out var residents) ? residents.Find(ResidentId)?.Profile : null;

        // ---- Other villages (P4-84) ----
        /// <summary>The village that posted and pays for it (<see cref="QuestClient.Village"/>), else null.</summary>
        public Villages.Village Village { get; internal set; }
        /// <summary>Its village's defence against a raid the guild saw: settled with the raid, not by a den.</summary>
        public bool IsVillageDefence { get; internal set; }

        // ---- Sudden incidents (P4-85) ----
        /// <summary>The incident this quest came from (its rumour), else null.</summary>
        public Incidents.Incident Incident { get; internal set; }
        /// <summary>
        /// The one monster a hunt is for (a rare variant): only it counts, wherever it is seen, and the area follows
        /// where the guild last saw it. Null for an ordinary hunt (any of the species in the area).
        /// </summary>
        public Monsters.Monster TargetUnit => Incident?.Target;

        /// <summary>
        /// A hunt for particular monsters (an incident's: the rare one, a horde's members, P4-85): only they count, wherever
        /// they are seen. Any other hunt counts its species inside its area.
        /// </summary>
        public bool HuntsParticularMonsters => Incident != null;

        /// <summary>Whether the hunt fights this monster (wherever it is, for particular monsters).</summary>
        public bool Fights(Monsters.Monster m) => m != null && (HuntsParticularMonsters ? Incident.IsTarget(m) : m.Def == Species);

        /// <summary>Whether killing this monster counts for the hunt (a ruin's quest is done by its treasure, not its guardians).</summary>
        public bool Counts(Monsters.Monster m) => Fights(m) && Incident is not { Kind: Incidents.IncidentKind.Ruin };

        // ---- Natural disasters (P4-72) ----
        /// <summary>The disaster to deal with.</summary>
        public Disasters.Disaster Disaster { get; internal set; }
        /// <summary>Cells (or cores) the workers have dealt with so far.</summary>
        public int Worked { get; internal set; }

        // ---- Errands (P4-133) ----
        /// <summary>Gather/Collect: the item brought in (iron ore, herbs, a monster's part); <see cref="Progress"/> counts it.</summary>
        public string ItemId { get; }
        /// <summary>Deliver: the village the goods go to (for a village's own errand also its client, <see cref="Village"/>).</summary>
        public Villages.Village Destination { get; internal set; }
        /// <summary>Deliver: what is carried (words only).</summary>
        public string GoodsName { get; }
        /// <summary>Deliver: the goods have left town with the takers; should every one of them fall or leave, the goods are lost.</summary>
        public bool GoodsOut { get; internal set; }
        /// <summary>The item the client pays besides the gold (P4-133, user decision: rare ones too), to the one who did most.</summary>
        public ItemStack RewardItem { get; internal set; }
        public bool HasRewardItem => !string.IsNullOrEmpty(RewardItem.itemId) && RewardItem.count > 0;
        /// <summary>Settled short (the den gone before all the parts came in): the pay is cut to what was brought, no item.</summary>
        public bool SettledShort { get; private set; }
        /// <summary>Collect: when the guild learned its den was gone (−1: not yet) — the parts lying there may still be picked up for a while.</summary>
        public float DenGoneAtHour { get; internal set; } = -1f;
        /// <summary>Who gets the item (the most done, chosen when the work is done), and whether it was handed over.</summary>
        string _itemFor;
        bool _itemGiven;
        public string ItemTakerId => _itemFor;

        /// <summary>The display name of an item, or its id.</summary>
        public static string ItemName(string id)
            => Core.Services.TryGet<GameDatabase>(out var db) && db.Item(id) is { } item ? item.displayName : id;

        public string ItemLabel => ItemName(ItemId);

        /// <summary>"2등급 장비", "체력 물약 x3": the item besides the gold, or empty.</summary>
        public string RewardItemLabel => HasRewardItem ? ErrandRules.ItemCount(ItemName(RewardItem.itemId), RewardItem.count) : "";

        /// <summary>Cuts what is paid to the share of the parts brought in (the client keeps the rest, and its item).</summary>
        internal void SettleShort(float share)
        {
            share = Mathf.Clamp01(share);
            SettledShort = true;
            PayGold = Mathf.RoundToInt(RewardGold * share);
            PayExp = Mathf.RoundToInt(RewardExp * share);
            FeeGold = Mathf.RoundToInt(FeeGold * share);
        }

        /// <summary>The one who did most gets the item (the first of them on a tie): chosen when the work is done.</summary>
        internal void ChooseItemTaker()
        {
            _itemFor = null;
            if (!HasRewardItem || SettledShort || _assignees.Count == 0) return;
            int best = -1;
            foreach (var a in _assignees)
            {
                _contribution.TryGetValue(a.TakerId, out var c);
                if (c <= best) continue;
                best = c;
                _itemFor = a.TakerId;
            }
        }

        /// <summary>Hands the item over to its taker as it reports (once).</summary>
        public bool TryTakeRewardItem(IQuestTaker t, out ItemStack item)
        {
            item = default;
            if (t == null || _itemGiven || _itemFor == null || _itemFor != t.TakerId || !HasRewardItem) return false;
            _itemGiven = true;
            item = RewardItem;
            return true;
        }

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
            // Four at most, but a region lord's or the dragon's quest takes two parties (P4-83, RegionLords.QuestParty; P4-93).
            MaxParty = Mathf.Clamp(d.MaxParty, 1, d.Species != null && (d.Species.isRegionLord || d.Species.isDragon) ? Monsters.RegionLords.QuestParty : 4);
            MaxHours = d.Type == QuestType.Escort ? Mathf.Clamp(d.Hours, 1, 24) : 0;
            PayGold = RewardGold;
            PayExp = RewardExp;
            PostedAtHour = postedAtHour;
            Status = QuestStatus.Open;
            ItemId = d.ItemId;
            GoodsName = d.GoodsName;
            RewardItem = d.RewardItem;
        }

        public string Title => Client == QuestClient.Resident ? ResidentTitle : BaseTitle;

        /// <summary>"[한스의 부탁] 철광석 x4 (마을 북쪽 12칸)": a resident's request goes by who asked it (P4-134).</summary>
        string ResidentTitle
        {
            get
            {
                string who = $"[{ResidentProfile?.Name ?? "주민"}의 부탁]";
                return Type switch
                {
                    QuestType.Deliver => $"{who} {GoodsName} → {Destination?.DisplayName ?? "다른 마을"} ({QuestBoard.DescribeArea(Point)})",
                    QuestType.Collect => $"{who} {ErrandRules.ItemCount(ItemLabel, RequiredCount)} · {Species?.displayName} ({QuestBoard.DescribeArea(Point)})",
                    _ => $"{who} {ErrandRules.ItemCount(ItemLabel, RequiredCount)} 채집 ({QuestBoard.DescribeArea(Point)})",
                };
            }
        }

        string BaseTitle => Type switch
        {
            QuestType.Hunt when Incident != null => Incidents.IncidentRules.QuestTitle(this),
            QuestType.Hunt when IsVillageDefence && Village != null => $"[방어] {Village.DisplayName} 습격 {Species?.displayName} x{RequiredCount} ({QuestBoard.DescribeArea(Point)})",
            QuestType.Hunt when Species != null && Species.isRegionLord => $"[지대 군주] {Species.displayName} 토벌 ({QuestBoard.DescribeArea(Point)})",
            QuestType.Hunt when Species != null && Species.isDragon => $"[드래곤] {Species.displayName} 토벌 ({QuestBoard.DescribeArea(Point)})",
            QuestType.Hunt => $"{Species?.displayName} 토벌 x{RequiredCount} ({QuestBoard.DescribeArea(Point)})",
            QuestType.Escort => $"길드 마스터 호위 (최대 {MaxHours}시간)",
            QuestType.Defend => "마을 방어",
            QuestType.Disaster when Disaster != null
                => $"{Disaster.Name} {Disasters.DisasterRules.WorkName(Disaster.Kind)} ({QuestBoard.DescribeArea(Point)}, 마을 의뢰)",
            QuestType.Treasure => $"[드래곤 둥지] 보물 회수 ({QuestBoard.DescribeArea(Point)})",
            QuestType.Gather => $"[채집] {ErrandRules.ItemCount(ItemLabel, RequiredCount)} ({QuestBoard.DescribeArea(Point)})",
            QuestType.Collect => $"[납품] {ErrandRules.ItemCount(ItemLabel, RequiredCount)} · {Species?.displayName} ({QuestBoard.DescribeArea(Point)})",
            QuestType.Deliver => $"[배달] {GoodsName} → {Destination?.DisplayName ?? "다른 마을"} ({QuestBoard.DescribeArea(Point)})",
            _ => Id,
        };

        public string ProgressText => Type switch
        {
            QuestType.Hunt when Incident is { Kind: Incidents.IncidentKind.Ruin } => Incidents.IncidentRules.RuinProgress(Incident),
            QuestType.Hunt => HuntProgressText,
            QuestType.Escort => $"{EscortHours:0.0}/{MaxHours}시간",
            // Only the guild's own work: how much is left out there is not known (P4-72 information rule).
            QuestType.Disaster => Worked > 0 ? $"{Worked}곳 처리" : "작업 전",
            QuestType.Treasure => Status == QuestStatus.Completed ? "보물을 챙김" : "둥지로",
            QuestType.Gather or QuestType.Collect => $"{Progress}/{RequiredCount}개" + (SettledShort ? " (모자람)" : ""),
            QuestType.Deliver => Status is QuestStatus.Completed or QuestStatus.TurnedIn ? "전달함"
                               : Status == QuestStatus.Failed ? "전하지 못함" : GoodsOut ? "짐을 들고 가는 중" : "출발 전",
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
            /// <summary>Read only from saves made before P4-82 (a disaster quest was the town's).</summary>
            public bool town_funded;
            public QuestClient client;
            public int fee_gold;
            public int village = -1;
            public bool village_defence;
            public int incident = -1;
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
            // Errands (P4-133).
            public string item_id;
            public string goods;
            public bool goods_out;
            public int destination = -1;
            public string reward_item;
            public int reward_item_count;
            public bool settled_short;
            public string item_for;
            public bool item_given;
            public float den_gone = -1f;
            // A resident's request (P4-134).
            public string resident;
        }

        internal QuestSave CaptureSave(IReadOnlyList<Nest> nests)
        {
            var s = new QuestSave
            {
                id = Id, type = Type, species = Species != null ? Species.id : null, point = Point, radius = Radius, required = RequiredCount,
                progress = Progress, reward_gold = RewardGold, bonus_gold = BonusGold, reward_exp = RewardExp, max_party = MaxParty,
                status = Status, posted = PostedAtHour, completed = CompletedAtHour, danger = Danger,
                target_nest = TargetNest != null ? IndexOf(nests, TargetNest) : -1, client = Client, fee_gold = FeeGold,
                village = Village != null ? Village.Id : -1, village_defence = IsVillageDefence, incident = Incident != null ? Incident.Id : -1,
                disaster = Disaster != null ? Disaster.Id : -1, worked = Worked, max_hours = MaxHours, escort_hours = EscortHours,
                escort_left = EscortLeftTown, pay_gold = PayGold, pay_exp = PayExp,
                item_id = ItemId, goods = GoodsName, goods_out = GoodsOut, destination = Destination != null ? Destination.Id : -1,
                reward_item = RewardItem.itemId, reward_item_count = RewardItem.count, settled_short = SettledShort,
                item_for = _itemFor, item_given = _itemGiven, den_gone = DenGoneAtHour, resident = ResidentId,
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
            Client = s.client == QuestClient.Guild && s.town_funded ? QuestClient.Town : s.client;
            FeeGold = s.fee_gold;
            IsVillageDefence = s.village_defence;
            Worked = s.worked;
            MaxHours = s.max_hours;
            EscortHours = s.escort_hours;
            EscortLeftTown = s.escort_left;
            PayGold = s.pay_gold;
            PayExp = s.pay_exp;
            ItemId = string.IsNullOrEmpty(s.item_id) ? null : s.item_id;
            GoodsName = string.IsNullOrEmpty(s.goods) ? null : s.goods;
            GoodsOut = s.goods_out;
            RewardItem = string.IsNullOrEmpty(s.reward_item) ? default : new ItemStack(s.reward_item, s.reward_item_count);
            SettledShort = s.settled_short;
            _itemFor = string.IsNullOrEmpty(s.item_for) ? null : s.item_for;
            _itemGiven = s.item_given;
            DenGoneAtHour = s.den_gone;
            ResidentId = string.IsNullOrEmpty(s.resident) ? null : s.resident;
            for (int i = 0; i < s.contribution_ids.Count && i < s.contribution.Count; i++) _contribution[s.contribution_ids[i]] = s.contribution[i];
            for (int i = 0; i < s.payout_ids.Count && i < s.payout_gold.Count && i < s.payout_exp.Count; i++)
                _payouts[s.payout_ids[i]] = (s.payout_gold[i], s.payout_exp[i]);
        }

        internal void LinkSave(QuestSave s, Core.SaveContext ctx)
        {
            foreach (var id in s.assignees) if (ctx.Find<IQuestTaker>(id) is { } t) _assignees.Add(t);
            if (s.disaster >= 0) Disaster = ctx.Find<Disasters.Disaster>(Disasters.Disaster.SaveKeyOf(s.disaster));
            if (s.village >= 0) Village = ctx.Find<Villages.Village>(Villages.Village.SaveKeyOf(s.village));
            if (s.destination >= 0) Destination = ctx.Find<Villages.Village>(Villages.Village.SaveKeyOf(s.destination));
            if (s.incident >= 0) Incident = ctx.Find<Incidents.Incident>(Incidents.Incident.SaveKeyOf(s.incident));
        }
    }
}
