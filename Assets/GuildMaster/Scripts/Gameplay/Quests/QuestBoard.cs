using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Economy;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Quests
{
    public struct QuestPostedEvent { public Quest Quest; }
    public struct QuestAcceptedEvent { public Quest Quest; public IQuestTaker Taker; }
    public struct QuestAbandonedEvent { public Quest Quest; public IQuestTaker Taker; }
    public struct QuestProgressEvent { public Quest Quest; }
    public struct QuestCompletedEvent { public Quest Quest; }
    public struct QuestRewardPaidEvent { public Quest Quest; public IQuestTaker Taker; public int Gold; public int Exp; }
    /// <summary><see cref="Reason"/> is set when the board closed it on its own (null: the player or the rules as usual).</summary>
    public struct QuestClosedEvent { public Quest Quest; public QuestStatus Status; public string Reason; }
    public struct QuestRewardRaisedEvent { public Quest Quest; public int Gold; }

    /// <summary>
    /// The quest board: posts quests — a client's (P4-82: the town, a patron; the client pays and the guild
    /// keeps a fee) or the guild's own escort (gold escrowed) — lets adventurers accept/abandon them,
    /// tracks progress from game events, and pays out contribution-weighted rewards on turn-in.
    /// </summary>
    public class QuestBoard : MonoBehaviour, ISaveable, ISaveLink
    {
        const float TurnInDeadlineHours = 36f;

        readonly List<Quest> _quests = new();
        readonly List<Quest> _history = new();
        GuildTreasury _treasury;
        EcosystemManager _eco;
        GameClock _clock;
        int _nextId = 101;

        public IReadOnlyList<Quest> Quests => _quests;
        public IReadOnlyList<Quest> History => _history;

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SavedEscortShare
        {
            public string quest;
            public string taker;
            public float hours;
        }

        [System.Serializable]
        class SaveData
        {
            public int next_id;
            public List<Quest.QuestSave> quests = new();
            public List<Quest.QuestSave> history = new();
            /// <summary>Hours each escort has been on duty so far (the pay is split by them at the end).</summary>
            public List<SavedEscortShare> escort_shares = new();
        }

        SaveData _restoring;
        readonly List<(Quest quest, Quest.QuestSave saved)> _restoredQuests = new();

        public string SaveKey => "board";

        public string Capture()
        {
            var nests = new List<Nest>(_eco.Nests);
            var s = new SaveData { next_id = _nextId };
            foreach (var q in _quests) s.quests.Add(q.CaptureSave(nests));
            foreach (var q in _history) s.history.Add(q.CaptureSave(nests));
            foreach (var kv in _escortShares)
                foreach (var share in kv.Value)
                    if (kv.Key.IsAssigned(share.Key)) // the dead and the departed are paid nothing anyway
                        s.escort_shares.Add(new SavedEscortShare { quest = kv.Key.Id, taker = share.Key.TakerId, hours = share.Value });
            s.escort_shares.Sort((a, b) => a.quest != b.quest ? string.CompareOrdinal(a.quest, b.quest) : string.CompareOrdinal(a.taker, b.taker));
            return JsonUtility.ToJson(s);
        }

        /// <summary>Quests are registered under their own ids (members and disasters point at them).</summary>
        public void Restore(string json, SaveContext ctx)
        {
            _restoring = JsonUtility.FromJson<SaveData>(json);
            _nextId = _restoring.next_id;
            var db = Services.Get<GameDatabase>();
            var nests = new List<Nest>(_eco.Nests);
            _restoredQuests.Clear();
            foreach (var qs in _restoring.quests) _quests.Add(RestoreQuest(qs, db, nests, ctx));
            foreach (var qs in _restoring.history) _history.Add(RestoreQuest(qs, db, nests, ctx));
        }

        Quest RestoreQuest(Quest.QuestSave qs, GameDatabase db, List<Nest> nests, SaveContext ctx)
        {
            var q = new Quest(qs, db, nests);
            ctx.Register(q.Id, q);
            _restoredQuests.Add((q, qs));
            return q;
        }

        public void Link(SaveContext ctx)
        {
            foreach (var (q, qs) in _restoredQuests) q.LinkSave(qs, ctx);
            _restoredQuests.Clear();
            // Escort time already served still counts in the split (P4-76: it restarted from zero).
            _escortShares.Clear();
            foreach (var es in _restoring.escort_shares)
            {
                if (FindById(es.quest) is not { } q || ctx.Find<IQuestTaker>(es.taker) is not { } taker) continue;
                if (!_escortShares.TryGetValue(q, out var shares)) _escortShares[q] = shares = new Dictionary<IQuestTaker, float>();
                shares[taker] = es.hours;
            }
            _restoring = null;
        }

        public void Init(GuildTreasury treasury, EcosystemManager eco, GameClock clock)
        {
            _treasury = treasury;
            _eco = eco;
            _clock = clock;
            EventBus.Subscribe<UnitDiedEvent>(OnUnitDied);
            EventBus.Subscribe<NestClearedEvent>(OnNestCleared);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<RegionCleansedEvent>(OnRegionCleansed);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<UnitDiedEvent>(OnUnitDied);
            EventBus.Unsubscribe<NestClearedEvent>(OnNestCleared);
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            EventBus.Unsubscribe<RegionCleansedEvent>(OnRegionCleansed);
        }

        /// <summary>Why the board took a lord's quest down: someone else, or something else, got there first.</summary>
        public const string LordFellReason = "군주가 쓰러졌습니다";

        /// <summary>
        /// The guild learned a region lord fell (P4-83): its quest is done if a group was on it (they were there or on
        /// the way), else it comes down — there is nothing left to do.
        /// </summary>
        void OnRegionCleansed(RegionCleansedEvent e)
        {
            foreach (var q in _quests.ToList())
            {
                if (!q.IsActive || q.Type != QuestType.Hunt || q.Species != e.Lord) continue;
                if (q.Assignees.Count == 0) Close(q, QuestStatus.Cancelled, refund: true, LordFellReason);
                else Complete(q);
            }
        }

        // ---------------- Posting ----------------

        /// <summary>Why the guild cannot post this itself (escrowing the gold), or null.</summary>
        public string Validate(QuestDraft d) => Validate(d, QuestClient.Guild);

        /// <summary>
        /// Why this quest cannot be posted for that client, or null. P4-82 (user decision 2026-10-17): the guild
        /// itself only hires escorts and pays them in gold — it has no EXP to give; every other quest is a
        /// client's, who pays the whole reward, so the treasury is not asked.
        /// </summary>
        public string Validate(QuestDraft d, QuestClient client)
        {
            if (d.RewardGold < 0 || d.RewardExp < 0) return "보상은 0 이상이어야 합니다.";
            if (client == QuestClient.Guild)
            {
                if (d.Type != QuestType.Escort) return "길드가 직접 올리는 의뢰는 호위뿐입니다.";
                if (d.RewardGold <= 0) return "호위 보상(골드)을 정하세요.";
                if (d.RewardExp > 0) return "길드는 골드로만 보상합니다.";
                if (!_treasury.CanAfford(d.RewardGold)) return "길드 자금이 부족합니다.";
            }
            else if (d.RewardGold == 0 && d.RewardExp == 0) return "골드 또는 EXP 보상을 설정하세요.";
            switch (d.Type)
            {
                case QuestType.Hunt:
                    if (d.Species == null) return "대상 몬스터를 선택하세요.";
                    if (Services.TryGet<GuildIntel>(out var known) && !known.IsKnown(d.Species)) return "아직 확인하지 못한 몬스터입니다.";
                    if (d.Count < 1) return "토벌 수는 1 이상이어야 합니다.";
                    if (!d.HasPoint) return "사냥할 위치를 지도에서 지정하세요.";
                    if (HuntPointError(d.Point) is { } pointError) return pointError;
                    break;
                case QuestType.Escort:
                    if (d.Hours < 1 || d.Hours > 24) return "호위 시간은 1~24시간이어야 합니다.";
                    if (_quests.Any(q => q.IsActive && q.Type == QuestType.Escort)) return "이미 진행 중인 호위 의뢰가 있습니다.";
                    break;
            }
            return null;
        }

        /// <summary>Hunting area radius (world units) around the point the player marks.</summary>
        public const float HuntRadius = 8f;

        /// <summary>Why a hunting point is unusable (outside the map, inside town, no walkable ground), or null.</summary>
        public static string HuntPointError(Vector2 point)
        {
            if (!Services.TryGet<World.WorldService>(out var world)) return null;
            var cell = World.GridMap.WorldToCell(point);
            if (!world.Grid.InBounds(cell)) return "지도 밖입니다.";
            if (world.Grid.IsInTown(cell)) return "마을 안은 사냥 위치로 지정할 수 없습니다.";
            if (!world.Grid.TryFindNearestWalkable(cell, 3, out _)) return "걸어서 갈 수 없는 위치입니다.";
            return null;
        }

        /// <summary>"마을 북동쪽 23칸": where a point lies as seen from the town centre.</summary>
        public static string DescribeArea(Vector2 point)
        {
            if (!Services.TryGet<World.WorldService>(out var world)) return $"({point.x:0}, {point.y:0})";
            var d = point - world.TownCenterWorld;
            int dist = Mathf.RoundToInt(d.magnitude);
            if (dist < 3) return "마을 근처";
            string[] dirs = { "동쪽", "북동쪽", "북쪽", "북서쪽", "서쪽", "남서쪽", "남쪽", "남동쪽" };
            int i = Mathf.RoundToInt(Mathf.Atan2(d.y, d.x) / (Mathf.PI / 4f));
            return $"마을 {dirs[(i + 8) % 8]} {dist}칸";
        }

        /// <summary>The guild posts a quest of its own (an escort): the gold reward leaves the treasury now, held until it is paid or refunded.</summary>
        public bool TryPost(QuestDraft draft, out Quest quest, out string error)
        {
            quest = null;
            error = Validate(draft);
            if (error != null) return false;
            if (!_treasury.TrySpendGold(draft.RewardGold)) { error = "길드 자금이 부족합니다."; return false; }
            quest = Add(new Quest($"Q_{_nextId++}", draft, _clock.TotalHours));
            return true;
        }

        /// <summary>
        /// A client's quest (P4-82): <see cref="QuestDraft.RewardGold"/> is everything the client pays; the guild's
        /// fee (<see cref="QuestPricing.Split"/>) comes off it and the members share the rest. Nothing leaves the
        /// treasury. <paramref name="target"/> is the den a hunt is for (else the one the guild saw in its area).
        /// </summary>
        public bool TryPostRequest(QuestDraft draft, QuestClient client, out Quest quest, out string error, Nest target = null,
                                   Villages.Village village = null, bool villageDefence = false)
        {
            quest = null;
            error = client == QuestClient.Guild ? "의뢰처가 없습니다."
                  : client == QuestClient.Village && (village == null || village.IsDestroyed) ? "의뢰처 마을이 없습니다."
                  : Validate(draft, client);
            if (error != null) return false;
            quest = PostRequest(draft, client, target, null, village, villageDefence);
            return true;
        }

        /// <summary>Why the board took a village's quest down: the village is gone (P4-84).</summary>
        public const string VillageFellReason = "의뢰처 마을이 무너졌습니다";

        /// <summary>
        /// A village fell (P4-84): its quests nobody took come down — there is nobody left to pay. The ones under way
        /// go on (the gold was put up when they were posted); a defence is settled with its raid.
        /// </summary>
        public void WithdrawFromVillage(Villages.Village v)
        {
            foreach (var q in _quests.ToList())
                if (q.IsActive && q.Village == v && q.Assignees.Count == 0) Close(q, QuestStatus.Cancelled, refund: true, VillageFellReason);
        }

        /// <summary>
        /// The town posts a quest to deal with a natural disaster the guild has seen (P4-72, user
        /// decision 2026-10-03): the town pays, so nothing leaves the treasury.
        /// </summary>
        public Quest PostTownQuest(QuestDraft draft, Disasters.Disaster disaster)
            => PostRequest(draft, QuestClient.Town, null, disaster);

        /// <summary>
        /// The town posts a raid on the dragon's hoard the guild has seen (P4-93, user decision 2026-10-20): the town pays,
        /// and whatever the raider carries home is theirs to sell besides. Done once someone on it reaches into the hoard.
        /// </summary>
        public Quest PostTreasureQuest(QuestDraft draft) => PostRequest(draft, QuestClient.Town, null);

        /// <summary>
        /// A sudden incident's quest, posted when its rumour reaches the town (P4-85, user decision 2026-10-18): its
        /// client pays (the guild keeps its fee), and it is about the incident, not a den — so the species need not be
        /// known yet (the rumour names it) and the area is the rumour's rough circle.
        /// </summary>
        public Quest PostIncidentQuest(QuestDraft draft, Incidents.Incident incident, QuestClient client = QuestClient.Incident)
            => PostRequest(draft, client, null, null, null, false, incident);

        /// <summary>
        /// The incident is over without its quest done (P4-85: the rare one slipped away, the traveller died...): nobody is
        /// paid. Claimed, it failed; unclaimed, it simply comes down.
        /// </summary>
        public void FailIncidentQuest(Quest q, string reason)
        {
            if (q == null || !q.IsActive) return;
            Close(q, q.Assignees.Count > 0 ? QuestStatus.Failed : QuestStatus.Cancelled, refund: true, reason);
        }

        Quest PostRequest(QuestDraft draft, QuestClient client, Nest target, Disasters.Disaster disaster = null,
                          Villages.Village village = null, bool villageDefence = false, Incidents.Incident incident = null)
        {
            var (members, fee) = QuestPricing.Split(draft.RewardGold);
            draft.RewardGold = members;
            return Add(new Quest($"Q_{_nextId++}", draft, _clock.TotalHours)
            {
                Client = client, FeeGold = fee, Disaster = disaster, Village = village, IsVillageDefence = villageDefence, Incident = incident,
            }, target);
        }

        Quest Add(Quest quest, Nest target = null)
        {
            quest.Danger = EvaluateDanger(quest);
            // A village's defence is about its raiders, wherever their den is (P4-84); an incident is about itself (P4-85).
            if (quest.Type == QuestType.Hunt && !quest.IsVillageDefence && quest.Incident == null) quest.TargetNest = target ?? OccupiedNestInArea(quest);
            _quests.Add(quest);
            EventBus.Publish(new QuestPostedEvent { Quest = quest });
            return quest;
        }

        /// <summary>An active hunt is already out on this den (the town and the proposals post one per den).</summary>
        public bool HasActiveHuntOn(Nest n)
        {
            foreach (var q in _quests)
                if (q.IsActive && q.Type == QuestType.Hunt && q.TargetNest == n) return true;
            return false;
        }

        /// <summary>
        /// The disaster is over: claimed, the quest is done (dealt with, or died down while they were
        /// on it, as with a den that is gone); unclaimed, the town takes it down.
        /// </summary>
        public void SettleTownQuest(Quest q, string reasonIfUnclaimed)
        {
            if (q == null || !q.IsActive) return;
            if (q.Assignees.Count == 0) Close(q, QuestStatus.Cancelled, refund: true, reasonIfUnclaimed ?? "재해가 수습되었습니다");
            else Complete(q);
        }

        public DangerLevel EvaluateDanger(Quest q)
        {
            DangerLevel d = DangerLevel.Low;
            // An incident rates itself (the rumour says what it is, P4-85).
            if (q.Incident != null) return Incidents.IncidentRules.Danger(q.Incident);
            switch (q.Type)
            {
                case QuestType.Hunt:
                    d = HuntDanger(q.Species, q.RequiredCount, q.Point, q.Radius);
                    break;
                case QuestType.Escort:
                    d = DangerLevel.Low; // depends on where the guild master walks
                    break;
                case QuestType.Defend:
                    d = DangerLevel.Medium;
                    break;
                case QuestType.Disaster:
                    d = q.Disaster != null ? Disasters.DisasterRules.DangerOf(q.Disaster.Kind) : DangerLevel.Medium;
                    break;
                case QuestType.Treasure:
                    d = DangerLevel.High; // a dragon's lair, asleep or not
                    break;
            }
            return d;
        }

        /// <summary>
        /// Adds gold to an active quest's reward (negotiation, P4-25): the gold leaves the treasury now and
        /// is escrowed with the rest of the reward, so it is refunded if the quest is cancelled.
        /// </summary>
        public bool TryRaiseReward(Quest q, int gold)
        {
            if (q == null || !q.IsActive || gold <= 0 || !_treasury.TrySpendGold(gold)) return false;
            q.RaiseReward(gold);
            EventBus.Publish(new QuestRewardRaisedEvent { Quest = q, Gold = gold });
            return true;
        }

        /// <summary>Why the board says a quest was taken down by the guild master (P4-82).</summary>
        public const string RemovedReason = "길드가 게시판에서 내렸습니다";

        /// <summary>
        /// Whether the guild master may take this quest down now. A client's quest only while nobody has
        /// taken it (P4-82: once taken the work is the members' and the client's), and never a disaster
        /// (the town's emergency, P4-72), a raid on the dragon's hoard or the dragon's hunt (P4-98, user decision
        /// 2026-10-21: the town put the hunt back up within the hour). The guild's own (an escort) at any time.
        /// </summary>
        public static bool CanTakeDown(Quest q)
            => q != null && q.IsActive && (!q.FundedByClient || (q.Assignees.Count == 0 && q.Type != QuestType.Disaster && q.Type != QuestType.Treasure
                                                                 && !(q.Species != null && q.Species.isDragon)));

        /// <summary>
        /// Takes a quest off the board. A client's quest nobody took simply comes down (the town will not post
        /// that den again, <see cref="TownQuestPoster"/>; user decision 2026-10-17: no cap and no expiry, the
        /// player clears the board). The guild's own, nobody on it yet: the escrow comes back. Someone already
        /// took it: they are paid as if they had finished it, so the player cannot let adventurers
        /// do the work and pull the reward back at the last moment (2026-09-20 user report).
        /// Escorts pay for the hours actually escorted, which is what they were posted to pay.
        /// </summary>
        public bool Cancel(Quest q)
        {
            if (!CanTakeDown(q)) return false;
            if (q.FundedByClient)
            {
                Close(q, QuestStatus.Cancelled, refund: true, RemovedReason);
                return true;
            }
            if (q.Assignees.Count == 0)
            {
                Close(q, QuestStatus.Cancelled, refund: true);
                return true;
            }
            if (q.Type == QuestType.Escort)
            {
                _escortShares.TryGetValue(q, out var shares);
                CompleteEscort(q, shares ?? new Dictionary<IQuestTaker, float>());
                return true;
            }
            Complete(q);
            return true;
        }

        // ---------------- Adventurer API ----------------

        /// <summary>
        /// Quests a taker may pick up on their own: only ones nobody has claimed yet. A quest is
        /// done by one group (2026-09-20 user decision) — a lone adventurer counts as a group of
        /// one — so the rest of a party joins by invitation, not by walking up to the board.
        /// </summary>
        public IEnumerable<Quest> AvailableFor(IQuestTaker t)
            => _quests.Where(q => q.Status == QuestStatus.Open && q.Assignees.Count == 0);

        public Quest FindById(string id) => _quests.FirstOrDefault(q => q.Id == id);

        public Quest ActiveQuestOf(IQuestTaker t) => _quests.FirstOrDefault(q => q.IsAssigned(t) && q.Status != QuestStatus.TurnedIn);

        /// <summary>
        /// Takes the quest. Only the first taker claims it; everyone after that must have been
        /// invited by the one who claimed it (<paramref name="invited"/>), so a quest is never
        /// worked by two separate groups and two adventurers cannot claim the same one at once
        /// — their LLM decisions arrive seconds apart (2026-09-20 user report).
        /// </summary>
        public bool TryAccept(Quest q, IQuestTaker t, bool invited = false)
        {
            if (q == null || !q.HasFreeSlot || q.IsAssigned(t) || !t.IsAvailableForQuest) return false;
            if (q.Assignees.Count > 0 && !invited) return false;
            q.AddAssignee(t);
            if (q.Status == QuestStatus.Open) q.Status = QuestStatus.InProgress;
            EventBus.Publish(new QuestAcceptedEvent { Quest = q, Taker = t });
            return true;
        }

        public void Abandon(Quest q, IQuestTaker t)
        {
            if (q == null || !q.RemoveAssignee(t)) return;
            if (q.IsActive && q.Assignees.Count == 0) q.Status = QuestStatus.Open;
            EventBus.Publish(new QuestAbandonedEvent { Quest = q, Taker = t });
        }

        /// <summary>Called when an assignee reports at the guild. Returns true if a reward was paid.</summary>
        public bool TryTurnIn(Quest q, IQuestTaker t, out int gold, out int exp)
        {
            gold = exp = 0;
            if (q == null || q.Status != QuestStatus.Completed) return false;
            if (!q.TryTakePayout(t, out gold, out exp)) return false;
            q.RemoveAssignee(t);
            EventBus.Publish(new QuestRewardPaidEvent { Quest = q, Taker = t, Gold = gold, Exp = exp });
            if (q.PendingPayouts == 0) Close(q, QuestStatus.TurnedIn, refund: false);
            return true;
        }

        // ---------------- Progress tracking ----------------

        void OnUnitDied(UnitDiedEvent e)
        {
            if (e.Victim is not Monster m || e.Killer is not IQuestTaker taker) return;
            foreach (var q in _quests)
            {
                if (q.Status != QuestStatus.InProgress || !q.IsAssigned(taker)) continue;
                // A hunt for particular monsters (a rare variant, a horde, P4-85) counts only those.
                if (q.Type == QuestType.Hunt && q.Counts(m))
                {
                    q.Progress++;
                    q.AddContribution(taker, 1);
                    EventBus.Publish(new QuestProgressEvent { Quest = q });
                    if (q.Progress >= q.RequiredCount) Complete(q);
                }
            }
        }

        /// <summary>
        /// A hunt is also done when the area's nest is wiped out, even with kills to spare
        /// (2026-09-20 user decision): the point of the quest was that nest.
        /// </summary>
        void OnNestCleared(NestClearedEvent e)
        {
            // What lived there is no longer a guess once it has been wiped out, so this goes by the
            // real owners, not by GuildIntel: the quest is settled by what happened, not by hearsay.
            foreach (var q in _quests.ToList())
                if (q.Status == QuestStatus.InProgress && q.Type == QuestType.Hunt && q.Incident == null && IsInArea(q, e.Nest, e.Nest?.Species)) Complete(q);
        }

        /// <summary>
        /// The nest of the quest's species inside its hunting area, or null. Guild-facing (UI, prompts),
        /// so it goes by what the guild has seen — a den whose owners nobody has seen does not count.
        /// </summary>
        public static Nest NestInArea(Quest q)
        {
            if (q == null || q.Type != QuestType.Hunt || q.Incident != null || !Services.TryGet<GuildIntel>(out var intel)) return null;
            foreach (var n in intel.KnownNests)
                if (IsInArea(q, n, intel.SpeciesOf(n))) return n;
            return null;
        }

        /// <summary>Inside the quest's circle and held by its species; the caller says whose owners to go by.</summary>
        static bool IsInArea(Quest q, Nest n, MonsterDefinition owners)
        {
            if (n == null || q.Species == null || owners == null) return false;
            return owners == q.Species && Vector2.Distance(n.WorldPosition, q.Point) <= q.Radius;
        }

        // ---------------- Escort (P3-4a) ----------------

        /// <summary>Escorts within this distance of the guild master count as "on duty".</summary>
        public const float EscortRange = 8f;

        readonly Dictionary<Quest, Dictionary<IQuestTaker, float>> _escortShares = new();

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || !Services.TryGet<Player.PlayerAvatar>(out var master) || !master.IsAlive) return;
            bool outside = !Services.Get<World.WorldService>().Grid.IsInTown(master.Position);
            float hours = dt / _clock.RealSecondsPerGameHour;
            foreach (var q in _quests.ToList())
            {
                if (q.Type != QuestType.Escort || q.Status != QuestStatus.InProgress) continue;
                if (!_escortShares.TryGetValue(q, out var shares)) _escortShares[q] = shares = new Dictionary<IQuestTaker, float>();
                int onDuty = 0;
                foreach (var t in q.Assignees)
                {
                    if (t is not Units.Unit u || !u.IsAlive || Vector2.Distance(u.Position, master.Position) > EscortRange) continue;
                    onDuty++;
                    if (outside) { shares.TryGetValue(t, out var s); shares[t] = s + hours; }
                }
                if (outside && onDuty > 0)
                {
                    q.EscortLeftTown = true;
                    int before = Mathf.FloorToInt(q.EscortHours);
                    q.EscortHours = Mathf.Min(q.MaxHours, q.EscortHours + hours);
                    if (Mathf.FloorToInt(q.EscortHours) != before) EventBus.Publish(new QuestProgressEvent { Quest = q });
                }
                if ((q.EscortLeftTown && !outside) || q.EscortHours >= q.MaxHours) CompleteEscort(q, shares);
            }
        }

        /// <summary>Pays for the hours actually escorted (split by time on duty) and refunds the rest.</summary>
        void CompleteEscort(Quest q, Dictionary<IQuestTaker, float> shares)
        {
            _escortShares.Remove(q);
            // Everyone left or died on the way: nobody to pay, refund everything.
            if (q.Assignees.Count == 0) { Close(q, QuestStatus.Failed, refund: true); return; }
            var (gold, exp) = EscortPay(q.RewardGold, q.RewardExp, q.EscortHours, q.MaxHours);
            q.SetPayout(gold, exp);
            _treasury.AddGold(q.RewardGold - q.PayGold);
            foreach (var kv in shares) q.AddContribution(kv.Key, Mathf.RoundToInt(kv.Value * 10f));
            Complete(q);
        }

        /// <summary>Escort payment: escrow × (hours escorted / max hours), rounded.</summary>
        public static (int gold, int exp) EscortPay(int escrowGold, int escrowExp, float hours, int maxHours)
        {
            float frac = maxHours <= 0 ? 0f : Mathf.Clamp01(hours / maxHours);
            return (Mathf.RoundToInt(escrowGold * frac), Mathf.RoundToInt(escrowExp * frac));
        }

        void OnHour(HourPassedEvent e)
        {
            foreach (var q in _quests.ToList())
            {
                // Rewards not collected in time (e.g. the adventurer died) return to the guild.
                if (q.Status == QuestStatus.Completed && _clock.TotalHours - q.CompletedAtHour > TurnInDeadlineHours && q.Assignees.Count == 0)
                    Close(q, QuestStatus.TurnedIn, refund: true);
            }
            SettleHuntsWhoseNestIsGone();
        }

        // ---------------- A target den that is gone ----------------

        /// <summary>Why the board took a hunt down by itself.</summary>
        public const string NestGoneReason = "대상 둥지가 비었습니다";

        /// <summary>
        /// A hunt whose den the guild has since seen empty, or held by another kind, has nothing left
        /// to do (2026-09-23 user decision). <see cref="OnNestCleared"/> only hears of dens wiped out by
        /// killing while a group is on it: a den that was killed off before anyone took the quest, or
        /// that moved away (migration, a pack gone roaming, driven off), left the quest open forever
        /// and the board blocked for days. Unclaimed ones are taken down with a full refund; claimed
        /// ones are done, as with a wiped-out den. Only what the guild saw counts (GuildIntel).
        /// </summary>
        void SettleHuntsWhoseNestIsGone()
        {
            if (!Services.TryGet<GuildIntel>(out var intel)) return;
            foreach (var q in _quests.ToList())
            {
                if (!q.IsActive || q.Type != QuestType.Hunt || q.TargetNest == null) continue;
                if (!SeenGone(q, intel)) continue;
                if (q.Assignees.Count == 0) Close(q, QuestStatus.Cancelled, refund: true, NestGoneReason);
                else Complete(q);
            }
        }

        /// <summary>Seen since the posting with nobody of the quest's kind living there.</summary>
        public static bool SeenGone(Quest q, GuildIntel intel)
        {
            // A lord's quest is settled by its fall (OnRegionCleansed): its lair is its guards' kind (P4-83).
            if (q.Species != null && q.Species.isRegionLord) return false;
            var seen = intel.Get(q.TargetNest);
            if (!seen.Observed || seen.LastSeenHour <= q.PostedAtHour) return false;
            return seen.LastCount <= 0 || intel.SpeciesOf(q.TargetNest) != q.Species;
        }

        /// <summary>The known den of the quest's species in its area that the guild last saw occupied.</summary>
        static Nest OccupiedNestInArea(Quest q)
        {
            if (!Services.TryGet<GuildIntel>(out var intel)) return null;
            foreach (var n in intel.KnownNests)
                if (IsInArea(q, n, intel.SpeciesOf(n)) && intel.Get(n).LastCount > 0) return n;
            return null;
        }

        // ---------------- Danger from observations only ----------------

        /// <summary>Raises the danger by that many steps, never past 높음.</summary>
        public static DangerLevel Raise(DangerLevel d, int steps)
            => (DangerLevel)Mathf.Clamp((int)d + Mathf.Max(0, steps), (int)DangerLevel.Low, (int)DangerLevel.High);

        /// <summary>
        /// Hunt danger (P4-58: by the level the monsters there are born at, not the species), +1 for
        /// large hunts (8+). Pure; shared with the posting form's preview.
        /// </summary>
        public static DangerLevel HuntDanger(int level, int count)
            => Raise(MonsterLevel.DangerAt(level), count >= 8 ? 1 : 0);

        /// <summary>A hunt with no marked spot: rated as the nearest ring (level 1).</summary>
        public static DangerLevel HuntDanger(MonsterDefinition species, int count) => HuntDanger(1, count);

        /// <summary>Birth level at a spot (its distance from town is no secret), 1 without an ecosystem.</summary>
        static int LevelAt(Vector2 point)
            => Services.TryGet<EcosystemManager>(out var eco) ? eco.BirthLevelAt(point) : 1;

        /// <summary>
        /// Hunt danger at a marked spot: also +1 when the guild has seen armed monsters of that
        /// species at a known nest inside the hunting area (P4-22).
        /// </summary>
        public static DangerLevel HuntDanger(MonsterDefinition species, int count, Vector2 point, float radius)
            => Raise(HuntDanger(LevelAt(point), count), GearedNearby(species, point, radius) ? 1 : 0);

        /// <summary>Did the guild observe danger-raising gear on this species inside the hunting area?</summary>
        static bool GearedNearby(MonsterDefinition species, Vector2 point, float radius)
        {
            if (!Services.TryGet<GuildIntel>(out var intel)) return false;
            if (GuildIntel.RaisesDanger(intel.GearOfHomeless(species))) return true;
            foreach (var n in intel.KnownNests)
            {
                if (intel.SpeciesOf(n) != species) continue;
                if (Vector2.Distance(n.WorldPosition, point) > radius) continue;
                if (GuildIntel.RaisesDanger(intel.GearAt(n))) return true;
            }
            return false;
        }

        /// <summary>
        /// Clear-nest danger: the level its young are born at (P4-58), +1 when at least 5 were last observed there, and
        /// +1 when the guild saw armed monsters (2+) or a crown wearer there (P4-22).
        /// </summary>
        public static DangerLevel NestDanger(Nest nest)
        {
            bool hasIntel = Services.TryGet<GuildIntel>(out var intel);
            var d = MonsterLevel.DangerAt(nest.BirthLevel);
            int seen = hasIntel ? intel.Get(nest).LastCount : 0;
            bool geared = hasIntel && GuildIntel.RaisesDanger(intel.GearAt(nest));
            // Residents seen fighting above their level (scavenged smarts, 2026-09-23) read as one
            // step more dangerous, the same way seen gear does — otherwise the board would promise a
            // fight easier than the one the members will walk into.
            bool sharpened = hasIntel && intel.Get(nest).ScavengedSteps >= MonsterLevel.MaxScavengedTierSteps;
            return Raise(d, (seen >= 5 ? 1 : 0) + (geared ? 1 : 0) + (sharpened ? 1 : 0));
        }


        /// <summary>Removes a dead/departed taker from every quest.</summary>
        public void RemoveTakerEverywhere(IQuestTaker t)
        {
            foreach (var q in _quests.ToList())
            {
                if (!q.IsAssigned(t)) continue;
                if (q.Status == QuestStatus.Completed)
                {
                    q.RemoveAssignee(t);
                    // A share nobody will collect goes back to whoever paid it (a client keeps its own).
                    if (q.TryTakePayout(t, out var g, out _) && !q.FundedByClient) _treasury.AddGold(g);
                    if (q.PendingPayouts == 0) Close(q, QuestStatus.TurnedIn, refund: false);
                }
                else Abandon(q, t);
            }
        }

        void Complete(Quest q)
        {
            if (q.Status == QuestStatus.Completed) return;
            q.Status = QuestStatus.Completed;
            q.CompletedAtHour = _clock.TotalHours;
            q.ComputePayouts();
            // The client pays the guild its fee once the work is done (P4-82), whoever turns in afterwards.
            if (q.FundedByClient && q.FeeGold > 0) _treasury.AddGold(q.FeeGold, GoldReason.Commission);
            EventBus.Publish(new QuestCompletedEvent { Quest = q });
        }

        void Close(Quest q, QuestStatus status, bool refund, string reason = null)
        {
            if (refund && q.FundedByClient)
            {
                // Only what the guild itself added (negotiation) was guild gold.
                int left = q.RewardGold;
                if (q.Status == QuestStatus.Completed) (left, _) = q.DrainRemainingPayouts();
                _treasury.AddGold(Mathf.Min(left, q.BonusGold));
            }
            else if (refund)
            {
                // The guild's own quest: its escrowed gold comes back (it never paid in EXP).
                _treasury.AddGold(q.Status == QuestStatus.Completed ? q.DrainRemainingPayouts().gold : q.RewardGold);
            }
            q.Status = status;
            _quests.Remove(q);
            _history.Add(q);
            EventBus.Publish(new QuestClosedEvent { Quest = q, Status = status, Reason = reason });
        }
    }
}
