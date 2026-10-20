using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using GuildMaster.Notifications;
using GuildMaster.Quests;
using UnityEngine;

namespace GuildMaster.Welfare
{
    /// <summary>What came of one of the guild master's care actions.</summary>
    public readonly struct CareResult
    {
        public bool Ok { get; }
        /// <summary>What happened (or why it did not), for the management window.</summary>
        public string Message { get; }
        /// <summary>What the member answered, when they did.</summary>
        public string Reply { get; }

        CareResult(bool ok, string message, string reply)
        {
            Ok = ok;
            Message = message;
            Reply = reply;
        }

        public static CareResult Success(string message, string reply = null) => new(true, message, reply);
        public static CareResult Fail(string message) => new(false, message, null);
    }

    /// <summary>
    /// Stress and its consequences for guild members (F-2). Watches every member once per game hour
    /// (<see cref="MemberCare.Tick"/>), turns what it finds into notifications and effects on the adventurer,
    /// adds the stress of hard moments (failed quests, fallen friends, overwork), and offers the guild master's
    /// tools: interview, leave, bonus and dismissal. The rules live in <see cref="StressRules"/> and
    /// <see cref="CareRules"/>; this class is the glue to the game world.
    /// </summary>
    public class GuildWelfare : IDisposable, ISaveable
    {
        readonly GameClock _clock;
        readonly GuildRoster _roster;
        readonly GuildTreasury _treasury;
        readonly QuestBoard _board;
        readonly NotificationCenter _center;
        readonly Func<float> _roll;
        readonly Dictionary<string, MemberCare> _care = new();

        public GuildWelfare(GameClock clock, GuildRoster roster, GuildTreasury treasury, QuestBoard board,
                            NotificationCenter center, Func<float> roll = null)
        {
            _clock = clock;
            _roster = roster;
            _treasury = treasury;
            _board = board;
            _center = center;
            _roll = roll ?? (() => GameRandom.Shared.Value);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<QuestClosedEvent>(OnQuestClosed);
            EventBus.Subscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Subscribe<AdventurerLeftEvent>(OnLeft);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            EventBus.Unsubscribe<QuestClosedEvent>(OnQuestClosed);
            EventBus.Unsubscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Unsubscribe<AdventurerLeftEvent>(OnLeft);
            _care.Clear();
        }

        // ---------------- Save (P5-4) ----------------

        [Serializable]
        class SaveData { public List<MemberCare.CareSave> care = new(); }

        public string SaveKey => "welfare";

        public string Capture()
        {
            var s = new SaveData();
            var ids = new List<string>(_care.Keys);
            ids.Sort(StringComparer.Ordinal);
            foreach (var id in ids) s.care.Add(_care[id].Capture(id));
            return UnityEngine.JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = UnityEngine.JsonUtility.FromJson<SaveData>(json);
            _care.Clear();
            foreach (var c in s.care) _care[c.member] = MemberCare.Restore(c);
        }

        static string DemandKey(string id) => "demand:" + id;
        static string ResignKey(string id) => "resign:" + id;

        /// <summary>The care record of a member (created on first use).</summary>
        public MemberCare Care(Adventurer a)
        {
            string id = a.Data.id;
            if (!_care.TryGetValue(id, out var c)) _care[id] = c = new MemberCare();
            return c;
        }

        public bool IsOnLeave(Adventurer a)
            => a != null && a.Data != null && _care.TryGetValue(a.Data.id, out var c) && c.OnLeave(_clock.TotalHours);

        // ---------------- What stress and leave mean for quest decisions ----------------

        /// <summary>Why this member takes no quest at all right now (leave, breaking point); null = they may.</summary>
        public static string AnyQuestRefusal(Adventurer a)
        {
            if (a == null || a.Data == null) return null;
            if (Services.TryGet<GuildWelfare>(out var w) && w.IsOnLeave(a)) return "휴가 중이라 쉬고 있다";
            // A badly wounded member (F-6) takes no quest until the wound heals or is treated.
            if (Legacy.GuildLegacy.WoundedRefusal(a) is { } wounded) return wounded;
            if (StressRules.LevelOf(a.Data.stress) == StressLevel.Breaking) return "지쳐서 의뢰를 맡을 기분이 아니다";
            return null;
        }

