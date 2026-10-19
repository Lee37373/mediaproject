using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.LLM;
using GuildMaster.Quests;
using GuildMaster.Session;
using UnityEngine;

namespace GuildMaster.Persuasion
{
    /// <summary>Published once when a persuasion talk reaches a verdict.</summary>
    public struct PersuasionEndedEvent
    {
        public Quest Quest;
        public Adventurer Target;
        public PersuadeOutcome Outcome;
        public int Turns;
    }

    /// <summary>
    /// One persuasion talk between the guild master and one adventurer about one quest (P4-24):
    /// willingness, transcript, LLM (or rule-based) replies and the verdict. Asynchronous; the game
    /// keeps running. On success the adventurer takes the quest through the normal
    /// <see cref="QuestBoard.TryAccept"/> path, so party invitations follow like for any accepted quest.
    /// </summary>
    public class PersuasionSession
    {
        readonly List<PersuadeTurn> _turns = new();
        readonly QuestBoard _board;
        readonly GameClock _clock;

        public Adventurer Target { get; }
        public Quest Quest { get; }
        /// <summary>The quest's risk for this adventurer when the talk began (여유/적당/위험/무모).</summary>
        public string RiskLabel { get; }
        /// <summary>Willingness needed for a yes (see <see cref="PersuasionRules.AcceptThreshold"/>).</summary>
        public int Threshold { get; }
        public int Willingness { get; private set; }

        // ---- Gold negotiation (P4-25) ----
        /// <summary>The quest's gold reward when the talk began; raises are measured against it.</summary>
        public int BaseRewardGold { get; }
        /// <summary>How much gold weighs for this adventurer (see <see cref="NegotiationRules.Greed"/>).</summary>
        public float Greed { get; }
        /// <summary>Extra gold promised so far in this talk. Paid from the treasury only when the adventurer takes the quest.</summary>
        public int OfferedGold { get; private set; }
        /// <summary>Extra gold the adventurer asks for on top of <see cref="OfferedGold"/> (0 = no counter-offer right now).</summary>
        public int Counter { get; private set; }
        /// <summary>Most extra gold that can still be offered now (cap left and the treasury), in whole steps.</summary>
        public int MaxOffer => Outcome == PersuadeOutcome.Ongoing
            ? NegotiationRules.MaxOffer(BaseRewardGold, OfferedGold, TreasuryGold)
            : 0;
        public bool CanOffer => Outcome == PersuadeOutcome.Ongoing && !Busy && StillValid() && MaxOffer >= NegotiationRules.Step;

        int _counterBlockedTurn = -1;

        static int TreasuryGold => Services.TryGet<GuildTreasury>(out var t) ? t.Gold : 0;

        public int PlayerTurns { get; private set; }
        public bool Busy { get; private set; }
        public PersuadeOutcome Outcome { get; private set; } = PersuadeOutcome.Ongoing;
        public PersuadeDecision LastDecision { get; private set; } = PersuadeDecision.Undecided;
        public bool LastReplyFromLlm { get; private set; }
        public IReadOnlyList<PersuadeTurn> Turns => _turns;

        public event Action Changed;

        /// <summary>False while a safety rule forbids taking this quest: badly hurt, or (F-2) too stressed or on leave.</summary>
        public bool AcceptAllowed => RefusalReason == null;

        /// <summary>Why the adventurer cannot take the quest right now, in the words of the persuasion window; null = they can.</summary>
        public string RefusalReason
        {
            get
            {
                if (Target == null) return "대상이 없습니다.";
                if (Target.HpRatio < PersuasionRules.MinHpToAccept) return "몸 상태가 나빠 지금은 어떤 의뢰도 맡지 않을 겁니다.";
                if (Welfare.GuildWelfare.QuestRefusal(Target, Quest.Danger) != null)
                    return Welfare.StressRules.LevelOf(Target.Data.stress) == Welfare.StressLevel.Breaking || Welfare.GuildWelfare.AnyQuestRefusal(Target) != null
                        ? "지쳐서(스트레스나 휴가) 지금은 어떤 의뢰도 맡지 않을 겁니다."
                        : "스트레스가 커서 이런 위험한 의뢰는 맡지 않을 겁니다.";
                return null;
            }
        }

