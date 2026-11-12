using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Economy;
using GuildMaster.Notifications;
using GuildMaster.Quests;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Social
{
    /// <summary>A resident's request was asked, taken on, done, failed or lapsed (the "!" marks, the window, the household list).</summary>
    public struct ResidentRequestEvent { public ResidentRequest Request; }

    /// <summary>A new visitor heard of the guild from the townsfolk (P4-134): its start affinity moved by this much.</summary>
    public struct VisitorHeardOfGuildEvent { public Adventurer Visitor; public int Delta; }

    /// <summary>One resident's request (P4-134).</summary>
    public class ResidentRequest
    {
        public int Id;
        public string ResidentId;
        public RequestKind Kind;
        /// <summary>Goods: the item and how many.</summary>
        public string ItemId;
        public int Count;
        /// <summary>Goods that are a monster's part: the kind it comes off (a member is sent to hunt for it).</summary>
        public string SpeciesId;
        /// <summary>Delivery: the village and what is carried.</summary>
        public int VillageId = -1;
        public string GoodsName;
        public float PostedAtHour;
        public float DeadlineHour;
        public RequestStatus Status;
        /// <summary>The quest of the member sent on it (goods dug or hunted for, a delivery), else null.</summary>
        public string QuestId;
        public string MemberId;
        public string MemberName;
        public float EndedAtHour = -1f;

        public bool IsOpen => Status == RequestStatus.Open;
        public bool IsActive => Status is RequestStatus.Open or RequestStatus.Underway;
    }

    /// <summary>
    /// The residents' requests (P4-134 F-14, user decision 2026-11-08): asked of the guild master directly — a "!" over the
    /// resident, the request window — and answered by handing the things over from the guild's stock or by sending one member
    /// (a gathering, parts or delivery errand for the resident, the F-5 kinds; or, for the child, a tale told on the spot).
    /// Done, the resident thanks the guild once (<see cref="ResidentRequestRules.ThanksOf"/>) and thinks better of it — which
    /// raises the standing favour of its trade (<see cref="ResidentPerks"/>); a member's failure costs opinion, a request left
    /// alone just lapses. The six residents' opinion on average is what new visitors hear of the guild
    /// (<see cref="ResidentRequestRules.VisitorWord"/>). Its own dice, so the shared ones are not moved. Saved as
    /// "resident_requests".
    /// </summary>
    public class ResidentRequests : IDisposable, ISaveable
    {
        /// <summary>Tests: off, residents ask only what a test makes them ask.</summary>
        public static bool Enabled = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Enabled = true;

        readonly QuestBoard _board;
        readonly ResidentService _residents;
        readonly GuildRoster _roster;
        readonly GuildTreasury _treasury;
        readonly GameClock _clock;
        readonly GameRandom _rng;
        readonly List<ResidentRequest> _requests = new();
        /// <summary>When each resident's last request ended (by resident id).</summary>
        readonly Dictionary<string, float> _lastEnded = new();
        int _nextId = 1;

        public int Done { get; private set; }
        public int Failed { get; private set; }
        /// <summary>The last visitor's start affinity change from what it heard in town (debug).</summary>
        public int LastVisitorWord { get; private set; }

        /// <summary>Open and underway requests, then the ones ended lately (a few kept for the windows).</summary>
        public IReadOnlyList<ResidentRequest> All => _requests;
        public IEnumerable<ResidentRequest> Active => _requests.Where(r => r.IsActive);

        const int KeepEnded = 12;

        public ResidentRequests(QuestBoard board, ResidentService residents, GuildRoster roster, GuildTreasury treasury, GameClock clock, int seed)
        {
            _board = board;
            _residents = residents;
            _roster = roster;
            _treasury = treasury;
            _clock = clock;
            _rng = new GameRandom(seed ^ 0x2E51D);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<QuestCompletedEvent>(OnCompleted);
            EventBus.Subscribe<QuestClosedEvent>(OnClosed);
            EventBus.Subscribe<Recruitment.VisitorArrivedEvent>(OnVisitor);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            EventBus.Unsubscribe<QuestCompletedEvent>(OnCompleted);
            EventBus.Unsubscribe<QuestClosedEvent>(OnClosed);
            EventBus.Unsubscribe<Recruitment.VisitorArrivedEvent>(OnVisitor);
        }

        // ---------------- Lookups ----------------

        public ResidentRequest Find(int id) => _requests.Find(r => r.Id == id);

        /// <summary>This resident's open or underway request, or null.</summary>
        public ResidentRequest ActiveOf(string residentId) => _requests.Find(r => r.IsActive && r.ResidentId == residentId);

        public Resident ResidentOf(ResidentRequest r) => r != null ? _residents?.Find(r.ResidentId) : null;

        public ResidentProfile ProfileOf(ResidentRequest r) => ResidentOf(r)?.Profile;

        /// <summary>The six residents' opinion of the guild on average.</summary>
        public int TownOpinion => _residents == null ? 0 : ResidentRequestRules.TownOpinion(_residents.Residents.Where(x => x != null).Select(x => x.Profile.GuildOpinion));

        /// <summary>What a visitor arriving now would hear: the town's word and the child's bragging.</summary>
        public int VisitorWordNow => ResidentRequestRules.VisitorWord(TownOpinion) + ResidentPerks.ChildBrag;

        public static string ItemName(string id) => Quest.ItemName(id);

        public string VillageName(ResidentRequest r) => VillageOf(r)?.DisplayName ?? "이웃 마을";

        static Villages.Village VillageOf(ResidentRequest r)
            => r != null && r.VillageId >= 0 && Services.TryGet<Villages.VillageService>(out var villages)
               ? villages.Villages.FirstOrDefault(v => v.Id == r.VillageId) : null;

        /// <summary>"철광석 x4", "빵 바구니 → 해마루 마을", "모험담".</summary>
        public string WhatText(ResidentRequest r) => r.Kind switch
        {
            RequestKind.Goods => ErrandRules.ItemCount(ItemName(r.ItemId), r.Count),
            RequestKind.Delivery => $"{r.GoodsName} → {VillageName(r)}",
            _ => "모험담 들려주기",
        };

        public string AskLine(ResidentRequest r)
        {
            var p = ProfileOf(r);
            return ResidentRequestRules.AskLine(p?.Job ?? ResidentJob.Child, r.Kind, ItemName(r.ItemId), r.Count, VillageName(r));
        }

        public float HoursLeft(ResidentRequest r) => r == null ? 0f : Mathf.Max(0f, r.DeadlineHour - _clock.TotalHours);

        // ---------------- Asking ----------------

        void OnHour(HourPassedEvent e) => Check();

        /// <summary>Lapses the overdue requests; then, by chance, one resident asks something (hourly; callable from debug).</summary>
        public ResidentRequest Check()
        {
            LapseOverdue();
            if (!Enabled || _residents == null) return null;
            if (!ResidentRequestRules.AskingHour(_clock.HourOfDay) || Active.Count() >= ResidentRequestRules.MaxOpen) return null;
            if (!_rng.Chance(ResidentRequestRules.ChancePerHour)) return null;
            foreach (var r in _residents.Residents.Where(x => x != null).OrderBy(_ => _rng.Value).ToList())
            {
                if (SkipReason(r) != null) continue;
                var request = Ask(r, null, out _);
                if (request != null) return request;
            }
            return null;
        }

        /// <summary>Why this resident would not ask now, or null.</summary>
        public string SkipReason(Resident r)
        {
            if (r == null) return "no resident";
            if (r.Asleep) return "asleep";
            if (ActiveOf(r.Profile.Id) != null) return "has a request";
            if (_lastEnded.TryGetValue(r.Profile.Id, out var ended) && _clock.TotalHours - ended < ResidentRequestRules.GapHours) return "asked lately";
            return null;
        }

        /// <summary>
        /// The resident asks one thing of the kinds it asks and there is a way to do (<paramref name="kind"/>: that one only).
        /// Null (and why) when nothing can be asked.
        /// </summary>
        public ResidentRequest Ask(Resident resident, RequestKind? kind, out string error)
        {
            error = null;
            if (resident == null) { error = "주민이 없습니다."; return null; }
            var job = resident.Profile.Job;
            var kinds = kind.HasValue ? new[] { kind.Value } : ResidentRequestRules.KindsOf(job).OrderBy(_ => _rng.Value).ToArray();
            foreach (var k in kinds)
            {
                var r = Make(resident, k);
                if (r == null) continue;
                Post(r, resident);
                return r;
            }
            error = "지금 부탁할 만한 일이 없습니다.";
            return null;
        }

        ResidentRequest Make(Resident resident, RequestKind kind)
        {
            var job = resident.Profile.Job;
            var r = new ResidentRequest { ResidentId = resident.Profile.Id, Kind = kind };
            switch (kind)
            {
                case RequestKind.Goods:
                    if (!PickGoods(job, r)) return null;
                    break;
                case RequestKind.Delivery:
                {
                    var v = NearestDestination();
                    if (v == null) return null;
                    r.VillageId = v.Id;
                    r.GoodsName = ResidentRequestRules.GoodsOf(job);
                    break;
                }
                default:
                    if (_roster == null || !_roster.Members.Any(m => m != null && m.IsAlive && !m.IsVisitor)) return null;
                    break;
            }
            return r;
        }

        /// <summary>
        /// Goods there is a way to come by: the resident's trade item (dug or picked at a spot the guild knows, or in the stock),
        /// else a part of a monster seen near town. As many as wanted where either way has them, else what there is (two at least).
        /// </summary>
        bool PickGoods(ResidentJob job, ResidentRequest r)
        {
            string trade = ResidentRequestRules.TradeItem(job);
            if (trade != null)
            {
                int supply = ErrandOffers.TryGather(trade == ErrandRules.Ore ? PlaceKind.OreVein : PlaceKind.HerbPatch, out var g) ? g.Supply : 0;
                if (Fit(r, trade, job, Mathf.Max(supply, _treasury.GetStock(trade)))) return true;
            }
            var dens = NearDens();
            if (dens.Count == 0) return false;
            var c = dens[_rng.Range(0, Mathf.Min(3, dens.Count))];
            if (!Fit(r, c.Entry.item.id, job, Mathf.Max(c.Supply, _treasury.GetStock(c.Entry.item.id)))) return false;
            r.SpeciesId = c.Species.id;
            return true;
        }

        bool Fit(ResidentRequest r, string itemId, ResidentJob job, int available)
        {
            int count = Mathf.Min(ResidentRequestRules.WantedCount(job, itemId, _rng), available);
            if (count < ErrandRules.MinCount) return false;
            r.ItemId = itemId;
            r.Count = count;
            return true;
        }

        /// <summary>The dens seen near town whose part can be asked for, nearest first.</summary>
        static List<ErrandOffers.Collect> NearDens()
        {
            var town = Services.TryGet<WorldService>(out var world) ? world.TownCenterWorld : Vector2.zero;
            return ErrandOffers.Collects(town, ResidentRequestRules.PartReach)
                .OrderBy(c => Vector2.Distance(c.Den.WorldPosition, town)).ToList();
        }

        static Villages.Village NearestDestination()
        {
            var town = Services.TryGet<WorldService>(out var world) ? world.TownCenterWorld : Vector2.zero;
            return ErrandOffers.Destinations().OrderBy(v => Vector2.Distance(v.WorldPosition, town)).FirstOrDefault();
        }

        void Post(ResidentRequest r, Resident resident)
        {
            r.Id = _nextId++;
            r.PostedAtHour = _clock.TotalHours;
            r.DeadlineHour = _clock.TotalHours + ResidentRequestRules.OpenHours;
            r.Status = RequestStatus.Open;
            _requests.Add(r);
            Trim();
            string who = ResidentRequestRules.Who(resident.Profile);
            EventBus.Publish(new TownSpeechEvent { Speaker = resident.transform, Text = AskLine(r) });
            if (Services.TryGet<Session.GameLog>(out var log))
                log.Add($"{who}의 부탁: {WhatText(r)} ({ResidentRequestRules.OpenHours:0}시간 안에)", Session.LogCategory.Quest);
            if (Services.TryGet<NotificationCenter>(out var center))
                center.Post(NotificationKind.Quest, $"{resident.Profile.Name}의 부탁", $"{who}: {WhatText(r)} — 주민을 눌러 들어줄 수 있습니다.",
                            "request:" + r.Id, NotificationLink.Resident(resident.Profile.Id));
            Changed(r);
        }

        void LapseOverdue()
        {
            foreach (var r in _requests.Where(r => r.IsOpen && _clock.TotalHours >= r.DeadlineHour).ToList())
            {
                End(r, RequestStatus.Lapsed);
                if (Services.TryGet<Session.GameLog>(out var log))
                    log.Add($"{ResidentRequestRules.Who(ProfileOf(r))}의 부탁({WhatText(r)})이 흐지부지되었습니다.");
            }
        }

        // ---------------- Answering: the stock ----------------

        /// <summary>Why the guild master cannot hand the things over from the stock now, or null.</summary>
        public string CannotHandOverReason(ResidentRequest r)
        {
            if (r == null || !r.IsOpen) return "지금 들어줄 수 있는 부탁이 아닙니다.";
            if (r.Kind != RequestKind.Goods) return "창고에서 건넬 수 있는 부탁이 아닙니다.";
            int have = _treasury.GetStock(r.ItemId);
            return have < r.Count ? $"창고에 {ItemName(r.ItemId)}이(가) 모자랍니다 ({have}/{r.Count})." : null;
        }

        public bool TryHandOver(ResidentRequest r, out string error)
        {
            error = CannotHandOverReason(r);
            if (error != null) return false;
            if (!_treasury.TryRemoveStock(r.ItemId, r.Count)) { error = "창고에서 꺼내지 못했습니다."; return false; }
            Fulfil(r, null, "길드 마스터가 창고에서 건넸습니다");
            return true;
        }

        // ---------------- Answering: a member ----------------

        /// <summary>A member who could be sent, and how risky the errand is for it alone (empty for a tale).</summary>
        public struct Candidate
        {
            public Adventurer Member;
            public string Risk;
        }

        /// <summary>
        /// The members who could take it on now, best fit first: in town with nothing on (for a tale, that is all), and for an
        /// errand fit to go out and not asked along by a party.
        /// </summary>
        public List<Candidate> Candidates(ResidentRequest r)
        {
            var list = new List<Candidate>();
            if (r == null || !r.IsOpen || _roster == null) return list;
            Quest probe = null;
            if (r.Kind != RequestKind.Story)
            {
                if (Plan(r, out var draft, out _, out _, out _) != null) return list;
                probe = new Quest("probe", draft, _clock.TotalHours);
                probe.Danger = _board.EvaluateDanger(probe);
            }
            Services.TryGet<PartyService>(out var party);
            foreach (var m in _roster.Members)
            {
                if (CannotSendReason(r, m, party) != null) continue;
                string risk = probe != null ? QuestRisk.Label(QuestRisk.Margin(AdventurerStats.Power(m.Data), 1, probe)) : "";
                list.Add(new Candidate { Member = m, Risk = risk });
            }
            return list
                .OrderBy(c => RiskOrder(c.Risk))
                .ThenByDescending(c => c.Member.Data.level)
                .ThenBy(c => c.Member.Data.name, StringComparer.Ordinal)
                .ToList();
        }

        static int RiskOrder(string risk) => risk switch
        {
            QuestRisk.Easy => 0, QuestRisk.Fair => 1, QuestRisk.Risky => 2, QuestRisk.Reckless => 3, _ => 0,
        };

        string CannotSendReason(ResidentRequest r, Adventurer m, PartyService party)
        {
            if (m == null || !m.IsAlive || m.IsVisitor || !m.IsGuildMember) return "길드원이 아닙니다.";
            if (!m.IsAtLeisureInTown) return $"{m.Data.name}은(는) 지금 마을에서 쉬고 있지 않습니다.";
            if (r.Kind == RequestKind.Story) return null;
            if (!m.IsAvailableForQuest) return $"{m.Data.name}은(는) 맡은 의뢰가 있습니다.";
            if (!Welfare.GuildWelfare.MayGoOut(m)) return $"{m.Data.name}은(는) 지금 나갈 수 있는 상태가 아닙니다.";
            if (party != null && party.IsInvited(m)) return $"{m.Data.name}은(는) 파티 초대를 받는 중입니다.";
            return null;
        }

        /// <summary>
        /// The errand a member would be sent on (null when it can go, else why not): the resident's goods dug at the spots the
        /// guild knows, or hunted off the kind near town; a delivery to its village. Half what a village pays, one member, no fee.
        /// </summary>
        public string Plan(ResidentRequest r, out QuestDraft draft, out Nest den, out Villages.Village village, out int count)
        {
            draft = default;
            den = null;
            village = null;
            count = 0;
            if (r == null) return "부탁이 없습니다.";
            switch (r.Kind)
            {
                case RequestKind.Delivery:
                    village = VillageOf(r);
                    if (village == null || !village.Known || village.SeenRuined || village.IsDestroyed) return "배달할 마을이 없습니다.";
                    draft = ErrandOffers.DeliverDraft(village, r.GoodsName, ResidentRequestRules.PayFactor);
                    break;
                case RequestKind.Goods when r.ItemId == ErrandRules.Ore || r.ItemId == ErrandRules.Herb:
                {
                    var kind = r.ItemId == ErrandRules.Ore ? PlaceKind.OreVein : PlaceKind.HerbPatch;
                    if (!ErrandOffers.TryGather(kind, out var g)) return r.ItemId == ErrandRules.Ore ? "캘 수 있는 광맥을 모릅니다." : "딸 수 있는 약초밭을 모릅니다.";
                    if (!ErrandOffers.GatherDraft(g, r.Count, ResidentRequestRules.PayFactor, out draft) || draft.Count < r.Count)
                        return $"아는 채집지에 {r.Count}개만큼 남아 있지 않습니다.";
                    break;
                }
                case RequestKind.Goods:
                {
                    var species = Services.TryGet<GameDatabase>(out var db) ? db.Monster(r.SpeciesId) : null;
                    var town = Services.TryGet<WorldService>(out var world) ? world.TownCenterWorld : Vector2.zero;
                    var dens = ErrandOffers.Collects().Where(c => c.Species == species && c.Entry.item.id == r.ItemId)
                                                      .OrderBy(c => Vector2.Distance(c.Den.WorldPosition, town)).ToList();
                    if (dens.Count == 0) return $"{species?.displayName ?? "그 몬스터"}의 둥지를 모릅니다.";
                    if (!ErrandOffers.CollectDraft(dens[0], r.Count, ResidentRequestRules.PayFactor, out draft) || draft.Count < r.Count)
                        return $"아는 둥지에서 {r.Count}개를 모으기 어렵습니다.";
                    den = dens[0].Den;
                    break;
                }
                default:
                    return null;
            }
            draft.MaxParty = 1;
            count = draft.Count;
            return null;
        }

        /// <summary>Sends this member on it (a tale: told at once). False (and why) when it cannot go.</summary>
        public bool TrySend(ResidentRequest r, Adventurer member, out string error)
        {
            error = r == null || !r.IsOpen ? "지금 들어줄 수 있는 부탁이 아닙니다." : CannotSendReason(r, member, Services.TryGet<PartyService>(out var p) ? p : null);
            if (error != null) return false;
            var profile = ProfileOf(r);
            if (r.Kind == RequestKind.Story)
            {
                Tell(r, member);
                return true;
            }
            error = Plan(r, out var draft, out var den, out var village, out _);
            if (error != null) return false;
            if (!_board.TryPostRequest(draft, QuestClient.Resident, out var quest, out error, den, null, false, village, r.ResidentId)) return false;
            if (!_board.TryAccept(quest, member))
            {
                _board.FailErrand(quest, ResidentRequestRules.GaveUpReason);
                error = $"{member.Data.name}이(가) 맡지 못했습니다.";
                return false;
            }
            member.CurrentQuest = quest;
            member.ChangeState(member.TravelState);
            r.Status = RequestStatus.Underway;
            r.QuestId = quest.Id;
            r.MemberId = member.Data.id;
            r.MemberName = member.Data.name;
            member.Say(ResidentRequestRules.SetOffLine(profile));
            AdventurerMemory.Add(member.Data.memories, _clock.TotalHours, 1, $"{ResidentRequestRules.Who(profile)}의 부탁({WhatText(r)})을 맡았다");
            if (Services.TryGet<Session.GameLog>(out var log))
                log.Add($"{member.Data.name}이(가) {ResidentRequestRules.Who(profile)}의 부탁을 맡아 나섭니다: {WhatText(r)}", Session.LogCategory.Quest);
            Changed(r);
            return true;
        }

        /// <summary>The child's tale (P4-134): told where the member is, and done.</summary>
        void Tell(ResidentRequest r, Adventurer member)
        {
            var chronicle = member.Data.chronicle;
            string moment = chronicle != null && chronicle.Count > 0 ? chronicle[chronicle.Count - 1].text : null;
            member.Say(ResidentRequestRules.TaleLine(moment, member.Data.level));
            if (ResidentOf(r) is { } child) EventBus.Publish(new TownSpeechEvent { Speaker = child.transform, Text = ResidentRequestRules.ChildAwedLine });
            r.MemberId = member.Data.id;
            r.MemberName = member.Data.name;
            Fulfil(r, member, $"{member.Data.name}이(가) 모험담을 들려주었습니다");
        }

        // ---------------- Done, failed ----------------

        void OnCompleted(QuestCompletedEvent e)
        {
            var r = ByQuest(e.Quest);
            if (r == null || r.Status != RequestStatus.Underway) return;
            Fulfil(r, _roster?.Find(r.MemberId), $"{r.MemberName}이(가) 해냈습니다");
        }

        void OnClosed(QuestClosedEvent e)
        {
            var r = ByQuest(e.Quest);
            if (r == null || r.Status != RequestStatus.Underway) return;
            if (e.Status == QuestStatus.Failed) Fail(r, e.Reason);
            // Taken down with nobody on it (it cannot be, but should it): the request just ends, nobody to blame.
            else if (e.Status == QuestStatus.Cancelled) End(r, RequestStatus.Lapsed);
        }

        ResidentRequest ByQuest(Quest q)
            => q == null || q.Client != QuestClient.Resident ? null : _requests.Find(r => r.QuestId == q.Id);

        void Fulfil(ResidentRequest r, Adventurer helper, string how)
        {
            var resident = ResidentOf(r);
            var profile = resident?.Profile;
            End(r, RequestStatus.Done);
            Done++;
            profile?.AddOpinion(ResidentRequestRules.OpinionWon);
            if (helper != null && helper.Data != null)
            {
                profile?.AddFeeling(helper.Data.id, ResidentRequestRules.FeelingForHelper);
                if (Services.TryGet<Welfare.GuildWelfare>(out var welfare)) welfare.AdjustStress(helper, -ResidentRequestRules.HelperStressRelief);
                AdventurerMemory.Add(helper.Data.memories, _clock.TotalHours, 1, $"{ResidentRequestRules.Who(profile)}의 부탁을 들어주고 고맙다는 말을 들었다");
            }
            var thanks = ResidentRequestRules.ThanksOf(profile?.Job ?? ResidentJob.Child, _rng.Value);
            string got = Give(thanks);
            if (resident != null) EventBus.Publish(new TownSpeechEvent { Speaker = resident.transform, Text = ResidentRequestRules.ThanksLine(profile.Job) });
            string who = ResidentRequestRules.Who(profile);
            if (Services.TryGet<Session.GameLog>(out var log))
                log.Add($"{who}의 부탁을 들어주었습니다 ({how}). 사례: {got} · 평판 +{ResidentRequestRules.OpinionWon}", Session.LogCategory.Economy);
            if (Services.TryGet<NotificationCenter>(out var center))
                center.Post(NotificationKind.Success, $"{profile?.Name}의 부탁 해결", $"{how}. 사례: {got} · {who} 평판 +{ResidentRequestRules.OpinionWon}",
                            "request-end:" + r.Id, NotificationLink.Resident(r.ResidentId));
        }

        /// <summary>Puts the thanks into the guild's hands; returns them in words.</summary>
        string Give(ResidentRequestRules.Thanks t)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(t.Item.itemId) && t.Item.count > 0)
            {
                _treasury.AddStock(t.Item.itemId, t.Item.count);
                parts.Add(ErrandRules.ItemCount(ItemName(t.Item.itemId), t.Item.count) + " → 창고");
            }
            if (t.Gold > 0)
            {
                _treasury.AddGold(t.Gold, GoldReason.Favor);
                parts.Add(t.Gold + "G");
            }
            if (t.Food > 0 && Services.TryGet<FoodStore>(out var food))
            {
                food.Add(t.Food);
                parts.Add($"식량 {t.Food}개");
            }
            return parts.Count == 0 ? "없음" : string.Join(", ", parts);
        }

        void Fail(ResidentRequest r, string reason)
        {
            var resident = ResidentOf(r);
            var profile = resident?.Profile;
            End(r, RequestStatus.Failed);
            Failed++;
            profile?.AddOpinion(-ResidentRequestRules.OpinionLost);
            if (resident != null) EventBus.Publish(new TownSpeechEvent { Speaker = resident.transform, Text = ResidentRequestRules.LetDownLine(profile.Job) });
            string who = ResidentRequestRules.Who(profile);
            string why = string.IsNullOrEmpty(reason) ? "" : $" ({reason})";
            if (Services.TryGet<Session.GameLog>(out var log))
                log.Add($"{who}의 부탁을 들어주지 못했습니다{why}. 평판 −{ResidentRequestRules.OpinionLost}", Session.LogCategory.Quest);
            if (Services.TryGet<NotificationCenter>(out var center))
                center.Post(NotificationKind.Warning, $"{profile?.Name}의 부탁 실패", $"{r.MemberName ?? "길드원"}이(가) 해내지 못했습니다{why}. {who} 평판 −{ResidentRequestRules.OpinionLost}",
                            "request-end:" + r.Id, NotificationLink.Resident(r.ResidentId));
        }

        void End(ResidentRequest r, RequestStatus status)
        {
            r.Status = status;
            r.EndedAtHour = _clock.TotalHours;
            _lastEnded[r.ResidentId] = _clock.TotalHours;
            Trim();
            Changed(r);
        }

        void Trim()
        {
            int ended = _requests.Count(r => !r.IsActive);
            while (ended > KeepEnded)
            {
                int i = _requests.FindIndex(r => !r.IsActive);
                if (i < 0) break;
                _requests.RemoveAt(i);
                ended--;
            }
        }

        static void Changed(ResidentRequest r) => EventBus.Publish(new ResidentRequestEvent { Request = r });

        // ---------------- The town's word to visitors ----------------

        /// <summary>A visitor arrives having heard the town on the guild (user decision ③: its start affinity).</summary>
        void OnVisitor(Recruitment.VisitorArrivedEvent e)
        {
            var v = e.Visitor;
            if (v == null || v.Data == null) return;
            int delta = VisitorWordNow;
            LastVisitorWord = delta;
            if (delta == 0) return;
            v.Data.recruitAffinity = Recruitment.RecruitmentSession.NextRecruitAffinity(v.Data.recruitAffinity, v.Traits, delta,
                                                                                      Recruitment.RecruitmentSession.LevelsAboveGuild(v));
            EventBus.Publish(new VisitorHeardOfGuildEvent { Visitor = v, Delta = delta });
            if (Services.TryGet<Session.GameLog>(out var log))
                log.Add(delta > 0
                    ? $"방문자 {v.Data.name}이(가) 마을에서 길드 칭찬을 듣고 왔습니다 (호감 +{delta})."
                    : $"방문자 {v.Data.name}이(가) 마을에서 길드 험담을 듣고 왔습니다 (호감 {delta}).");
        }

        // ---------------- Save ----------------

        [Serializable]
        class RequestData
        {
            public int id;
            public string resident;
            public RequestKind kind;
            public string item;
            public int count;
            public string species;
            public int village = -1;
            public string goods;
            public float posted;
            public float deadline;
            public RequestStatus status;
            public string quest;
            public string member;
            public string member_name;
            public float ended = -1f;
        }

        [Serializable]
        class SaveData
        {
            public int next_id = 1;
            public int done;
            public int failed;
            public List<RequestData> requests = new();
            public List<string> residents = new();
            public List<float> ended = new();
        }

        public string SaveKey => "resident_requests";

        public string Capture()
        {
            var s = new SaveData { next_id = _nextId, done = Done, failed = Failed };
            foreach (var r in _requests)
                s.requests.Add(new RequestData
                {
                    id = r.Id, resident = r.ResidentId, kind = r.Kind, item = r.ItemId, count = r.Count, species = r.SpeciesId,
                    village = r.VillageId, goods = r.GoodsName, posted = r.PostedAtHour, deadline = r.DeadlineHour, status = r.Status,
                    quest = r.QuestId, member = r.MemberId, member_name = r.MemberName, ended = r.EndedAtHour,
                });
            foreach (var kv in _lastEnded.OrderBy(k => k.Key, StringComparer.Ordinal)) { s.residents.Add(kv.Key); s.ended.Add(kv.Value); }
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            _requests.Clear();
            _lastEnded.Clear();
            _nextId = Mathf.Max(1, s.next_id);
            Done = s.done;
            Failed = s.failed;
            foreach (var d in s.requests)
            {
                if (d == null || _residents?.Find(d.resident) == null) { ctx.Warnings.Add("resident request of unknown resident " + d?.resident); continue; }
                _requests.Add(new ResidentRequest
                {
                    Id = d.id, ResidentId = d.resident, Kind = d.kind, ItemId = Blank(d.item), Count = d.count, SpeciesId = Blank(d.species),
                    VillageId = d.village, GoodsName = Blank(d.goods), PostedAtHour = d.posted, DeadlineHour = d.deadline, Status = d.status,
                    QuestId = Blank(d.quest), MemberId = Blank(d.member), MemberName = Blank(d.member_name), EndedAtHour = d.ended,
                });
            }
            for (int i = 0; i < s.residents.Count && i < s.ended.Count; i++) _lastEnded[s.residents[i]] = s.ended[i];
        }

        static string Blank(string s) => string.IsNullOrEmpty(s) ? null : s;
    }
}