        /// <summary>Why this member will not take a quest of this danger right now; null = they may.</summary>
        public static string QuestRefusal(Adventurer a, DangerLevel danger)
        {
            string any = AnyQuestRefusal(a);
            if (any != null) return any;
            if (a != null && a.Data != null && !StressRules.AllowsQuest(a.Data.stress, danger))
                return "스트레스가 커서 위험한 의뢰는 피하고 싶다";
            return null;
        }

        // ---------------- The guild master's tools ----------------

        public int BonusCost(Adventurer a) => CareRules.BonusCost(a.Data.level);

        /// <summary>
        /// Fit to go out at all — a quest, a hunt, a party's outing: not on leave, not badly wounded,
        /// not at the breaking point (P4-69). A hunt is the same risk as a quest; the refusal used to
        /// be checked for quests only, so members sent home to rest walked out to hunt instead.
        /// </summary>
        public static bool MayGoOut(Adventurer a) => AnyQuestRefusal(a) == null;

        static string NotAMember(Adventurer a)
            => a == null || a.Data == null || !a.IsAlive || a.IsVisitor || !a.IsGuildMember ? "길드원이 아닙니다." : null;

        public CareCheck CanInterview(Adventurer a)
            => NotAMember(a) is { } no ? CareCheck.No(no) : Care(a).CanInterview(_clock.TotalHours);

        public CareCheck CanGrantLeave(Adventurer a)
            => NotAMember(a) is { } no ? CareCheck.No(no)
             : Care(a).CanGrantLeave(_clock.TotalHours, a.IsAtLeisureInTown && a.CurrentQuest == null);

        public CareCheck CanGiveBonus(Adventurer a)
        {
            if (NotAMember(a) is { } no) return CareCheck.No(no);
            var check = Care(a).CanGiveBonus(_clock.TotalHours);
            if (!check.Ok) return check;
            int cost = BonusCost(a);
            return _treasury.Gold >= cost ? CareCheck.Yes : CareCheck.No($"길드 자금이 부족합니다. (필요 {cost}G)");
        }

        /// <summary>Talks to the member: relief that depends on their personality, more when they asked for it.</summary>
        public CareResult Interview(Adventurer a)
        {
            var check = CanInterview(a);
            if (!check.Ok) return CareResult.Fail(check.Reason);
            var d = a.Data;
            var c = Care(a);
            float relief = CareRules.InterviewRelief(d.values, d.mbti);
            if (c.RecordInterview(_clock.TotalHours)) relief *= CareRules.DemandMetFactor;
            int before = Mathf.RoundToInt(d.stress);
            Relieve(a, c, relief);
            string reply = CareTexts.InterviewReply(d.values, GameRandom.Shared);
            a.Say(reply);
            Remember(a, 2, CareTexts.MemoryTalked, "care-talk");
            SettleDemand(a, c);
            return CareResult.Success(CareTexts.Relieved("면담", relief, before, Mathf.RoundToInt(d.stress)), reply);
        }

        /// <summary>Sends the member to rest for <see cref="CareRules.LeaveHours"/>; they take no quest until it is over.</summary>
        public CareResult GrantLeave(Adventurer a)
        {
            var check = CanGrantLeave(a);
            if (!check.Ok) return CareResult.Fail(check.Reason);
            var d = a.Data;
            var c = Care(a);
            float relief = CareRules.LeaveRelief;
            if (c.RecordLeave(_clock.TotalHours)) relief *= CareRules.DemandMetFactor;
            int before = Mathf.RoundToInt(d.stress);
            Relieve(a, c, relief);
            a.Say(CareTexts.LeaveReply);
            a.ChangeState(a.RestState);
            Remember(a, 2, CareTexts.MemoryLeave, "care-leave");
            SettleDemand(a, c);
            return CareResult.Success(
                $"{CareRules.LeaveHours:0}시간 휴가를 주었습니다. 스트레스 -{relief:0} ({before} → {Mathf.RoundToInt(d.stress)})", CareTexts.LeaveReply);
        }

