using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Economy;
using GuildMaster.LLM;
using GuildMaster.Notifications;
using GuildMaster.Quests;
using UnityEngine;

namespace GuildMaster.Proposals
{
    /// <summary>Published whenever the proposal list, or whether it is being made, changes.</summary>
    public struct QuestProposalsChangedEvent { }

    /// <summary>
    /// LLM quest proposals (F-8): a few ready-made quests for the guild master to choose from instead of filling in
    /// the posting form. The rules build valid, affordable plans from what the guild knows (<see cref="ProposalRules"/>);
    /// the LLM picks and words them (<see cref="ProposalPrompt"/>), and any failure falls back to the rules' own pick.
    /// Choosing one posts it through the normal <see cref="QuestBoard.TryPost"/>, so escrow, negotiation and
    /// persuasion work as for any quest. A new list is made every game day, and on request after a cool-down.
    /// </summary>
    public class QuestProposalService : IDisposable
    {
        readonly QuestBoard _board;
        readonly GuildIntel _intel;
        readonly GuildRoster _roster;
        readonly GuildTreasury _treasury;
        readonly GameClock _clock;
        readonly NotificationCenter _notifications;

        List<ProposalPlan> _current = new();
        bool _generating;
        float _rerollReadyAt;

        public QuestProposalService(QuestBoard board, GuildIntel intel, GuildRoster roster, GuildTreasury treasury, GameClock clock,
            NotificationCenter notifications)
        {
            _board = board;
            _intel = intel;
            _roster = roster;
            _treasury = treasury;
            _clock = clock;
            _notifications = notifications;
            EventBus.Subscribe<DayPassedEvent>(OnDayPassed);
        }

        public void Dispose() => EventBus.Unsubscribe<DayPassedEvent>(OnDayPassed);

        public IReadOnlyList<ProposalPlan> Current => _current;
        public bool Generating => _generating;
        /// <summary>"LLM" or "규칙" for the current list; empty before the first one.</summary>
        public string Source { get; private set; } = "";
        /// <summary>Why the list is empty (null while there are cards).</summary>
        public string EmptyReason { get; private set; }
        public float GeneratedAtHour { get; private set; } = -1f;
        public int Generation { get; private set; }

        // ---------------- Making the list ----------------

        void OnDayPassed(DayPassedEvent e)
        {
            if (_board.Quests.Count(q => q.IsActive) >= ProposalRules.MaxOpenQuests) return;
            _ = GenerateAsync(auto: true);
        }

        /// <summary>Makes the first list when the window opens and there is none yet.</summary>
        public void Ensure()
        {
            if (_current.Count == 0 && !_generating && GeneratedAtHour < 0f) _ = GenerateAsync(auto: false);
        }

        public bool CanReroll(out string reason)
        {
            reason = null;
            if (_generating) { reason = "제안을 만드는 중입니다."; return false; }
            float left = _rerollReadyAt - _clock.TotalHours;
            if (left > 0f) { reason = $"약 {Mathf.CeilToInt(left)}시간 뒤에 다시 뽑을 수 있습니다."; return false; }
            return true;
        }

        /// <summary>Redraws the list (cool-down applies).</summary>
        public bool Reroll(out string message)
        {
            message = null;
            if (!CanReroll(out message)) return false;
            _rerollReadyAt = _clock.TotalHours + ProposalRules.RerollCooldownHours;
            _ = GenerateAsync(auto: false);
            return true;
        }

        /// <summary>What the guild could go after: hunting grounds of species it has seen, and nests it knows.</summary>
        List<ProposalTarget> BuildTargets()
        {
            var list = new List<ProposalTarget>();
            var center = Services.TryGet<World.WorldService>(out var world) ? world.TownCenterWorld : Vector2.zero;

            foreach (var species in _intel.KnownSpecies)
            {
                if (species == null) continue;
                if (_board.Quests.Any(q => q.IsActive && q.Type == QuestType.Hunt && q.Species == species)) continue;
                var nest = _intel.FindNearestKnownNest(center, species);
                if (nest == null) continue;
                var point = nest.WorldPosition;
                if (QuestBoard.HuntPointError(point) != null) continue;
                list.Add(new ProposalTarget
                {
                    Key = "hunt:" + species.id,
                    Type = QuestType.Hunt,
                    Danger = species.danger,
                    Label = species.displayName,
                    Area = QuestBoard.DescribeArea(point),
                    Distance = Vector2.Distance(center, point),
                    Observed = _intel.ObservedPopulation(species),
                    Point = point,
                    Source = species,
                });
            }

            foreach (var nest in _intel.KnownNests)
            {
                if (nest == null || !nest.Discovered) continue;
                if (_intel.Get(nest) is { Observed: true, LastCount: 0 }) continue;
                if (_board.Quests.Any(q => q.IsActive && q.Type == QuestType.ClearNest && q.Nest == nest)) continue;
                list.Add(new ProposalTarget
                {
                    Key = "nest:" + _intel.NameOf(nest),
                    Type = QuestType.ClearNest,
                    Danger = QuestBoard.NestDanger(nest),
                    Label = _intel.NameOf(nest),
                    Area = QuestBoard.DescribeArea(nest.WorldPosition),
                    Distance = Vector2.Distance(center, nest.WorldPosition),
                    Point = nest.WorldPosition,
                    Source = nest,
                });
            }
            return list;
        }