        public PersuasionSession(Adventurer target, Quest quest, QuestBoard board, GameClock clock)
        {
            Target = target;
            Quest = quest;
            _board = board;
            _clock = clock;
            RiskLabel = QuestRisk.RiskLabel(target, quest);
            float aversion = target.Traits?.DangerAversion ?? 0f;
            Willingness = PersuasionRules.InitialWillingness(RiskLabel, aversion, target.Data.values);
            Threshold = PersuasionRules.AcceptThreshold(RiskLabel, PersuasionRules.IsRecklessBlocked(RiskLabel, aversion, target.Data.values));
            BaseRewardGold = quest.RewardGold;
            Greed = NegotiationRules.Greed(target.Data.values, target.Traits?.GoldGreed ?? 1f);
        }

        /// <summary>The adventurer is alive, free and the quest still has a free slot.</summary>
        public bool StillValid()
            => Target != null && Target.IsAlive && Target.IsAvailableForQuest && Quest != null && Quest.HasFreeSlot;

        public async Task SendAsync(string playerText, bool proposal)
        {
            if (Busy || Outcome != PersuadeOutcome.Ongoing || Target == null) return;
            if (!StillValid())
            {
                Finish(PersuadeOutcome.Unavailable);
                Changed?.Invoke();
                return;
            }
            playerText = string.IsNullOrWhiteSpace(playerText)
                ? (proposal ? "이 의뢰를 맡아 줄 수 있겠나?" : "...")
                : playerText.Trim();
            Busy = true;
            AddTurn(true, playerText);
            PlayerTurns++;
            Changed?.Invoke();

            RuleBasedPersuader.Reply reply;
            try { reply = await GetReplyAsync(playerText, proposal); }
            finally { Busy = false; }

            // Died or left while the model was thinking.
            if (Target == null || !Target.IsAlive)
            {
                Finish(PersuadeOutcome.Unavailable);
                Changed?.Invoke();
                return;
            }
            Willingness = Mathf.Clamp(Willingness + Mathf.Clamp(reply.WillingnessDelta, -PersuasionRules.MaxDelta, PersuasionRules.MaxDelta), 0, 100);
            LastDecision = reply.Decision;
            AddTurn(false, reply.Text);
            Target.Say(reply.Text, toFeed: false);   // the persuade window shows the transcript
            Finish(PersuasionRules.Judge(Willingness, reply.Decision, PlayerTurns, Threshold, AcceptAllowed));
            UpdateCounter();
            Changed?.Invoke();
        }

        // ---------------- Gold negotiation (P4-25) ----------------

        /// <summary>
        /// The guild master promises <paramref name="extraGold"/> more on top of the reward. The adventurer answers at once
        /// (rule-based lines): a raise that lifts willingness to the threshold closes the deal, otherwise they may name a price.
        /// Returns false when the offer is not allowed (talk over, too small, above the cap or the treasury).
        /// </summary>
        public bool Offer(int extraGold)
        {
            if (!CanOffer || extraGold < NegotiationRules.Step || extraGold > MaxOffer) return false;
            extraGold = extraGold / NegotiationRules.Step * NegotiationRules.Step;
            int before = NegotiationRules.WillingnessFromExtra(OfferedGold, BaseRewardGold, Greed);
            OfferedGold += extraGold;
            int gain = NegotiationRules.WillingnessFromExtra(OfferedGold, BaseRewardGold, Greed) - before;
            Counter = 0;
            AddTurn(true, $"보상을 {extraGold}G 더 얹겠습니다. (지금까지 +{OfferedGold}G)");
            PlayerTurns++;
            Willingness = Mathf.Clamp(Willingness + gain, 0, 100);

            var rng = GameRandom.Shared;
            string line = NegotiationLines.OfferReply(Greed, Willingness, Threshold, AcceptAllowed, rng);
            LastDecision = AcceptAllowed && Willingness >= Threshold ? PersuadeDecision.Accept : PersuadeDecision.Undecided;
            AddTurn(false, line);
            Target.Say(line, toFeed: false);
            Finish(PersuasionRules.Judge(Willingness, LastDecision, PlayerTurns, Threshold, AcceptAllowed));
            UpdateCounter();
            Changed?.Invoke();
            return true;
        }