        /// <summary>Pays the member a bonus out of the guild's gold.</summary>
        public CareResult GiveBonus(Adventurer a)
        {
            var check = CanGiveBonus(a);
            if (!check.Ok) return CareResult.Fail(check.Reason);
            var d = a.Data;
            int cost = BonusCost(a);
            if (!_treasury.TrySpendGold(cost, GoldReason.Bonus)) return CareResult.Fail($"길드 자금이 부족합니다. (필요 {cost}G)");
            d.gold += cost;
            var c = Care(a);
            float relief = CareRules.BonusRelief(d.values);
            if (c.RecordBonus(_clock.TotalHours)) relief *= CareRules.DemandMetFactor;
            int before = Mathf.RoundToInt(d.stress);
            Relieve(a, c, relief);
            string reply = CareTexts.BonusReply(d.values);
            a.Say(reply);
            Remember(a, 2, CareTexts.MemoryBonus(cost), "care-bonus");
            SettleDemand(a, c);
            return CareResult.Success(CareTexts.BonusResult(cost, relief, before, Mathf.RoundToInt(d.stress)), reply);
        }

        /// <summary>Dismisses the member (what they carry goes with them). Friends of theirs take it badly.</summary>
        public CareResult Fire(Adventurer a)
        {
            if (NotAMember(a) is { } no) return CareResult.Fail(no);
            string name = a.Data.name;
            AfterDeparture(a);
            _roster.Dismiss(a);
            return CareResult.Success(CareTexts.Fired(name));
        }

        // ---------------- Stress ----------------

        static void AddStress(Adventurer a, float amount)
        {
            if (a == null || a.Data == null || amount == 0f) return;
            a.Data.stress = Mathf.Clamp(a.Data.stress + amount, 0f, 100f);
        }

        void Relieve(Adventurer a, MemberCare c, float relief)
        {
            a.Data.stress = Mathf.Max(0f, a.Data.stress - relief);
            if (c.Reassure(a.Data.stress)) _center?.Resolve(ResignKey(a.Data.id));
        }

        /// <summary>After an action: an answered demand is no longer pending.</summary>
        void SettleDemand(Adventurer a, MemberCare c)
        {
            if (c.Demand == CareDemand.None) _center?.Resolve(DemandKey(a.Data.id));
        }

        void Remember(Adventurer a, int importance, string text, string key = null)
        {
            if (a == null || a.Data == null) return;
            AdventurerMemory.Add(a.Data.memories, _clock.TotalHours, importance, text, key);
        }

        /// <summary>The members who counted the leaver as a friend take it hard.</summary>
        void AfterDeparture(Adventurer gone)
        {
            foreach (var m in _roster.Members.ToList())
            {
                if (m == null || m == gone || !m.IsAlive) continue;
                if (Relationships.Get(m, gone) < StressRules.FriendRelation) continue;
                AddStress(m, StressRules.FriendLeftStress);
                Remember(m, 2, CareTexts.MemoryFriendLeft(gone.Data.name), "friend-left:" + gone.Data.id);
            }
        }

        // ---------------- Events ----------------

        void OnQuestClosed(QuestClosedEvent e)
        {
            if (e.Status != QuestStatus.Failed || e.Quest == null) return;
            foreach (var t in e.Quest.Assignees)
                if (t is Adventurer a && a.IsAlive) AddStress(a, StressRules.QuestFailedStress);
        }

        void OnDied(AdventurerDiedEvent e)
        {
            var dead = e.Adventurer;
            if (dead == null || dead.Data == null) return;
            Forget(dead.Data.id);
            foreach (var m in _roster.Members.ToList())
            {
                if (m == null || !m.IsAlive) continue;
                // Only those who cared about the dead feel it, and the closer they were the harder it hits.
                AddStress(m, StressRules.DeathStress(Relationships.Get(m, dead)));
            }
        }

        void OnLeft(AdventurerLeftEvent e)
        {
            if (e.Adventurer != null && e.Adventurer.Data != null) Forget(e.Adventurer.Data.id);
        }

        void Forget(string id)
        {
            _care.Remove(id);
            if (_center == null) return;
            foreach (var key in new[] { DemandKey(id), ResignKey(id), "limit:" + id, "ignored:" + id, "sabotage:" + id })
                _center.Resolve(key);
        }

