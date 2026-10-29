using System;
using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Notifications;
using GuildMaster.Proposals;
using GuildMaster.Quests;
using UnityEngine;

namespace GuildMaster.Economy
{
    /// <summary>Who asked for a quest and what they pay the guild when it is done.</summary>
    public struct Commission
    {
        public string Client;
        /// <summary>What the client thinks the job is worth (the going rate for it).</summary>
        public int Budget;
        /// <summary>The guild's cut on top of the reward.</summary>
        public int Fee;

        /// <summary>What the client pays on completion for a quest posted at <paramref name="rewardGold"/>.</summary>
        public int Payment(int rewardGold) => CommissionRules.Payment(rewardGold, Budget, Fee);
    }

    /// <summary>
    /// Quest commissions (user decision 2026-10-29: "의뢰인이 완료 대금을 냄"). Every quest has a client in town who
    /// pays the guild when it is done: the reward back, up to the going rate for such a job (what the proposal
    /// cards would offer), plus the guild's cut — 20 / 30 / 40% of the going rate by danger. Posting a reward above
    /// the going rate is the guild's own money; the cut does not grow with it. Pure rules, covered by tests.
    /// </summary>
    public static class CommissionRules
    {
        public static float Rate(DangerLevel d) => d switch
        {
            DangerLevel.Low => 0.2f,
            DangerLevel.Medium => 0.3f,
            _ => 0.4f,
        };

        /// <summary>The going rate: what a proposal card of the balanced kind would offer for the job.</summary>
        public static int Budget(QuestType type, DangerLevel danger, int count, float distance)
            => ProposalRules.RewardGold(type, danger, Mathf.Max(1, count), distance, ProposalStyle.Balanced);

        public static int Fee(int budget, DangerLevel danger) => Mathf.Max(5, Mathf.RoundToInt(budget * Rate(danger) / 5f) * 5);

        /// <summary>The reward back (no more than the going rate, raises from negotiation not included) plus the cut.</summary>
        public static int Payment(int rewardGold, int budget, int fee) => Mathf.Clamp(rewardGold, 0, budget) + fee;

        /// <summary>The townsperson who asks for this kind of job (residents by trade, picked by the quest's id).</summary>
        public static string Client(QuestType type, string questId)
        {
            string[] who = type switch
            {
                QuestType.Hunt => new[] { "경비병 그레타", "촌장 바르톨", "대장장이 한스" },
                QuestType.Escort => new[] { "약초상 마리", "빵집 엘리", "촌장 바르톨" },
                _ => new[] { "촌장 바르톨", "경비병 그레타" },
            };
            int h = 0;
            foreach (char c in questId ?? "") h = h * 31 + c;
            return who[Mathf.Abs(h) % who.Length];
        }
    }

    /// <summary>
    /// Gives every posted quest a client and collects the payment when it is done (2026-10-29). Leaves the quest board
    /// as it is: it listens to the board's events and pays into the treasury with <see cref="GoldReason.Commission"/>.
    /// </summary>
    public class CommissionService : IDisposable
    {
        readonly GuildTreasury _treasury;
        readonly Dictionary<string, Commission> _byQuest = new();

        /// <summary>All the cuts collected since the game began.</summary>
        public int TotalFees { get; private set; }
        public int Paid { get; private set; }

        public CommissionService(GuildTreasury treasury)
        {
            _treasury = treasury;
            EventBus.Subscribe<QuestPostedEvent>(OnPosted);
            EventBus.Subscribe<QuestCompletedEvent>(OnCompleted);
            EventBus.Subscribe<QuestClosedEvent>(OnClosed);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<QuestPostedEvent>(OnPosted);
            EventBus.Unsubscribe<QuestCompletedEvent>(OnCompleted);
            EventBus.Unsubscribe<QuestClosedEvent>(OnClosed);
        }

        /// <summary>The commission behind a quest (for the windows).</summary>
        public bool TryGet(Quest q, out Commission c)
        {
            c = default;
            return q != null && _byQuest.TryGetValue(q.Id, out c);
        }

        public static Commission For(Quest q)
        {
            float distance = 0f;
            if (q.Type == QuestType.Hunt && Services.TryGet<World.WorldService>(out var world))
                distance = Vector2.Distance(q.Point, world.TownCenterWorld);
            int budget = CommissionRules.Budget(q.Type, q.Danger, q.RequiredCount, distance);
            return new Commission { Client = CommissionRules.Client(q.Type, q.Id), Budget = budget, Fee = CommissionRules.Fee(budget, q.Danger) };
        }

        void OnPosted(QuestPostedEvent e)
        {
            if (e.Quest != null) _byQuest[e.Quest.Id] = For(e.Quest);
        }

        void OnCompleted(QuestCompletedEvent e)
        {
            var q = e.Quest;
            if (q == null || !_byQuest.TryGetValue(q.Id, out var c)) return;
            _byQuest.Remove(q.Id);
            // Called off by the guild after someone took it: the takers are paid, the client is not asked to.
            if (q.CalledOff) return;
            int pay = c.Payment(q.RewardGold - q.BonusGold);
            _treasury.AddGold(pay, GoldReason.Commission);
            TotalFees += c.Fee;
            Paid++;
            if (Services.TryGet<Social.ResidentService>(out var residents))
                foreach (var r in residents.Residents)
                    if (r != null && c.Client.EndsWith(r.Profile.Name)) r.Profile.AddOpinion(3);
            string text = $"{c.Client}{MemoryRecorder.Josa(c.Client, "이", "가")} '{q.Title}' 의뢰 대금 {pay}G를 냈습니다 (수수료 {c.Fee}G).";
            if (Services.TryGet<Session.GameLog>(out var log)) log.Add(text);
            if (Services.TryGet<NotificationCenter>(out var center)) center.Post(NotificationKind.Success, "의뢰 대금", text, null);
        }

        void OnClosed(QuestClosedEvent e)
        {
            if (e.Quest != null) _byQuest.Remove(e.Quest.Id);
        }
    }
}