        /// <summary>The guild master agrees to the adventurer's asking price: the extra gold is added and the adventurer takes the quest.</summary>
        public bool AcceptCounter()
        {
            if (Counter <= 0 || Busy || Outcome != PersuadeOutcome.Ongoing) return false;
            int ask = Counter;
            if (!StillValid()) { Counter = 0; Finish(PersuadeOutcome.Unavailable); Changed?.Invoke(); return false; }
            if (ask > MaxOffer) { Counter = 0; Changed?.Invoke(); return false; }
            int before = NegotiationRules.WillingnessFromExtra(OfferedGold, BaseRewardGold, Greed);
            OfferedGold += ask;
            int gain = NegotiationRules.WillingnessFromExtra(OfferedGold, BaseRewardGold, Greed) - before;
            Counter = 0;
            AddTurn(true, $"좋습니다, {ask}G를 더 드리죠. (지금까지 +{OfferedGold}G)");
            Willingness = Mathf.Clamp(Willingness + gain, 0, 100);
            string line = NegotiationLines.DealLine(Greed, GameRandom.Shared);
            LastDecision = PersuadeDecision.Accept;
            AddTurn(false, line);
            Target.Say(line, toFeed: false);
            Finish(PersuadeOutcome.Accepted);
            Changed?.Invoke();
            return true;
        }

        /// <summary>The guild master turns the asking price down. The adventurer will not ask again until the talk moves on.</summary>
        public void DeclineCounter()
        {
            if (Counter <= 0 || Busy || Outcome != PersuadeOutcome.Ongoing) return;
            Counter = 0;
            _counterBlockedTurn = PlayerTurns;
            string line = NegotiationLines.DeclinedLine(GameRandom.Shared);
            AddTurn(true, "그 금액은 곤란합니다.");
            AddTurn(false, line);
            Target?.Say(line, toFeed: false);
            Changed?.Invoke();
        }

        /// <summary>After a reply: a willing-but-not-yet adventurer names a price (once per turn, not right after being turned down).</summary>
        void UpdateCounter()
        {
            Counter = 0;
            if (Outcome != PersuadeOutcome.Ongoing || Target == null || !StillValid() || PlayerTurns <= _counterBlockedTurn) return;
            int ask = NegotiationRules.CounterOffer(Willingness, Threshold, OfferedGold, BaseRewardGold, Greed, AcceptAllowed, MaxOffer);
            if (ask <= 0) return;
            Counter = ask;
            string line = NegotiationLines.CounterLine(Greed, ask, GameRandom.Shared);
            AddTurn(false, line);
            Target.Say(line, toFeed: false);
        }

        async Task<RuleBasedPersuader.Reply> GetReplyAsync(string playerText, bool proposal)
        {
            LastReplyFromLlm = false;
            // Everything the fallback needs is read before awaiting: the adventurer may vanish meanwhile.
            string values = Target.Data.values;
            int maxParty = Quest.MaxParty;
            int willingness = Willingness;
            if (Services.TryGet<LlmGateway>(out var gw) && gw.Enabled && gw.IsReady)
            {
                var result = await gw.RequestAsync(PersuadePrompt.BuildRequest(Target, Quest, willingness, Threshold, _turns, proposal, OfferedGold));
                if (result.Ok && LlmJson.TryParse<PersuadePrompt.ResponseDto>(result.Text, out var dto) && !string.IsNullOrWhiteSpace(dto.reply))
                {
                    string line = LlmJson.CleanSentence(dto.reply, PersuadePrompt.ReplyMaxChars, PersuadePrompt.ReplyGrammarChars, false);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        LastReplyFromLlm = true;
                        if (result.Exchange != null) result.Exchange.Note = $"설득 대화: 설득도 {dto.willingness_delta:+0;-0;0}, {dto.decision}";
                        return new RuleBasedPersuader.Reply
                        {
                            Text = line,
                            WillingnessDelta = dto.willingness_delta,
                            Decision = PersuadePrompt.ParseDecision(dto.decision),
                        };
                    }
                }
                if (result.Exchange != null) result.Exchange.Note = "규칙 기반으로 대체: " + (result.Ok ? "parse" : result.Error);
            }
            return RuleBasedPersuader.Respond(playerText, values, RiskLabel, maxParty, willingness, Threshold, proposal, GameRandom.Shared);
        }