        // ---------------- Each game hour ----------------

        void OnHour(HourPassedEvent e)
        {
            float now = _clock.TotalHours;
            foreach (var a in _roster.Members.ToList())
            {
                if (a == null || !a.IsAlive || a.IsVisitor || !a.IsGuildMember) continue;
                var d = a.Data;
                if (d.fatigue >= StressRules.OverworkFatigue && a.Fsm.Current != a.RestState)
                    AddStress(a, StressRules.OverworkStressPerHour);

                var c = Care(a);
                var asked = c.Demand;
                var q = a.CurrentQuest;
                var step = c.Tick(new CareInput
                {
                    Now = now,
                    Stress = d.stress,
                    Fatigue = d.fatigue,
                    Values = d.values,
                    OnQuest = q != null && q.IsActive,
                    InTownFree = q == null && a.IsAtLeisureInTown,
                    InFight = a.InTheThickOfIt,
                }, _roll);
                if (step != CareStep.None) Apply(a, c, step, asked);
            }
        }

        void Apply(Adventurer a, MemberCare c, CareStep step, CareDemand asked)
        {
            string id = a.Data.id, name = a.Data.name;
            int stress = Mathf.RoundToInt(a.Data.stress);

            if (step.HasFlag(CareStep.LeaveEnded)) a.Say(CareTexts.LeaveEndedLine);
            if (step.HasFlag(CareStep.DemandWithdrawn)) _center?.Resolve(DemandKey(id));

            if (step.HasFlag(CareStep.DemandRaised))
            {
                _center?.Post(NotificationKind.Warning, CareTexts.DemandTitle(c.Demand), CareTexts.DemandNotice(name, c.Demand, stress),
                    DemandKey(id), NotificationLink.Management(id));
                a.Say(CareTexts.DemandLine(c.Demand));
                Remember(a, 2, CareTexts.MemoryAsked(c.Demand), "care-ask");
            }

            if (step.HasFlag(CareStep.DemandIgnored))
            {
                _center?.Resolve(DemandKey(id));
                AddStress(a, StressRules.IgnoredDemandStress);
                Remember(a, 2, CareTexts.MemoryIgnored(asked), "care-ignored");
                _center?.Post(c.Grudges >= CareRules.QuitGrudges ? NotificationKind.Danger : NotificationKind.Warning, "요구 무시됨",
                    CareTexts.DemandIgnored(name, c.Grudges), "ignored:" + id, NotificationLink.Management(id));
            }

            if (step.HasFlag(CareStep.ResignNotice))
                _center?.Post(NotificationKind.Danger, "이탈 예고", CareTexts.ResignNotice(name), ResignKey(id), NotificationLink.Management(id));

            if (step.HasFlag(CareStep.LimitWarning))
                _center?.Post(NotificationKind.Danger, "스트레스 한계", CareTexts.LimitWarning(name, stress), "limit:" + id, NotificationLink.Management(id));

            if (step.HasFlag(CareStep.Sabotage)) WalkAwayFromQuest(a, c);
            if (step.HasFlag(CareStep.Quit)) QuitGuild(a, c.Cause);
        }

        /// <summary>The member gives up the quest they are on and heads back to town.</summary>
        void WalkAwayFromQuest(Adventurer a, MemberCare c)
        {
            var q = a.CurrentQuest;
            if (q == null) return;
            _board.Abandon(q, a);
            a.CurrentQuest = null;
            a.Data.stress = Mathf.Max(0f, a.Data.stress - StressRules.SabotageRelief);
            c.ResetBreaking();
            a.Say(CareTexts.SabotageLine);
            a.ChangeState(a.ReturnState);
            _center?.Post(NotificationKind.Warning, "의뢰 포기", CareTexts.Sabotage(a.Data.name, q.Title),
                "sabotage:" + a.Data.id, NotificationLink.Management(a.Data.id));
        }

        void QuitGuild(Adventurer a, QuitCause cause)
        {
            string name = a.Data.name;
            AfterDeparture(a);
            _center?.Post(NotificationKind.Danger, "길드원 이탈", CareTexts.Quit(name, cause), "quit:" + a.Data.id, NotificationLink.Roster());
            _roster.Dismiss(a);
        }
    }
}