        async Task GenerateAsync(bool auto)
        {
            if (_generating) return;
            _generating = true;
            EventBus.Publish(new QuestProposalsChangedEvent());
            try
            {
                var members = _roster.Members.Where(a => a != null && a.IsAlive).ToList();
                var chosen = new List<ProposalPlan>();
                string source = "규칙";
                string emptyReason = null;

                if (members.Count == 0) emptyReason = ProposalTexts.NoMembers;
                else
                {
                    var targets = BuildTargets();
                    if (targets.Count == 0) emptyReason = ProposalTexts.NoTargets;
                    else
                    {
                        var strongest = members.OrderByDescending(a => AdventurerStats.Power(a.Data)).First();
                        float power = AdventurerStats.Power(strongest.Data);
                        var pool = ProposalRules.BuildPool(targets, power, members.Count, _treasury.Gold, _treasury.Exp, GameRandom.Shared);
                        if (pool.Count == 0) emptyReason = ProposalTexts.NoAffordable;
                        else
                        {
                            chosen = await ChooseAsync(pool, members.Count, strongest.Data.level);
                            source = chosen.Count > 0 && chosen.Any(p => p.FromLlm) ? "LLM" : "규칙";
                        }
                    }
                }

                _current = chosen;
                Source = chosen.Count > 0 ? source : "";
                EmptyReason = chosen.Count > 0 ? null : emptyReason;
                GeneratedAtHour = _clock.TotalHours;
                Generation++;

                if (auto && chosen.Count > 0)
                    _notifications?.Post(NotificationKind.Quest, "의뢰 제안", ProposalTexts.NewProposals(chosen.Count), "proposals", NotificationLink.Proposals());
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PROPOSALS] " + ex);
                _current = new List<ProposalPlan>();
                EmptyReason = "제안을 만들지 못했습니다. 잠시 뒤 다시 시도하세요.";
            }
            finally
            {
                _generating = false;
                EventBus.Publish(new QuestProposalsChangedEvent());
            }
        }

        /// <summary>The LLM's pick and wording when it works, else the rules' own varied pick with rule-based wording.</summary>
        async Task<List<ProposalPlan>> ChooseAsync(List<ProposalPlan> pool, int members, int strongestLevel)
        {
            if (Services.TryGet<LlmGateway>(out var gateway) && gateway.Enabled && gateway.IsReady)
            {
                var ctx = ProposalPrompt.BuildContext(_clock.Day, _treasury.Gold, members, strongestLevel, Situation(), pool);
                var result = await gateway.RequestAsync(ProposalPrompt.BuildRequest(ctx));
                if (result.Ok && LlmJson.TryParse<ProposalPrompt.ResponseDto>(result.Text, out var dto)
                    && ProposalPrompt.TryInterpret(dto, pool, out var picked, out var reject, GameRandom.Shared))
                {
                    if (result.Exchange != null) result.Exchange.Note = "의뢰 제안: " + string.Join(",", picked.Select(p => p.Id));
                    return picked;
                }
                if (result.Exchange != null) result.Exchange.Note = "규칙 기반으로 대체: " + (result.Ok ? "parse" : result.Error);
            }
            var fallback = ProposalRules.Recommend(pool);
            foreach (var plan in fallback) ProposalTexts.ApplyFallback(plan, GameRandom.Shared);
            return fallback;
        }

        /// <summary>A few words on the guild's situation for the prompt.</summary>
        string Situation()
        {
            int open = _board.Quests.Count(q => q.IsActive);
            var bits = new List<string>();
            bits.Add(open == 0 ? "게시판이 비어 있다" : $"열린 의뢰 {open}건");
            if (_treasury.Gold < 150) bits.Add("자금이 빠듯하다");
            return string.Join(", ", bits);
        }

        // ---------------- Choosing ----------------

        /// <summary>Why the plan cannot be posted right now (funds, the target is gone...), or null.</summary>
        public string Check(ProposalPlan plan)
        {
            if (plan == null) return "제안이 없습니다.";
            if (plan.RewardGold > _treasury.Gold || plan.RewardExp > _treasury.Exp) return "길드 자금이 부족합니다.";
            return _board.Validate(ProposalRules.ToDraft(plan));
        }

        /// <summary>Posts the plan as a quest. On success it leaves the list.</summary>
        public bool Choose(ProposalPlan plan, out string message)
        {
            message = Check(plan);
            if (message != null) return false;
            if (!_board.TryPost(ProposalRules.ToDraft(plan), out var quest, out message)) return false;
            _current.Remove(plan);
            if (_current.Count == 0) EmptyReason = "제안한 의뢰를 모두 처리했습니다. 다시 뽑으면 새 제안을 받을 수 있습니다.";
            message = $"'{quest.Title}' 의뢰가 등록되었습니다. (보상 {quest.RewardGold}G 예치)";
            EventBus.Publish(new QuestProposalsChangedEvent());
            return true;
        }

        public void Dismiss(ProposalPlan plan)
        {
            if (plan == null || !_current.Remove(plan)) return;
            if (_current.Count == 0) EmptyReason = "제안을 모두 넘겼습니다. 다시 뽑으면 새 제안을 받을 수 있습니다.";
            EventBus.Publish(new QuestProposalsChangedEvent());
        }

        /// <summary>Moves the plan's gold reward up or down a step (within its allowed range).</summary>
        public bool AdjustReward(ProposalPlan plan, int steps)
        {
            if (plan == null || !plan.AdjustReward(steps)) return false;
            EventBus.Publish(new QuestProposalsChangedEvent());
            return true;
        }
    }
}