        void AddTurn(bool fromPlayer, string text) => _turns.Add(new PersuadeTurn { FromPlayer = fromPlayer, Text = text });

        void Finish(PersuadeOutcome outcome)
        {
            if (outcome == PersuadeOutcome.Ongoing) return;
            if (outcome == PersuadeOutcome.Accepted) outcome = TryApplyAcceptance();
            Counter = 0;
            Outcome = outcome;
            Remember();
            LogResult();
            EventBus.Publish(new PersuasionEndedEvent { Quest = Quest, Target = Target, Outcome = outcome, Turns = PlayerTurns });
        }

        /// <summary>
        /// Same steps as an adventurer's own decision to accept (see DecidingState), plus paying for the deal:
        /// the promised extra gold is taken from the treasury and added to the quest's reward.
        /// </summary>
        PersuadeOutcome TryApplyAcceptance()
        {
            if (!StillValid() || !AcceptAllowed) return PersuadeOutcome.Unavailable;
            // Checked before the quest is taken, so a talk that cannot be paid never leaves a half-done deal.
            if (OfferedGold > TreasuryGold) return PersuadeOutcome.NoFunds;
            if (!_board.TryAccept(Quest, Target)) return PersuadeOutcome.Unavailable;
            if (OfferedGold > 0 && !_board.TryRaiseReward(Quest, OfferedGold)) OfferedGold = 0;
            Target.CurrentQuest = Quest;
            Target.ChangeState(Target.TravelState);
            return PersuadeOutcome.Accepted;
        }

        void Remember()
        {
            if (Target == null || Target.Data == null || Quest == null) return;
            float hour = _clock != null ? _clock.TotalHours : 0f;
            if (Outcome == PersuadeOutcome.Accepted && OfferedGold > 0)
                AdventurerMemory.Add(Target.Data.memories, hour, 2, $"길드 마스터와 협상해 보상을 {OfferedGold}G 더 받기로 하고 '{Quest.Title}' 의뢰를 맡았다");
            else if (Outcome == PersuadeOutcome.Accepted)
                AdventurerMemory.Add(Target.Data.memories, hour, 2, $"길드 마스터의 설득으로 '{Quest.Title}' 의뢰를 맡기로 했다");
            else if (Outcome == PersuadeOutcome.Refused)
                AdventurerMemory.Add(Target.Data.memories, hour, 1, $"길드 마스터가 '{Quest.Title}' 의뢰를 맡으라고 설득했지만 거절했다");
        }

        void LogResult()
        {
            if (Target == null || Target.Data == null || Quest == null || !Services.TryGet<GameLog>(out var log)) return;
            string name = Target.Data.name;
            // An acceptance is already logged by the board's own "accepted" entry.
            if (Outcome == PersuadeOutcome.Refused) log.Add($"{name}이(가) '{Quest.Title}' 의뢰 설득을 거절했습니다.", LogCategory.Quest);
            else if (Outcome == PersuadeOutcome.NoFunds) log.Add($"길드 자금이 부족해 {name}과(와)의 '{Quest.Title}' 협상이 무산되었습니다.", LogCategory.Quest);
            else if (Outcome == PersuadeOutcome.Accepted && OfferedGold > 0) log.Add($"'{Quest.Title}' 보상이 협상으로 {OfferedGold}G 인상되었습니다. ({name})", LogCategory.Quest);
        }
    }
}
